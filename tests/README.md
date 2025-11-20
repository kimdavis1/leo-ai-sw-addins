# Leo AI PDM Add-in E2E Test Suite

Comprehensive end-to-end test suite for validating Leo AI PDM add-in functionality. Tests all file and folder operations with server-side verification.

## Overview

This test suite validates:
- **File Operations**: Add, Modify (checkout/checkin), Rename, Move, Delete
- **Folder Operations**: Add folders with multiple files
- **File Types**: SLDPRT (parts), SLDASM (assemblies with dependencies), PDF (documents)
- **Paths**: Root-level folders and nested subfolders
- **Server Verification**: Polls Leo API until status reaches COMPLETE or IN_ERROR

## Prerequisites

1. **SOLIDWORKS PDM** installed and configured
2. **PDM Vault** with user credentials
3. **Leo AI Add-in** installed in the vault
4. **Leo API Access**: API key JSON file and directory ID
5. **.NET Framework 4.8** (for LeoApiVerifier)
6. **PowerShell 5.1+**

## Test Suite Components

### Core Scripts

- **E2ETests.ps1** - Main test orchestrator that runs all test scenarios
- **TestHelpers.psm1** - Reusable PowerShell module with PDM operation functions
- **config.template.json** - Configuration template for test parameters

### Helper Tools

- **LeoApiVerifier/** - C# console application for verifying file status on Leo server
  - Polls Leo API to check if files reach COMPLETE/IN_ERROR status
  - Returns exit codes: 0=COMPLETE, 1=IN_ERROR, 2=TIMEOUT, 3=ERROR

### Test Assets

- **assets/test.pdf** - Simple test PDF file
- **assets/multiLayerSWAssembly/** - User-provided SOLIDWORKS assembly files (required)
- **assets/GenerateTestPDF.ps1** - Script to regenerate test.pdf if needed

## Setup Instructions

### Step 1: Configure Test Parameters

Copy the configuration template and fill in your vault-specific values:

```powershell
Copy-Item tests\config.template.json tests\config.json
```

Edit `tests\config.json` with your values:

```json
{
  "vaultName": "YourVaultName",
  "vaultRootPath": "C:\\path\\to\\vault\\root",
  "testRootFolder": "\\E2E_Tests",
  "apiKeyJsonPath": "C:\\path\\to\\your\\api-key.json",
  "timeoutMinutes": 5,
  "pollIntervalSeconds": 10,
  "cleanupAfterTests": true,
  "testFileTypes": {
    "sldprt": true,
    "sldasm": true,
    "pdf": true
  },
  "testOperations": {
    "fileAdd": true,
    "fileModify": true,
    "fileRename": true,
    "fileMove": true,
    "fileDelete": true,
    "folderAdd": true,
    "folderRename": true,
    "folderMove": true,
    "folderDelete": true
  }
}
```

**Configuration Parameters:**

- `vaultName` - Name of your PDM vault
- `vaultRootPath` - Physical path to vault root folder (e.g., `C:\VaultName`)
- `testRootFolder` - Root folder path for test files (will be created/cleaned)
- `apiKeyJsonPath` - Path to JSON file containing `{"apiKey":"...", "projectId":"..."}`
- `timeoutMinutes` - Max time to wait for server processing (default: 5)
- `pollIntervalSeconds` - Interval between API status checks (default: 10)
- `cleanupAfterTests` - Delete test folder after completion (default: true)
- `testFileTypes` - Enable/disable specific file type tests
- `testOperations` - Enable/disable specific operation tests

**Note:** The directory ID is automatically resolved from the vault root path by querying the Leo API. The test tool gets the machine's MAC address and looks up all synced directories, then finds the one matching your vault path.

### Step 2: Provide SOLIDWORKS Assembly Files

Place SOLIDWORKS part and assembly files in `tests/assets/multiLayerSWAssembly/`:

```
tests/
  assets/
    multiLayerSWAssembly/
      PartA.sldprt
      PartB.sldprt
      Assembly.sldasm
```

These files will be used to test assembly operations with dependencies.

### Step 3: Build LeoApiVerifier

Build the C# API verification tool:

```powershell
cd tests\LeoApiVerifier
dotnet build -c Release
cd ..\..
```

Verify the executable exists: `tests\LeoApiVerifier\bin\Release\net48\LeoApiVerifier.exe`

The build will happen automatically during test run if not already built.

### Step 4: Verify PDM Connection

Ensure you can connect to the vault:

```powershell
Import-Module .\tests\TestHelpers.psm1
$vault = Connect-PdmVault -VaultName "YourVaultName"
$vault.Name  # Should display vault name
$vault.Logout()
```

## Running the Tests

Execute the full test suite:

```powershell
.\tests\E2ETests.ps1
```

Or specify a custom config file:

```powershell
.\tests\E2ETests.ps1 -ConfigPath ".\tests\my-config.json"
```

### Test Execution Flow

1. **Initialization**
   - Loads configuration from config.json
   - Builds LeoApiVerifier if needed
   - Connects to PDM vault
   - Cleans previous test folder if exists

2. **Test Scenarios** (configurable via config.json)
   - TEST 1: Add PDF file to root folder
   - TEST 2: Add SLDPRT file to subfolder
   - TEST 3: Rename PDF file
   - TEST 4: Move PDF file to subfolder
   - TEST 5: Modify file (checkout/checkin)
   - TEST 6: Delete file
   - TEST 7: Add folder with multiple files

3. **Server Verification**
   - Each test calls LeoApiVerifier.exe
   - Polls Leo API every N seconds (configurable)
   - Waits until status is COMPLETE or IN_ERROR
   - Times out after configured minutes

4. **Cleanup**
   - Deletes test folder if `cleanupAfterTests: true`
   - Generates test summary report
   - Outputs detailed results with timing

## Test Results

### Output Locations

- **Console** - Color-coded test results (Green=PASS, Red=FAIL, Yellow=SKIP)
- **Log File** - `tests\results\TestRun_YYYYMMDD_HHMMSS.log`

### Exit Codes

- **0** - All tests passed
- **1** - One or more tests failed

### Sample Output

```
[2025-01-15 10:30:00] [INFO] === Leo AI PDM Add-in E2E Test Suite ===
[2025-01-15 10:30:01] [INFO] Configuration: .\tests\config.json
[2025-01-15 10:30:01] [INFO] Log file: .\tests\results\TestRun_20250115_103000.log

[2025-01-15 10:30:02] [INFO] ========================================
[2025-01-15 10:30:02] [INFO] TEST 1: Add PDF file to root folder
[2025-01-15 10:30:02] [INFO] ========================================
[2025-01-15 10:30:03] [SUCCESS] File added successfully: e2e_test.pdf
[2025-01-15 10:30:06] [SUCCESS] Server verification: COMPLETE
[2025-01-15 10:30:06] [TESTPASS] TEST PASSED: Add PDF file to root folder
[2025-01-15 10:30:06] [INFO] Duration: 4.23 seconds

...

[2025-01-15 10:35:00] [INFO] ========================================
[2025-01-15 10:35:00] [INFO] TEST SUMMARY
[2025-01-15 10:35:00] [INFO] ========================================
[2025-01-15 10:35:00] [INFO] Total Tests: 7
[2025-01-15 10:35:00] [SUCCESS] Passed: 7
[2025-01-15 10:35:00] [INFO] Failed: 0
[2025-01-15 10:35:00] [WARN] Skipped: 0
```

## Test Coverage

### File Operations

| Operation | File Types | Locations | Server Verification |
|-----------|-----------|-----------|---------------------|
| Add | PDF, SLDPRT | Root, Subfolder | Yes |
| Modify | PDF | Subfolder | Yes |
| Rename | PDF | Root | Yes |
| Move | PDF | Root → Subfolder | Yes |
| Delete | PDF | Subfolder | Vault only |

### Folder Operations

| Operation | Description | Server Verification |
|-----------|-------------|---------------------|
| Add | Create folder with 3 files | No (bulk test) |

### Assembly Testing

When SLDASM files are provided in assets, tests will:
- Upload assemblies with dependencies
- Verify parent-child relationships
- Check all components reach COMPLETE status

## Helper Functions (TestHelpers.psm1)

### Vault Connection

```powershell
Connect-PdmVault -VaultName "MyVault"
```

### Folder Operations

```powershell
Get-PdmFolder -Vault $vault -FolderPath "\Folder1\Subfolder2" -CreateIfMissing $true
Remove-FolderFromVault -Folder $folder
```

### File Operations

```powershell
Add-FileToVault -Folder $folder -SourceFilePath "C:\temp\test.pdf"
Get-FileFromVault -Vault $vault -FilePath "\Folder\file.pdf"
Rename-FileInVault -File $file -NewName "newname.pdf"
Move-FileInVault -File $file -TargetFolder $destFolder
Remove-FileFromVault -Folder $folder -File $file
Checkout-FileInVault -File $file
Checkin-FileInVault -File $file -Comment "Test modification"
```

### Logging

```powershell
Initialize-TestLogging -LogPath ".\results\test.log"
Write-TestLog "Message" "INFO"    # White
Write-TestLog "Warning" "WARN"    # Yellow
Write-TestLog "Error" "ERROR"     # Red
Write-TestLog "Success" "SUCCESS" # Green
```

## LeoApiVerifier Usage

Command-line interface for server status verification:

```powershell
.\LeoApiVerifier.exe <apiKeyJsonPath> <vaultRootPath> <relativeFilePath> <timeoutMinutes> <pollIntervalSeconds>
```

**Example:**

```powershell
.\LeoApiVerifier.exe "C:\keys\api.json" "C:\test_pro" "E2E_Tests/test.pdf" 5 10
```

**Exit Codes:**
- `0` - File reached COMPLETE status
- `1` - File reached IN_ERROR status
- `2` - Timeout (file did not complete within timeout period)
- `3` - Error (API failure, invalid config, etc.)

**API Key JSON Format:**

```json
{
  "apiKey": "your-api-key-here",
  "projectId": "your-project-id-here"
}
```

## Troubleshooting

### PDM Connection Issues

**Error:** "Failed to connect to vault"

**Solutions:**
- Verify vault name is correct
- Ensure PDM is installed: Check `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\`
- Try logging into vault via PDM Explorer first
- Run PowerShell as administrator

### LeoApiVerifier Build Issues

**Error:** "LeoApiVerifier.exe not found"

**Solutions:**
- Build manually: `dotnet build tests\LeoApiVerifier -c Release`
- Verify .NET Framework 4.8 is installed
- Check for build errors in output

### Server Verification Timeout

**Error:** "TIMEOUT - File did not reach COMPLETE status within 5 minutes"

**Solutions:**
- Increase `timeoutMinutes` in config.json
- Check Leo server status (may be processing backlog)
- Verify file actually uploaded to Leo (check Leo web UI)
- Check add-in logs: `C:\Program Files\LeoAISwPdmAddIn\logs\`

### File Not Found Errors

**Error:** "File not found for rename/move/delete test"

**Solutions:**
- Previous test may have failed, leaving vault in unexpected state
- Set `cleanupAfterTests: false` to inspect vault state after failure
- Manually clean test folder in PDM Explorer and re-run

### Assembly Test Skipped

**Warning:** "SKIPPED: No .sldprt files found in assets/multiLayerSWAssembly"

**Solution:**
- Add SOLIDWORKS part/assembly files to `tests\assets\multiLayerSWAssembly\`
- Ensure files are actual SOLIDWORKS files, not placeholders

### Permission Denied

**Error:** "Access denied" or "Cannot delete file"

**Solutions:**
- Ensure you have permission to create/delete files in vault
- Check if files are locked by another user
- Verify PDM user has admin rights (for folder deletion)

### COM Errors

**Error:** "COM object error 0x80070005"

**Solutions:**
- Run PowerShell as administrator
- Restart PDM services: `net stop "SolidWorks PDM"` then `net start "SolidWorks PDM"`
- Log out all PDM users and retry

## Extending the Tests

### Adding New Test Scenarios

Add new test blocks to `E2ETests.ps1`:

```powershell
Invoke-Test -Name "Your new test name" -TestCode {
    # Your test logic here
    # Return $true for pass, $false for fail

    # Example: Test folder rename
    $folder = Get-PdmFolder -Vault $vault -FolderPath "\TestFolder"
    $result = Rename-FolderInVault -Folder $folder -NewName "RenamedFolder"

    if ($result) {
        return $true
    }
    return $false
}
```

### Testing Custom File Types

Update `IsProcessableFile()` in `SharedSyncOperations.cs` to include your file type, then add test block:

```powershell
if ($config.testFileTypes.dwg) {
    Invoke-Test -Name "Add DWG file" -TestCode {
        # Your DWG test logic
    }
}
```

### Modifying Verification Behavior

Edit `LeoApiVerifier/Program.cs` to change:
- Status codes checked (currently COMPLETE/IN_ERROR)
- Polling logic
- Output format
- Error handling

Rebuild after changes: `dotnet build -c Release`

## Architecture Notes

### Test Isolation

Each test operates on unique file/folder names to prevent interference:
- PDF files use unique names: `e2e_test.pdf`, `e2e_test_renamed.pdf`
- Folders are created per-test: `Parts`, `Documents`, `BulkTest`
- Root test folder (`E2E_Tests`) is cleaned before and after run

### Parallel Execution

Tests currently run sequentially. For parallel execution:
- Remove dependencies between tests (each test uses unique files)
- Use PowerShell workflows or background jobs
- Ensure `uploadedFiles` tracking in add-in handles concurrent operations (uses ConcurrentDictionary)

### API Rate Limiting

LeoApiVerifier polls at configurable intervals (default: 10 seconds):
- Adjust `pollIntervalSeconds` to reduce API load
- Increase `timeoutMinutes` for large files/assemblies
- Consider implementing exponential backoff for production use

## Continuous Integration

### Running in CI/CD

Example GitHub Actions workflow:

```yaml
name: PDM E2E Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v2

      - name: Setup config
        run: |
          Copy-Item tests\config.template.json tests\config.json
          # Update config.json with secrets

      - name: Run tests
        run: .\tests\E2ETests.ps1

      - name: Upload logs
        if: always()
        uses: actions/upload-artifact@v2
        with:
          name: test-logs
          path: tests\results\*.log
```

**Note:** Requires PDM vault accessible from CI environment (VPN or cloud vault).

## Support

For issues or questions:
- Check add-in logs: `C:\Program Files\LeoAISwPdmAddIn\logs\`
- Review test logs in `tests\results\`
- Verify vault connectivity via PDM Explorer
- Contact Leo AI support with test logs and vault configuration

## Version History

- **v1.0** - Initial release with core file/folder operations
  - File operations: Add, Modify, Rename, Move, Delete
  - Folder operations: Add
  - Server verification via LeoApiVerifier
  - Support for PDF, SLDPRT, SLDASM file types
