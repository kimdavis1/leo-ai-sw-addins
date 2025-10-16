# LeoAI PDM Add-in Installation Guide

## Quick Installation

### Step 1: Install the MSI
You can use the pre-built MSI installer or build it yourself. [Prebuilt installers are available for download here.](https://github.com/kimdavis1/leo-ai-sw-addins/blob/main/SwPdmAddin/LeoAISetUp/LeoAISetUp.msi)

1. Run the `LeoAISetUp.msi` file as Administrator.
2. The add-in files will be installed to:
   - `C:\Program Files\LeoAISwPdmAddIn`
3. Follow the manual configuration steps below to complete the installation.

### Step 2: Add Task Add-in to PDM Vault
1. Open **PDM Administration** and connect to your vault.
2. Navigate to **Add-ins** in the vault tree.
3. Right-click on **Add-ins** and select **New Add-in**.
4. Browse to `C:\Program Files\LeoAISwPdmAddIn` and select the following files:
   - `LeoAISwPdmTaskAddIn.dll`
   - `LeoAICadDataClient.dll`
5. Click **Open** to register the task add-in.
6. Approve different windows.


### Step 3: Configure Task Host
1. In **PDM Administration**, right click on the vualt and choose **explore**.
2. In the PDM valut explorer, choose the **Tools** menu, and click on **Task Host Configuration**.
3. Click on the **prmit** check box for the Leo AI Sync Task.
4. Click **OK**.

### Step 4: Create and Assign Task
1. In **PDM Administration** relevant vualt droplist, Right click on **Tasks** and choose **New Task...**.
2. For **task name**, type in **Leo AI Sync Task**.
3. For **Add-in**, choose **Leo AI Sync Task** from the drop list.
4. For **Execute task as user**, choose **Admin** and type in password.
5. For **Number of retries on failure**, fill in 3.
6. Click on **Execution Method** tab (Or just **Next** button which should take you to the same window)
7. Choose the task host you configured in step 3.
8. Click **OK**.

### Step 5: Add Client Add-in to PDM Vault
1. Still in **PDM Administration**, right-click on **Add-ins** and select **New Add-in**.
2. Browse to `C:\Program Files\LeoAISwPdmAddIn` and select the following files:
   - `LeoAISwPdmAddIn.dll`
   - `LeoAICadDataClient.dll`
3. Click **Open** to register the client add-in.
4. Approve different windows.

### Step 6: Trigger Initial Sync
1. In the **Add-in**, left click on the **LeoAISwPdmAddIn** and click on Initiate complete sync button from menu.
2. Click on **Tasks**, click on **Task List** where you should see a task running for the initial sync.
4. Wait for the initial sync to complete.

### Step 7: Verification
After installation:
1. Check **PDM Administration** → **Add-ins** - you should see both:
   - **LeoAISolidWorksPDMAdddIn** (client add-in)
   - **Leo AI Sync Task** (task add-in)
2. Check **Task Host Computers** - your computer should be listed.
3. Check **Tasks** → **Leo AI Sync Task** - it should be assigned to your task host.

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
