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
            LogFileWriter.LogMessage("=== LeoAiSyncTask.GetAddInInfo called ===");

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

                LogFileWriter.LogMessage($"Task add-in registered: {TASK_NAME}");
                LogFileWriter.LogMessage("Registered for EdmCmd_TaskSetup and EdmCmd_TaskRun");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"GetAddInInfo failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        public void OnCmd(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage($"=== LeoAiSyncTask.OnCmd called - CmdType: {poCmd.meCmdType} ===");

            try
            {
                switch (poCmd.meCmdType)
                {
                    case EdmCmdType.EdmCmd_TaskSetup:
                        LogFileWriter.LogMessage("Handling EdmCmd_TaskSetup");
                        OnTaskSetup(ref poCmd, ref ppoData);
                        break;

                    case EdmCmdType.EdmCmd_TaskRun:
                        LogFileWriter.LogMessage("Handling EdmCmd_TaskRun");
                        OnTaskRun(ref poCmd, ref ppoData);
                        break;

                    default:
                        LogFileWriter.LogMessage($"Unhandled command type: {poCmd.meCmdType}");
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
            LogFileWriter.LogMessage("=== OnTaskSetup: Configuring task properties ===");

            try
            {
                IEdmTaskProperties taskProps = (IEdmTaskProperties)poCmd.mpoExtra;

                // Task does not support manual scheduling or launch
                // It's only triggered programmatically by the client add-in
                taskProps.TaskFlags = 0;

                // Read and store the retry count from task configuration
                _maxRetries = taskProps.RetryCount;

                LogFileWriter.LogMessage($"Task ID: {taskProps.TaskID}");
                LogFileWriter.LogMessage($"Task Name: {taskProps.TaskName}");
                LogFileWriter.LogMessage($"Task GUID: {taskProps.TaskGUID}");
                LogFileWriter.LogMessage($"Task Retry Count: {_maxRetries}");
                LogFileWriter.LogMessage("Task configured: no scheduling, no manual launch (client-triggered only)");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnTaskSetup failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void OnTaskRun(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage("=== OnTaskRun: Starting sync operation ===");

            IEdmTaskInstance taskInstance = null;
            string metadataPath = null;
            string metadataFolder = null;
            string metadataFileName = null;
            long timestamp = 0;
            int currentTrial = 0;

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

                // Log sample file path to understand how GetLocalPath works on task host
                if (ppoData != null && ppoData.Length > 0)
                {
                    try
                    {
                        IEdmFile5 sampleFile = (IEdmFile5)vault.GetObject(EdmObjectType.EdmObject_File, ppoData[0].mlObjectID1);
                        if (sampleFile != null)
                        {
                            string samplePath = sampleFile.GetLocalPath(ppoData[0].mlObjectID2);
                            LogFileWriter.LogMessage($"Sample file GetLocalPath: {samplePath}");
                            LogFileWriter.LogMessage($"Sample file exists at path: {File.Exists(samplePath)}");
                        }
                    }
                    catch (Exception diagEx)
                    {
                        LogFileWriter.LogMessage($"Diagnostic logging error: {diagEx.Message}");
                    }
                }

                // Read metadata from vault hidden folder
                // Files are named: {unixTimestamp}_{trialNumber}.json
                LogFileWriter.LogMessage("Looking for metadata files in vault...");
                string vaultRoot = vault.RootFolderPath;
                metadataFolder = Path.Combine(vaultRoot, ".LeoAI_Metadata");

                if (!Directory.Exists(metadataFolder))
                {
                    throw new Exception($"Metadata folder not found in vault: {metadataFolder}");
                }

                // Find the earliest metadata file (lowest timestamp)
                string[] metadataFiles = Directory.GetFiles(metadataFolder, "*.json")
                    .OrderBy(f => Path.GetFileName(f))
                    .ToArray();

                if (metadataFiles.Length == 0)
                {
                    throw new Exception($"No metadata files found in: {metadataFolder}");
                }

                metadataPath = metadataFiles[0]; // Earliest file
                metadataFileName = Path.GetFileName(metadataPath);
                LogFileWriter.LogMessage($"Processing earliest metadata file: {metadataFileName}");

                // Parse filename to get timestamp and trial number
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(metadataFileName);
                string[] parts = fileNameWithoutExt.Split('_');
                timestamp = long.Parse(parts[0]);
                currentTrial = int.Parse(parts[1]);
                LogFileWriter.LogMessage($"Timestamp: {timestamp}, Current trial: {currentTrial}");

                // Read metadata
                string metadataJson = File.ReadAllText(metadataPath);
                dynamic metadataObj = Newtonsoft.Json.JsonConvert.DeserializeObject(metadataJson);

                string operation = metadataObj.Operation;
                List<string> filePaths = metadataObj.FilePaths.ToObject<List<string>>();
                List<int> fileIDs = metadataObj.FileIDs.ToObject<List<int>>();
                List<int> folderIDs = metadataObj.FolderIDs.ToObject<List<int>>();
                Dictionary<string, string> additionalData = metadataObj.AdditionalData != null
                    ? metadataObj.AdditionalData.ToObject<Dictionary<string, string>>()
                    : new Dictionary<string, string>();

                LogFileWriter.LogMessage($"Operation: {operation}");
                LogFileWriter.LogMessage($"File count: {filePaths.Count}");
                if (additionalData.Count > 0)
                {
                    LogFileWriter.LogMessage($"Additional data keys: {string.Join(", ", additionalData.Keys)}");
                }

                taskInstance.SetProgressPos(10, "Initializing Leo AI client...");

                // Initialize Leo AI client
                LogFileWriter.LogMessage("Reading auth config...");
                LeoAuthConfig authConfig = ReadAuthConfig();
                LogFileWriter.LogMessage($"Auth config loaded - ProjectId: {authConfig.ProjectId}");

                _leoClient = new SecureApiClient(authConfig.ApiKey, authConfig.ProjectId);
                LogFileWriter.LogMessage("SecureApiClient initialized");

                taskInstance.SetProgressPos(20, "Getting directory ID...");

                // Get or create directory
                string vaultDir = vault.RootFolderPath;
                _directoryId = GetOrCreateDirectoryId(vaultDir).Result;
                LogFileWriter.LogMessage($"Directory ID: {_directoryId}");

                taskInstance.SetProgressPos(30, $"Executing {operation} operation...");

                // Build file data from task variables
                List<EdmCmdData> fileDataList = new List<EdmCmdData>();

                for (int i = 0; i < filePaths.Count; i++)
                {
                    EdmCmdData cmdData = new EdmCmdData();
                    cmdData.mbsStrData1 = filePaths[i];
                    cmdData.mlObjectID1 = fileIDs[i];
                    cmdData.mlObjectID2 = folderIDs[i];
                    fileDataList.Add(cmdData);
                    LogFileWriter.LogMessage($"File {i + 1}: {filePaths[i]} (FileID: {fileIDs[i]}, FolderID: {folderIDs[i]})");
                }

                EdmCmdData[] actualFiles = fileDataList.ToArray();
                LogFileWriter.LogMessage($"Processing {actualFiles.Length} files");

                // Execute operation
                switch (operation)
                {
                    case "Upload":
                        ExecuteUpload(vault, taskInstance, actualFiles, vaultDir).Wait();
                        break;

                    case "Delete":
                        ExecuteDelete(vault, taskInstance, actualFiles, additionalData).Wait();
                        break;

                    case "Move":
                        ExecuteMove(vault, taskInstance, actualFiles, additionalData).Wait();
                        break;

                    case "Rename":
                        ExecuteRename(vault, taskInstance, actualFiles, additionalData).Wait();
                        break;

                    case "CompleteSync":
                        ExecuteCompleteSync(vault, taskInstance, actualFiles).Wait();
                        break;

                    default:
                        throw new Exception($"Unknown operation: {operation}");
                }

                taskInstance.SetProgressPos(90, "Finalizing...");
                LogFileWriter.LogMessage("Task execution completed");

                // Delete metadata file on success
                if (!string.IsNullOrEmpty(metadataPath) && File.Exists(metadataPath))
                {
                    try
                    {
                        File.Delete(metadataPath);
                        LogFileWriter.LogMessage($"Deleted metadata file on success: {metadataFileName}");
                    }
                    catch (Exception deleteEx)
                    {
                        LogFileWriter.LogMessage($"Warning: Could not delete metadata file: {deleteEx.Message}");
                    }
                }

                taskInstance.SetProgressPos(100, "Sync completed successfully");
                taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, "", null, "Leo AI sync completed successfully");

                LogFileWriter.LogMessage("=== OnTaskRun: Sync completed successfully ===");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnTaskRun failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");

                if (taskInstance != null)
                {
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, $"Sync failed: {ex.Message}");
                }

                // Handle metadata file on failure - increment trial or delete if max reached
                if (!string.IsNullOrEmpty(metadataPath) && File.Exists(metadataPath))
                {
                    try
                    {
                        // Use retry count from task configuration (set in OnTaskSetup)
                        LogFileWriter.LogMessage($"Using max retries from task config: {_maxRetries}");

                        // Check if we've exhausted retries
                        if (currentTrial >= _maxRetries)
                        {
                            // Max trials reached - delete file and let next one run
                            File.Delete(metadataPath);
                            LogFileWriter.LogMessage($"Max retries ({_maxRetries}) reached for {metadataFileName} - deleted file");
                        }
                        else
                        {
                            // Increment trial number for next retry
                            int nextTrial = currentTrial + 1;
                            string newFileName = $"{timestamp}_{nextTrial}.json";
                            string newPath = Path.Combine(metadataFolder, newFileName);
                            File.Move(metadataPath, newPath);
                            LogFileWriter.LogMessage($"Incremented trial: {metadataFileName} → {newFileName} (will retry)");
                        }
                    }
                    catch (Exception retryEx)
                    {
                        LogFileWriter.LogError($"Error handling metadata file retry logic: {retryEx.Message}");
                    }
                }
            }
        }

        #region Sync Operations

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

                if (!File.Exists(localPath))
                {
                    string errorMsg = $"File does not exist locally: {localPath}";
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

                    // Upload file with new path first
                    if (File.Exists(newPath))
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

                    // Upload with new path first
                    if (File.Exists(newFilePath))
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

            foreach (string filePath in filePaths)
            {
                string relativePath = GetRelativePath(vaultRootPath, filePath);
                LogFileWriter.LogMessage($"Processing: {relativePath}");

                string actualFilePath = null;
                bool needsCleanup = false;

                try
                {
                    // Get readable file path (archive or temp copy)
                    IEdmFolder5 folder;
                    IEdmFile5 file = vault.GetFileFromPath(filePath, out folder);

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
                // Get file object from vault
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(logicalPath, out folder);

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

        #endregion
    }
}
