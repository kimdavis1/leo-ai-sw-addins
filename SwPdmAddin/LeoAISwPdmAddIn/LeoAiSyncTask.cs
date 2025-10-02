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
        private Dictionary<string, string> _pathToServerFileCache;
        private readonly object _cacheLock = new object();
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
                LogFileWriter.LogMessage($"Vault Root: {vault.RootFolderPath}");

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

                taskInstance.SetProgressPos(30, "Refreshing cache...");

                // Refresh cache
                RefreshCache().Wait();
                LogFileWriter.LogMessage($"Cache refreshed - {_pathToServerFileCache.Count} files cached");

                taskInstance.SetProgressPos(40, $"Executing {operation} operation...");

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
                await UpdateFilesToLeoAI(new[] { localPath }, vaultRootPath);

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

                    string componentId = null;
                    lock (_cacheLock)
                    {
                        if (_pathToServerFileCache.TryGetValue(relativePath, out componentId))
                        {
                            LogFileWriter.LogMessage($"Found component ID in cache: {componentId}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"File not in cache, skipping: {relativePath}");
                        }
                    }

                    if (componentId != null)
                    {
                        bool deleted = await _leoClient.DeleteFileAsync(_directoryId, componentId, relativePath);
                        if (deleted)
                        {
                            lock (_cacheLock)
                            {
                                _pathToServerFileCache.Remove(relativePath);
                            }
                            LogFileWriter.LogMessage($"Deleted from server: {relativePath}");
                        }
                    }

                    processed++;
                    int progress = 40 + (int)((processed / (float)total) * 50);
                    taskInstance.SetProgressPos(progress, $"Deleted {processed}/{total} files");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to delete file {filePath}: {ex.Message}");
                }
            }

            LogFileWriter.LogMessage($"=== ExecuteDelete: Completed - {processed}/{total} files deleted ===");
        }

        private async Task ExecuteMove(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, Dictionary<string, string> additionalData)
        {
            LogFileWriter.LogMessage($"=== ExecuteMove: Processing {files.Length} files ===");

            string oldPathsStr = additionalData.ContainsKey("OldPaths") ? additionalData["OldPaths"] : "";
            string newPathsStr = additionalData.ContainsKey("NewPaths") ? additionalData["NewPaths"] : "";

            string[] oldPaths = string.IsNullOrEmpty(oldPathsStr) ? new string[0] : oldPathsStr.Split('|');
            string[] newPaths = string.IsNullOrEmpty(newPathsStr) ? new string[0] : newPathsStr.Split('|');

            LogFileWriter.LogMessage($"Old paths: {string.Join(", ", oldPaths)}");
            LogFileWriter.LogMessage($"New paths: {string.Join(", ", newPaths)}");

            if (oldPaths.Length != newPaths.Length)
            {
                throw new Exception($"Path count mismatch: {oldPaths.Length} old paths vs {newPaths.Length} new paths");
            }

            int processed = 0;
            int total = oldPaths.Length;

            for (int i = 0; i < oldPaths.Length; i++)
            {
                try
                {
                    string oldPath = oldPaths[i];
                    string newPath = newPaths[i];

                    LogFileWriter.LogMessage($"Moving file: {oldPath} -> {newPath}");

                    string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldPath);
                    string newRelativePath = GetRelativePath(vault.RootFolderPath, newPath);

                    string componentId = null;
                    lock (_cacheLock)
                    {
                        if (_pathToServerFileCache.TryGetValue(oldRelativePath, out componentId))
                        {
                            LogFileWriter.LogMessage($"Found component ID: {componentId}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"Old file not in cache");
                        }
                    }

                    if (componentId != null)
                    {
                        // Delete old file
                        bool deleted = await _leoClient.DeleteFileAsync(_directoryId, componentId, oldRelativePath);
                        if (deleted)
                        {
                            lock (_cacheLock)
                            {
                                _pathToServerFileCache.Remove(oldRelativePath);
                            }
                            LogFileWriter.LogMessage($"Deleted old file from server");
                        }
                    }

                    // Upload new file
                    if (File.Exists(newPath))
                    {
                        await UpdateFilesToLeoAI(new[] { newPath }, vault.RootFolderPath);
                        LogFileWriter.LogMessage($"Uploaded new file to server");
                    }

                    processed++;
                    int progress = 40 + (int)((processed / (float)total) * 50);
                    taskInstance.SetProgressPos(progress, $"Moved {processed}/{total} files");

                    LogFileWriter.LogMessage($"Move completed: {oldRelativePath} -> {newRelativePath}");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to move file {oldPaths[i]}: {ex.Message}");
                }
            }

            LogFileWriter.LogMessage($"=== ExecuteMove: Completed - {processed}/{total} files moved ===");
        }

        private async Task ExecuteRename(IEdmVault11 vault, IEdmTaskInstance taskInstance, EdmCmdData[] files, Dictionary<string, string> additionalData)
        {
            LogFileWriter.LogMessage("=== ExecuteRename: Using same logic as Move ===");
            await ExecuteMove(vault, taskInstance, files, additionalData);
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

        private async Task RefreshCache()
        {
            LogFileWriter.LogMessage("Refreshing server file cache...");

            lock (_cacheLock)
            {
                _pathToServerFileCache = new Dictionary<string, string>();
            }

            var syncData = await _leoClient.GetSyncMetadataAsync(_directoryId);
            LogFileWriter.LogMessage($"Retrieved sync metadata from server");

            if (syncData?.Files != null)
            {
                lock (_cacheLock)
                {
                    foreach (var file in syncData.Files)
                    {
                        _pathToServerFileCache[file.FilePathInDirectory] = file.ComponentId;
                    }
                }
                LogFileWriter.LogMessage($"Cache refreshed with {_pathToServerFileCache.Count} entries");
            }
            else
            {
                LogFileWriter.LogMessage("No files found in sync metadata");
            }
        }

        private async Task UpdateFilesToLeoAI(string[] filePaths, string vaultRootPath)
        {
            LogFileWriter.LogMessage($"Updating {filePaths.Length} files to Leo AI");

            foreach (string filePath in filePaths)
            {
                string relativePath = GetRelativePath(vaultRootPath, filePath);
                LogFileWriter.LogMessage($"Processing: {relativePath}");

                // Check cache first (outside lock to avoid deadlock)
                string existingComponentId = null;
                lock (_cacheLock)
                {
                    _pathToServerFileCache.TryGetValue(relativePath, out existingComponentId);
                }

                // Retry logic with exponential backoff for rate limits
                int maxRetries = 5;
                int retryDelay = 1000; // Start with 1 second

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // CreateFileAsync handles both create and update
                        var fileInfo = await _leoClient.CreateFileAsync(_directoryId, vaultRootPath, filePath, null);

                        if (fileInfo != null)
                        {
                            lock (_cacheLock)
                            {
                                _pathToServerFileCache[relativePath] = fileInfo.ComponentId;
                            }
                            LogFileWriter.LogMessage($"File synced to server: {relativePath} (ID: {fileInfo.ComponentId})");
                        }

                        break; // Success - exit retry loop
                    }
                    catch (Exception ex)
                    {
                        // Check if it's a rate limit error (HTTP 429)
                        bool isRateLimit = ex.Message.Contains("429") ||
                                          ex.Message.ToLower().Contains("rate limit") ||
                                          ex.Message.ToLower().Contains("too many requests");

                        if (isRateLimit && attempt < maxRetries)
                        {
                            // Exponential backoff for rate limits
                            LogFileWriter.LogMessage($"Rate limit hit for {relativePath}, retrying in {retryDelay}ms (attempt {attempt + 1}/{maxRetries})");
                            await Task.Delay(retryDelay);
                            retryDelay *= 2; // Exponential backoff
                            continue;
                        }

                        // For all other errors or exhausted retries, log and re-throw to fail the task
                        LogFileWriter.LogError($"Failed to update file {filePath}: {ex.Message}");
                        throw new Exception($"Failed to sync file {relativePath}: {ex.Message}", ex);
                    }
                }
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
