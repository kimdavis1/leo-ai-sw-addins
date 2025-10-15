using EPDM.Interop.epdm;
using LeoAICadDataClient;
using LeoAICadDataClient.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LeoAISwPdmAddIn
{
    /// <summary>
    /// Authentication configuration model for Leo AI
    /// </summary>
    public class LeoAuthConfig
    {
        public string ApiKey { get; set; }
        public string ProjectId { get; set; }
    }

    /// <summary>
    /// Represents a single file operation with its status
    /// </summary>
    public class OperationMetadata
    {
        public string Id { get; set; }
        public string Operation { get; set; }  // "Rename", "Move", "Upload", "Delete", "CompleteSync"
        public string OldPath { get; set; }
        public string NewPath { get; set; }
        public int FileID { get; set; }
        public int FolderID { get; set; }
        public string Status { get; set; }     // "in-work" or "ready"
        public long Timestamp { get; set; }
        public Dictionary<string, string> AdditionalData { get; set; }

        public OperationMetadata()
        {
            Id = Guid.NewGuid().ToString();
            AdditionalData = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Per-user session metadata file
    /// </summary>
    public class UserSessionMetadata
    {
        public string SessionId { get; set; }
        public List<OperationMetadata> Operations { get; set; }
        public long LastModified { get; set; }

        public UserSessionMetadata()
        {
            Operations = new List<OperationMetadata>();
        }
    }

    [ComVisible(true)]
    [Guid("5C9C2B58-C7E9-4052-9321-00433F32A479")]
    public class SwPdmAddinMain : IEdmAddIn5
    {
        // Command ID for Complete Sync menu item
        private const int CMD_COMPLETE_SYNC = 1001;
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
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostAddFolder);    // Folder added
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostDeleteFolder); // Folder deleted
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

                    // Create a "CompleteSync" operation
                    var completeSyncOp = new OperationMetadata
                    {
                        Id = Guid.NewGuid().ToString(),
                        Operation = "CompleteSync",
                        OldPath = null,
                        NewPath = null,
                        FileID = 0,
                        FolderID = 0,
                        Status = "ready",
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                    };

                    // Create task file and execute immediately
                    string taskFilePath = CreateTaskFileAndExecute(vault, completeSyncOp);
                    LogFileWriter.LogMessage($"CompleteSync task created and executed: {Path.GetFileName(taskFilePath)}");
                    return;
                }
            }

            switch (poCmd.meCmdType)
            {
                // File operations - create immediate task for each
                case EdmCmdType.EdmCmd_PostAdd:
                    LogFileWriter.LogMessage("PostAdd event - creating Add task");
                    CreateImmediateTask(cmd, data, "Add");
                    break;

                case EdmCmdType.EdmCmd_PostUnlock:
                    LogFileWriter.LogMessage("PostUnlock event - creating Upload task");
                    CreateImmediateTask(cmd, data, "Upload");
                    break;

                case EdmCmdType.EdmCmd_PostUndoLock:
                    LogFileWriter.LogMessage("PostUndoLock event - creating Upload task");
                    CreateImmediateTask(cmd, data, "Upload");
                    break;

                case EdmCmdType.EdmCmd_PostDelete:
                    LogFileWriter.LogMessage("PostDelete event - creating Delete task");
                    CreateImmediateTask(cmd, data, "Delete");
                    break;

                case EdmCmdType.EdmCmd_PostMove:
                    LogFileWriter.LogMessage("PostMove event - creating Move task");
                    CreateImmediateTask(cmd, data, "Move");
                    break;

                case EdmCmdType.EdmCmd_PostRename:
                    LogFileWriter.LogMessage("PostRename event - creating Rename task");
                    CreateImmediateTask(cmd, data, "Rename");
                    break;

                case EdmCmdType.EdmCmd_PostCopy:
                    LogFileWriter.LogMessage("PostCopy event - creating Copy task");
                    CreateImmediateTask(cmd, data, "Copy");
                    break;

                // Folder operations - create immediate task for each
                case EdmCmdType.EdmCmd_PostAddFolder:
                    LogFileWriter.LogMessage("PostAddFolder event - creating AddFolder task");
                    CreateImmediateFolderTask(cmd, data, "AddFolder");
                    break;

                case EdmCmdType.EdmCmd_PostDeleteFolder:
                    LogFileWriter.LogMessage("PostDeleteFolder event - creating DeleteFolder task");
                    CreateImmediateFolderTask(cmd, data, "DeleteFolder");
                    break;

                case EdmCmdType.EdmCmd_PostMoveFolder:
                    LogFileWriter.LogMessage("PostMoveFolder event - creating MoveFolder task");
                    CreateImmediateFolderTask(cmd, data, "MoveFolder");
                    break;

                case EdmCmdType.EdmCmd_PostRenameFolder:
                    LogFileWriter.LogMessage("PostRenameFolder event - creating RenameFolder task");
                    CreateImmediateFolderTask(cmd, data, "RenameFolder");
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

                        // Perform initial sync using new immediate task approach
                        LogFileWriter.LogMessage("Creating CompleteSync task for initial vault sync...");
                        var completeSyncOp = new OperationMetadata
                        {
                            Id = Guid.NewGuid().ToString(),
                            Operation = "CompleteSync",
                            OldPath = null,
                            NewPath = null,
                            FileID = 0,
                            FolderID = 0,
                            Status = "ready",
                            Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                        };
                        string taskFilePath = CreateTaskFileAndExecute(vault, completeSyncOp);
                        LogFileWriter.LogMessage($"CompleteSync task created for installation: {Path.GetFileName(taskFilePath)}");
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

                    // For move/rename/copy, get both paths
                    string newPath = cmdData.mbsStrData2;

                    // Convert absolute paths to vault-relative paths for storage in metadata
                    // This ensures task host can reconstruct paths using its own vault root
                    string relativeOldPath = GetVaultRelativePath(vault, filePath);
                    string relativeNewPath = string.IsNullOrEmpty(newPath) ? null : GetVaultRelativePath(vault, newPath);

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
            }
        }

        /// <summary>
        /// Creates an immediate task for folder operations
        /// </summary>
        private void CreateImmediateFolderTask(EdmCmd poCmd, EdmCmdData[] ppoData, string operationType)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                foreach (EdmCmdData cmdData in ppoData)
                {
                    // For folders, the path info might be in different fields
                    string folderPath = cmdData.mbsStrData1;
                    string newPath = cmdData.mbsStrData2;

                    if (string.IsNullOrEmpty(folderPath))
                        continue;

                    // Convert absolute paths to vault-relative paths for storage in metadata
                    string relativeOldPath = GetVaultRelativePath(vault, folderPath);
                    string relativeNewPath = string.IsNullOrEmpty(newPath) ? null : GetVaultRelativePath(vault, newPath);

                    // Create operation metadata for folder with relative paths
                    var operation = new OperationMetadata
                    {
                        Id = Guid.NewGuid().ToString(),
                        Operation = operationType,
                        OldPath = (operationType == "MoveFolder" || operationType == "RenameFolder") ? relativeOldPath : null,
                        NewPath = (operationType == "MoveFolder" || operationType == "RenameFolder") ? relativeNewPath : relativeOldPath,
                        FileID = cmdData.mlObjectID1,
                        FolderID = cmdData.mlObjectID2,
                        Status = "ready",
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                    };

                    LogFileWriter.LogMessage($"Creating {operationType} task for folder: {folderPath} (relative: {relativeOldPath})");

                    // Create task file and execute
                    string taskFilePath = CreateTaskFileAndExecute(vault, operation);
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error creating immediate folder task: {ex.Message}");
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

        #region Folder Event Handling

        /// <summary>
        /// Processes folder rename/move events by finding first file in folder as dummy for RunTask
        /// </summary>
        private void ProcessFolderEvent(EdmCmd poCmd, EdmCmdData[] folderData, string operation)
        {
            try
            {
                IEdmVault11 vault = (IEdmVault11)poCmd.mpoVault;

                foreach (EdmCmdData folder in folderData)
                {
                    int folderID = folder.mlObjectID1;
                    string oldFolderName = folder.mbsStrData1;
                    string newFolderName = folder.mbsStrData2;

                    LogFileWriter.LogMessage($"Processing folder {operation}: {oldFolderName} -> {newFolderName}, FolderID: {folderID}");

                    // Get folder object
                    IEdmFolder5 edmFolder = (IEdmFolder5)vault.GetObject(EdmObjectType.EdmObject_Folder, folderID);
                    if (edmFolder == null)
                    {
                        LogFileWriter.LogError($"Could not get folder object with ID: {folderID}");
                        continue;
                    }

                    // Get first file in folder to use as dummy for RunTask
                    IEdmPos5 pos = edmFolder.GetFirstFilePosition();
                    if (pos.IsNull)
                    {
                        LogFileWriter.LogMessage($"Folder is empty, using vault root file as dummy");
                        // Use file from vault root as fallback
                        IEdmFolder5 rootFolder = vault.RootFolder;
                        pos = rootFolder.GetFirstFilePosition();
                        if (pos.IsNull)
                        {
                            LogFileWriter.LogError("Cannot process folder event: vault is completely empty");
                            continue;
                        }
                        IEdmFile5 rootFile = rootFolder.GetNextFile(pos);
                        EdmCmdData dummyData = new EdmCmdData();
                        dummyData.mlObjectID1 = rootFile.ID;
                        dummyData.mlObjectID2 = rootFolder.ID;

                        var folderEventData = new Dictionary<string, string>();
                        folderEventData["OldPaths"] = oldFolderName;
                        folderEventData["NewPaths"] = newFolderName;
                        folderEventData["IsFolder"] = "true";

                        ExecuteSyncTask(poCmd, new EdmCmdData[] { dummyData }, operation, folderEventData);
                    }
                    else
                    {
                        IEdmFile5 firstFile = edmFolder.GetNextFile(pos);
                        EdmCmdData dummyData = new EdmCmdData();
                        dummyData.mlObjectID1 = firstFile.ID;
                        dummyData.mlObjectID2 = folderID;

                        var folderEventData = new Dictionary<string, string>();
                        folderEventData["OldPaths"] = oldFolderName;
                        folderEventData["NewPaths"] = newFolderName;
                        folderEventData["IsFolder"] = "true";

                        LogFileWriter.LogMessage($"Using dummy file: {firstFile.Name} (ID: {firstFile.ID}) from folder ID: {folderID}");

                        ExecuteSyncTask(poCmd, new EdmCmdData[] { dummyData }, operation, folderEventData);
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error processing folder event: {ex.Message}");
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

        #region Configuration Helpers

        /// <summary>
        /// Reads the Leo AI authentication configuration from JSON file
        /// </summary>
        private LeoAuthConfig ReadAuthConfig()
        {
            try
            {
                string configFilePath = null;

                // First, try to read the path from environment variable
                string envPath = LeoAIDataUtilities.ReadEnvVariableByName("LEO_AUTH_KEY", false);
                if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                {
                    configFilePath = envPath;
                    LogFileWriter.LogMessage($"Using auth config from environment variable path: {configFilePath}");
                }
                else
                {
                    // Fallback to default location
                    string defaultPath = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.json");
                    if (File.Exists(defaultPath))
                    {
                        configFilePath = defaultPath;
                        LogFileWriter.LogMessage($"Using auth config from default path: {configFilePath}");
                    }
                    else
                    {
                        LogFileWriter.LogError($"Auth config not found. Tried environment variable and default path: {defaultPath}");
                        return null;
                    }
                }

                // Read and parse the config file
                string jsonContent = File.ReadAllText(configFilePath);
                return ParseAuthConfig(jsonContent);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to read auth config: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses the authentication configuration JSON
        /// </summary>
        private LeoAuthConfig ParseAuthConfig(string jsonContent)
        {
            try
            {
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<LeoAuthConfig>(jsonContent);

                if (string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ProjectId))
                {
                    LogFileWriter.LogError("Auth config is missing ApiKey or ProjectId");
                    return null;
                }

                LogFileWriter.LogMessage("Auth config parsed successfully");
                return config;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to parse auth config: {ex.Message}");
                return null;
            }
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
        /// Unregisters vault installation from the registry
        /// </summary>
        private void UnregisterVaultInstallation(string vaultName)
        {
            try
            {
                string registryPath = @"SOFTWARE\LeoAI\SwPdmAddIn\Vaults";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, true))
                {
                    if (key != null && key.GetValue(vaultName) != null)
                    {
                        key.DeleteValue(vaultName);
                        LogFileWriter.LogMessage($"Unregistered vault installation: {vaultName}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to unregister vault installation: {ex.Message}");
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
