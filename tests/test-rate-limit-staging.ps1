# Integration helper: triggers the leo-api sync-metadata rate limiter on STAGING and
# verifies the 429 response body matches the format the C# regex expects.
#
# Usage:
#   pwsh ./tests/test-rate-limit-staging.ps1 -KeyJsonPath "C:\path\to\key.json"
#
# What it does:
#   1. Exchanges the Descope access key for a session JWT.
#   2. Creates a throw-away synced directory in staging.
#   3. Fires GET /sync-metadata in a tight burst until the limiter trips.
#   4. Prints each 429 response body verbatim and validates it against the same
#      regex used by SecureApiClient.ParseRetryAfterMs.
#   5. Deletes the throw-away directory.
#
# Pointing at STAGING explicitly (not prod) -- see $LeoApiBase below.

param(
    [Parameter(Mandatory = $true)]
    [string]$KeyJsonPath,

    [int]$BurstSize = 40
)

$ErrorActionPreference = 'Stop'

$LeoApiBase = 'https://api.staging.leo-primary.com'
$DescopeBase = 'https://api.descope.com'

# Mirrors SecureApiClient.RetryAfterMsRegex.
$RetryAfterMsRegex = [regex]'"retryAfterMs"\s*:\s*(\d+)'

Write-Host "[1/5] Loading key from $KeyJsonPath" -ForegroundColor Cyan
$key = Get-Content -Raw -Path $KeyJsonPath | ConvertFrom-Json
if (-not $key.apiKey -or -not $key.projectId) {
    throw "Key file missing apiKey or projectId."
}
Write-Host "    projectId: $($key.projectId)"

Write-Host "[2/5] Exchanging access key for session JWT via Descope" -ForegroundColor Cyan
$exchangeHeaders = @{
    'Authorization' = "Bearer $($key.projectId):$($key.apiKey)"
    'Accept'        = 'application/json'
}
$exchangeResponse = Invoke-RestMethod `
    -Method Post `
    -Uri "$DescopeBase/v1/auth/accesskey/exchange" `
    -Headers $exchangeHeaders `
    -Body '{}' `
    -ContentType 'application/json'

$jwt = $exchangeResponse.sessionJwt
if (-not $jwt) {
    throw "Descope did not return a sessionJwt. Response: $($exchangeResponse | ConvertTo-Json -Depth 5)"
}
Write-Host "    JWT acquired (length=$($jwt.Length))"

$apiHeaders = @{
    'Authorization' = "Bearer $jwt"
    'Accept'        = 'application/json'
}

$machineId = "rate-limit-test-$([guid]::NewGuid().ToString('N').Substring(0, 12))"
$testUri = "C:\\RateLimitTest\\$machineId"

Write-Host "[3/5] Creating throw-away synced directory in staging" -ForegroundColor Cyan
Write-Host "    machineId: $machineId"
Write-Host "    uri:       $testUri"

$createBody = @{ machineId = $machineId; uri = $testUri } | ConvertTo-Json
$createResponse = Invoke-RestMethod `
    -Method Post `
    -Uri "$LeoApiBase/api/v1/synced-directories" `
    -Headers $apiHeaders `
    -Body $createBody `
    -ContentType 'application/json'

# Leo-monolith returns the directory object with an `id` (UUID).
$directoryId = $createResponse.id
if (-not $directoryId) {
    Write-Host "    Full create response: $($createResponse | ConvertTo-Json -Depth 5)"
    throw "Could not extract directoryId from create response."
}
Write-Host "    directoryId: $directoryId"

try {
    Write-Host "[4/5] Firing $BurstSize parallel GETs against /sync-metadata to trip the limiter" -ForegroundColor Cyan
    $metadataUrl = "$LeoApiBase/api/v1/synced-directories/$directoryId/files/sync-metadata"

    # Use a single .NET HttpClient with parallel tasks. This mirrors how the PDM C# client
    # actually behaves (single HttpClient, multiple concurrent calls via Task.WhenAll) and
    # fires requests fast enough to exceed 20/3s comfortably.
    Add-Type -AssemblyName System.Net.Http
    $httpClient = New-Object System.Net.Http.HttpClient
    $httpClient.Timeout = [TimeSpan]::FromSeconds(30)
    $httpClient.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $jwt)

    $tasks = New-Object 'System.Collections.Generic.List[System.Threading.Tasks.Task]'
    for ($i = 1; $i -le $BurstSize; $i++) {
        $tasks.Add($httpClient.GetAsync($metadataUrl))
    }
    [System.Threading.Tasks.Task]::WaitAll($tasks.ToArray())

    $results = @()
    for ($i = 0; $i -lt $tasks.Count; $i++) {
        $resp = $tasks[$i].Result
        $body = $resp.Content.ReadAsStringAsync().Result
        $results += [PSCustomObject]@{
            Index  = $i + 1
            Status = [int]$resp.StatusCode
            Body   = $body
        }
        $resp.Dispose()
    }
    $httpClient.Dispose()

    $statusCounts = $results | Group-Object Status | ForEach-Object { "$($_.Name)=$($_.Count)" }
    Write-Host "    status distribution: $($statusCounts -join ', ')"

    $rateLimited = @($results | Where-Object { $_.Status -eq 429 })
    if ($rateLimited.Count -eq 0) {
        Write-Host "    WARNING: no 429 responses captured." -ForegroundColor Yellow
        $sample200 = @($results | Where-Object { $_.Status -eq 200 } | Select-Object -First 1)
        if ($sample200.Count -gt 0) { Write-Host "    Sample 200 body: $($sample200[0].Body)" }
        $sampleErr = @($results | Where-Object { $_.Status -lt 0 -or $_.Status -ge 400 } | Select-Object -First 1)
        if ($sampleErr.Count -gt 0) { Write-Host "    Sample error body: $($sampleErr[0].Body)" }
    }
    else {
        Write-Host ""
        Write-Host "[5/5] Inspecting 429 response bodies -- verifying C# regex matches" -ForegroundColor Cyan

        $verified = 0
        $unmatched = 0
        $allValues = @()
        foreach ($r in ($rateLimited | Select-Object -First 5)) {
            Write-Host "    raw body: $($r.Body)"
            $match = $RetryAfterMsRegex.Match($r.Body)
            if ($match.Success) {
                $ms = [int]$match.Groups[1].Value
                $allValues += $ms
                Write-Host "    -> regex matched, retryAfterMs = $ms ms" -ForegroundColor Green
                $verified++
            } else {
                Write-Host "    -> regex DID NOT match this body!" -ForegroundColor Red
                $unmatched++
            }
        }

        # Also sniff the synthetic exception-message format the C# throw produces.
        $syntheticEx = "Rate limit (429): $($rateLimited[0].Body)"
        Write-Host ""
        Write-Host "    Synthesized C# exception message: $syntheticEx"
        $match = $RetryAfterMsRegex.Match($syntheticEx)
        if ($match.Success) {
            Write-Host "    -> regex matched on synthesized message, retryAfterMs = $([int]$match.Groups[1].Value) ms" -ForegroundColor Green
        } else {
            Write-Host "    -> regex DID NOT match on synthesized message!" -ForegroundColor Red
        }

        Write-Host ""
        Write-Host "Summary:" -ForegroundColor Cyan
        Write-Host "    429 responses: $($rateLimited.Count)"
        Write-Host "    regex verified: $verified / $($verified + $unmatched)"
        if ($allValues.Count -gt 0) {
            Write-Host "    retryAfterMs values seen: min=$(($allValues | Measure-Object -Minimum).Minimum), max=$(($allValues | Measure-Object -Maximum).Maximum)"
        }
    }
}
finally {
    Write-Host ""
    Write-Host "[cleanup] Deleting throw-away directory $directoryId" -ForegroundColor Cyan
    try {
        Invoke-RestMethod `
            -Method Delete `
            -Uri "$LeoApiBase/api/v1/synced-directories/$directoryId" `
            -Headers $apiHeaders | Out-Null
        Write-Host "    deleted." -ForegroundColor Green
    } catch {
        Write-Host "    cleanup failed: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "    you may need to delete directory $directoryId manually."
    }
}
