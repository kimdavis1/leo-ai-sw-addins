# List available PDM vaults
try {
    # Load PDM DLL
    $pdmDllPath = Join-Path $env:ProgramFiles "SOLIDWORKS Corp\SOLIDWORKS PDM\EPDM.Interop.epdm.dll"
    if (-not (Test-Path $pdmDllPath)) {
        Write-Host "ERROR: PDM DLL not found at $pdmDllPath" -ForegroundColor Red
        exit 1
    }

    $null = [System.Reflection.Assembly]::LoadFrom($pdmDllPath)

    # Create vault object
    $vault = New-Object -ComObject ConisioLib.EdmVault

    # Get list of vaults
    $views = $null
    $vault.GetVaultViews([ref]$views, $false)

    Write-Host "`nAvailable PDM Vaults:" -ForegroundColor Cyan
    Write-Host "=====================" -ForegroundColor Cyan

    if ($null -eq $views -or $views.Length -eq 0) {
        Write-Host "No vaults found" -ForegroundColor Yellow
    }
    else {
        $vaultIndex = 1
        foreach ($view in $views) {
            Write-Host "$vaultIndex. Name: $($view.mbsVaultName)" -ForegroundColor Green
            Write-Host "   Server: $($view.mbsServerName)" -ForegroundColor Gray
            Write-Host "   Path: $($view.mbsPath)" -ForegroundColor Gray
            Write-Host ""
            $vaultIndex++
        }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
