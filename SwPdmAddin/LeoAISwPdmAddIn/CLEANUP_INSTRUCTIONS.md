# Leo AI PDM Add-in Cleanup Instructions

## Overview
The Leo AI PDM Add-in uses a standalone uninstaller to handle cleanup when the add-in is removed from a vault. This approach ensures reliable cleanup even when PDM's built-in uninstall events don't fire properly.

## Cleanup Methods

### 1. Standalone Uninstaller (Primary Method)
The recommended approach for cleaning up Leo AI data when removing the add-in from vaults:

#### Interactive Mode:
```bash
LeoAIStandaloneUninstaller.exe
```

#### Silent Mode:
```bash
LeoAIStandaloneUninstaller.exe /silent
```

### 2. How It Works
1. **Vault Detection**: The uninstaller detects which PDM vaults currently have the add-in installed
2. **Registry Tracking**: It compares this with vaults that previously had the add-in installed (tracked in registry)
3. **Smart Cleanup**: Only performs cleanup for vaults that no longer have the add-in installed
4. **Preservation**: Leaves data intact for vaults that still have the add-in active

## What Gets Cleaned Up

### Server-Side Data:
- All synced directories for this machine
- All files within those directories
- Associated metadata

### Local Data:
- Session tracking data
- Cache files

### Registry Data:
- Vault installation tracking (`HKEY_CURRENT_USER\SOFTWARE\LeoAI\PDM-AddIn\InstalledVaults`)

## Troubleshooting

### When to Use the Standalone Uninstaller
Run the standalone uninstaller in these scenarios:

1. **After removing add-in from PDM Admin**: Always run after using "Remove" in PDM Administration
2. **Multiple vault cleanup**: When the add-in was removed from some vaults but not others
3. **Failed uninstall**: If the normal PDM uninstall process didn't complete successfully
4. **Orphaned data**: When you suspect Leo AI data remains after add-in removal

### Authentication Issues
If cleanup fails due to authentication issues:
1. Ensure `auth.json` exists in the add-in directory
2. Verify the file contains valid authentication credentials
3. Check network connectivity to Leo AI servers

### Partial Cleanup
If cleanup is only partially successful:
1. Check the console output for specific error messages
2. Re-run the standalone uninstaller
3. Manually verify server-side data through Leo AI dashboard

## Log Files
Cleanup operations are logged to help troubleshoot issues:
- Standalone uninstaller logs to console output
- Server communication errors are displayed in real-time

## Manual Server Cleanup
If all automated cleanup methods fail, you can manually clean up server-side data:
1. Access the Leo AI Admin Dashboard
2. Navigate to Synced Directories
3. Remove directories associated with the problematic machine/vault

## Best Practices
To ensure smooth cleanup operations:
1. Always run the standalone uninstaller after removing the add-in from PDM Admin
2. Ensure network connectivity when running the uninstaller
3. Keep authentication files accessible during cleanup
4. Run the uninstaller on each machine where the add-in was installed 