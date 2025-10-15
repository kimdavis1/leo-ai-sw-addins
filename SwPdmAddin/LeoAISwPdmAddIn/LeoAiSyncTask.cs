using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EPDM.Interop.epdm;
using LeoAICadDataClient;
using LeoAICadDataClient.Utilities;

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
    /// Operation metadata for tracking file operations (from client add-in)
    /// </summary>
    public class OperationMetadata
    {
        public string Id { get; set; }
        public string Operation { get; set; }      // "Rename", "Move", "Upload", "Delete"
        public string OldPath { get; set; }
        public string NewPath { get; set; }
        public int FileID { get; set; }
        public int FolderID { get; set; }
        public string Status { get; set; }         // "in-work" or "ready"
        public long Timestamp { get; set; }
        public Dictionary<string, string> AdditionalData { get; set; }
    }

    /// <summary>
    /// User session metadata containing all operations for a user session
    /// </summary>
    public class UserSessionMetadata
    {
        public string SessionId { get; set; }
        public List<OperationMetadata> Operations { get; set; }
        public long LastModified { get; set; }
    }

    /// <summary>
    /// Leo AI Sync Task Add-in - Executes sync operations on task host (server)
    /// This add-in runs on the designated task host and has access to API keys
    /// </summary>
    [ComVisible(true)]
    [Guid("8F7A3B2C-9E4D-4A1F-B6C5-2D8E3F1A4B7C")]
    public class LeoAiSyncTask : IEdmAddIn5
    {
        private const string TASK_NAME = "Leo AI Sync Task";
        private SecureApiClient _leoClient;
        private string _directoryId;
        private int _maxRetries = 3; // Default fallback, set from task config in OnTaskSetup

        public void GetAddInInfo(ref EdmAddInInfo poInfo, IEdmVault5 poVault, IEdmCmdMgr5 poCmdMgr)
        {
            LogFileWriter.LogDebug("=== LeoAiSyncTask.GetAddInInfo called ===");

            try
            {
                // Set add-in info
                poInfo.mbsAddInName = TASK_NAME;
                poInfo.mbsCompany = "Leo AI";
                poInfo.mbsDescription = "Syncs PDM files with Leo AI server (runs on task host)";
                poInfo.mlAddInVersion = 1;
                poInfo.mlRequiredVersionMajor = 20; // PDM 2020 or later
                poInfo.mlRequiredVersionMinor = 0;

                // Register for task commands using hooks
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_TaskSetup);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_TaskRun);

                LogFileWriter.LogInfo($"Task add-in registered: {TASK_NAME}");
                LogFileWriter.LogDebug("Registered for EdmCmd_TaskSetup and EdmCmd_TaskRun");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"GetAddInInfo failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        public void OnCmd(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogDebug($"=== LeoAiSyncTask.OnCmd called - CmdType: {poCmd.meCmdType} ===");

            try
            {
                switch (poCmd.meCmdType)
                {
                    case EdmCmdType.EdmCmd_TaskSetup:
                        LogFileWriter.LogDebug("Handling EdmCmd_TaskSetup");
                        OnTaskSetup(ref poCmd, ref ppoData);
                        break;

                    case EdmCmdType.EdmCmd_TaskRun:
                        LogFileWriter.LogInfo("Handling EdmCmd_TaskRun");
                        OnTaskRun(ref poCmd, ref ppoData);
                        break;

                    default:
                        LogFileWriter.LogWarning($"Unhandled command type: {poCmd.meCmdType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnCmd failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        private void OnTaskSetup(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogDebug("=== OnTaskSetup: Configuring task properties ===");

            try
            {
                IEdmTaskProperties taskProps = (IEdmTaskProperties)poCmd.mpoExtra;

                // Task does not support manual scheduling or launch
                // It's only triggered programmatically by the client add-in
                taskProps.TaskFlags = 0;

                // Read and store the retry count from task configuration
                _maxRetries = taskProps.RetryCount;

                LogFileWriter.LogDebug($"Task ID: {taskProps.TaskID}");
                LogFileWriter.LogDebug($"Task Name: {taskProps.TaskName}");
                LogFileWriter.LogDebug($"Task GUID: {taskProps.TaskGUID}");
                LogFileWriter.LogInfo($"Task Retry Count: {_maxRetries}");
                LogFileWriter.LogDebug("Task configured: no scheduling, no manual launch (client-triggered only)");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnTaskSetup failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void OnTaskLaunch(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage("=== OnTaskLaunch: Task launched on client, storing operation metadata ===");

            try
            {
                IEdmTaskInstance taskInstance = (IEdmTaskInstance)poCmd.mpoExtra;
                IEdmVault11 vault = (IEdmVault11)poCmd.mpoVault;

                LogFileWriter.LogMessage($"Task Instance ID: {taskInstance.ID}");
                LogFileWriter.LogMessage($"Task Instance GUID: {taskInstance.InstanceGUID}");

                // Read operation metadata from temp file written by client add-in
                // File name pattern: LeoAI_TaskData_{timestamp}.json in vault root/LeoAI_TaskData folder
                string vaultRoot = vault.RootFolderPath;
                string tempDataFolder = Path.Combine(vaultRoot, "LeoAI_TaskData");

                if (!Directory.Exists(tempDataFolder))
                {
                    LogFileWriter.LogMessage("No LeoAI_TaskData folder found - no operations to process");
                    return;
                }

                // Find the most recent task data file
                string[] jsonFiles = Directory.GetFiles(tempDataFolder, "*.json");
                if (jsonFiles.Length == 0)
                {
                    LogFileWriter.LogMessage("No task data files found - no operations to process");
                    return;
                }

                // Sort by timestamp in filename (descending) and take the most recent
                string taskDataFile = jsonFiles.OrderByDescending(f => f).FirstOrDefault();
                LogFileWriter.LogMessage($"Reading task data from: {Path.GetFileName(taskDataFile)}");

                string operationsJson = File.ReadAllText(taskDataFile);
                LogFileWriter.LogMessage($"Read operation metadata JSON ({operationsJson.Length} chars)");

                // Store filename in task variable so OnTaskRun knows which file to read
                string filename = Path.GetFileName(taskDataFile);
                taskInstance.SetVar("LeoAI_TaskDataFile", filename);
                LogFileWriter.LogMessage($"Stored task data filename in task variable: {filename}");

                // Don't delete the temp file yet - TaskRun will read and delete it
                LogFileWriter.LogMessage("Temp file will be read and deleted by TaskRun on server");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnTaskLaunch failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void OnTaskRun(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage("=== OnTaskRun: Starting sync operation ===");

            IEdmTaskInstance taskInstance = null;

            try
            {
                taskInstance = (IEdmTaskInstance)poCmd.mpoExtra;
                taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_Running);
                taskInstance.SetProgressRange(100, 0, "Starting Leo AI sync...");

                LogFileWriter.LogMessage($"Task Instance ID: {taskInstance.ID}");
                LogFileWriter.LogMessage($"Task Instance GUID: {taskInstance.InstanceGUID}");
                LogFileWriter.LogMessage($"Task Name: {taskInstance.TaskName}");

                // Get vault
                IEdmVault11 vault = (IEdmVault11)poCmd.mpoVault;
                LogFileWriter.LogMessage($"Vault: {vault.Name}");
                LogFileWriter.LogMessage($"Vault Root Path: {vault.RootFolderPath}");
                LogFileWriter.LogMessage($"Vault Root Path Exists: {Directory.Exists(vault.RootFolderPath)}");

                taskInstance.SetProgressPos(5, "Reading task metadata file from vault...");

                // CRITICAL: Process only the metadata file that was passed to this task
                // Each task processes its own unique metadata file to avoid conflicts
                IEdmFile5 metadataFile = null;
                IEdmFolder5 metadataFolder = null;
                string metadataFilePath = null;

                // Check if we have file data passed to the task
                if (ppoData != null && ppoData.Length > 0)
                {
                    // Get the metadata file from the first EdmCmdData
                    EdmCmdData fileData = ppoData[0];
                    metadataFile = (IEdmFile5)vault.GetObject(EdmObjectType.EdmObject_File, fileData.mlObjectID1);
                    metadataFolder = (IEdmFolder5)vault.GetObject(EdmObjectType.EdmObject_Folder, fileData.mlObjectID2);

                    if (metadataFile != null && metadataFile.Name.StartsWith("Task_") && metadataFile.Name.EndsWith(".json"))
                    {
                        // This is our task metadata file
                        metadataFilePath = metadataFile.GetLocalPath(metadataFolder.ID);
                        LogFileWriter.LogMessage($"Processing task metadata file: {metadataFile.Name} (ID: {metadataFile.ID})");
                    }
                    else
                    {
                        LogFileWriter.LogError($"Invalid metadata file passed to task: {metadataFile?.Name ?? "null"}");
                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, "Invalid metadata file");
                        return;
                    }
                }
                else
                {
                    LogFileWriter.LogError("No file data passed to task");
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, "No metadata file specified");
                    return;
                }

                if (string.IsNullOrEmpty(metadataFilePath))
                {
                    LogFileWriter.LogError("Could not get metadata file path");
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, "Could not access metadata file");
                    return;
                }

                LogFileWriter.LogMessage($"Metadata file path: {metadataFilePath}");

                // Read and process the single task metadata file
                UserSessionMetadata taskMetadata = null;
                List<OperationMetadata> allOperations = new List<OperationMetadata>();

                try
                {
                    LogFileWriter.LogMessage($"Reading metadata file: {Path.GetFileName(metadataFilePath)}");

                    // Read the task metadata file using PDM API (archive or local view)
                    string json = null;
                    if (File.Exists(metadataFilePath))
                    {
                        // File is in local view
                        json = File.ReadAllText(metadataFilePath);
                        LogFileWriter.LogMessage("Read metadata file from local view");
                    }
                    else
                    {
                        // File not in local view - get from archive using PDM API
                        LogFileWriter.LogMessage("Metadata file not in local view, using GetReadableFilePath");
                        string actualPath;
                        bool needsCleanup;
                        (actualPath, needsCleanup) = GetReadableFilePath(vault, metadataFilePath, metadataFolder.ID);

                        json = File.ReadAllText(actualPath);
                        LogFileWriter.LogMessage($"Read metadata file from: {actualPath}");

                        // Clean up temp file if needed
                        if (needsCleanup && !string.IsNullOrEmpty(actualPath))
                        {
                            DeleteTempFile(actualPath);
                        }
                    }

                    // Deserialize as UserSessionMetadata
                    taskMetadata = Newtonsoft.Json.JsonConvert.DeserializeObject<UserSessionMetadata>(json);

                    if (taskMetadata != null && taskMetadata.Operations != null && taskMetadata.Operations.Count > 0)
                    {
                        // All operations in task file should be ready (already filtered by client)
                        allOperations = taskMetadata.Operations;
                        LogFileWriter.LogMessage($"Task {taskMetadata.SessionId}: {allOperations.Count} operations to process");
                    }
                    else
                    {
                        LogFileWriter.LogMessage("No operations in task metadata file");
                        taskInstance.SetProgressPos(100, "No operations");
                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, "", null, "No operations in metadata");

                        // Delete the empty task file
                        DeleteMetadataFile(vault, metadataFile, metadataFolder);
                        return;
                    }
                }
                catch (Exception fileEx)
                {
                    LogFileWriter.LogError($"Error reading metadata file: {fileEx.Message}");
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, $"Failed to read metadata: {fileEx.Message}");
                    return;
                }

                if (allOperations.Count == 0)
                {
                    LogFileWriter.LogMessage("No operations to process");
                    taskInstance.SetProgressPos(100, "No operations");
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, "", null, "No operations to process");

                    // Delete the empty task file
                    DeleteMetadataFile(vault, metadataFile, metadataFolder);
                    return;
                }

                LogFileWriter.LogMessage($"Processing {allOperations.Count} operations");

                // Sort operations by timestamp (oldest first)
                allOperations = allOperations.OrderBy(op => op.Timestamp).ToList();

                taskInstance.SetProgressPos(10, "Initializing Leo AI client...");

                // Initialize Leo AI client
                LogFileWriter.LogMessage("Reading auth config...");
                LeoAuthConfig authConfig = ReadAuthConfig();
                LogFileWriter.LogMessage($"Auth config loaded - ProjectId: {authConfig.ProjectId}");

                _leoClient = new SecureApiClient(authConfig.ApiKey, authConfig.ProjectId);
                LogFileWriter.LogMessage("SecureApiClient initialized");

                taskInstance.SetProgressPos(15, "Getting directory ID...");

                // Get or create directory
                _directoryId = GetOrCreateDirectoryId(vault.RootFolderPath).Result;
                LogFileWriter.LogMessage($"Directory ID: {_directoryId}");

                taskInstance.SetProgressPos(20, "Processing operations...");

                // Process each operation in order
                int processed = 0;
                int total = allOperations.Count;

                foreach (var operation in allOperations)
                {
                    try
                    {
                        int progressBase = 20 + (int)((processed / (float)total) * 70);
                        taskInstance.SetProgressPos(progressBase, $"Processing operation {processed + 1}/{total}: {operation.Operation}");

                        LogFileWriter.LogMessage($"=== Processing operation {processed + 1}/{total} ===");
                        LogFileWriter.LogMessage($"  Type: {operation.Operation}");
                        LogFileWriter.LogMessage($"  ID: {operation.Id}");
                        LogFileWriter.LogMessage($"  OldPath: {operation.OldPath ?? "N/A"}");
                        LogFileWriter.LogMessage($"  NewPath: {operation.NewPath ?? "N/A"}");
                        LogFileWriter.LogMessage($"  Timestamp: {operation.Timestamp}");

                        // Execute operation
                        switch (operation.Operation)
                        {
                            case "Add":
                            case "Upload":
                                ProcessUploadOperation(vault, operation).Wait();
                                break;

                            case "Delete":
                                ProcessDeleteOperation(vault, operation).Wait();
                                break;

                            case "Move":
                                ProcessMoveOperation(vault, operation).Wait();
                                break;

                            case "Rename":
                                ProcessRenameOperation(vault, operation).Wait();
                                break;

                            case "CompleteSync":
                                ProcessCompleteSyncOperation(vault, taskInstance).Wait();
                                break;

                            case "Copy":
                                ProcessCopyOperation(vault, operation).Wait();
                                break;

                            case "AddFolder":
                                ProcessAddFolderOperation(vault, operation).Wait();
                                break;

                            case "DeleteFolder":
                                ProcessDeleteFolderOperation(vault, operation).Wait();
                                break;

                            case "MoveFolder":
                                ProcessMoveFolderOperation(vault, operation).Wait();
                                break;

                            case "RenameFolder":
                                ProcessRenameFolderOperation(vault, operation).Wait();
                                break;

                            default:
                                LogFileWriter.LogWarning($"Unknown operation type: {operation.Operation} - skipping");
                                break;
                        }

                        processed++;
                        LogFileWriter.LogMessage($"Operation completed successfully");
                    }
                    catch (Exception opEx)
                    {
                        LogFileWriter.LogError($"Operation failed: {opEx.Message}");
                        // Continue with next operation instead of failing entire task
                    }
                }

                // Delete the task metadata file after successful processing
                taskInstance.SetProgressPos(90, "Cleaning up task metadata...");

                try
                {
                    DeleteMetadataFile(vault, metadataFile, metadataFolder);
                    LogFileWriter.LogMessage($"Deleted task metadata file: {metadataFile.Name}");
                }
                catch (Exception delEx)
                {
                    LogFileWriter.LogError($"Failed to delete task metadata file: {delEx.Message}");
                    // Don't fail the task just because we couldn't delete the metadata file
                }

                taskInstance.SetProgressPos(100, $"Completed {processed}/{total} operations");
                taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, "", null, $"Processed {processed} operations successfully");

                LogFileWriter.LogMessage($"=== OnTaskRun: Completed successfully - processed {processed}/{total} operations ===");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnTaskRun failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");

                if (taskInstance != null)
                {
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, $"Sync failed: {ex.Message}");
                }
            }
        }

        #region Sync Operations (New Format - OperationMetadata)

        /// <summary>
        /// Process an Upload operation from metadata
        /// Checks if file exists on server, compares checksums, and handles accordingly
        /// Always checks before upload regardless of operation type (upload, rename, move, replace, etc)
        /// </summary>
        private async Task ProcessUploadOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessUploadOperation: {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("Upload operation missing NewPath");
            }

            // Check if file exists in vault using PDM API (not local view)
            if (!FileExistsInVault(vault, operation.NewPath))
            {
                throw new FileNotFoundException($"File not found in vault: {operation.NewPath}");
            }

            string relativePath = GetRelativePath(vault.RootFolderPath, operation.NewPath);
            LogFileWriter.LogMessage($"Processing upload for: {relativePath}");

            // Always check if file exists on server and compare checksums
            // This handles all cases: new files, updates, renames, moves, replaces, etc
            LogFileWriter.LogMessage($"Checking if file exists on server: {relativePath}");

            try
            {
                var serverFile = await _leoClient.GetFileInfoByPathAsync(_directoryId, relativePath);

                if (serverFile != null)
                {
                    LogFileWriter.LogMessage($"File exists on server with checksum: {serverFile.CheckSum}");

                    // Calculate local file checksum - use GetReadableFilePath to access from archive or temp copy
                    string localChecksum = null;
                    try
                    {
                        string fullPath = EnsureFullPath(vault, operation.NewPath);
                        IEdmFolder5 folder;
                        IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);

                        if (file != null && folder != null)
                        {
                            string readablePath;
                            bool needsCleanup;
                            (readablePath, needsCleanup) = GetReadableFilePath(vault, operation.NewPath, folder.ID);

                            var fileInfo = LeoFileInfo.GetFileInfo(readablePath);
                            localChecksum = fileInfo.CheckSum;

                            if (needsCleanup)
                            {
                                DeleteTempFile(readablePath);
                            }
                        }
                    }
                    catch (Exception csEx)
                    {
                        LogFileWriter.LogError($"Failed to compute checksum for {operation.NewPath}: {csEx.Message}");
                        throw;
                    }

                    LogFileWriter.LogMessage($"Local file checksum: {localChecksum}");

                    // Check if file needs reupload
                    bool needsReupload = false;
                    string reason = "";

                    if (serverFile.ParentStatus == "IN_ERROR")
                    {
                        needsReupload = true;
                        reason = "file has IN_ERROR status";
                        LogFileWriter.LogMessage($"File has IN_ERROR status - forcing reupload even though checksum may match");
                    }
                    else if (serverFile.CheckSum != localChecksum)
                    {
                        needsReupload = true;
                        reason = "checksum changed";
                    }

                    if (!needsReupload && serverFile.CheckSum == localChecksum)
                    {
                        LogFileWriter.LogMessage($"File unchanged (checksums match, no errors) - skipping upload");
                        // TODO: Check if metadata changed and update if needed
                        return;
                    }

                    if (needsReupload)
                    {
                        LogFileWriter.LogMessage($"File changed ({reason}) - deleting old version before upload");

                        // Delete the old version first
                        bool deleted = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
                        if (deleted)
                        {
                            LogFileWriter.LogMessage($"Deleted old version: {relativePath}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"Warning: Failed to delete old version: {relativePath}");
                        }
                    }
                }
                else
                {
                    LogFileWriter.LogMessage($"File does not exist on server - uploading as new file");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage($"Could not check server file (treating as new): {ex.Message}");
            }

            // Upload the file
            LogFileWriter.LogMessage($"Uploading: {relativePath}");
            await UpdateFilesToLeoAI(vault, new[] { operation.NewPath }, vault.RootFolderPath);

            LogFileWriter.LogMessage($"Upload completed: {relativePath}");
        }

        /// <summary>
        /// Process a Delete operation from metadata
        /// </summary>
        private async Task ProcessDeleteOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessDeleteOperation: {operation.OldPath}");

            if (string.IsNullOrEmpty(operation.OldPath))
            {
                throw new Exception("Delete operation missing OldPath");
            }

            string relativePath = GetRelativePath(vault.RootFolderPath, operation.OldPath);
            LogFileWriter.LogMessage($"Deleting from server: {relativePath}");

            bool deleted = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
            if (deleted)
            {
                LogFileWriter.LogMessage($"Delete completed: {relativePath}");
            }
            else
            {
                LogFileWriter.LogMessage($"Delete returned false: {relativePath}");
            }
        }

        /// <summary>
        /// Process a Rename operation from metadata
        /// Upload new path first, then delete old path
        /// </summary>
        private async Task ProcessRenameOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessRenameOperation: {operation.OldPath} → {operation.NewPath}");
            // Rename and Move operations are identical in implementation
            await ProcessRenameOrMoveOperation(vault, operation);
        }

        /// <summary>
        /// Process a Move operation from metadata
        /// Upload new path first, then delete old path
        /// </summary>
        private async Task ProcessMoveOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessMoveOperation: {operation.OldPath} → {operation.NewPath}");
            // Rename and Move operations are identical in implementation
            await ProcessRenameOrMoveOperation(vault, operation);
        }

        /// <summary>
        /// Common implementation for Rename and Move operations
        /// Upload new path first, then delete old path
        /// </summary>
        private async Task ProcessRenameOrMoveOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            if (string.IsNullOrEmpty(operation.OldPath) || string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception($"{operation.Operation} operation missing OldPath or NewPath");
            }

            string oldRelativePath = GetRelativePath(vault.RootFolderPath, operation.OldPath);
            string newRelativePath = GetRelativePath(vault.RootFolderPath, operation.NewPath);

            // Step 1: Upload file with new path - check if file exists in vault using PDM API
            if (FileExistsInVault(vault, operation.NewPath))
            {
                LogFileWriter.LogMessage($"Uploading file with new path: {newRelativePath}");
                await UpdateFilesToLeoAI(vault, new[] { operation.NewPath }, vault.RootFolderPath);
                LogFileWriter.LogMessage($"Upload completed: {newRelativePath}");
            }
            else
            {
                LogFileWriter.LogMessage($"Warning: File not found in vault: {operation.NewPath}");
            }

            // Step 2: Delete old path from server
            LogFileWriter.LogMessage($"Deleting old path from server: {oldRelativePath}");
            bool deleted = await _leoClient.DeleteFileAsync(_directoryId, oldRelativePath);
            if (deleted)
            {
                LogFileWriter.LogMessage($"Deleted old path: {oldRelativePath}");
            }
            else
            {
                LogFileWriter.LogMessage($"Delete returned false for old path: {oldRelativePath}");
            }

            LogFileWriter.LogMessage($"{operation.Operation} completed: {oldRelativePath} → {newRelativePath}");
        }

        /// <summary>
        /// Process a Copy operation from metadata
        /// The copied file is a new independent file - just check if destination exists and upload
        /// </summary>
        private async Task ProcessCopyOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessCopyOperation: {operation.OldPath} → {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("Copy operation missing NewPath");
            }

            string newRelativePath = GetRelativePath(vault.RootFolderPath, operation.NewPath);

            // Check if destination file exists in vault
            if (!FileExistsInVault(vault, operation.NewPath))
            {
                LogFileWriter.LogMessage($"Warning: Destination file not found in vault: {operation.NewPath}");
                return;
            }

            // Check if destination already exists on server
            var serverFile = await _leoClient.GetFileInfoByPathAsync(_directoryId, newRelativePath);

            // If destination already exists on server, delete it first
            if (serverFile != null)
            {
                LogFileWriter.LogMessage($"Destination already exists on server - deleting before upload");
                await _leoClient.DeleteFileAsync(_directoryId, newRelativePath);
            }

            // Upload the copied file
            await UpdateFilesToLeoAI(vault, new[] { operation.NewPath }, vault.RootFolderPath);
            LogFileWriter.LogMessage($"Copy completed: {newRelativePath}");
        }

        /// <summary>
        /// Process an AddFolder operation - uploads all files in the folder
        /// </summary>
        private async Task ProcessAddFolderOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessAddFolderOperation: {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("AddFolder operation missing NewPath");
            }

            // Get folder from vault
            string folderFullPath = EnsureFullPath(vault, operation.NewPath);
            IEdmFolder5 folder = vault.GetFolderFromPath(folderFullPath);

            if (folder == null)
            {
                LogFileWriter.LogMessage($"Warning: Folder not found in vault: {operation.NewPath}");
                return;
            }

            // Get all files in folder (recursively)
            List<string> filesToUpload = new List<string>();
            EnumerateFolderFilesRecursive(folder, folder.LocalPath, folder.LocalPath, new List<string>(), filesToUpload);

            LogFileWriter.LogMessage($"Found {filesToUpload.Count} files in folder to upload");

            // Upload each file
            foreach (string filePath in filesToUpload)
            {
                try
                {
                    await UpdateFilesToLeoAI(vault, new[] { filePath }, vault.RootFolderPath);
                    string relativePath = GetRelativePath(vault.RootFolderPath, filePath);
                    LogFileWriter.LogMessage($"Uploaded file from new folder: {relativePath}");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");
                    // Continue with other files
                }
            }

            LogFileWriter.LogMessage($"AddFolder completed: {operation.NewPath} ({filesToUpload.Count} files)");
        }

        /// <summary>
        /// Process a DeleteFolder operation - deletes all files in the folder from server
        /// </summary>
        private async Task ProcessDeleteFolderOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessDeleteFolderOperation: {operation.OldPath}");

            if (string.IsNullOrEmpty(operation.OldPath))
            {
                throw new Exception("DeleteFolder operation missing OldPath");
            }

            string folderRelativePath = GetRelativePath(vault.RootFolderPath, operation.OldPath);
            LogFileWriter.LogMessage($"Deleting all files in folder from server: {folderRelativePath}");

            // Get all files from server that are in this folder path
            var serverData = await _leoClient.GetSyncMetadataAsync(_directoryId);
            List<string> filesToDelete = new List<string>();

            if (serverData?.Files != null)
            {
                foreach (var serverFile in serverData.Files)
                {
                    // Check if file path starts with folder path (including subfolder files)
                    if (serverFile.FilePathInDirectory.StartsWith(folderRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        filesToDelete.Add(serverFile.FilePathInDirectory);
                    }
                }
            }

            LogFileWriter.LogMessage($"Found {filesToDelete.Count} files in folder to delete from server");

            // Delete each file
            int deleted = 0;
            foreach (string relativePath in filesToDelete)
            {
                try
                {
                    bool success = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
                    if (success)
                    {
                        deleted++;
                        LogFileWriter.LogMessage($"Deleted file from deleted folder: {relativePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to delete file {relativePath}: {ex.Message}");
                    // Continue with other files
                }
            }

            LogFileWriter.LogMessage($"DeleteFolder completed: {operation.OldPath} ({deleted}/{filesToDelete.Count} files deleted)");
        }

        /// <summary>
        /// Process a MoveFolder operation - uploads files to new paths and deletes old paths
        /// </summary>
        private async Task ProcessMoveFolderOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessMoveFolderOperation: {operation.OldPath} → {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.OldPath) || string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("MoveFolder operation missing OldPath or NewPath");
            }

            // Get new folder from vault
            string newFolderFullPath = EnsureFullPath(vault, operation.NewPath);
            IEdmFolder5 newFolder = vault.GetFolderFromPath(newFolderFullPath);

            if (newFolder == null)
            {
                LogFileWriter.LogMessage($"Warning: New folder not found in vault: {operation.NewPath}");
                return;
            }

            // Get all files in new folder location
            List<string> oldFilePaths = new List<string>();
            List<string> newFilePaths = new List<string>();

            string oldFolderPath = EnsureFullPath(vault, operation.OldPath);
            string newFolderPath = newFolder.LocalPath;

            EnumerateFolderFilesRecursive(newFolder, oldFolderPath, newFolderPath, oldFilePaths, newFilePaths);

            LogFileWriter.LogMessage($"Found {newFilePaths.Count} files to move");

            // Process each file: upload new path, delete old path
            int processed = 0;
            for (int i = 0; i < newFilePaths.Count; i++)
            {
                try
                {
                    string oldFilePath = oldFilePaths[i];
                    string newFilePath = newFilePaths[i];

                    string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldFilePath);
                    string newRelativePath = GetRelativePath(vault.RootFolderPath, newFilePath);

                    // Upload file with new path
                    await UpdateFilesToLeoAI(vault, new[] { newFilePath }, vault.RootFolderPath);
                    LogFileWriter.LogMessage($"Uploaded file to new location: {newRelativePath}");

                    // Delete old path
                    bool deleted = await _leoClient.DeleteFileAsync(_directoryId, oldRelativePath);
                    if (deleted)
                    {
                        LogFileWriter.LogMessage($"Deleted old file path: {oldRelativePath}");
                    }

                    processed++;
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to move file {oldFilePaths[i]}: {ex.Message}");
                    // Continue with other files
                }
            }

            LogFileWriter.LogMessage($"MoveFolder completed: {operation.OldPath} → {operation.NewPath} ({processed}/{newFilePaths.Count} files moved)");
        }

        /// <summary>
        /// Process a RenameFolder operation - same as MoveFolder
        /// </summary>
        private async Task ProcessRenameFolderOperation(IEdmVault11 vault, OperationMetadata operation)
        {
            LogFileWriter.LogMessage($"ProcessRenameFolderOperation: {operation.OldPath} → {operation.NewPath}");
            // Rename and Move folder operations are identical in implementation
            await ProcessMoveFolderOperation(vault, operation);
        }

        /// <summary>
        /// Process a CompleteSync operation - syncs entire vault with server
        /// </summary>
        private async Task ProcessCompleteSyncOperation(IEdmVault11 vault, IEdmTaskInstance taskInstance)
        {
            LogFileWriter.LogMessage("ProcessCompleteSyncOperation: Starting full vault sync");

            try
            {
                taskInstance.SetProgressPos(30, "Enumerating vault files...");

                // Get all files from vault using SolidWorksPdmHelper
                SolidWorksPdmHelper pdmHelper = new SolidWorksPdmHelper(vault);
                pdmHelper.ProcessFolders(vault);
                List<FileData> vaultFiles = pdmHelper.FilesInfo;
                LogFileWriter.LogMessage($"Found {vaultFiles.Count} files in vault");

                taskInstance.SetProgressPos(35, "Calculating checksums...");

                // Calculate checksums for all vault files using archive-first approach
                int checksumProgress = 0;
                foreach (var vaultFile in vaultFiles)
                {
                    try
                    {
                        string fullPath = EnsureFullPath(vault, vaultFile.file);
                        IEdmFolder5 folder;
                        IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);

                        if (file != null && folder != null)
                        {
                            string readablePath;
                            bool needsCleanup;
                            (readablePath, needsCleanup) = GetReadableFilePath(vault, vaultFile.file, folder.ID);

                            var fileInfo = LeoFileInfo.GetFileInfo(readablePath);
                            vaultFile.checkSum = fileInfo.CheckSum;

                            if (needsCleanup)
                            {
                                DeleteTempFile(readablePath);
                            }
                        }
                    }
                    catch (Exception csEx)
                    {
                        LogFileWriter.LogError($"Failed to compute checksum for {vaultFile.file}: {csEx.Message}");
                        // Leave checksum as null - will be treated as changed
                    }

                    checksumProgress++;
                    if (checksumProgress % 10 == 0)
                    {
                        int progress = 35 + (int)((checksumProgress / (float)vaultFiles.Count) * 5);
                        taskInstance.SetProgressPos(progress, $"Calculated checksums for {checksumProgress}/{vaultFiles.Count} files");
                    }
                }

                taskInstance.SetProgressPos(40, "Getting server state...");

                // Get all files from server
                var serverData = await _leoClient.GetSyncMetadataAsync(_directoryId);
                Dictionary<string, SyncMetadataFile> serverFiles = new Dictionary<string, SyncMetadataFile>(StringComparer.OrdinalIgnoreCase);
                if (serverData?.Files != null)
                {
                    foreach (var file in serverData.Files)
                    {
                        serverFiles[file.FilePathInDirectory] = file;
                    }
                }
                LogFileWriter.LogMessage($"Found {serverFiles.Count} files on server");

                taskInstance.SetProgressPos(50, "Comparing vault and server state...");

                // Build sets for comparison
                HashSet<string> vaultPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var vaultFile in vaultFiles)
                {
                    string relativePath = GetRelativePath(vault.RootFolderPath, vaultFile.file);
                    vaultPaths.Add(relativePath);
                }

                // Find files to upload (in vault but not on server, or changed)
                List<string> newFilesToUpload = new List<string>();
                List<string> modifiedFilesToUpload = new List<string>();
                foreach (var vaultFile in vaultFiles)
                {
                    string relativePath = GetRelativePath(vault.RootFolderPath, vaultFile.file);
                    if (!serverFiles.ContainsKey(relativePath))
                    {
                        newFilesToUpload.Add(vaultFile.file);
                        LogFileWriter.LogMessage($"New file to upload: {relativePath}");
                    }
                    else
                    {
                        // Check if file changed (compare checksum) or has IN_ERROR status
                        var serverFile = serverFiles[relativePath];

                        // Check if file has IN_ERROR status - force reupload even if checksum matches
                        if (serverFile.ParentStatus == "IN_ERROR")
                        {
                            modifiedFilesToUpload.Add(vaultFile.file);
                            LogFileWriter.LogMessage($"Modified file to upload: {relativePath} (IN_ERROR status - forcing reupload)");
                        }
                        // Only compare if we successfully calculated checksum
                        else if (!string.IsNullOrEmpty(vaultFile.checkSum) && serverFile.CheckSum != vaultFile.checkSum)
                        {
                            modifiedFilesToUpload.Add(vaultFile.file);
                            LogFileWriter.LogMessage($"Modified file to upload: {relativePath} (server: {serverFile.CheckSum}, vault: {vaultFile.checkSum})");
                        }
                        else if (string.IsNullOrEmpty(vaultFile.checkSum))
                        {
                            // Couldn't calculate checksum - treat as modified to be safe
                            modifiedFilesToUpload.Add(vaultFile.file);
                            LogFileWriter.LogMessage($"Modified file to upload (no checksum): {relativePath}");
                        }
                        else
                        {
                            // Checksums match - skip
                            LogFileWriter.LogMessage($"File unchanged (checksums match): {relativePath}");
                        }
                    }
                }

                // Find files to delete (on server but not in vault - vault is master)
                List<string> filesToDelete = new List<string>();
                foreach (var serverPath in serverFiles.Keys)
                {
                    if (!vaultPaths.Contains(serverPath))
                    {
                        filesToDelete.Add(serverPath);
                        LogFileWriter.LogMessage($"File to delete from server: {serverPath}");
                    }
                }

                int totalFilesToUpload = newFilesToUpload.Count + modifiedFilesToUpload.Count;
                LogFileWriter.LogMessage($"New files: {newFilesToUpload.Count}, Modified files: {modifiedFilesToUpload.Count}, Files to delete: {filesToDelete.Count}");

                // Delete modified files first (before uploading new versions)
                if (modifiedFilesToUpload.Count > 0)
                {
                    taskInstance.SetProgressPos(55, $"Deleting {modifiedFilesToUpload.Count} modified files before re-upload...");
                    int deletedModified = 0;
                    foreach (var filePath in modifiedFilesToUpload)
                    {
                        try
                        {
                            string relativePath = GetRelativePath(vault.RootFolderPath, filePath);
                            bool deleteSuccess = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
                            if (deleteSuccess)
                            {
                                deletedModified++;
                                LogFileWriter.LogMessage($"Deleted modified file before re-upload: {relativePath}");
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"Warning: Failed to delete modified file: {relativePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogFileWriter.LogError($"Error deleting modified file {filePath}: {ex.Message}");
                            // Continue with upload anyway
                        }
                    }
                    LogFileWriter.LogMessage($"Deleted {deletedModified} modified files before re-upload");
                }

                // Upload all files (new + modified)
                List<string> allFilesToUpload = new List<string>();
                allFilesToUpload.AddRange(newFilesToUpload);
                allFilesToUpload.AddRange(modifiedFilesToUpload);

                taskInstance.SetProgressPos(60, $"Uploading {totalFilesToUpload} files...");
                int uploaded = 0;
                int uploadTotal = totalFilesToUpload > 0 ? totalFilesToUpload : 1;
                foreach (var filePath in allFilesToUpload)
                {
                    try
                    {
                        await UpdateFilesToLeoAI(vault, new[] { filePath }, vault.RootFolderPath);
                        uploaded++;
                        int progress = 60 + (int)((uploaded / (float)uploadTotal) * 20);
                        taskInstance.SetProgressPos(progress, $"Uploaded {uploaded}/{totalFilesToUpload} files");
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");
                        // Continue with next file instead of failing entire sync
                    }
                }
                LogFileWriter.LogMessage($"Uploaded {uploaded} files");

                // Delete files
                taskInstance.SetProgressPos(80, $"Deleting {filesToDelete.Count} files from server...");
                int deleted = 0;
                int deleteTotal = filesToDelete.Count > 0 ? filesToDelete.Count : 1;
                foreach (var relativePath in filesToDelete)
                {
                    try
                    {
                        bool success = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
                        if (success)
                        {
                            deleted++;
                            LogFileWriter.LogMessage($"Deleted from server: {relativePath}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"Failed to delete from server: {relativePath}");
                        }
                        int progress = 80 + (int)((deleted / (float)deleteTotal) * 10);
                        taskInstance.SetProgressPos(progress, $"Deleted {deleted}/{filesToDelete.Count} files");
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Error deleting file {relativePath}: {ex.Message}");
                        // Continue with next file instead of failing entire sync
                    }
                }
                LogFileWriter.LogMessage($"Deleted {deleted} files from server");

                LogFileWriter.LogMessage($"ProcessCompleteSyncOperation: Completed - {uploaded} uploaded, {deleted} deleted");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"ProcessCompleteSyncOperation failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        #endregion

        #region Sync Operations (Old Format - Legacy)

        private async Task ExecuteUpload(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, string vaultRootPath)
        {
            LogFileWriter.LogMessage($"=== ExecuteUpload: Processing {files.Length} files ===");

            int processed = 0;
            int total = files.Length;

            foreach (EdmCmdData fileData in files)
            {
                // Use file path instead of IDs - IDs may not be valid on task host
                string localPath = fileData.mbsStrData1; // File path from metadata
                LogFileWriter.LogMessage($"Uploading file: {localPath}");

                if (string.IsNullOrEmpty(localPath))
                {
                    LogFileWriter.LogMessage($"Skipping - no file path provided");
                    continue;
                }

                // Check if file exists in vault using PDM API (not local view)
                if (!FileExistsInVault(vault, localPath))
                {
                    string errorMsg = $"File not found in vault: {localPath}";
                    LogFileWriter.LogError(errorMsg);
                    throw new FileNotFoundException(errorMsg);
                }

                string relativePath = GetRelativePath(vaultRootPath, localPath);
                LogFileWriter.LogMessage($"Relative path: {relativePath}");

                // Upload to Leo AI (will throw on error to fail task)
                await UpdateFilesToLeoAI(vault, new[] { localPath }, vaultRootPath);

                processed++;
                int progress = 40 + (int)((processed / (float)total) * 50);
                taskInstance.SetProgressPos(progress, $"Uploaded {processed}/{total} files");

                LogFileWriter.LogMessage($"Upload successful: {relativePath}");
            }

            LogFileWriter.LogMessage($"=== ExecuteUpload: Completed - {processed}/{total} files uploaded ===");
        }

        private async Task ExecuteDelete(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, Dictionary<string, string> additionalData)
        {
            LogFileWriter.LogMessage($"=== ExecuteDelete: Processing {files.Length} files ===");

            // Get file paths from additional data
            string filePathsStr = additionalData.ContainsKey("FilePaths") ? additionalData["FilePaths"] : "";
            string[] filePaths = string.IsNullOrEmpty(filePathsStr) ? new string[0] : filePathsStr.Split('|');

            LogFileWriter.LogMessage($"File paths from metadata: {string.Join(", ", filePaths)}");

            int processed = 0;
            int total = filePaths.Length;

            foreach (string filePath in filePaths)
            {
                try
                {
                    LogFileWriter.LogMessage($"Deleting file from Leo AI: {filePath}");

                    string relativePath = GetRelativePath(vault.RootFolderPath, filePath);

                    // Call delete API with just directoryId and file path (no componentId needed)
                    bool deleted = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
                    if (deleted)
                    {
                        LogFileWriter.LogMessage($"Deleted from server: {relativePath}");
                    }
                    else
                    {
                        LogFileWriter.LogMessage($"Delete returned false for: {relativePath}");
                    }

                    processed++;
                    int progress = 30 + (int)((processed / (float)total) * 50);
                    taskInstance.SetProgressPos(progress, $"Deleted {processed}/{total} files");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to delete file {filePath}: {ex.Message}");
                    throw; // Propagate exception to fail the task
                }
            }

            LogFileWriter.LogMessage($"=== ExecuteDelete: Completed - {processed}/{total} files deleted ===");
        }

        private async Task ExecuteMove(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, Dictionary<string, string> additionalData)
        {
            LogFileWriter.LogMessage($"=== ExecuteMove: Starting ===");

            bool isFolder = additionalData.ContainsKey("IsFolder") && additionalData["IsFolder"] == "true";

            if (isFolder)
            {
                // Folder move - expand and process all files
                await ExecuteFolderMove(vault, taskInstance, files, additionalData);
            }
            else
            {
                // File move - process individual files
                await ExecuteFileMove(vault, taskInstance, additionalData);
            }

            LogFileWriter.LogMessage($"=== ExecuteMove: Completed ===");
        }

        private async Task ExecuteFileMove(IEdmVault11 vault, IEdmTaskInstance taskInstance, Dictionary<string, string> additionalData)
        {
            string oldPathsStr = additionalData.ContainsKey("OldPaths") ? additionalData["OldPaths"] : "";
            string newPathsStr = additionalData.ContainsKey("NewPaths") ? additionalData["NewPaths"] : "";

            string[] oldPaths = string.IsNullOrEmpty(oldPathsStr) ? new string[0] : oldPathsStr.Split('|');
            string[] newPaths = string.IsNullOrEmpty(newPathsStr) ? new string[0] : newPathsStr.Split('|');

            if (oldPaths.Length != newPaths.Length)
            {
                throw new Exception($"Path count mismatch: {oldPaths.Length} old paths vs {newPaths.Length} new paths");
            }

            int processed = 0;
            int total = oldPaths.Length > 0 ? oldPaths.Length : 1; // Prevent division by zero

            for (int i = 0; i < oldPaths.Length; i++)
            {
                try
                {
                    string oldPath = oldPaths[i];
                    string newPath = newPaths[i];

                    LogFileWriter.LogMessage($"Moving file: {oldPath} -> {newPath}");

                    string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldPath);
                    string newRelativePath = GetRelativePath(vault.RootFolderPath, newPath);

                    // Upload file with new path first - check if exists in vault
                    if (FileExistsInVault(vault, newPath))
                    {
                        await UpdateFilesToLeoAI(vault, new[] { newPath }, vault.RootFolderPath);
                        LogFileWriter.LogMessage($"Uploaded file with new path: {newRelativePath}");
                    }

                    // Then delete old path
                    bool deleted = await _leoClient.DeleteFileAsync(_directoryId, oldRelativePath);
                    if (deleted)
                    {
                        LogFileWriter.LogMessage($"Deleted old file path: {oldRelativePath}");
                    }

                    processed++;
                    int progress = 40 + (int)((processed / (float)total) * 50);
                    taskInstance.SetProgressPos(progress, $"Moved {processed}/{oldPaths.Length} files");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to move file {oldPaths[i]}: {ex.Message}");
                }
            }
        }

        private async Task ExecuteFolderMove(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, Dictionary<string, string> additionalData)
        {
            string oldFolderName = additionalData.ContainsKey("OldPaths") ? additionalData["OldPaths"] : "";
            string newFolderName = additionalData.ContainsKey("NewPaths") ? additionalData["NewPaths"] : "";

            LogFileWriter.LogMessage($"Folder move: {oldFolderName} -> {newFolderName}");

            taskInstance.SetProgressPos(20, "Getting folder from vault...");

            // Get folder from vault - use first file in dummy files array to find folder
            IEdmFolder5 targetFolder = null;
            if (files.Length > 0)
            {
                IEdmFile5 dummyFile = (IEdmFile5)vault.GetObject(EdmObjectType.EdmObject_File, files[0].mlObjectID1);
                if (dummyFile != null)
                {
                    targetFolder = (IEdmFolder5)vault.GetObject(EdmObjectType.EdmObject_Folder, files[0].mlObjectID2);
                }
            }

            if (targetFolder == null)
            {
                throw new Exception($"Could not get folder from dummy file");
            }

            string newFolderPath = targetFolder.LocalPath;
            // Reconstruct old path by replacing new name with old name
            string oldFolderPath = newFolderPath;
            if (newFolderPath.EndsWith(newFolderName))
            {
                oldFolderPath = newFolderPath.Substring(0, newFolderPath.Length - newFolderName.Length) + oldFolderName;
            }

            LogFileWriter.LogMessage($"Folder paths - Old: {oldFolderPath}, New: {newFolderPath}");

            taskInstance.SetProgressPos(30, "Enumerating files in folder...");

            // Get all files recursively
            List<string> oldFilePaths = new List<string>();
            List<string> newFilePaths = new List<string>();
            EnumerateFolderFilesRecursive(targetFolder, oldFolderPath, newFolderPath, oldFilePaths, newFilePaths);

            LogFileWriter.LogMessage($"Found {oldFilePaths.Count} files to move");

            // Process each file individually
            int processed = 0;
            int total = oldFilePaths.Count > 0 ? oldFilePaths.Count : 1; // Prevent division by zero

            for (int i = 0; i < oldFilePaths.Count; i++)
            {
                try
                {
                    string oldFilePath = oldFilePaths[i];
                    string newFilePath = newFilePaths[i];

                    LogFileWriter.LogMessage($"Moving file: {oldFilePath} -> {newFilePath}");

                    string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldFilePath);
                    string newRelativePath = GetRelativePath(vault.RootFolderPath, newFilePath);

                    // Upload with new path first - check if exists in vault
                    if (FileExistsInVault(vault, newFilePath))
                    {
                        await UpdateFilesToLeoAI(vault, new[] { newFilePath }, vault.RootFolderPath);
                        LogFileWriter.LogMessage($"Uploaded: {newRelativePath}");
                    }

                    // Delete old path
                    bool deleted = await _leoClient.DeleteFileAsync(_directoryId, oldRelativePath);
                    if (deleted)
                    {
                        LogFileWriter.LogMessage($"Deleted old path: {oldRelativePath}");
                    }

                    processed++;
                    int progress = 40 + (int)((processed / (float)total) * 50);
                    taskInstance.SetProgressPos(progress, $"Moved {processed}/{oldFilePaths.Count} files");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to move file {oldFilePaths[i]}: {ex.Message}");
                }
            }
        }

        private void EnumerateFolderFilesRecursive(IEdmFolder5 folder, string oldBasePath, string newBasePath, List<string> oldFilePaths, List<string> newFilePaths)
        {
            // Get all files in current folder
            IEdmPos5 pos = folder.GetFirstFilePosition();
            while (!pos.IsNull)
            {
                IEdmFile5 file = folder.GetNextFile(pos);
                string filePath = file.GetLocalPath(folder.ID);

                // Only process SLDPRT, SLDASM, SLDDRW files
                if (!string.IsNullOrEmpty(filePath) && IsProcessableFile(filePath))
                {
                    // Calculate old path by replacing new base with old base
                    string relativePath = filePath.Substring(newBasePath.Length).TrimStart('\\', '/');
                    string oldFilePath = Path.Combine(oldBasePath, relativePath);

                    oldFilePaths.Add(oldFilePath);
                    newFilePaths.Add(filePath);
                }
            }

            // Process subfolders recursively
            IEdmPos5 subPos = folder.GetFirstSubFolderPosition();
            while (!subPos.IsNull)
            {
                IEdmFolder5 subFolder = folder.GetNextSubFolder(subPos);
                EnumerateFolderFilesRecursive(subFolder, oldBasePath, newBasePath, oldFilePaths, newFilePaths);
            }
        }

        private bool IsProcessableFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string ext = Path.GetExtension(filePath).ToUpper();
            return ext == ".SLDPRT" ||
                   ext == ".SLDASM" ||
                   ext == ".STEP" ||
                   ext == ".STP" ||
                   ext == ".PRT" ||
                   ext == ".ASM" ||
                   ext == ".IPT" ||
                   ext == ".IAM" ||
                   ext == ".X_T" ||
                   ext == ".XT" ||
                   ext == ".TXT" ||
                   ext == ".PDF" ||
                   ext == ".DOC" ||
                   ext == ".DOCX";
        }

        private async Task ExecuteRename(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, Dictionary<string, string> additionalData)
        {
            LogFileWriter.LogMessage("=== ExecuteRename: Using same logic as Move ===");
            await ExecuteMove(vault, taskInstance, files, additionalData);
        }

        private async Task ExecuteCompleteSync(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files)
        {
            LogFileWriter.LogMessage("=== ExecuteCompleteSync: Starting full vault sync ===");

            try
            {
                taskInstance.SetProgressPos(30, "Enumerating vault files...");

                // Get all files from vault using SolidWorksPdmHelper
                SolidWorksPdmHelper pdmHelper = new SolidWorksPdmHelper(vault);
                pdmHelper.ProcessFolders(vault);
                List<FileData> vaultFiles = pdmHelper.FilesInfo;
                LogFileWriter.LogMessage($"Found {vaultFiles.Count} files in vault");

                taskInstance.SetProgressPos(40, "Getting server state...");

                // Get all files from server
                var serverData = await _leoClient.GetSyncMetadataAsync(_directoryId);
                Dictionary<string, SyncMetadataFile> serverFiles = new Dictionary<string, SyncMetadataFile>(StringComparer.OrdinalIgnoreCase);
                if (serverData?.Files != null)
                {
                    foreach (var file in serverData.Files)
                    {
                        serverFiles[file.FilePathInDirectory] = file;
                    }
                }
                LogFileWriter.LogMessage($"Found {serverFiles.Count} files on server");

                taskInstance.SetProgressPos(50, "Comparing vault and server state...");

                // Build sets for comparison
                HashSet<string> vaultPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var vaultFile in vaultFiles)
                {
                    string relativePath = GetRelativePath(vault.RootFolderPath, vaultFile.file);
                    vaultPaths.Add(relativePath);
                }

                // Find files to upload (in vault but not on server, or changed)
                List<string> filesToUpload = new List<string>();
                foreach (var vaultFile in vaultFiles)
                {
                    string relativePath = GetRelativePath(vault.RootFolderPath, vaultFile.file);
                    if (!serverFiles.ContainsKey(relativePath))
                    {
                        filesToUpload.Add(vaultFile.file);
                        LogFileWriter.LogMessage($"New file to upload: {relativePath}");
                    }
                    else
                    {
                        // Check if file changed (compare checksum)
                        var serverFile = serverFiles[relativePath];
                        if (serverFile.CheckSum != vaultFile.checkSum)
                        {
                            filesToUpload.Add(vaultFile.file);
                            LogFileWriter.LogMessage($"Modified file to upload: {relativePath}");
                        }
                    }
                }

                // Find files to delete (on server but not in vault - vault is master)
                List<string> filesToDelete = new List<string>();
                foreach (var serverPath in serverFiles.Keys)
                {
                    if (!vaultPaths.Contains(serverPath))
                    {
                        filesToDelete.Add(serverPath);
                        LogFileWriter.LogMessage($"File to delete from server: {serverPath}");
                    }
                }

                LogFileWriter.LogMessage($"Files to upload: {filesToUpload.Count}, Files to delete: {filesToDelete.Count}");

                // Upload files (using temp file copy approach)
                taskInstance.SetProgressPos(60, $"Uploading {filesToUpload.Count} files...");
                int uploaded = 0;
                int uploadTotal = filesToUpload.Count > 0 ? filesToUpload.Count : 1; // Prevent division by zero
                foreach (var filePath in filesToUpload)
                {
                    try
                    {
                        await UpdateFilesToLeoAI(vault, new[] { filePath }, vault.RootFolderPath);
                        uploaded++;
                        int progress = 60 + (int)((uploaded / (float)uploadTotal) * 20);
                        taskInstance.SetProgressPos(progress, $"Uploaded {uploaded}/{filesToUpload.Count} files");
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");
                        // Continue with next file instead of failing entire sync
                    }
                }
                LogFileWriter.LogMessage($"Uploaded {uploaded} files");

                // Delete files
                taskInstance.SetProgressPos(80, $"Deleting {filesToDelete.Count} files from server...");
                int deleted = 0;
                int deleteTotal = filesToDelete.Count > 0 ? filesToDelete.Count : 1; // Prevent division by zero
                foreach (var relativePath in filesToDelete)
                {
                    try
                    {
                        bool success = await _leoClient.DeleteFileAsync(_directoryId, relativePath);
                        if (success)
                        {
                            deleted++;
                            LogFileWriter.LogMessage($"Deleted from server: {relativePath}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"Failed to delete from server: {relativePath}");
                        }
                        int progress = 80 + (int)((deleted / (float)deleteTotal) * 10);
                        taskInstance.SetProgressPos(progress, $"Deleted {deleted}/{filesToDelete.Count} files");
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Error deleting file {relativePath}: {ex.Message}");
                        // Continue with next file instead of failing entire sync
                    }
                }
                LogFileWriter.LogMessage($"Deleted {deleted} files from server");

                LogFileWriter.LogMessage($"=== ExecuteCompleteSync: Completed - {uploaded} uploaded, {deleted} deleted ===");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"ExecuteCompleteSync failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        #endregion

        #region Helper Methods (copied from SwPdmAddinMain)

        /// <summary>
        /// Deletes a metadata file from vault
        /// </summary>
        private void DeleteMetadataFile(IEdmVault11 vault, IEdmFile5 file, IEdmFolder5 folder)
        {
            try
            {
                if (file == null || folder == null)
                {
                    LogFileWriter.LogError("Cannot delete metadata file: file or folder is null");
                    return;
                }

                // Delete the file from vault using folder.DeleteFile
                folder.DeleteFile(0, file.ID, true); // window handle, file ID, permanently delete
                LogFileWriter.LogMessage($"Deleted metadata file from vault: {file.Name}");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error deleting metadata file: {ex.Message}");
                throw;
            }
        }

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
                }

                if (string.IsNullOrEmpty(configFilePath))
                {
                    throw new FileNotFoundException("Leo AI authentication configuration not found!\n\n" +
                        "Please place the LeoAuthKey.json file in one of the following locations:\n" +
                        "1. Default location: C:\\Program Files\\LeoAISwPdmAddIn\\LeoAuthKey.json\n" +
                        "2. Custom location specified in LEO_AUTH_KEY environment variable (stored in registry per vault)");
                }

                LogFileWriter.LogMessage($"Reading auth config from: {configFilePath}");
                string json = File.ReadAllText(configFilePath);
                var authConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<LeoAuthConfig>(json);

                if (authConfig == null || string.IsNullOrEmpty(authConfig.ApiKey) || string.IsNullOrEmpty(authConfig.ProjectId))
                {
                    throw new Exception("Invalid auth config - missing ApiKey or ProjectId");
                }

                return authConfig;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to read auth config: {ex.Message}");
                throw;
            }
        }

        private async Task<string> GetOrCreateDirectoryId(string vaultPath)
        {
            LogFileWriter.LogMessage($"Getting/creating directory for vault: {vaultPath}");

            string macAddress = LeoAIDataUtilities.GetFormattedMacAddress();
            var directories = await _leoClient.GetDirectoryInfoAsync(macAddress);
            var directory = directories.FirstOrDefault(d => d.Uri.Equals(vaultPath, StringComparison.OrdinalIgnoreCase));

            if (directory != null)
            {
                LogFileWriter.LogMessage($"Directory exists: {directory.Id}");
                return directory.Id;
            }

            LogFileWriter.LogMessage("Directory not found, creating new directory");
            string directoryId = await _leoClient.CreateDirectoryAsync(macAddress, vaultPath);
            LogFileWriter.LogMessage($"Created directory: {directoryId}");

            return directoryId;
        }

        private async Task UpdateFilesToLeoAI(IEdmVault11 vault, string[] filePaths, string vaultRootPath)
        {
            LogFileWriter.LogMessage($"Updating {filePaths.Length} files to Leo AI");

            // Sort files by type: parts first, then assemblies, then other files
            // This ensures dependencies exist before assemblies reference them
            var sortedFiles = SortFilesByUploadOrder(filePaths);
            LogFileWriter.LogMessage($"Upload order: {sortedFiles.partFiles.Length} parts, {sortedFiles.assemblyFiles.Length} assemblies, {sortedFiles.otherFiles.Length} other files");

            // Upload parts first
            await UploadFileGroup(vault, sortedFiles.partFiles, vaultRootPath, "Parts");

            // Then assemblies
            await UploadFileGroup(vault, sortedFiles.assemblyFiles, vaultRootPath, "Assemblies");

            // Finally other files (PDFs, images, etc.)
            await UploadFileGroup(vault, sortedFiles.otherFiles, vaultRootPath, "Other files");
        }

        /// <summary>
        /// Sorts files into three groups for ordered upload: parts, assemblies, other
        /// Parts are uploaded first so assemblies can reference them
        /// Supports multiple CAD formats: SOLIDWORKS, Creo, NX, CATIA, etc.
        /// </summary>
        private (string[] partFiles, string[] assemblyFiles, string[] otherFiles) SortFilesByUploadOrder(string[] filePaths)
        {
            List<string> partFiles = new List<string>();
            List<string> assemblyFiles = new List<string>();
            List<string> otherFiles = new List<string>();

            // Part file extensions (various CAD systems)
            HashSet<string> partExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".sldprt",  // SOLIDWORKS Part
                ".prt",     // Creo/Pro-E Part, NX Part
                ".par",     // Solid Edge Part
                ".ipt",     // Inventor Part
                ".catpart"  // CATIA Part
            };

            // Assembly file extensions (various CAD systems)
            HashSet<string> assemblyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".sldasm",  // SOLIDWORKS Assembly
                ".asm",     // Creo/Pro-E Assembly, NX Assembly
                ".psm",     // Solid Edge Assembly
                ".iam",     // Inventor Assembly
                ".catproduct" // CATIA Product (Assembly)
            };

            foreach (string filePath in filePaths)
            {
                string extension = Path.GetExtension(filePath);

                if (partExtensions.Contains(extension))
                {
                    partFiles.Add(filePath);
                }
                else if (assemblyExtensions.Contains(extension))
                {
                    assemblyFiles.Add(filePath);
                }
                else
                {
                    otherFiles.Add(filePath);
                }
            }

            return (partFiles.ToArray(), assemblyFiles.ToArray(), otherFiles.ToArray());
        }

        /// <summary>
        /// Uploads a group of files with proper error handling
        /// </summary>
        private async Task UploadFileGroup(IEdmVault11 vault, string[] filePaths, string vaultRootPath, string groupName)
        {
            if (filePaths.Length == 0)
            {
                LogFileWriter.LogMessage($"No files in group '{groupName}' to upload");
                return;
            }

            LogFileWriter.LogMessage($"=== Uploading {groupName}: {filePaths.Length} files ===");

            foreach (string filePath in filePaths)
            {
                string relativePath = GetRelativePath(vaultRootPath, filePath);
                LogFileWriter.LogMessage($"Processing: {relativePath}");

                string actualFilePath = null;
                bool needsCleanup = false;

                try
                {
                    // Ensure we have full path for GetFileFromPath
                    string fullPath = EnsureFullPath(vault, filePath);

                    // Get readable file path (archive or temp copy)
                    IEdmFolder5 folder;
                    IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);

                    if (file != null && folder != null)
                    {
                        (actualFilePath, needsCleanup) = GetReadableFilePath(vault, filePath, folder.ID);
                        LogFileWriter.LogMessage($"File path for upload: {actualFilePath} (cleanup: {needsCleanup})");
                    }
                    else
                    {
                        // Fallback: use the provided path directly
                        actualFilePath = filePath;
                        LogFileWriter.LogMessage($"Could not get file from vault, using path directly: {actualFilePath}");
                    }

                    // CreateFileAsync handles both create and update (with automatic retry for rate limits)
                    var fileInfo = await _leoClient.CreateFileAsync(_directoryId, vaultRootPath, filePath, actualFilePath, null);

                    if (fileInfo != null)
                    {
                        LogFileWriter.LogMessage($"File synced to server: {relativePath} (ID: {fileInfo.ComponentId})");
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to update file {filePath}: {ex.Message}");
                    throw new Exception($"Failed to sync file {relativePath}: {ex.Message}", ex);
                }
                finally
                {
                    // Clean up temp file if needed
                    if (needsCleanup && !string.IsNullOrEmpty(actualFilePath))
                    {
                        DeleteTempFile(actualFilePath);
                    }
                }
            }

            LogFileWriter.LogMessage($"=== Completed uploading {groupName} ===");
        }

        /// <summary>
        /// Gets the archive root path for the vault from:
        /// 1. Environment variable LEOAI_PDM_ARCHIVE_ROOT (user override)
        /// 2. Windows Registry (ArchiveServer\Vaults\{vaultName}\ArchiveTable - using ArchiveTable0 path, removing \0 suffix)
        /// 3. Default location
        /// All paths are validated for existence before returning
        /// </summary>
        private string GetArchiveRootPath(string vaultName)
        {
            try
            {
                // Option 1: Check environment variable first (allows user override for non-standard installs)
                string envOverride = Environment.GetEnvironmentVariable("LEOAI_PDM_ARCHIVE_ROOT");
                if (!string.IsNullOrEmpty(envOverride))
                {
                    string envPath = Path.Combine(envOverride, vaultName);
                    if (Directory.Exists(envPath))
                    {
                        LogFileWriter.LogMessage($"Archive root from environment variable: {envPath}");
                        return envPath;
                    }
                    else
                    {
                        LogFileWriter.LogMessage($"Environment variable path does not exist: {envPath}, trying next option");
                    }
                }

                // Option 2: Try reading from registry: ArchiveServer\Vaults\{vaultName}\ArchiveTable
                string registryPath = $@"SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise\ArchiveServer\Vaults\{vaultName}\ArchiveTable";
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        // Try ArchiveTable0 first (most common)
                        object archiveTable0 = key.GetValue("ArchiveTable0");
                        if (archiveTable0 != null)
                        {
                            string archiveTablePath = archiveTable0.ToString();
                            // Remove the \0 suffix to get the base archive path
                            string archivePath = archiveTablePath.TrimEnd('\\', '0');

                            if (Directory.Exists(archivePath))
                            {
                                LogFileWriter.LogMessage($"Archive root from registry ArchiveTable0: {archivePath}");
                                return archivePath;
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"Registry ArchiveTable0 path does not exist: {archivePath}, trying next option");
                            }
                        }
                    }
                }

                // Option 3: Fallback to default location
                string defaultPath = $@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\Data\{vaultName}";
                if (Directory.Exists(defaultPath))
                {
                    LogFileWriter.LogMessage($"Using default archive path: {defaultPath}");
                    return defaultPath;
                }
                else
                {
                    LogFileWriter.LogMessage($"Default path does not exist: {defaultPath}");
                    LogFileWriter.LogMessage($"WARNING: No valid archive path found. Files will be copied using GetFileCopy.");
                    return null; // Return null to indicate no archive path found - will fall back to GetFileCopy
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error reading archive path from registry: {ex.Message}");
                return null; // Return null to fall back to GetFileCopy
            }
        }

        /// <summary>
        /// Constructs the actual archive file path based on File ID and Version.
        /// Archive structure: {ArchiveRoot}\{LastHexDigit}\{FileID_8DigitHex}\{Version_8DigitHex}.{Extension}
        /// Example: test_pro\0\00000010\00000002.SLDPRT for FileID 16 version 2
        /// </summary>
        private string GetArchiveFilePath(IEdmVault11 vault, IEdmFile5 file, string logicalFilePath)
        {
            try
            {
                string archiveRoot = GetArchiveRootPath(vault.Name);
                if (string.IsNullOrEmpty(archiveRoot))
                {
                    LogFileWriter.LogMessage("No archive root found, cannot construct archive path");
                    return null;
                }

                int fileID = file.ID;
                int currentVersion = file.CurrentVersion;

                string hexID = fileID.ToString("X8"); // Convert to 8-digit hexadecimal (padded with zeros)
                string hexVersion = currentVersion.ToString("X8"); // Version as 8-digit hex
                string lastHexDigit = hexID.Substring(hexID.Length - 1); // Last hex digit determines subfolder
                string extension = Path.GetExtension(logicalFilePath);

                // Structure: {ArchiveRoot}\{LastHexDigit}\{FileID_8DigitHex}\{Version_8DigitHex}.{Extension}
                string archivePath = Path.Combine(archiveRoot, lastHexDigit, hexID, $"{hexVersion}{extension}");

                LogFileWriter.LogMessage($"Archive path construction:");
                LogFileWriter.LogMessage($"  Logical file: {logicalFilePath}");
                LogFileWriter.LogMessage($"  File ID: {fileID} (hex: {hexID})");
                LogFileWriter.LogMessage($"  Current version: {currentVersion} (hex: {hexVersion})");
                LogFileWriter.LogMessage($"  Archive root: {archiveRoot}");
                LogFileWriter.LogMessage($"  Constructed path: {archivePath}");
                LogFileWriter.LogMessage($"  File exists: {File.Exists(archivePath)}");

                return archivePath;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to construct archive path: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Gets the best available file path for reading - tries archive path first, falls back to GetFileCopy if needed.
        /// Returns: (actualFilePath, needsCleanup)
        /// </summary>
        private (string filePath, bool needsCleanup) GetReadableFilePath(IEdmVault11 vault, string logicalPath, int folderID)
        {
            try
            {
                // Ensure we have full path for GetFileFromPath
                string fullPath = EnsureFullPath(vault, logicalPath);

                // Get file object from vault
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);

                if (file == null)
                {
                    throw new Exception($"Could not find file in vault: {logicalPath}");
                }

                // Try archive path first (best option - no copy needed)
                string archivePath = GetArchiveFilePath(vault, file, logicalPath);
                if (archivePath != null && File.Exists(archivePath))
                {
                    LogFileWriter.LogMessage($"File found in archive: {archivePath}");
                    return (archivePath, false); // No cleanup needed
                }

                LogFileWriter.LogMessage($"File not in archive, using GetFileCopy to temp location");

                // Fallback: Copy file to temp location using PDM API
                string tempDir = Path.Combine(Path.GetTempPath(), "LeoAI_PDM_Temp");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                string fileName = Path.GetFileName(logicalPath);
                string tempFilePath = Path.Combine(tempDir, $"{Guid.NewGuid()}_{fileName}");

                file.GetFileCopy(0, 0, folderID, (int)EdmGetCmdFlags.Egcf_Nothing, tempFilePath);

                LogFileWriter.LogMessage($"Copied to temp: {tempFilePath}");
                return (tempFilePath, true); // Cleanup needed
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to get readable file path for {logicalPath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Safely deletes a temporary file
        /// </summary>
        private void DeleteTempFile(string tempFilePath)
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                    LogFileWriter.LogMessage($"Deleted temp file: {tempFilePath}");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage($"Warning: Could not delete temp file {tempFilePath}: {ex.Message}");
                // Don't throw - temp file cleanup failure is not critical
            }
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relative.Replace(Path.DirectorySeparatorChar, '/');
            }
            return fullPath.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Checks if a file exists in the PDM vault (not in local view, but in vault archive/database)
        /// This is safer than File.Exists() which only checks local view
        /// Handles both full paths and relative paths/filenames by constructing full path if needed
        /// </summary>
        private bool FileExistsInVault(IEdmVault11 vault, string filePath)
        {
            try
            {
                string fullPath = filePath;

                // If filePath is not absolute (just filename or relative path), construct full path
                if (!Path.IsPathRooted(filePath))
                {
                    fullPath = Path.Combine(vault.RootFolderPath, filePath);
                    LogFileWriter.LogMessage($"Constructed full path: {fullPath} from relative path: {filePath}");
                }

                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);
                bool exists = file != null;

                if (exists)
                {
                    LogFileWriter.LogMessage($"File exists in vault: {fullPath} (ID: {file.ID})");
                }
                else
                {
                    LogFileWriter.LogMessage($"File not found in vault: {fullPath}");
                }

                return exists;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error checking file existence in vault for {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensures a file path is absolute (fully qualified) and uses the task host's vault root
        /// Handles paths from different vault views (client local view vs task host local view)
        /// If the path is relative, constructs full path by combining with task host's vault root
        /// If the path is absolute but from a different vault view, extracts relative portion and recombines with task host's vault root
        /// </summary>
        private string EnsureFullPath(IEdmVault11 vault, string filePath)
        {
            string taskHostVaultRoot = vault.RootFolderPath;

            // If path is not rooted, it's relative - just combine with vault root
            if (!Path.IsPathRooted(filePath))
            {
                string fullPath = Path.Combine(taskHostVaultRoot, filePath);
                LogFileWriter.LogMessage($"EnsureFullPath: Converted relative path '{filePath}' to absolute path '{fullPath}'");
                return fullPath;
            }

            // Path is absolute - check if it starts with task host's vault root
            if (filePath.StartsWith(taskHostVaultRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Path already uses task host's vault root - return as-is
                LogFileWriter.LogMessage($"EnsureFullPath: Path already uses task host vault root: '{filePath}'");
                return filePath;
            }

            // Path is absolute but uses different vault root (e.g., from client's local view)
            // Need to extract the vault-relative portion and recombine with task host's vault root
            LogFileWriter.LogMessage($"EnsureFullPath: Path uses different vault root: '{filePath}', task host root: '{taskHostVaultRoot}'");

            // Try to find vault-relative path by looking for common vault structure
            // Strategy: Use PDM API to get file by path, then reconstruct using task host's vault root
            try
            {
                // Get file from vault using the provided path
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(filePath, out folder);

                if (file != null && folder != null)
                {
                    // Success - get the path using task host's vault root
                    string taskHostPath = file.GetLocalPath(folder.ID);
                    LogFileWriter.LogMessage($"EnsureFullPath: Resolved via PDM API to task host path: '{taskHostPath}'");
                    return taskHostPath;
                }
                else
                {
                    LogFileWriter.LogMessage($"EnsureFullPath: PDM API returned null - trying path extraction fallback");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage($"EnsureFullPath: PDM API lookup failed: {ex.Message} - trying path extraction fallback");
            }

            // Fallback: Extract vault-relative path by removing known vault root patterns
            // Common pattern: C:\test_pro\ or C:\Users\...\test_pro\
            // We need to find where the vault name portion starts
            string vaultName = vault.Name;
            int vaultNameIndex = filePath.IndexOf(vaultName, StringComparison.OrdinalIgnoreCase);

            if (vaultNameIndex >= 0)
            {
                // Extract everything after "vaultName\"
                int relativeStartIndex = vaultNameIndex + vaultName.Length;
                if (relativeStartIndex < filePath.Length && (filePath[relativeStartIndex] == '\\' || filePath[relativeStartIndex] == '/'))
                {
                    relativeStartIndex++; // Skip the separator
                }

                if (relativeStartIndex < filePath.Length)
                {
                    string relativePath = filePath.Substring(relativeStartIndex);
                    string reconstructedPath = Path.Combine(taskHostVaultRoot, relativePath);
                    LogFileWriter.LogMessage($"EnsureFullPath: Extracted relative path '{relativePath}', reconstructed as '{reconstructedPath}'");
                    return reconstructedPath;
                }
            }

            // Last resort: use path as-is and hope for the best
            LogFileWriter.LogMessage($"EnsureFullPath: Could not normalize path - using as-is: '{filePath}'");
            return filePath;
        }

        #endregion
    }
}
