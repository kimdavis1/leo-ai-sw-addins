# Find vault name from vault root path
param([string]$VaultPath = "C:\moti-test-pro")

try {
    # Load PDM DLL
    $pdmDllPath = Join-Path $env:ProgramFiles "SOLIDWORKS Corp\SOLIDWORKS PDM\EPDM.Interop.epdm.dll"
    if (-not (Test-Path $pdmDllPath)) {
        Write-Host "ERROR: PDM DLL not found" -ForegroundColor Red
        exit 1
    }

    $null = [System.Reflection.Assembly]::LoadFrom($pdmDllPath)
    $vault = New-Object -ComObject ConisioLib.EdmVault

    # Get all vault views
    $views = $null
    $vault.GetVaultViews([ref]$views, $false)

    if ($null -eq $views -or $views.Length -eq 0) {
        Write-Host "No vault views found. Please log into PDM at least once." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Searching for vault with path: $VaultPath" -ForegroundColor Cyan
    Write-Host ""

    $found = $false
    foreach ($view in $views) {
        if ($view.mbsPath -eq $VaultPath) {
            Write-Host "FOUND!" -ForegroundColor Green
            Write-Host "  Vault Name: $($view.mbsVaultName)" -ForegroundColor Green
            Write-Host "  Server: $($view.mbsServerName)" -ForegroundColor Gray
            Write-Host "  Path: $($view.mbsPath)" -ForegroundColor Gray
            Write-Host ""
            Write-Host "Update your config.json with:" -ForegroundColor Yellow
            Write-Host "  `"vaultName`": `"$($view.mbsVaultName)`"" -ForegroundColor Cyan
            $found = $true
            break
        }
    }

    if (-not $found) {
        Write-Host "No vault found with path: $VaultPath" -ForegroundColor Red
        Write-Host ""
        Write-Host "Available vaults:" -ForegroundColor Yellow
        foreach ($view in $views) {
            Write-Host "  - $($view.mbsVaultName) at $($view.mbsPath)" -ForegroundColor Gray
        }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Gray
    exit 1
}
