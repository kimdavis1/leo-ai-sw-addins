# Logging Configuration for Leo AI PDM Add-in

## Overview

The Leo AI PDM Add-in supports configurable logging levels to help with debugging while keeping production logs clean. By default, only ERROR level messages are logged to avoid overwhelming the log files.

## Log Levels

The add-in supports four log levels (from most to least verbose):

1. **DEBUG** - Most verbose, shows all internal operations and method calls
2. **INFO** - Shows important operational information and events  
3. **WARNING** - Shows warning messages that don't halt operation
4. **ERROR** - Shows only error messages (DEFAULT)

## Configuration

### Environment Variable

Set the `LEO_LOG_LEVEL` environment variable to control logging verbosity:

```cmd
# For debugging (most verbose)
set LEO_LOG_LEVEL=DEBUG

# For operational monitoring
set LEO_LOG_LEVEL=INFO

# For warnings and errors only
set LEO_LOG_LEVEL=WARNING

# For errors only (default behavior)
set LEO_LOG_LEVEL=ERROR
```

### PowerShell
```powershell
# Set for current session
$env:LEO_LOG_LEVEL = "DEBUG"

# Set persistently for user
[Environment]::SetEnvironmentVariable("LEO_LOG_LEVEL", "DEBUG", "User")

# Set persistently for machine (requires admin)
[Environment]::SetEnvironmentVariable("LEO_LOG_LEVEL", "DEBUG", "Machine")
```

### System Environment Variables (Windows)
1. Open System Properties → Advanced → Environment Variables
2. Add new variable:
   - Variable name: `LEO_LOG_LEVEL`
   - Variable value: `DEBUG` (or `INFO`, `WARNING`, `ERROR`)

## Default Behavior

- **If `LEO_LOG_LEVEL` is not set**: Only ERROR messages are logged
- **If `LEO_LOG_LEVEL` is set to invalid value**: Defaults to ERROR level
- **If environment variable cannot be read**: Defaults to ERROR level

## Log File Location

The add-in writes to two log files:

1. **PDM Add-in logs**: `%TEMP%\Logging\LeoAISWPDMAddIn_Logfile.log`
   - Contains PDM-specific operations (file events, vault operations, etc.)

2. **API Client logs**: `%TEMP%\Logging\LeoAICadDataClient_Logfile.log`
   - Contains API communication logs (authentication, file uploads, server responses)

Typical paths:
- `C:\Users\{Username}\AppData\Local\Temp\Logging\LeoAISWPDMAddIn_Logfile.log`
- `C:\Users\{Username}\AppData\Local\Temp\Logging\LeoAICadDataClient_Logfile.log`

Both log files respect the same `LEO_LOG_LEVEL` environment variable.

## Customer Debugging

To enable debug logging on a customer computer:

1. **Temporary (current session only)**:
   ```cmd
   set LEO_LOG_LEVEL=DEBUG
   ```

2. **Persistent (survives reboots)**:
   ```cmd
   setx LEO_LOG_LEVEL DEBUG
   ```

3. **To disable debug logging**:
   ```cmd
   setx LEO_LOG_LEVEL ERROR
   ```
   Or simply delete the environment variable.

## What Each Level Shows

### ERROR (Default)
- Authentication failures
- COM exceptions
- File operation failures
- Network errors
- Critical initialization failures

### WARNING
- All ERROR messages plus:
- Authentication warnings
- File processing warnings
- Cache inconsistencies

### INFO  
- All WARNING messages plus:
- PDM events (file check-in, move, delete, etc.)
- Vault initialization
- File sync operations
- Authentication success

### DEBUG (Most Verbose)
- All INFO messages plus:
- Method entry/exit traces
- Event hook registrations
- File processing details
- Cache operations
- Environment variable readings
- Internal state changes

## Restart Requirements

Changes to the `LEO_LOG_LEVEL` environment variable require:
1. Restarting SOLIDWORKS PDM
2. Or restarting the PDM vault view
3. The add-in will pick up the new log level on next initialization

## Example Usage

```cmd
# Enable debug logging
set LEO_LOG_LEVEL=DEBUG

# Restart SOLIDWORKS PDM
# Reproduce the issue
# Check log files at:
# - %TEMP%\Logging\LeoAISWPDMAddIn_Logfile.log
# - %TEMP%\Logging\LeoAICadDataClient_Logfile.log

# Disable debug logging when done
set LEO_LOG_LEVEL=ERROR
```

## Troubleshooting

If logging doesn't change:
1. Verify the environment variable is set correctly: `echo %LEO_LOG_LEVEL%`
2. Restart SOLIDWORKS PDM completely
3. Check that the add-in is loading (should see "Log level initialized to: X" message)
4. Verify write permissions to the temp directory 