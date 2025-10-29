using EPDM.Interop.epdm;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using LeoAISwPdmAddIn.ErrorTracking;

namespace LeoAISwPdmAddIn
{
    [ComVisible(true)]
    [Guid("5C9C2B58-C7E9-4052-9321-00433F32A479")]
    public class SwPdmAddinMain : IEdmAddIn5
    {
        // Command ID for Complete Sync menu item
        private const int CMD_COMPLETE_SYNC = 1001;

        // Client-side Leo AI client (for event-based sync)
        private LeoAICadDataClient.SecureApiClient _leoClient;
        private string _directoryId;
        private bool _isClientInitialized = false;
        private bool _isSentryInitialized = false;
        private readonly object _initLock = new object();
        public void GetAddInInfo(ref EdmAddInInfo poInfo, IEdmVault5 poVault, IEdmCmdMgr5 poCmdMgr)
        {
            try
            {
                LogFileWriter.LogDebug("GetAddInInfo method called");

                // Step 1: Always provide the basic Add-in info
                poInfo.mbsAddInName = "LeoAISolidWorksPDMAdddIn";
                poInfo.mbsCompany = "LeoAI.";
                poInfo.mbsDescription = "Your AI engineering design copilot";
                poInfo.mlAddInVersion = 1;
                poInfo.mlRequiredVersionMajor = 17;
                LogFileWriter.LogDebug("Basic add-in info provided.");

                // Step 2: Perform vault-specific initialization only once
                if (poVault == null)
                {
                    LogFileWriter.LogDebug("GetAddInInfo called without a vault context. Skipping vault-specific initialization.");
                    return;
                }

                string vaultName = poVault.Name;
                LogFileWriter.LogMessage($"GetAddInInfo executing for vault: '{vaultName}'");

                // Register hooks every time to ensure the add-in is responsive
                LogFileWriter.LogDebug("Registering event hooks...");

                // File events - each creates immediate task (no tracking)
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostAdd);        // File added to vault
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostUnlock);     // File checked in
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostUndoLock);   // Undo checkout
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostDelete);     // File deleted
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostMove);       // File moved
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostRename);     // File renamed
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostCopy);       // File copied

                // Folder events - each creates immediate task
                // NOTE: PostAddFolder and PostDeleteFolder are NOT registered because:
                // - PostAdd fires for each file when a folder is added
                // - PostDelete fires for each file when a folder is deleted
                // poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostAddFolder);    // DISABLED - redundant with PostAdd
                // poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostDeleteFolder); // DISABLED - redundant with PostDelete
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostMoveFolder);   // Folder moved
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostRenameFolder); // Folder renamed

                // Installation event for one-time setup
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_InstallAddIn);

                LogFileWriter.LogDebug("All event hooks have been registered.");

                // Add menu command for Complete Sync (only in Administration tool)
                poCmdMgr.AddCmd(
                    CMD_COMPLETE_SYNC,
                    "Initiate complete sync",
                    (int)EdmMenuFlags.EdmMenu_Administration,
                    "Synchronize all vault files with Leo AI server",
                    "Initiate complete sync"
                );
                LogFileWriter.LogDebug("Complete Sync menu command added (Administration tool only).");
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                LogFileWriter.LogError($"COM Exception in GetAddInInfo: HRESULT = 0x{ex.ErrorCode:X}, {ex.Message}");
                System.Windows.Forms.MessageBox.Show("HRESULT = 0x" + ex.ErrorCode.ToString("X") + ex.Message);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"General Exception in GetAddInInfo: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
                SentryErrorHandler.CaptureException(ex, new Dictionary<string, string>
                {
                    { "operation", "GetAddInInfo" },
                    { "vault", poVault?.Name ?? "unknown" }
                }, Sentry.SentryLevel.Fatal);
                System.Windows.Forms.MessageBox.Show(ex.Message);
            }
            finally
            {
                LogFileWriter.LogDebug("GetAddInInfo method finished execution.");
            }
        }

        public void OnCmd(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogDebug($"OnCmd method called for command {poCmd.meCmdType}");
            LogFileWriter.LogDebug($"OnCmd: Command type = {poCmd.meCmdType} (value: {(int)poCmd.meCmdType}), Data count = {ppoData?.Length ?? 0}");

            // Copy ref parameters to local variables for use in lambda expressions
            EdmCmd cmd = poCmd;
            EdmCmdData[] data = ppoData;

            // Log file information if available
            if (ppoData != null && ppoData.Length > 0)
            {
                for (int i = 0; i < ppoData.Length; i++)
                {
                    LogFileWriter.LogDebug($"File {i}: {ppoData[i].mbsStrData1}");
                }
            }

            // Check for custom menu commands
            if (poCmd.meCmdType == EdmCmdType.EdmCmd_Menu)
            {
                LogFileWriter.LogMessage($"Menu command received with ID: {poCmd.mlCmdID}");

                if (poCmd.mlCmdID == CMD_COMPLETE_SYNC)
                {
                    LogFileWriter.LogMessage("Complete Sync menu command triggered");

                    IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                    if (vault == null) return;

                    // Trigger complete sync by executing task on auth key file
                    string configPath = Path.Combine(vault.RootFolderPath, "LeoAI_TaskData", "LeoAuthKey.txt");
                    IEdmFolder5 folder;
                    IEdmFile5 configFile = vault.GetFileFromPath(configPath, out folder);

                    if (configFile == null)
                    {
                        LogFileWriter.LogError("LeoAuthKey.txt not found in vault - cannot trigger complete sync");
                        System.Windows.Forms.MessageBox.Show(
                            "LeoAuthKey.txt not found in vault. Please reinstall the add-in.",
                            "Complete Sync Error",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Error);
                        return;
                    }

                    // Execute task on config file (task will run complete sync)
                    LogFileWriter.LogMessage($"Triggering complete sync task on file: {configFile.Name}");
                    var cmdData = new EdmCmdData
                    {
                        mlObjectID1 = configFile.ID,
                        mlObjectID2 = folder.ID
                    };
                    ExecuteSyncTask(poCmd, new EdmCmdData[] { cmdData }, "CompleteSync");

                    return;
                }
            }

            switch (poCmd.meCmdType)
            {
                // File operations - process on client side (no metadata files)
                case EdmCmdType.EdmCmd_PostAdd:
                    LogFileWriter.LogMessage("PostAdd event - processing on client");
                    ProcessEventOnClient(cmd, data, "Add");
                    break;

                case EdmCmdType.EdmCmd_PostUnlock:
                    LogFileWriter.LogMessage("PostUnlock event - processing on client");
                    ProcessEventOnClient(cmd, data, "Upload");
                    break;

                case EdmCmdType.EdmCmd_PostUndoLock:
                    LogFileWriter.LogMessage("PostUndoLock event - processing on client");
                    ProcessEventOnClient(cmd, data, "Upload");
                    break;

                case EdmCmdType.EdmCmd_PostDelete:
                    LogFileWriter.LogMessage("PostDelete event - processing on client");
                    ProcessEventOnClient(cmd, data, "Delete");
                    break;

                case EdmCmdType.EdmCmd_PostMove:
                    LogFileWriter.LogMessage("PostMove event - processing on client");
                    ProcessEventOnClient(cmd, data, "Move");
                    break;

                case EdmCmdType.EdmCmd_PostRename:
                    LogFileWriter.LogMessage("PostRename event - processing on client");
                    ProcessEventOnClient(cmd, data, "Rename");
                    break;

                case EdmCmdType.EdmCmd_PostCopy:
                    LogFileWriter.LogMessage("PostCopy event - processing on client");
                    ProcessEventOnClient(cmd, data, "Copy");
                    break;

                // Folder operations - process on client side (no metadata files)
                case EdmCmdType.EdmCmd_PostAddFolder:
                    // COMMENTED OUT: Add events fire for each file in the folder already
                    // The file-level PostAdd events handle all files, so folder-level is redundant
                    LogFileWriter.LogMessage("PostAddFolder event - SKIPPED (files already added via PostAdd)");
                    break;

                case EdmCmdType.EdmCmd_PostDeleteFolder:
                    // COMMENTED OUT: Delete events fire for both files AND folder, causing duplicates
                    // The file-level PostDelete events handle all files in the folder already
                    LogFileWriter.LogMessage("PostDeleteFolder event - SKIPPED (files already deleted via PostDelete)");
                    break;

                case EdmCmdType.EdmCmd_PostMoveFolder:
                    LogFileWriter.LogMessage("PostMoveFolder event - processing on client");
                    ProcessEventOnClient(cmd, data, "MoveFolder");
                    break;

                case EdmCmdType.EdmCmd_PostRenameFolder:
                    LogFileWriter.LogMessage("PostRenameFolder event - processing on client");
                    ProcessEventOnClient(cmd, data, "RenameFolder");
                    break;

                case EdmCmdType.EdmCmd_InstallAddIn:
                    LogFileWriter.LogMessage("InstallAddIn event detected. Performing one-time initial data sync and creating persistent flag.");
                    IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                    if (vault != null)
                    {
                        // Extract data from COM object on main thread first
                        string vaultName = vault.Name;
                        string vaultRootPath = vault.RootFolderPath;

                        // Register this vault installation in registry for tracking
                        RegisterVaultInstallation(vaultName, vaultRootPath);

                        // Copy LeoAuthKey.txt to vault (NEW - for client-side config access)
                        CopyAuthConfigToVault(vault);

                        // Trigger complete sync by executing task on auth key file
                        LogFileWriter.LogMessage("Triggering initial complete sync after installation...");

                        string configPath = Path.Combine(vault.RootFolderPath, "LeoAI_TaskData", "LeoAuthKey.txt");
                        IEdmFolder5 folder;
                        IEdmFile5 configFile = vault.GetFileFromPath(configPath, out folder);

                        if (configFile != null && folder != null)
                        {
                            LogFileWriter.LogMessage($"Triggering complete sync task on file: {configFile.Name}");

                            var cmdData = new EdmCmdData
                            {
                                mlObjectID1 = configFile.ID,
                                mlObjectID2 = folder.ID
                            };

                            ExecuteSyncTask(poCmd, new EdmCmdData[] { cmdData }, "CompleteSync");
                            LogFileWriter.LogMessage("Complete sync task triggered successfully");
                        }
                        else
                        {
                            LogFileWriter.LogError("LeoAuthKey.txt not found in vault - cannot trigger initial complete sync");
                            LogFileWriter.LogError("You may need to manually trigger complete sync from the menu");
                        }
                    }
                    else
                    {
                        LogFileWriter.LogError("Failed to get vault context during add-in installation.");
                    }
                    break;

                default:
                    LogFileWriter.LogMessage($"Unhandled command type: {poCmd.meCmdType} (value: {(int)poCmd.meCmdType})");
                    break;
            }
            LogFileWriter.LogDebug($"OnCmd method finished for command {poCmd.meCmdType}");
        }

        #region Simplified Direct Task Creation (No Tracking)

        /// <summary>
        /// Creates an immediate task for file operations
        /// Each operation creates its own task file and triggers execution
        /// </summary>
        private void CreateImmediateTask(EdmCmd poCmd, EdmCmdData[] ppoData, string operationType)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                foreach (EdmCmdData cmdData in ppoData)
                {
                    string filePath = cmdData.mbsStrData1;
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    // Skip metadata folder files
                    if (filePath.Contains("\\LeoAI_TaskData\\"))
                    {
                        LogFileWriter.LogDebug($"Skipping metadata file: {filePath}");
                        continue;
                    }

                    // For move/rename/copy, get both paths from event data
                    string newPath = cmdData.mbsStrData2;

                    // Get actual file paths using PDM API for rename/move operations
                    // This ensures we get vault-relative paths correctly
                    string actualOldPath = filePath;
                    string actualNewPath = newPath;

                    if ((operationType == "Rename" || operationType == "Move") && cmdData.mlObjectID1 > 0)
                    {
                        try
                        {
                            LogFileWriter.LogMessage($"===File Rename/Move RAW data===");
                            LogFileWriter.LogMessage($"  mbsStrData1: '{filePath}'");
                            LogFileWriter.LogMessage($"  mbsStrData2: '{newPath}'");
                            LogFileWriter.LogMessage($"  mlObjectID1 (FileID): {cmdData.mlObjectID1}");
                            LogFileWriter.LogMessage($"  mlObjectID2 (FolderID): {cmdData.mlObjectID2}");

                            if (operationType == "Move")
                            {
                                // For Move: PDM provides both full paths in the event data
                                // mbsStrData1 = OLD full path
                                // mbsStrData2 = NEW full path
                                actualOldPath = filePath;
                                actualNewPath = newPath;
                                LogFileWriter.LogMessage($"Move operation - using event paths directly");
                                LogFileWriter.LogMessage($"  OLD path: '{actualOldPath}'");
                                LogFileWriter.LogMessage($"  NEW path: '{actualNewPath}'");
                            }
                            else // Rename
                            {
                                // For Rename: we need PDM API to get the full path
                                // Get file object by ID (more reliable than path)
                                IEdmFile5 file = (IEdmFile5)vault.GetObject(EdmObjectType.EdmObject_File, cmdData.mlObjectID1);
                                if (file != null)
                                {
                                    // Get parent folder from FolderID
                                    IEdmFolder5 parentFolder = (IEdmFolder5)vault.GetObject(EdmObjectType.EdmObject_Folder, cmdData.mlObjectID2);
                                    if (parentFolder != null)
                                    {
                                        // Get current file path (this is the NEW path after rename)
                                        actualNewPath = file.GetLocalPath(parentFolder.ID);
                                        LogFileWriter.LogMessage($"Current file path from PDM API: '{actualNewPath}'");

                                        // Reconstruct old path in same folder
                                        // If newPath (mbsStrData2) matches current filename, then filePath (mbsStrData1) is old name
                                        string currentFileName = Path.GetFileName(actualNewPath);
                                        if (!string.IsNullOrEmpty(newPath) && currentFileName.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            // mbsStrData1 is the old filename
                                            string parentPath = Path.GetDirectoryName(actualNewPath);
                                            actualOldPath = Path.Combine(parentPath, filePath);
                                            LogFileWriter.LogMessage($"Reconstructed OLD path for rename: '{actualOldPath}'");
                                        }
                                        else
                                        {
                                            // Fallback: assume filePath is already a full path
                                            actualOldPath = filePath;
                                            LogFileWriter.LogMessage($"Using event old path as-is: '{actualOldPath}'");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogFileWriter.LogDebug($"Could not get file from PDM API, using event paths: {ex.Message}");
                        }
                    }

                    // Convert absolute paths to vault-relative paths for storage in metadata
                    // This ensures task host can reconstruct paths using its own vault root
                    string relativeOldPath = GetVaultRelativePath(vault, actualOldPath);
                    string relativeNewPath = string.IsNullOrEmpty(actualNewPath) ? null : GetVaultRelativePath(vault, actualNewPath);

                    LogFileWriter.LogMessage($"Final file paths - Old: '{relativeOldPath}', New: '{relativeNewPath}'");

                    // Create operation metadata with relative paths
                    var operation = new OperationMetadata
                    {
                        Id = Guid.NewGuid().ToString(),
                        Operation = operationType,
                        OldPath = (operationType == "Move" || operationType == "Rename" || operationType == "Copy" || operationType == "Delete") ? relativeOldPath : null,
                        NewPath = (operationType == "Move" || operationType == "Rename" || operationType == "Copy") ? relativeNewPath : (operationType == "Delete" ? null : relativeOldPath),
                        FileID = cmdData.mlObjectID1,
                        FolderID = cmdData.mlObjectID2,
                        Status = "ready",
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                    };

                    LogFileWriter.LogMessage($"Creating {operationType} task for: {filePath} (relative: {relativeOldPath})");

                    // Create task file and execute
                    string taskFilePath = CreateTaskFileAndExecute(vault, operation);
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error creating immediate task: {ex.Message}");
                SentryErrorHandler.CaptureException(ex, new Dictionary<string, string>
                {
                    { "operation", "create_immediate_task" },
                    { "operation_type", operationType }
                });
            }
        }

        /// <summary>
        /// Creates an immediate task for folder operations
        /// For PDM folder events:
        /// - mbsStrData1 = OLD folder path (before rename/move)
        /// - mbsStrData2 = NEW folder name only (not full path, just the name)
        /// - mlObjectID1 = Folder ID
        ///
        /// IMPORTANT: Creates ONE task for the entire folder (not per file)
        /// The task host will iterate over all files in the folder
        /// </summary>
        private void CreateImmediateFolderTask(EdmCmd poCmd, EdmCmdData[] ppoData, string operationType)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                // For folder operations, ppoData should contain only ONE folder entry
                // We process only the first item and create ONE task for the entire folder
                if (ppoData == null || ppoData.Length == 0)
                {
                    LogFileWriter.LogWarning($"No folder data provided for {operationType}");
                    return;
                }

                // Take ONLY the first folder entry
                EdmCmdData cmdData = ppoData[0];

                // For folders, PDM provides:
                // mbsStrData1 = folder path (might be relative or absolute)
                // mbsStrData2 = NEW folder name (for rename) or destination path (for move)
                // mlObjectID1 = Folder ID
                string folderPath1 = cmdData.mbsStrData1;
                string folderPath2 = cmdData.mbsStrData2;
                int folderID = cmdData.mlObjectID1;

                LogFileWriter.LogMessage($"===Folder event RAW data===");
                LogFileWriter.LogMessage($"  mbsStrData1: '{folderPath1}'");
                LogFileWriter.LogMessage($"  mbsStrData2: '{folderPath2}'");
                LogFileWriter.LogMessage($"  mlObjectID1 (FolderID): {folderID}");

                // Get the folder object from PDM using the folder ID
                // This is more reliable than using the paths from event data
                IEdmFolder5 folder = (IEdmFolder5)vault.GetObject(EdmObjectType.EdmObject_Folder, folderID);
                if (folder == null)
                {
                    LogFileWriter.LogError($"Could not get folder object with ID: {folderID}");
                    return;
                }

                // Get the CURRENT folder path from PDM (this is the NEW path after rename/move)
                string currentFolderPath = folder.LocalPath;
                LogFileWriter.LogMessage($"Current folder path from PDM API: '{currentFolderPath}'");

                // For Rename/Move: we need to figure out the OLD path
                // For AddFolder: we just need the current path
                string oldFolderPath = null;
                string newFolderPath = currentFolderPath;

                if (operationType == "RenameFolder" || operationType == "MoveFolder")
                {
                    // For rename: mbsStrData2 is the NEW name, so we can deduce the old name
                    // Current folder name is in currentFolderPath
                    // OLD name should be: parent path + old name from (currentName vs newName from mbsStrData2)

                    string currentFolderName = Path.GetFileName(currentFolderPath);
                    string parentPath = Path.GetDirectoryName(currentFolderPath);

                    LogFileWriter.LogMessage($"Current folder name: '{currentFolderName}'");
                    LogFileWriter.LogMessage($"Parent path: '{parentPath}'");

                    // For rename: if mbsStrData2 matches current name, then mbsStrData1 is the old name
                    if (!string.IsNullOrEmpty(folderPath2) && currentFolderName.Equals(folderPath2, StringComparison.OrdinalIgnoreCase))
                    {
                        // mbsStrData1 is the old folder name
                        oldFolderPath = Path.Combine(parentPath, folderPath1);
                        LogFileWriter.LogMessage($"Reconstructed OLD path: '{oldFolderPath}'");
                    }
                    else
                    {
                        // Fallback: assume mbsStrData1 is old path, mbsStrData2 is new name
                        if (!string.IsNullOrEmpty(folderPath1))
                        {
                            oldFolderPath = folderPath1;
                        }
                        else
                        {
                            LogFileWriter.LogWarning($"Could not determine old folder path for {operationType}");
                            oldFolderPath = currentFolderPath; // Fallback
                        }
                    }
                }
                else
                {
                    // For AddFolder, we only need the current path
                    oldFolderPath = null;
                }

                // Convert to vault-relative paths
                string relativeOldPath = oldFolderPath != null ? GetVaultRelativePath(vault, oldFolderPath) : null;
                string relativeNewPath = GetVaultRelativePath(vault, newFolderPath);

                LogFileWriter.LogMessage($"Final paths - Old: '{relativeOldPath}', New: '{relativeNewPath}'");

                // Create operation metadata for folder with relative paths
                var operation = new OperationMetadata
                {
                    Id = Guid.NewGuid().ToString(),
                    Operation = operationType,
                    OldPath = (operationType == "MoveFolder" || operationType == "RenameFolder") ? relativeOldPath : null,
                    NewPath = (operationType == "MoveFolder" || operationType == "RenameFolder") ? relativeNewPath : relativeNewPath,
                    FileID = cmdData.mlObjectID1,  // Folder ID
                    FolderID = cmdData.mlObjectID2,
                    Status = "ready",
                    Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                };

                LogFileWriter.LogMessage($"Creating ONE {operationType} task for entire folder: OldPath={operation.OldPath}, NewPath={operation.NewPath}");

                // Create ONE task file for the entire folder and execute
                // The task host (LeoAiSyncTask) will enumerate all files in the folder
                string taskFilePath = CreateTaskFileAndExecute(vault, operation);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error creating immediate folder task: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Creates a task file with single operation and executes it immediately
        /// </summary>
        private string CreateTaskFileAndExecute(IEdmVault5 vault, OperationMetadata operation)
        {
            try
            {
                // Create task file with single operation
                var taskOperations = new List<OperationMetadata> { operation };
                string taskFilePath = CreateTaskMetadataFile(vault, taskOperations);

                // Execute task immediately
                LogFileWriter.LogMessage($"Executing task for operation: {operation.Operation}");
                var poCmd = new EdmCmd { mpoVault = vault };
                ExecuteSyncTaskWithMetadataFile(poCmd, taskFilePath);

                return taskFilePath;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error creating and executing task: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Task File Management

        /// <summary>
        /// Creates a task-specific metadata file with only ready operations
        /// </summary>
        private string CreateTaskMetadataFile(IEdmVault5 vault, List<OperationMetadata> readyOperations)
        {
            try
            {
                string vaultRoot = vault.RootFolderPath;
                string metadataFolder = Path.Combine(vaultRoot, "LeoAI_TaskData");

                if (!Directory.Exists(metadataFolder))
                {
                    Directory.CreateDirectory(metadataFolder);
                }

                // Create unique task file name
                string taskId = Guid.NewGuid().ToString("N").Substring(0, 8);
                string timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
                string taskFileName = $"Task_{timestamp}_{taskId}.json";
                string taskFilePath = Path.Combine(metadataFolder, taskFileName);

                // Create task-specific metadata
                var taskMetadata = new UserSessionMetadata
                {
                    SessionId = $"Task_{taskId}",
                    Operations = readyOperations,
                    LastModified = DateTimeOffset.Now.ToUnixTimeSeconds()
                };

                // Write task metadata file
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(taskMetadata, Newtonsoft.Json.Formatting.Indented);
                using (FileStream fs = new FileStream(taskFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                LogFileWriter.LogMessage($"Created task metadata file: {taskFileName}");

                // Add to vault and check in
                AddFileToVault(vault, taskFilePath);

                return taskFilePath;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error creating task metadata file: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Executes sync task by passing metadata file to the task
        /// </summary>
        private void ExecuteSyncTaskWithMetadataFile(EdmCmd poCmd, string metadataFilePath)
        {
            try
            {
                IEdmVault11 vault = (IEdmVault11)poCmd.mpoVault;
                IEdmFolder5 folder;
                IEdmFile5 metadataFile = vault.GetFileFromPath(metadataFilePath, out folder);

                if (metadataFile == null)
                {
                    LogFileWriter.LogError($"Metadata file not found in vault: {metadataFilePath}");
                    return;
                }

                // Create EdmCmdData for the metadata file
                EdmCmdData metadataData = new EdmCmdData();
                metadataData.mlObjectID1 = metadataFile.ID;
                metadataData.mlObjectID2 = folder.ID;

                // Execute task with metadata file
                ExecuteSyncTask(poCmd, new EdmCmdData[] { metadataData }, "ProcessMetadata");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error executing sync task with metadata file: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds file to vault if not already present
        /// </summary>
        private void AddFileToVault(IEdmVault5 vault, string filePath)
        {
            try
            {
                string folderPath = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileName(filePath);

                IEdmFolder5 folder = vault.GetFolderFromPath(folderPath);
                if (folder == null)
                {
                    // Create the folder if it doesn't exist
                    string parentPath = Path.GetDirectoryName(folderPath);
                    string folderName = Path.GetFileName(folderPath);

                    IEdmFolder5 parentFolder = vault.GetFolderFromPath(parentPath);
                    if (parentFolder != null)
                    {
                        parentFolder.AddFolder(0, folderName);
                        LogFileWriter.LogMessage($"Added folder to vault: {folderName}");
                        folder = vault.GetFolderFromPath(folderPath);
                    }
                }

                if (folder == null)
                {
                    LogFileWriter.LogError($"Failed to get/create folder: {folderPath}");
                    return;
                }

                // Check if file already in vault
                IEdmFile5 file = null;
                IEdmFolder5 fileFolder = null;

                try
                {
                    file = vault.GetFileFromPath(filePath, out fileFolder);
                }
                catch
                {
                    // File not in vault yet
                }

                if (file == null)
                {
                    LogFileWriter.LogMessage($"Starting add file to vault process in path: {filePath}");
                    folder.AddFile(0, filePath);
                    LogFileWriter.LogMessage($"File added to vault");

                    // Get file and check it in
                    file = vault.GetFileFromPath(filePath, out fileFolder);
                    if (file != null)
                    {
                        LogFileWriter.LogMessage($"Checking in file: {fileName}");
                        file.UnlockFile(0, "Added by Leo AI PDM Add-in", (int)EdmUnlockFlag.EdmUnlock_IgnoreReferences);
                        LogFileWriter.LogMessage($"File checked in successfully: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error adding file to vault: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        #endregion

        #region Task Execution Methods (Client-Side)

        /// <summary>
        /// Executes a sync task on the task host (server) by calling IEdmTaskMgr.RunTask()
        /// This is a simplified method that just triggers the task - no metadata creation here
        /// </summary>
        private void ExecuteSyncTask(EdmCmd poCmd, EdmCmdData[] ppoData, string operation, Dictionary<string, string> additionalData = null)
        {
            LogFileWriter.LogMessage($"=== ExecuteSyncTask called - Operation: {operation}, Files: {ppoData?.Length ?? 0} ===");

            try
            {
                IEdmVault11 vault = (IEdmVault11)poCmd.mpoVault;
                LogFileWriter.LogMessage($"Getting IEdmTaskMgr from vault...");

                IEdmTaskMgr taskMgr = (IEdmTaskMgr)vault.CreateUtility(EdmUtility.EdmUtil_TaskMgr);
                if (taskMgr == null)
                {
                    LogFileWriter.LogError("Failed to create IEdmTaskMgr - CreateUtility returned null");
                    return;
                }

                LogFileWriter.LogMessage("IEdmTaskMgr created successfully");

                // Get all configured tasks
                EdmTaskInfo[] tasks = taskMgr.GetTasks();
                LogFileWriter.LogMessage($"Retrieved {tasks?.Length ?? 0} tasks from vault");

                if (tasks == null || tasks.Length == 0)
                {
                    LogFileWriter.LogError("No tasks found in vault. Please configure 'Leo AI Sync Task' in PDM Administration.");
                    return;
                }

                // Find the Leo AI Sync Task by name
                EdmTaskInfo syncTask = new EdmTaskInfo();
                bool foundTask = false;

                foreach (EdmTaskInfo task in tasks)
                {
                    LogFileWriter.LogMessage($"Found task: {task.mbsTaskName} (ID: {task.mlTaskID})");
                    if (task.mbsTaskName == "Leo AI Sync Task")
                    {
                        syncTask = task;
                        foundTask = true;
                        break;
                    }
                }

                if (!foundTask || syncTask.mlTaskID == 0)
                {
                    LogFileWriter.LogError("Leo AI Sync Task not found. Please configure the task in PDM Administration.");
                    LogFileWriter.LogMessage($"Available tasks: {string.Join(", ", tasks.Select(t => t.mbsTaskName))}");
                    return;
                }

                LogFileWriter.LogMessage($"Found sync task: {syncTask.mbsTaskName} (ID: {syncTask.mlTaskID})");

                // Convert ppoData to EdmSelItem2[] for task file list
                LogFileWriter.LogMessage("Building file selection list...");
                List<EdmSelItem2> fileList = new List<EdmSelItem2>();

                foreach (EdmCmdData data in ppoData)
                {
                    EdmSelItem2 item = new EdmSelItem2();
                    item.mlID = data.mlObjectID1;        // File/Document ID
                    item.mlParentID = data.mlObjectID2;  // Parent Folder ID
                    item.meType = EdmObjectType.EdmObject_File;
                    item.mlVersion = 0;                  // Use latest version
                    fileList.Add(item);

                    LogFileWriter.LogMessage($"Added file to selection: ID={item.mlID}, ParentID={item.mlParentID}");
                }

                LogFileWriter.LogMessage($"File selection built: {fileList.Count} items");
                LogFileWriter.LogMessage($"Calling RunTask() with {fileList.Count} items...");
                taskMgr.RunTask(syncTask, fileList.ToArray(), 0);

                LogFileWriter.LogMessage($"=== ExecuteSyncTask: Task execution initiated successfully ===");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"ExecuteSyncTask failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        #endregion

        #region Client Initialization

        /// <summary>
        /// Ensures Leo AI client is initialized for client-side event processing
        /// </summary>
        private void EnsureClientInitialized(IEdmVault5 vault)
        {
            if (_isClientInitialized) return;

            lock (_initLock)
            {
                if (_isClientInitialized) return;

                try
                {
                    LogFileWriter.LogMessage("Initializing Leo AI client on client side...");

                    string vaultConfigPath = Path.Combine(vault.RootFolderPath, "LeoAI_TaskData", "LeoAuthKey.txt");

                    // Use PDM API to get local copy if file exists in vault
                    IEdmFolder5 configFolder;
                    IEdmFile5 configFile = vault.GetFileFromPath(vaultConfigPath, out configFolder);

                    if (configFile != null && configFolder != null)
                    {
                        LogFileWriter.LogMessage($"Auth key file found in vault - getting local copy: {vaultConfigPath}");

                        // Get local copy to ensure file is synced to this machine
                        try
                        {
                            // GetFileCopy signature: GetFileCopy(int hwnd, ref object version, ref object pathOrFolderID, int flags, string newName)
                            // Folder paths must be terminated by a backslash (per PDM API documentation)
                            string destFolder = Path.GetDirectoryName(vaultConfigPath);
                            LogFileWriter.LogMessage($"Auth key GetFileCopy - vaultConfigPath: '{vaultConfigPath}'");
                            LogFileWriter.LogMessage($"Auth key GetFileCopy - destFolder: '{destFolder}'");

                            string folderPathWithBackslash = destFolder.TrimEnd('\\') + "\\";
                            LogFileWriter.LogMessage($"Auth key GetFileCopy - calling with folder path: '{folderPathWithBackslash}'");

                            // Parameters: version (0 for latest), path or folder ID (path with backslash), flags, new name (empty)
                            object version = 0;
                            object folderPath = folderPathWithBackslash;
                            configFile.GetFileCopy(0, ref version, ref folderPath, (int)EdmGetFlag.EdmGet_Simple, "");
                            LogFileWriter.LogMessage("Auth key file retrieved successfully");
                        }
                        catch (Exception getEx)
                        {
                            LogFileWriter.LogWarning($"Failed to get local copy of auth key (might already be local): {getEx.Message}");
                        }
                    }
                    else
                    {
                        LogFileWriter.LogWarning("Auth key file not found in vault - will try fallback locations");
                    }

                    // Create client from vault config (will fall back to installation folder if not in vault)
                    _leoClient = LeoAICadDataClient.SecureApiClient.CreateFromStandardLocations(vaultConfigPath);

                    // Get/create directory
                    string directoryPath = $"swpdm/{vault.Name}";
                    _directoryId = SharedSyncOperations.GetOrCreateDirectoryId(_leoClient, directoryPath).Result;

                    _isClientInitialized = true;
                    LogFileWriter.LogMessage($"Client initialized successfully - DirectoryId: {_directoryId}");

                    // Initialize Sentry error tracking
                    if (!_isSentryInitialized)
                    {
                        SentryErrorHandler.Initialize(vault.Name, "Production");
                        SentryErrorHandler.SetUser(System.Environment.UserName, vault.Name);
                        _isSentryInitialized = true;
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to initialize Leo AI client: {ex.Message}");
                    // Capture to Sentry if available
                    SentryErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "client_initialization" },
                        { "vault", vault.Name }
                    });
                    // Don't throw - allow PDM to continue, just log the error
                }
            }
        }

        /// <summary>
        /// Processes file operation events directly on client side (no metadata files)
        /// </summary>
        private void ProcessEventOnClient(EdmCmd cmd, EdmCmdData[] data, string operationType)
        {
            IEdmVault5 vault = null;
            try
            {
                vault = cmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                foreach (EdmCmdData cmdData in data)
                {
                    string filePath = cmdData.mbsStrData1;
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    // Skip metadata folder files
                    if (filePath.Contains("\\LeoAI_TaskData\\"))
                    {
                        LogFileWriter.LogDebug($"Skipping metadata file: {filePath}");
                        continue;
                    }

                    // Run async operation without blocking PDM
                    // IMPORTANT: Initialize client INSIDE Task.Run to avoid STA thread deadlock with Descope auth
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Ensure client is initialized on thread pool thread (not PDM STA thread)
                            EnsureClientInitialized(vault);

                            if (!_isClientInitialized)
                            {
                                LogFileWriter.LogWarning($"Client not initialized - cannot process {operationType} event");
                                return;
                            }

                            var operation = CreateOperationMetadata(operationType, cmdData, vault);
                            await ProcessOperationAsync(vault, operation);
                        }
                        catch (Exception ex)
                        {
                            LogFileWriter.LogError($"Failed to process {operationType} on {filePath}: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error in ProcessEventOnClient: {ex.Message}");
                SentryErrorHandler.CaptureException(ex, new Dictionary<string, string>
                {
                    { "operation", "ProcessEventOnClient" },
                    { "operation_type", operationType },
                    { "vault", vault?.Name ?? "unknown" }
                });
            }
        }

        /// <summary>
        /// Creates operation metadata from PDM event data
        /// </summary>
        private OperationMetadata CreateOperationMetadata(string operationType, EdmCmdData cmdData, IEdmVault5 vault)
        {
            // For PostAdd events:
            //   mbsStrData1 = vault path (where file was added TO)
            //   mbsStrData2 = local/source path (where it came FROM) or empty
            // For Upload/PostUnlock events:
            //   mbsStrData1 = vault path
            //   mbsStrData2 = empty
            // For Move/Rename events:
            //   mbsStrData1 = old path
            //   mbsStrData2 = new path

            string oldPath, newPath;

            if (operationType == "Add")
            {
                // For Add: file is being added TO vault
                // NewPath = where it's going (vault path in mbsStrData1)
                // OldPath = where it came from (local path in mbsStrData2, or empty)
                newPath = cmdData.mbsStrData1;  // vault path
                oldPath = cmdData.mbsStrData2;  // local/source path (may be empty)
            }
            else if (string.IsNullOrEmpty(cmdData.mbsStrData2))
            {
                // Upload/PostUnlock: only mbsStrData1 is set (vault path)
                newPath = cmdData.mbsStrData1;
                oldPath = cmdData.mbsStrData1;
            }
            else
            {
                // Move/Rename: mbsStrData1 = old, mbsStrData2 = new
                oldPath = cmdData.mbsStrData1;
                newPath = cmdData.mbsStrData2;
            }

            return new OperationMetadata
            {
                Id = Guid.NewGuid().ToString(),
                Operation = operationType,
                OldPath = oldPath,
                NewPath = newPath,
                FileID = cmdData.mlObjectID1,
                FolderID = cmdData.mlObjectID2,
                Status = "ready",
                Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
            };
        }

        /// <summary>
        /// Processes a single operation asynchronously on client side
        /// TODO: Currently just logs - will implement actual sync when OperationExecution class is complete
        /// </summary>
        private async Task ProcessOperationAsync(IEdmVault5 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"[CLIENT] Processing operation: {operation.Operation} for file: {operation.OldPath ?? operation.NewPath}");

            try
            {
                IEdmVault11 vault11 = (IEdmVault11)vault;
                string operationJson = Newtonsoft.Json.JsonConvert.SerializeObject(operation);

                SharedSyncOperations.OnOperationRun(vault11, operationJson, _leoClient, _directoryId);

                LogFileWriter.LogMessage($"[CLIENT] Operation completed: {operation.Operation}");
            }
            catch (Exception ex)
            {
                // Unwrap AggregateException to get the real error
                Exception realException = ex;
                if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
                {
                    realException = aggEx.InnerException ?? aggEx.InnerExceptions[0];
                    LogFileWriter.LogError($"[CLIENT] Operation failed (AggregateException unwrapped): {realException.Message}");
                }
                else
                {
                    LogFileWriter.LogError($"[CLIENT] Operation failed: {ex.Message}");
                }

                LogFileWriter.LogError($"[CLIENT] Stack trace: {realException.StackTrace}");

                // Log inner exceptions if present
                if (realException.InnerException != null)
                {
                    LogFileWriter.LogError($"[CLIENT] Inner exception: {realException.InnerException.Message}");
                    LogFileWriter.LogError($"[CLIENT] Inner exception stack trace: {realException.InnerException.StackTrace}");
                }

                // Capture exception to Sentry
                SentryErrorHandler.CaptureException(realException, new Dictionary<string, string>
                {
                    { "operation", "ProcessOperationAsync" },
                    { "operation_type", operation?.Operation ?? "unknown" },
                    { "file_path", operation?.NewPath ?? operation?.OldPath ?? "unknown" },
                    { "vault", vault?.Name ?? "unknown" }
                });
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Registry Helpers

        /// <summary>
        /// Registers vault installation in the registry for tracking
        /// </summary>
        private void RegisterVaultInstallation(string vaultName, string vaultPath)
        {
            try
            {
                string registryPath = @"SOFTWARE\LeoAI\SwPdmAddIn\Vaults";

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath))
                {
                    if (key != null)
                    {
                        key.SetValue(vaultName, vaultPath);
                        LogFileWriter.LogMessage($"Registered vault installation: {vaultName} -> {vaultPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to register vault installation: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies LeoAuthKey.txt (encrypted) from installation folder to vault for client-side access
        /// Supports both .txt (new encrypted format) and .json (legacy) files
        /// </summary>
        private void CopyAuthConfigToVault(IEdmVault5 vault)
        {
            try
            {
                // Try .txt (encrypted) first, fallback to .json (legacy)
                string sourceConfigPathTxt = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.txt");
                string sourceConfigPathJson = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.json");

                string sourceConfigPath;
                string targetFileName;

                if (File.Exists(sourceConfigPathTxt))
                {
                    sourceConfigPath = sourceConfigPathTxt;
                    targetFileName = "LeoAuthKey.txt";
                    LogFileWriter.LogMessage($"Found encrypted auth file: {sourceConfigPath}");
                }
                else if (File.Exists(sourceConfigPathJson))
                {
                    sourceConfigPath = sourceConfigPathJson;
                    targetFileName = "LeoAuthKey.json";
                    LogFileWriter.LogMessage($"Found legacy auth file: {sourceConfigPath}");
                }
                else
                {
                    LogFileWriter.LogWarning($"LeoAuthKey.txt (or .json) not found in installation folder");
                    LogFileWriter.LogWarning("Skipping config copy to vault. Client-side events will use installation folder.");
                    return;
                }

                string vaultLeoFolder = Path.Combine(vault.RootFolderPath, "LeoAI_TaskData");
                string vaultConfigPath = Path.Combine(vaultLeoFolder, targetFileName);

                // Ensure folder exists
                if (!Directory.Exists(vaultLeoFolder))
                {
                    Directory.CreateDirectory(vaultLeoFolder);
                    LogFileWriter.LogMessage($"Created vault Leo folder: {vaultLeoFolder}");
                }

                // Check if file already exists in vault
                IEdmFolder5 existingFolder;
                IEdmFile5 existingFile = vault.GetFileFromPath(vaultConfigPath, out existingFolder);

                // Read source file content (encrypted or plaintext - just copy as-is)
                string configContent = File.ReadAllText(sourceConfigPath);
                LogFileWriter.LogMessage($"Read source config from: {sourceConfigPath}");

                if (existingFile != null)
                {
                    // File exists - need to replace it because encryption key changed
                    // Each build has a different encryption key, so old encrypted file is useless
                    LogFileWriter.LogMessage($"{targetFileName} already exists in vault - replacing with new version (encryption key changed)");

                    // Check out the file so we can modify it
                    try
                    {
                        existingFile.LockFile(existingFolder.ID, 0, (int)EdmLockFlag.EdmLock_Simple);
                        LogFileWriter.LogMessage($"Checked out {targetFileName} for modification");
                    }
                    catch (Exception lockEx)
                    {
                        LogFileWriter.LogWarning($"Could not check out file (may already be checked out): {lockEx.Message}");
                        // Continue anyway - file might already be unlocked
                    }

                    // Write new content to the local copy
                    using (FileStream fs = new FileStream(vaultConfigPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        writer.Write(configContent);
                        writer.Flush();
                    }
                    LogFileWriter.LogMessage($"Updated local copy of {targetFileName}");

                    // Check in the modified file
                    try
                    {
                        existingFile.UnlockFile(0, "Updated encryption key - Leo AI PDM Add-in", (int)EdmUnlockFlag.EdmUnlock_IgnoreReferences);
                        LogFileWriter.LogMessage($"Checked in updated {targetFileName}");
                    }
                    catch (Exception unlockEx)
                    {
                        LogFileWriter.LogError($"Could not check in file: {unlockEx.Message}");
                        throw;
                    }
                }
                else
                {
                    // File doesn't exist - create new one
                    LogFileWriter.LogMessage($"{targetFileName} doesn't exist in vault - creating new file");

                    // Write to vault folder using FileStream
                    using (FileStream fs = new FileStream(vaultConfigPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        writer.Write(configContent);
                        writer.Flush();
                    }
                    LogFileWriter.LogMessage($"Wrote config to vault folder: {vaultConfigPath}");

                    // Add to vault and check in
                    AddFileToVault(vault, vaultConfigPath);
                    LogFileWriter.LogMessage($"{targetFileName} added to vault successfully");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to copy auth config to vault: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                LogFileWriter.LogWarning("Installation will continue. Client will use config from installation folder.");
                // Don't throw - installation can proceed with config in install folder
            }
        }

        #endregion

        #region Path Utilities

        /// <summary>
        /// Converts an absolute file path to a vault-relative path
        /// This allows the task host to reconstruct the full path using its own vault root
        /// </summary>
        private string GetVaultRelativePath(IEdmVault5 vault, string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            string vaultRoot = vault.RootFolderPath;

            // If path starts with vault root, extract the relative portion
            if (absolutePath.StartsWith(vaultRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = absolutePath.Substring(vaultRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relativePath;
            }

            // Path doesn't start with vault root - might be from different local view
            // Try to extract vault-relative path by finding vault name
            string vaultName = vault.Name;
            int vaultNameIndex = absolutePath.IndexOf(vaultName, StringComparison.OrdinalIgnoreCase);

            if (vaultNameIndex >= 0)
            {
                // Extract everything after "vaultName\"
                int relativeStartIndex = vaultNameIndex + vaultName.Length;
                if (relativeStartIndex < absolutePath.Length && (absolutePath[relativeStartIndex] == '\\' || absolutePath[relativeStartIndex] == '/'))
                {
                    relativeStartIndex++; // Skip the separator
                }

                if (relativeStartIndex < absolutePath.Length)
                {
                    string relativePath = absolutePath.Substring(relativeStartIndex);
                    LogFileWriter.LogMessage($"GetVaultRelativePath: Extracted '{relativePath}' from '{absolutePath}' using vault name");
                    return relativePath;
                }
            }

            // If we can't extract relative path, return as-is (shouldn't happen in normal operation)
            LogFileWriter.LogMessage($"GetVaultRelativePath: Could not extract relative path from '{absolutePath}' - returning as-is");
            return absolutePath;
        }

        #endregion
    }
}
