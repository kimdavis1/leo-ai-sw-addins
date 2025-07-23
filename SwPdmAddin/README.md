# LeoAI PDM Add-in Installation Guide

## Quick Installation

### Step 1: Install the MSI
You can use the pre-built MSI installer or build it yourself. [Prebuilt installers are available for download here.](https://github.com/kimdavis1/leo-ai-sw-addins/blob/main/SwPdmAddin/LeoAISetUp/LeoAISetUp.msi)

1. Run the `LeoAISetUp.msi` file as Administrator.
2. The add-in and all required files will be installed to:
   - `C:\Program Files\LeoAISwPdmAddIn`
3. The installer will automatically register the add-in with your PDM vault(s). No further manual steps are required.

### Step 2: Verification
After installation:
1. Open PDM Administrator and connect to your vault.
2. Navigate to Add-ins in the vault tree. The LeoAI add-in should be visible under your vault's add-ins.

---

## Troubleshooting

### Installation Issues
- Run the MSI as Administrator from a command prompt if a normal run fails.
- Check Windows Event Logs for detailed error messages.

### Add-in Not Loading
- Ensure the correct vault is selected during installation (if prompted).
- Check PDM Administrator for add-in registration.

---

## Support Notes
- DLL files and configuration are stored in `C:\Program Files\LeoAISwPdmAddIn`
- For advanced configuration, use the PDM Administrator interface to manually install an addin in a vault.

---

## File Locations Summary
| Component         | Location                                         |
|-------------------|-------------------------------------------------|
| Installation Dir  | C:\Program Files\LeoAISwPdmAddIn                |
| LoadAddIn Tool    | C:\Program Files\LeoAISwPdmAddIn\LoadAddIn.exe  |

---

## For Developers: Building from Source

If you want to build the solution yourself, follow these instructions:

### Prerequisites
Before you begin, ensure you have the following:
1. Visual Studio 2022 (with C# development capabilities)
2. HeatWave Extension for VS2022  
   - Available from FireGiant in Visual Studio Extension Manager
3. Administrative privileges on the development machine
4. PDM Administrator access for vault configuration

### Step 1: Install HeatWave Extension
1. Open Visual Studio 2022
2. Go to Extensions → Manage Extensions
3. Search for "HeatWave for VS2022"
4. Install the extension by FireGiant (if not already installed)
5. Restart Visual Studio if prompted

### Step 2: Open the Solution
1. Navigate to the solution file: `LeoAISwPdmAddIn.sln`
2. Open the solution in Visual Studio
3. The Solution Explorer should show the following projects:
   - LeoAICadDataClient
   - LeoAISetUp
   - LeoAISwPdmAddIn
   - LoadAddIn

### Step 3: Build the Solution
1. Set the solution configuration to `Release`
2. Select `64` as the platform
3. Right-click on the `LeoAISetUp` project in Solution Explorer
4. Select Build or Rebuild
5. The build process will:
   - Compile all referenced projects
   - Generate the MSI installer
   - Place the output in the Release folder

### Step 4: Locate the MSI File
After a successful build, find the generated MSI file in:
```
[Solution Directory]\LeoAISetUp\bin\x64\Release\en-US
```

### Build Errors
- Ensure the HeatWave extension is properly installed.
- Verify all project references are resolved.

---

## Required Reference DLLs
The following files from `SwPdmAddin/SWPDMReferences/` are required:
- EPDM.Interop.EPDMResultCode.dll
- EPDM.Interop.epdm.dll

---

This guide provides comprehensive instructions for both end users and developers. Installation is streamlined and automatic after running the MSI installer. For advanced scenarios or troubleshooting, refer to the sections above.
