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

                // File events that require processing
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostUnlock);     // Check-in - marks operations ready
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostUndoLock);   // Undo checkout - marks operations ready
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostDelete);     // Delete - immediate ready operation
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostMove);       // Move - creates in-work operation
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostRename);     // Rename - creates in-work operation
                // Note: PostCopy removed - copy creates new file that will be handled by PostUnlock
                // Note: PostAdd removed - fires during Ctrl+S before check-in when file is still locked

                // Folder events that require processing
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostRenameFolder);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostMoveFolder);

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

                    // Load existing metadata
                    string metadataPath = GetMetadataFilePath(vault);
                    UserSessionMetadata metadata = LoadMetadataFile(metadataPath);

                    // Create a "CompleteSync" operation
                    var completeSyncOp = new OperationMetadata
                    {
                        Id = Guid.NewGuid().ToString(),
                        Operation = "CompleteSync",
                        OldPath = null,
                        NewPath = null,
                        FileID = 0,
                        FolderID = 0,
                        Status = "ready",  // Immediately ready
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                        AdditionalData = new Dictionary<string, string>()
                    };

                    LogFileWriter.LogMessage($"Created CompleteSync operation: {completeSyncOp.Id}");

                    // Add to metadata and save
                    metadata.Operations.Add(completeSyncOp);
                    SaveMetadataFile(metadataPath, metadata, vault);

                    // Create task metadata file with CompleteSync operation
                    string taskMetadataPath = CreateTaskMetadataFile(vault, new List<OperationMetadata> { completeSyncOp });

                    // Pass the metadata file to the task
                    LogFileWriter.LogMessage("Triggering task for CompleteSync operation with metadata file");
                    ExecuteSyncTaskWithMetadataFile(poCmd, taskMetadataPath);
                    return;
                }
            }

            switch (poCmd.meCmdType)
            {
                case EdmCmdType.EdmCmd_PostUnlock:
                    LogFileWriter.LogMessage("PostUnlock event detected - marking operations ready");
                    HandleUnlockOperation(cmd, data);
                    break;

                case EdmCmdType.EdmCmd_PostUndoLock:
                    LogFileWriter.LogMessage("PostUndoLock event detected - marking operations ready");
                    HandleUnlockOperation(cmd, data);
                    break;

                case EdmCmdType.EdmCmd_PostDelete:
                    LogFileWriter.LogMessage("PostDelete event detected - creating delete operations");
                    HandleDeleteOperation(cmd, data);
                    break;

                case EdmCmdType.EdmCmd_PostMove:
                    LogFileWriter.LogMessage("PostMove event detected - tracking move operation");
                    HandleMoveOperation(cmd, data);
                    break;

                case EdmCmdType.EdmCmd_PostRename:
                    LogFileWriter.LogMessage("PostRename event detected - tracking rename operation");
                    HandleRenameOperation(cmd, data);
                    break;

                case EdmCmdType.EdmCmd_PostMoveFolder:
                    LogFileWriter.LogMessage("PostMoveFolder event detected - sending folder move to task");
                    ProcessFolderEvent(cmd, data, "Move");
                    break;

                case EdmCmdType.EdmCmd_PostRenameFolder:
                    LogFileWriter.LogMessage("PostRenameFolder event detected - sending folder rename to task");
                    ProcessFolderEvent(cmd, data, "Rename");
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

                        // Perform initial sync by executing CompleteSync task on task host
                        LogFileWriter.LogMessage("Queuing CompleteSync task for initial vault sync...");
                        EdmCmdData[] emptyData = new EdmCmdData[0]; // CompleteSync doesn't need file list, task will enumerate
                        ExecuteSyncTask(poCmd, emptyData, "CompleteSync");
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

        #region Metadata File Management

        /// <summary>
        /// Gets the user's session ID (username_PID)
        /// </summary>
        private string GetUserSessionId()
        {
            string username = Environment.UserName.Replace(" ", "_");
            int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            return $"{username}_{processId}";
        }

        /// <summary>
        /// Gets the path to the user's session metadata file
        /// </summary>
        private string GetMetadataFilePath(IEdmVault5 vault)
        {
            string sessionId = GetUserSessionId();
            string vaultRoot = vault.RootFolderPath;
            string metadataFolder = Path.Combine(vaultRoot, "LeoAI_TaskData");

            if (!Directory.Exists(metadataFolder))
            {
                Directory.CreateDirectory(metadataFolder);
            }

            return Path.Combine(metadataFolder, $"{sessionId}.json");
        }

        /// <summary>
        /// Loads or creates the metadata file
        /// </summary>
        private UserSessionMetadata LoadMetadataFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var metadata = Newtonsoft.Json.JsonConvert.DeserializeObject<UserSessionMetadata>(json);
                    if (metadata != null)
                        return metadata;
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error loading metadata file: {ex.Message}");
            }

            // Return new metadata if file doesn't exist or couldn't be loaded
            return new UserSessionMetadata
            {
                SessionId = GetUserSessionId(),
                Operations = new List<OperationMetadata>(),
                LastModified = DateTimeOffset.Now.ToUnixTimeSeconds()
            };
        }

        /// <summary>
        /// Saves user metadata file locally (NOT checked into vault - just for tracking)
        /// </summary>
        private void SaveMetadataFile(string metadataPath, UserSessionMetadata metadata, IEdmVault5 vault)
        {
            try
            {
                metadata.LastModified = DateTimeOffset.Now.ToUnixTimeSeconds();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(metadata, Newtonsoft.Json.Formatting.Indented);

                // Write with proper disposal
                using (FileStream fs = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                LogFileWriter.LogDebug($"User metadata file saved locally: {metadataPath}");

                // DO NOT add user metadata files to vault - they're just for local tracking
                // Only Task_*.json files should be in vault
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error saving user metadata file: {ex.Message}");
            }
        }

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

        #region Operation Handlers

        /// <summary>
        /// Handles PostUnlock/PostUndoLock - marks pending operations as ready
        /// </summary>
        private void HandleUnlockOperation(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                string metadataPath = GetMetadataFilePath(vault);
                UserSessionMetadata metadata = LoadMetadataFile(metadataPath);

                bool hasChanges = false;
                foreach (EdmCmdData cmdData in ppoData)
                {
                    string filePath = cmdData.mbsStrData1;
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    // Skip metadata files
                    if (filePath.Contains("\\LeoAI_TaskData\\"))
                    {
                        LogFileWriter.LogDebug($"Skipping metadata file: {filePath}");
                        continue;
                    }

                    // Check if there's a pending operation for this file
                    var pendingOp = metadata.Operations.FirstOrDefault(
                        op => op.Status == "in-work" &&
                        (op.NewPath?.Equals(filePath, StringComparison.OrdinalIgnoreCase) ?? false));

                    if (pendingOp != null)
                    {
                        // Mark existing operation as ready
                        pendingOp.Status = "ready";
                        pendingOp.FileID = cmdData.mlObjectID1;
                        pendingOp.FolderID = cmdData.mlObjectID2;
                        LogFileWriter.LogMessage($"Marked operation ready: {pendingOp.Operation} - {filePath}");
                        hasChanges = true;
                    }
                    else
                    {
                        // Create new upload operation
                        var uploadOp = new OperationMetadata
                        {
                            Operation = "Upload",
                            NewPath = filePath,
                            FileID = cmdData.mlObjectID1,
                            FolderID = cmdData.mlObjectID2,
                            Status = "ready",
                            Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                        };
                        metadata.Operations.Add(uploadOp);
                        LogFileWriter.LogMessage($"Created upload operation for: {filePath}");
                        hasChanges = true;
                    }
                }

                if (hasChanges)
                {
                    SaveMetadataFile(metadataPath, metadata, vault);

                    // Trigger task if there are ready operations
                    var readyOps = metadata.Operations.Where(op => op.Status == "ready").ToList();
                    if (readyOps.Count > 0)
                    {
                        // Create a new task-specific metadata file with only ready operations
                        string taskMetadataPath = CreateTaskMetadataFile(vault, readyOps);

                        // Pass the metadata file itself to the task
                        LogFileWriter.LogMessage($"Triggering task for {readyOps.Count} ready operations with metadata file: {Path.GetFileName(taskMetadataPath)}");
                        ExecuteSyncTaskWithMetadataFile(poCmd, taskMetadataPath);

                        // Mark operations as processing in original metadata
                        foreach (var op in readyOps)
                        {
                            op.Status = "processing";
                        }
                        SaveMetadataFile(metadataPath, metadata, vault);
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error in HandleUnlockOperation: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles PostDelete - creates ready delete operations
        /// </summary>
        private void HandleDeleteOperation(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                string metadataPath = GetMetadataFilePath(vault);
                UserSessionMetadata metadata = LoadMetadataFile(metadataPath);

                foreach (EdmCmdData cmdData in ppoData)
                {
                    string filePath = cmdData.mbsStrData1;
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    // Skip metadata files
                    if (filePath.Contains("\\LeoAI_TaskData\\"))
                    {
                        LogFileWriter.LogDebug($"Skipping metadata file: {filePath}");
                        continue;
                    }

                    var deleteOp = new OperationMetadata
                    {
                        Operation = "Delete",
                        OldPath = filePath,
                        NewPath = filePath,
                        FileID = cmdData.mlObjectID1,
                        FolderID = cmdData.mlObjectID2,
                        Status = "ready",  // Delete is immediately ready
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                    };
                    metadata.Operations.Add(deleteOp);
                    LogFileWriter.LogMessage($"Created delete operation for: {filePath}");
                }

                SaveMetadataFile(metadataPath, metadata, vault);

                // Trigger task
                LogFileWriter.LogMessage("Triggering task for delete operations");
                var deleteData = new Dictionary<string, string>();
                var deletedPaths = ppoData.Select(d => d.mbsStrData1).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                deleteData["FilePaths"] = string.Join("|", deletedPaths);
                ExecuteSyncTask(poCmd, ppoData, "Delete", deleteData);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error in HandleDeleteOperation: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles PostRename - tracks operation as in-work until check-in
        /// </summary>
        private void HandleRenameOperation(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                string metadataPath = GetMetadataFilePath(vault);
                UserSessionMetadata metadata = LoadMetadataFile(metadataPath);

                foreach (EdmCmdData cmdData in ppoData)
                {
                    string oldPath = cmdData.mbsStrData1;
                    string newPath = cmdData.mbsStrData2;

                    if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
                        continue;

                    // Skip metadata files
                    if (oldPath.Contains("\\LeoAI_TaskData\\") || newPath.Contains("\\LeoAI_TaskData\\"))
                    {
                        LogFileWriter.LogDebug($"Skipping metadata file rename: {oldPath} -> {newPath}");
                        continue;
                    }

                    var renameOp = new OperationMetadata
                    {
                        Operation = "Rename",
                        OldPath = oldPath,
                        NewPath = newPath,
                        FileID = cmdData.mlObjectID1,
                        FolderID = cmdData.mlObjectID2,
                        Status = "in-work",  // Will be ready on check-in
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                    };
                    metadata.Operations.Add(renameOp);
                    LogFileWriter.LogMessage($"Created rename operation (in-work): {oldPath} -> {newPath}");
                }

                SaveMetadataFile(metadataPath, metadata, vault);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error in HandleRenameOperation: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles PostMove - tracks operation as in-work until check-in
        /// </summary>
        private void HandleMoveOperation(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                IEdmVault5 vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null) return;

                string metadataPath = GetMetadataFilePath(vault);
                UserSessionMetadata metadata = LoadMetadataFile(metadataPath);

                foreach (EdmCmdData cmdData in ppoData)
                {
                    string oldPath = cmdData.mbsStrData1;
                    string newPath = cmdData.mbsStrData2;

                    if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
                        continue;

                    // Skip metadata files
                    if (oldPath.Contains("\\LeoAI_TaskData\\") || newPath.Contains("\\LeoAI_TaskData\\"))
                    {
                        LogFileWriter.LogDebug($"Skipping metadata file move: {oldPath} -> {newPath}");
                        continue;
                    }

                    var moveOp = new OperationMetadata
                    {
                        Operation = "Move",
                        OldPath = oldPath,
                        NewPath = newPath,
                        FileID = cmdData.mlObjectID1,
                        FolderID = cmdData.mlObjectID2,
                        Status = "in-work",  // Will be ready on check-in
                        Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
                    };
                    metadata.Operations.Add(moveOp);
                    LogFileWriter.LogMessage($"Created move operation (in-work): {oldPath} -> {newPath}");
                }

                SaveMetadataFile(metadataPath, metadata, vault);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error in HandleMoveOperation: {ex.Message}");
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

                // Prepare metadata to pass to task via vault hidden folder
                List<string> filePaths = new List<string>();
                List<int> fileIDs = new List<int>();
                List<int> folderIDs = new List<int>();

                foreach (EdmCmdData data in ppoData)
                {
                    filePaths.Add(data.mbsStrData1 ?? "");
                    fileIDs.Add(data.mlObjectID1);
                    folderIDs.Add(data.mlObjectID2);
                }

                // Create metadata JSON
                var metadata = new
                {
                    Operation = operation,
                    FilePaths = filePaths,
                    FileIDs = fileIDs,
                    FolderIDs = folderIDs,
                    AdditionalData = additionalData,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };

                string metadataJson = Newtonsoft.Json.JsonConvert.SerializeObject(metadata);
                LogFileWriter.LogMessage($"Metadata JSON (length: {metadataJson.Length})");

                // Store metadata in vault folder
                string vaultRoot = vault.RootFolderPath;
                string metadataFolder = Path.Combine(vaultRoot, "LeoAI_TaskData");

                // Create hidden folder if it doesn't exist
                if (!Directory.Exists(metadataFolder))
                {
                    Directory.CreateDirectory(metadataFolder);
                    // Set as hidden
                    DirectoryInfo dirInfo = new DirectoryInfo(metadataFolder);
                    dirInfo.Attributes |= FileAttributes.Hidden;
                    LogFileWriter.LogMessage($"Created hidden metadata folder: {metadataFolder}");
                }

                // Use Unix timestamp for ordering and trial number for retries
                // Format: {unixTimestamp}_{trialNumber}.json
                // Task will process earliest file and increment trial on failure
                long unixTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                int trialNumber = 0;
                string metadataFileName = $"{unixTimestamp}_{trialNumber}.json";
                string metadataPath = Path.Combine(metadataFolder, metadataFileName);

                File.WriteAllText(metadataPath, metadataJson);
                LogFileWriter.LogMessage($"Metadata written to vault: {metadataPath} (timestamp: {unixTimestamp}, trial: {trialNumber})");

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

                // Execute the task on the task host
                // Note: For CompleteSync with 0 files, we create a dummy file entry to avoid "operation cannot be performed" error
                if (fileList.Count == 0)
                {
                    // CompleteSync doesn't need file selection - task will enumerate vault
                    // But RunTask() requires at least one item, so we pass a dummy entry
                    // The task add-in will ignore the file list and enumerate the vault itself
                    LogFileWriter.LogMessage($"No files provided for CompleteSync - creating dummy file entry for RunTask()");

                    // Get any file from vault root to use as dummy
                    IEdmFolder5 rootFolder = vault.RootFolder;
                    IEdmPos5 pos = rootFolder.GetFirstFilePosition();

                    if (pos.IsNull)
                    {
                        LogFileWriter.LogError("Cannot execute CompleteSync: vault is empty (no files to use as task target)");
                        return;
                    }

                    IEdmFile5 dummyFile = rootFolder.GetNextFile(pos);
                    EdmSelItem2 dummyItem = new EdmSelItem2();
                    dummyItem.mlID = dummyFile.ID;
                    dummyItem.mlParentID = rootFolder.ID;
                    dummyItem.meType = EdmObjectType.EdmObject_File;
                    dummyItem.mlVersion = 0;
                    fileList.Add(dummyItem);

                    LogFileWriter.LogMessage($"Using dummy file for RunTask(): ID={dummyFile.ID}, Name={dummyFile.Name}");
                }

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
    }
}
