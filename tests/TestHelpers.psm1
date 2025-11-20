<#
.SYNOPSIS
    Helper functions for PDM E2E tests

.DESCRIPTION
    Provides reusable functions for PDM vault operations during testing
#>

# Global test log path
$script:TestLogPath = ""

function Initialize-TestLogging {
    param([string]$LogPath)
    $script:TestLogPath = $LogPath
}

function Write-TestLog {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"

    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        "SUCCESS" { "Green" }
        "TESTPASS" { "Cyan" }
        "TESTFAIL" { "Magenta" }
        default { "White" }
    }

    Write-Host $logMessage -ForegroundColor $color
    if (-not [string]::IsNullOrEmpty($script:TestLogPath)) {
        Add-Content -Path $script:TestLogPath -Value $logMessage
    }
}

function Connect-PdmVault {
    param(
        [Parameter(Mandatory=$true)]
        [string]$VaultName
    )

    try {
        # Load PDM DLL
        $pdmDllPath = Join-Path $env:ProgramFiles "SOLIDWORKS Corp\SOLIDWORKS PDM\EPDM.Interop.epdm.dll"
        if (-not (Test-Path $pdmDllPath)) {
            throw "PDM DLL not found at $pdmDllPath"
        }

        $null = [System.Reflection.Assembly]::LoadFrom($pdmDllPath)
        Write-TestLog "Loaded PDM interop"

        # Create and login to vault
        $vault = New-Object -ComObject ConisioLib.EdmVault
        $vault.LoginAuto($VaultName, 0)
        Write-TestLog "Connected to vault: $VaultName" "SUCCESS"

        return $vault
    }
    catch {
        Write-TestLog "Failed to connect to vault: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Get-PdmFolder {
    param(
        [Parameter(Mandatory=$true)]
        $Vault,

        [Parameter(Mandatory=$true)]
        [string]$FolderPath,

        [Parameter(Mandatory=$false)]
        [bool]$CreateIfMissing = $false
    )

    try {
        # Handle root folder
        if ($FolderPath -eq "\") {
            return $Vault.RootFolder
        }

        # Try to get existing folder
        $folder = $null
        try {
            $folder = $Vault.GetFolderFromPath($FolderPath)
        }
        catch {
            # Folder doesn't exist
        }

        # Create if requested and missing
        if ($null -eq $folder -and $CreateIfMissing) {
            Write-TestLog "Creating folder: $FolderPath"
            $folder = $Vault.RootFolder.CreateFolderPath($FolderPath, 0)
            Start-Sleep -Milliseconds 200
        }

        return $folder
    }
    catch {
        Write-TestLog "Error getting folder $FolderPath : $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Add-FileToVault {
    param(
        [Parameter(Mandatory=$true)]
        $Folder,

        [Parameter(Mandatory=$true)]
        [string]$SourceFilePath
    )

    try {
        $fileName = [System.IO.Path]::GetFileName($SourceFilePath)
        Write-TestLog "Adding file: $fileName"

        # Check if file already exists in the folder
        try {
            $existingFile = $Folder.GetFile($fileName)
            if ($null -ne $existingFile) {
                Write-TestLog "File already exists in vault, deleting it first" "WARN"
                $Folder.DeleteFile(0, $existingFile.ID, $true)
                Start-Sleep -Seconds 3
                Write-TestLog "Deleted existing file successfully" "SUCCESS"
            }
        }
        catch {
            # File doesn't exist, that's fine
        }

        $Folder.AddFile(0, $SourceFilePath)
        Start-Sleep -Milliseconds 100

        Write-TestLog "File added successfully: $fileName" "SUCCESS"
        return $true
    }
    catch {
        Write-TestLog "Failed to add file: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Get-FileFromVault {
    param(
        [Parameter(Mandatory=$true)]
        $Vault,

        [Parameter(Mandatory=$true)]
        [string]$FilePath
    )

    try {
        $ppoRetParentFolder = $null
        $file = $Vault.GetFileFromPath($FilePath, [ref]$ppoRetParentFolder)
        return $file
    }
    catch {
        Write-TestLog "Failed to get file from vault at path $FilePath : $($_.Exception.Message)" "WARN"
        return $null
    }
}

function Rename-FileInVault {
    param(
        [Parameter(Mandatory=$true)]
        $File,

        [Parameter(Mandatory=$true)]
        [string]$NewName
    )

    try {
        Write-TestLog "Renaming file to: $NewName"
        $File.Rename(0, $NewName)
        Start-Sleep -Milliseconds 200
        Write-TestLog "File renamed successfully" "SUCCESS"
        return $true
    }
    catch {
        Write-TestLog "Failed to rename file: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Move-FileInVault {
    param(
        [Parameter(Mandatory=$true)]
        $File,

        [Parameter(Mandatory=$true)]
        $TargetFolder
    )

    try {
        $fileName = $File.Name
        Write-TestLog "Moving file $fileName to folder ID $($TargetFolder.ID)"
        $File.Move(0, $TargetFolder.ID)
        Start-Sleep -Milliseconds 200
        Write-TestLog "File moved successfully" "SUCCESS"
        return $true
    }
    catch {
        Write-TestLog "Failed to move file: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Remove-FileFromVault {
    param(
        [Parameter(Mandatory=$true)]
        $Folder,

        [Parameter(Mandatory=$true)]
        $File
    )

    try {
        $fileName = $File.Name
        Write-TestLog "Deleting file: $fileName"
        $Folder.DeleteFile($File.ID, 0, $true)
        Start-Sleep -Milliseconds 200
        Write-TestLog "File deleted successfully" "SUCCESS"
        return $true
    }
    catch {
        Write-TestLog "Failed to delete file: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Remove-FolderFromVault {
    param(
        [Parameter(Mandatory=$true)]
        $Folder
    )

    try {
        $folderName = $Folder.Name
        Write-TestLog "Deleting folder: $folderName"
        # IEdmFolder5.Delete expects (bDestroyPhysically, lHWndParent)
        $Folder.Delete(1, 0)  # 1 = destroy physically, 0 = no parent window
        Start-Sleep -Milliseconds 300
        Write-TestLog "Folder deleted successfully" "SUCCESS"
        return $true
    }
    catch {
        Write-TestLog "Failed to delete folder: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Copy-DirectoryToVault {
    param(
        [Parameter(Mandatory=$true)]
        [string]$SourcePath,

        [Parameter(Mandatory=$true)]
        $TargetFolder,

        [Parameter(Mandatory=$false)]
        [string]$FileExtensionFilter = $null,

        [Parameter(Mandatory=$false)]
        [bool]$PartsFirst = $false
    )

    try {
        if ($PartsFirst) {
            # STEP 1: Copy all .SLDPRT files first (parts before assemblies)
            Write-TestLog "STEP 1: Copying all .SLDPRT files first (dependency order)" "INFO"
            $result = Copy-DirectoryToVault-Internal -SourcePath $SourcePath -TargetFolder $TargetFolder -FileExtensionFilter ".SLDPRT"
            if (-not $result) { return $false }

            # STEP 2: Copy all .SLDASM files after parts
            Write-TestLog "STEP 2: Copying all .SLDASM files (after parts)" "INFO"
            $result = Copy-DirectoryToVault-Internal -SourcePath $SourcePath -TargetFolder $TargetFolder -FileExtensionFilter ".SLDASM"
            if (-not $result) { return $false }

            return $true
        } else {
            return Copy-DirectoryToVault-Internal -SourcePath $SourcePath -TargetFolder $TargetFolder -FileExtensionFilter $FileExtensionFilter
        }
    }
    catch {
        Write-TestLog "Failed to copy directory: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Copy-DirectoryToVault-Internal {
    param(
        [Parameter(Mandatory=$true)]
        [string]$SourcePath,

        [Parameter(Mandatory=$true)]
        $TargetFolder,

        [Parameter(Mandatory=$false)]
        [string]$FileExtensionFilter = $null
    )

    try {
        # Get all files in source directory (with filter if provided)
        $files = if ($FileExtensionFilter) {
            Get-ChildItem -Path $SourcePath -Filter "*$FileExtensionFilter" -File
        } else {
            Get-ChildItem -Path $SourcePath -File
        }

        foreach ($file in $files) {
            $result = Add-FileToVault -Folder $TargetFolder -SourceFilePath $file.FullName
            if (-not $result) {
                Write-TestLog "Failed to add file: $($file.Name)" "ERROR"
                return $false
            }
            Write-TestLog "Added: $($file.Name)" "SUCCESS"
            Start-Sleep -Milliseconds 300
        }

        # Recursively copy subdirectories
        $subdirs = Get-ChildItem -Path $SourcePath -Directory
        foreach ($subdir in $subdirs) {
            # Create subdirectory in vault
            $targetSubPath = "$($TargetFolder.LocalPath)\$($subdir.Name)"
            $targetSubFolder = Get-PdmFolder -Vault $TargetFolder.Vault -FolderPath $targetSubPath -CreateIfMissing $true

            if ($null -eq $targetSubFolder) {
                Write-TestLog "Failed to create subfolder: $($subdir.Name)" "ERROR"
                return $false
            }

            # Recursively copy subdirectory contents
            $result = Copy-DirectoryToVault-Internal -SourcePath $subdir.FullName -TargetFolder $targetSubFolder -FileExtensionFilter $FileExtensionFilter
            if (-not $result) {
                return $false
            }
        }

        return $true
    }
    catch {
        Write-TestLog "Failed to copy directory: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Get-AllFilesInVaultFolder {
    param(
        [Parameter(Mandatory=$true)]
        $Vault,

        [Parameter(Mandatory=$true)]
        [string]$FolderPath,

        [Parameter(Mandatory=$false)]
        [string]$FileExtensionFilter = $null
    )

    try {
        $allFiles = @()
        $folder = Get-PdmFolder -Vault $Vault -FolderPath $FolderPath

        if ($null -eq $folder) {
            return $allFiles
        }

        # Get files in this folder
        $folderPos = $folder.GetFirstFilePosition()
        while ($folderPos.IsNull -eq $false) {
            $file = $folder.GetNextFile($folderPos)
            if ($null -ne $file) {
                if (-not $FileExtensionFilter -or $file.Name.EndsWith($FileExtensionFilter, [StringComparison]::OrdinalIgnoreCase)) {
                    $allFiles += $file
                }
            }
        }

        # Get files in subdirectories
        $subfolderPos = $folder.GetFirstSubFolderPosition()
        while ($subfolderPos.IsNull -eq $false) {
            $subfolder = $folder.GetNextSubFolder($subfolderPos)
            if ($null -ne $subfolder) {
                $subfolderPath = $subfolder.LocalPath
                $subFiles = Get-AllFilesInVaultFolder -Vault $Vault -FolderPath $subfolderPath -FileExtensionFilter $FileExtensionFilter
                $allFiles += $subFiles
            }
        }

        return $allFiles
    }
    catch {
        Write-TestLog "Failed to get files from folder $FolderPath : $($_.Exception.Message)" "WARN"
        return @()
    }
}

function Wait-ForFileToExistInLocalView {
    param(
        [Parameter(Mandatory=$true)]
        [string]$FilePath,

        [Parameter(Mandatory=$false)]
        [int]$TimeoutSeconds = 30
    )

    $elapsed = 0
    while ($elapsed -lt $TimeoutSeconds) {
        if (Test-Path $FilePath) {
            return $true
        }
        Start-Sleep -Seconds 1
        $elapsed++
    }

    return $false
}

function Checkout-FileInVault {
    param(
        [Parameter(Mandatory=$true)]
        $File
    )

    try {
        if ($File.IsLocked) {
            Write-TestLog "File is already checked out"
            return $true
        }

        Write-TestLog "Checking out file: $($File.Name)"
        $File.LockFile($File.GetLocalFolderID(), 0)
        Start-Sleep -Milliseconds 200
        Write-TestLog "File checked out successfully" "SUCCESS"
        return $true
    }
    catch {
        Write-TestLog "Failed to check out file: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Checkin-FileInVault {
    param(
        [Parameter(Mandatory=$true)]
        $File,

        [Parameter(Mandatory=$false)]
        [string]$Comment = "Test check-in"
    )

    try {
        if (-not $File.IsLocked) {
            Write-TestLog "File is not checked out" "WARN"
            return $true
        }

        # Get the folder where the file is locked
        $lockedInFolderId = 0
        try {
            $lockedInFolderId = $File.LockedInFolderID
        }
        catch {
            # If we can't get the folder ID, try with 0 (current folder)
            $lockedInFolderId = 0
        }

        Write-TestLog "Checking in file: $($File.Name)"

        # Try to check in - use the simpler overload first (default behavior with reference tree)
        try {
            $File.UnlockFile($lockedInFolderId, $Comment)
            Write-TestLog "File checked in successfully" "SUCCESS"
            Start-Sleep -Milliseconds 200
            return $true
        }
        catch {
            $errorMsg = $_.Exception.Message
            Write-TestLog "First check-in attempt failed: $errorMsg" "WARN"

            # Try with explicit window handle (0) - might need UI interaction
            try {
                Write-TestLog "Retrying check-in with window handle..." "WARN"
                $File.UnlockFile($lockedInFolderId, $Comment, 0, 0)
                Write-TestLog "File checked in successfully (retry)" "SUCCESS"
                Start-Sleep -Milliseconds 200
                return $true
            }
            catch {
                $retryError = $_.Exception.Message
                Write-TestLog "Failed to check in file: First: $errorMsg; Retry: $retryError" "ERROR"
                return $false
            }
        }
    }
    catch {
        Write-TestLog "Failed to check in file: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

Export-ModuleMember -Function *
