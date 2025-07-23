using EPDM.Interop.epdm;
using LeoAICadDataClient;
using LeoAICadDataClient.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
    /// Represents the changes detected during sync analysis
    /// </summary>
    public class SyncChanges
    {
        public List<FileData> NewFiles { get; set; } = new List<FileData>();
        public List<FileData> ModifiedFiles { get; set; } = new List<FileData>();
        public List<FileData> MovedFiles { get; set; } = new List<FileData>();
        public List<SyncMetadataFile> DeletedFiles { get; set; } = new List<SyncMetadataFile>();
    }

    [ComVisible(true)]
    [Guid("5C9C2B58-C7E9-4052-9321-00433F32A479")]
    public class SwPdmAddinMain : IEdmAddIn5
    {

        private Dictionary<string, string> LeoFilesInformation = new Dictionary<string, string>(); // FilePath -> ComponentId

        // Cache for server file state to avoid repeated API calls
        private readonly object _cacheLock = new object();
        private Dictionary<string, string> _pathToServerFileCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Path -> ComponentId
        private bool _isCachePopulated = false;
        private bool _isRefreshingCache = false; // Flag to prevent concurrent cache refreshes

        public SecureApiClient LeoClient { get; set; }

        // Destructor to catch when the add-in is being unloaded
        ~SwPdmAddinMain()
        {
            try
            {
                LogFileWriter.LogMessage("SwPdmAddinMain destructor called - add-in is being unloaded");
                // Note: We can't reliably perform cleanup here because we don't have vault context
                // But we can at least log that the add-in is being destroyed
            }
            catch (Exception)
            {
                // Ignore exceptions in destructor
            }
        }

        private async Task RefreshCache(string directoryId)
        {
            if (string.IsNullOrEmpty(directoryId) || LeoClient == null)
            {
                LogFileWriter.LogMessage("Skipping cache refresh because directoryId is missing or LeoClient is not initialized.");
                return;
            }

            // Check if another thread is already refreshing the cache
            lock (_cacheLock)
            {
                if (_isRefreshingCache)
                {
                    LogFileWriter.LogMessage("Cache refresh already in progress by another thread. Skipping.");
                    return;
                }
                _isRefreshingCache = true;
            }

            try
            {
                LogFileWriter.LogMessage("Refreshing server file state cache...");
                var dirInfoForCache = await LeoClient.GetSyncMetadataAsync(directoryId);

                lock (_cacheLock)
                {
                    _pathToServerFileCache.Clear();

                    if (dirInfoForCache?.Files != null)
                    {
                        foreach (var serverFile in dirInfoForCache.Files)
                        {
                            var normalizedPath = NormalizePath(serverFile.FilePathInDirectory);
                            _pathToServerFileCache[normalizedPath] = serverFile.ComponentId;
                        }
                    }

                    _isCachePopulated = true;
                    LogFileWriter.LogMessage($"Cache refreshed with {_pathToServerFileCache.Count} files.");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error refreshing cache: {ex.Message}");
            }
            finally
            {
                lock (_cacheLock)
                {
                    _isRefreshingCache = false;
                }
            }
        }

        private async Task<string> GetFileIdWithCacheFallback(string normalizedPath, string directoryId)
        {
            lock (_cacheLock)
            {
                if (_isCachePopulated && _pathToServerFileCache.TryGetValue(normalizedPath, out string cachedId))
                {
                    LogFileWriter.LogMessage($"Found cached component ID for '{normalizedPath}': {cachedId}");
                    return cachedId;
                }
            }

            LogFileWriter.LogMessage($"File '{normalizedPath}' not found in cache. Falling back to API lookup.");
            var fileInfo = await LeoClient.GetFileInfoByPathAsync(directoryId, normalizedPath);
            if (fileInfo != null)
            {
                lock (_cacheLock)
                {
                    _pathToServerFileCache[normalizedPath] = fileInfo.ComponentId;
                }
                return fileInfo.ComponentId;
            }

            return null;
        }

        private async Task EnsureCacheIsPopulated(string directoryId)
        {
            lock (_cacheLock)
            {
                if (_isCachePopulated)
                {
                    return;
                }
            }

            await RefreshCache(directoryId);
        }

        private void AddFileToCache(SyncMetadataFile file)
        {
            if (file == null) return;

            lock (_cacheLock)
            {
                var normalizedPath = NormalizePath(file.FilePathInDirectory);
                _pathToServerFileCache[normalizedPath] = file.ComponentId;
                LogFileWriter.LogMessage($"Added file to cache: '{normalizedPath}' -> {file.ComponentId}");
            }
        }

        private void RemoveFileFromCache(string normalizedPath)
        {
            lock (_cacheLock)
            {
                if (_pathToServerFileCache.Remove(normalizedPath))
                {
                    LogFileWriter.LogMessage($"Removed file from cache: '{normalizedPath}'");
                }
            }
        }

        private SyncMetadataFile ConvertToFileMetadata(LeoAICadDataClient.Utilities.FileInfo fileInfo)
        {
            if (fileInfo == null) return null;

            return new SyncMetadataFile
            {
                ComponentId = fileInfo.ComponentId,
                FilePathInDirectory = fileInfo.FilePathInDirectory,
                CheckSum = fileInfo.CheckSum,
                MimeType = fileInfo.mimeType
            };
        }

        /// <summary>
        /// Shows a message box that appears on top of all other windows and is focused
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="title">The title of the message box</param>
        /// <param name="icon">The icon to display</param>
        private void ShowTopMostMessage(string message, string title, System.Windows.Forms.MessageBoxIcon icon)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(message, title, System.Windows.Forms.MessageBoxButtons.OK, icon, System.Windows.Forms.MessageBoxDefaultButton.Button1, System.Windows.Forms.MessageBoxOptions.ServiceNotification);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error showing TopMost message: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the Leo AI authentication configuration from JSON file
        /// </summary>
        /// <returns>Authentication configuration or null if not found/invalid</returns>
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
                    string errorMessage = "Leo AI authentication configuration not found!\n\n" +
                        "Please place the auth.json file in one of the following locations:\n" +
                        "1. Default location: C:\\Program Files\\LeoAISwPdmAddIn\\auth.json\n" +
                        "2. Custom location specified in LEO_AUTH_KEY environment variable\n\n" +
                        "The auth.json file should contain:\n" +
                        "{\n" +
                        "  \"ApiKey\": \"your-api-key\",\n" +
                        "  \"ProjectId\": \"your-project-id\"\n" +
                        "}\n\n" +
                        "You can get the authentication keys from the Leo AI Admin Dashboard\n" +
                        "(available in Leo Business/Enterprise accounts).";

                    LogFileWriter.LogError("Auth config file not found");
                    ShowTopMostMessage(errorMessage, "Leo AI Authentication Required", System.Windows.Forms.MessageBoxIcon.Warning);
                    return null;
                }

                // Read and parse the JSON file
                string jsonContent = File.ReadAllText(configFilePath);
                LeoAuthConfig config = ParseAuthConfig(jsonContent);

                // Validate the configuration
                if (config == null || string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ProjectId))
                {
                    string errorMessage = $"Invalid Leo AI authentication configuration in file: {configFilePath}\n\n" +
                        "The auth.json file should contain:\n" +
                        "{\n" +
                        "  \"ApiKey\": \"your-api-key\",\n" +
                        "  \"ProjectId\": \"your-project-id\"\n" +
                        "}\n\n" +
                        "Both ApiKey and ProjectId are required and cannot be empty.\n" +
                        "You can get the authentication keys from the Leo AI Admin Dashboard\n" +
                        "(available in Leo Business/Enterprise accounts).";

                    LogFileWriter.LogError($"Invalid auth config in file: {configFilePath}");
                    ShowTopMostMessage(errorMessage, "Leo AI Authentication Invalid", System.Windows.Forms.MessageBoxIcon.Error);
                    return null;
                }

                LogFileWriter.LogMessage("Leo AI authentication configuration loaded successfully");
                return config;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error reading Leo AI authentication configuration:\n{ex.Message}\n\n" +
                    "Please ensure the auth.json file is properly formatted:\n" +
                    "{\n" +
                    "  \"ApiKey\": \"your-api-key\",\n" +
                    "  \"ProjectId\": \"your-project-id\"\n" +
                    "}\n\n" +
                    "You can get the authentication keys from the Leo AI Admin Dashboard\n" +
                    "(available in Leo Business/Enterprise accounts).";

                LogFileWriter.LogError($"Exception reading auth config: {ex.Message}");
                ShowTopMostMessage(errorMessage, "Leo AI Authentication Error", System.Windows.Forms.MessageBoxIcon.Error);
                return null;
            }
        }

        /// <summary>
        /// Simple JSON parser for Leo AI authentication configuration
        /// </summary>
        /// <param name="jsonContent">JSON content to parse</param>
        /// <returns>Parsed authentication configuration</returns>
        private LeoAuthConfig ParseAuthConfig(string jsonContent)
        {
            try
            {
                var config = new LeoAuthConfig();

                // Remove whitespace and braces
                jsonContent = jsonContent.Trim().Trim('{', '}');

                // Split by comma to get individual properties
                string[] properties = jsonContent.Split(',');

                foreach (string property in properties)
                {
                    // Split by colon to get key-value pairs
                    string[] keyValue = property.Split(':');
                    if (keyValue.Length == 2)
                    {
                        string key = keyValue[0].Trim().Trim('"');
                        string value = keyValue[1].Trim().Trim('"');

                        if (key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
                        {
                            config.ApiKey = value;
                        }
                        else if (key.Equals("ProjectId", StringComparison.OrdinalIgnoreCase))
                        {
                            config.ProjectId = value;
                        }
                    }
                }

                return config;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error parsing JSON config: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Addin basic information like name and description and version
        /// </summary>
        /// <param name="poInfo"></param>
        /// <param name="poVault"></param>
        /// <param name="poCmdMgr"></param>
        /// <exception cref="System.NotImplementedException"></exception>
        public void GetAddInInfo(ref EdmAddInInfo poInfo, IEdmVault5 poVault, IEdmCmdMgr5 poCmdMgr)
        {
            try
            {
                LogFileWriter.LogDebug("GetAddInInfo method called");

                // Step 1: Always provide the basic Add-in info. This part can be called multiple times.
                poInfo.mbsAddInName = "LeoAISolidWorksPDMAdddIn";
                poInfo.mbsCompany = "LeoAI.";
                poInfo.mbsDescription = "Your AI engineering design copilot";
                poInfo.mlAddInVersion = 1;
                poInfo.mlRequiredVersionMajor = 17;
                LogFileWriter.LogDebug("Basic add-in info provided.");

                // Step 2: Perform vault-specific initialization only once.
                // This part is critical and should not run multiple times for the same vault.
                if (poVault == null)
                {
                    LogFileWriter.LogDebug("GetAddInInfo called without a vault context. Skipping vault-specific initialization.");
                    return;
                }

                string vaultName = poVault.Name;
                LogFileWriter.LogMessage($"GetAddInInfo executing for vault: '{vaultName}'");

                // Step 2a: Register hooks every time to ensure the add-in is responsive in all PDM processes.
                #region PDM User Events Registration
                LogFileWriter.LogDebug("Registering event hooks...");

                // File events that require processing
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostUnlock);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostUndoLock);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostDelete);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostMove);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostCopy);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostAdd);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostRename);

                // Folder events that require processing
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostRenameFolder);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_PostMoveFolder);

                // Add a hook for the installation event, which is a better place for one-time setup.
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_InstallAddIn);
                LogFileWriter.LogDebug("All event hooks have been registered.");
                #endregion

                // Note: Cleanup when add-in is removed from vaults is handled by the standalone uninstaller

                // Step 2c: On subsequent loads, perform a "session startup" sync.
                // This runs only if the add-in has been successfully installed (i.e., the persistent flag exists).
                // It uses a Mutex and a static list to ensure it only runs once per session across all processes.
                // RunStartupSync(poVault);
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



        /// <summary>
        /// Events Trigger(Check-in/wrok flow ..etc)
        /// </summary>
        /// <param name="poCmd"></param>
        /// <param name="ppoData"></param>
        /// <exception cref="System.NotImplementedException"></exception>
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

            switch (poCmd.meCmdType)
            {
                case EdmCmdType.EdmCmd_PostUnlock:
                    LogFileWriter.LogMessage("PostUnlock event detected - after checkin");
                    Task.Run(async () => await HandleFileCheckIn(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostUndoLock:
                    LogFileWriter.LogMessage("PostUndoLock event detected - after undo checkout or new file added");
                    Task.Run(async () => await HandleFileUndoCheckOut(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostDelete:
                    LogFileWriter.LogMessage("PostDelete event detected - after file deletion");
                    Task.Run(async () => await HandleFileDeleted(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostMove:
                    LogFileWriter.LogMessage("PostMove event detected - after file move");
                    Task.Run(async () => await HandleFileMoved(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostCopy:
                    LogFileWriter.LogMessage("PostCopy event detected - after file copy");
                    // Handle copied files (similar to new files)
                    Task.Run(async () => await HandleFileCopied(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostAdd:
                    LogFileWriter.LogMessage("PostAdd event detected - after file add");
                    // Handle newly added files
                    Task.Run(async () => await PerformLeoAIActions(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostRename:
                    LogFileWriter.LogMessage("PostRename event detected - after file rename");
                    Task.Run(async () => await HandleFileRename(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostMoveFolder:
                    LogFileWriter.LogMessage("PostMoveFolder event detected - after folder move");
                    Task.Run(async () => await HandleFolderMove(cmd, data));
                    break;

                case EdmCmdType.EdmCmd_PostRenameFolder:
                    LogFileWriter.LogMessage("PostRenameFolder event detected - after folder rename");
                    Task.Run(async () => await HandleFolderRename(cmd, data));
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

                        // Perform initial sync using the safe method - wait for completion to prevent PDM unloading
                        LogFileWriter.LogMessage("Performing initial vault sync...");
                        SafeUploadData(vault, waitForCompletion: true);

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


        /// <summary>
        /// Perform Leo AI actions on top of check-in files..
        /// </summary>
        /// <param name="poCmd"></param>
        /// <param name="ppoData"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task PerformLeoAIActions(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            string asmPath = Assembly.GetExecutingAssembly().Location;
            try
            {
                LogFileWriter.LogMessage("PerformLeoAIActions method called");
                LeoFilesInformation.Clear();
                await UploadFileChangesToLeo(poCmd, ppoData);
                LogFileWriter.LogMessage("PerformLeoAIActions method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in PerformLeoAIActions: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
                System.Windows.Forms.MessageBox.Show($"Error in PerformLeoAIActions: {ex.Message}");
            }
        }

        /// <summary>
        /// Uploads specific files that triggered the event to Leo AI
        /// This is for event-based uploads, not full sync
        /// </summary>
        /// <param name="poCmd"></param>
        /// <param name="ppoData"></param>
        /// <returns></returns>
        private async Task UploadFileChangesToLeo(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                LogFileWriter.LogMessage("UploadFileChangesToLeo method called for event-based file upload.");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success)
                {
                    return;
                }
                string directoryId = initResult.directoryId;
                string vaultDir = initResult.vaultDir;
                SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

                // This method is now a generic uploader for any event that adds/modifies files.
                // The specific logic for deletes/moves is handled in the dedicated handlers.
                List<FileData> filesToUpload = new List<FileData>();

                foreach (var cmdData in ppoData)
                {
                    string filePath = cmdData.mbsStrData1;
                    LogFileWriter.LogMessage($"Processing event file: {filePath}");

                    // Check if this is a file we should process
                    if (IsProcessableFile(filePath))
                    {
                        // Check if this file can have dependencies (SOLIDWORKS assemblies/drawings)
                        if (FileHasDependencies(filePath))
                        {
                            // Get file structure for files that may have dependencies
                            List<FileData> fileStructure = pdmHelper.GetFileStructure(filePath);

                            if (fileStructure != null && fileStructure.Count > 0)
                            {
                                LogFileWriter.LogMessage($"Found {fileStructure.Count} files in structure for {Path.GetFileName(filePath)}");
                                filesToUpload.AddRange(fileStructure);
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"No structure found for {filePath}, processing as single file");
                                // Process as single file
                                var fileData = pdmHelper.GetSingleFileData(filePath);
                                if (fileData != null)
                                {
                                    filesToUpload.Add(fileData);
                                }
                            }
                        }
                        else
                        {
                            // For files without dependencies (parts, CAD files, documents), process as single file
                            LogFileWriter.LogMessage($"Processing {filePath} as single file (no dependencies expected)");
                            var fileData = pdmHelper.GetSingleFileData(filePath);
                            if (fileData != null)
                            {
                                filesToUpload.Add(fileData);
                            }
                        }
                    }
                    else
                    {
                        LogFileWriter.LogMessage($"Skipping unsupported file: {filePath}");
                    }
                }

                if (filesToUpload.Count == 0)
                {
                    LogFileWriter.LogMessage("No supported files to upload from this event");
                    return;
                }

                // Remove duplicates (in case multiple events reference the same file)
                var uniqueFiles = filesToUpload
                    .GroupBy(f => f.file)
                    .Select(g => g.First())
                    .ToList();

                LogFileWriter.LogMessage($"Uploading {uniqueFiles.Count} unique files from event");

                // Upload files in correct order: CAD files first, then assemblies, then documents
                var cadFiles = uniqueFiles.Where(f =>
                    f.mimeType == "application/x-sldprt" ||
                    f.mimeType == "model/step").ToList();
                var assemblies = uniqueFiles.Where(f => f.mimeType == "application/x-sldasm").ToList();
                var documents = uniqueFiles.Where(f =>
                    f.mimeType == "text/plain" ||
                    f.mimeType == "application/pdf" ||
                    f.mimeType == "application/msword" ||
                    f.mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document").ToList();

                // Upload in dependency order
                if (cadFiles.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {cadFiles.Count} CAD files from event");
                    await UpdateFilesToLeoAI(cadFiles, directoryId, vaultDir);
                }

                if (assemblies.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {assemblies.Count} assemblies from event");
                    await UpdateFilesToLeoAI(assemblies, directoryId, vaultDir);
                }

                if (documents.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {documents.Count} documents from event");
                    await UpdateFilesToLeoAI(documents, directoryId, vaultDir);
                }

                LeoFilesInformation.Clear();
                LogFileWriter.LogMessage("Event-based file upload completed successfully");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in UploadFileChangesToLeo: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        SyncMetadataResponse dirInfo = null;
        /// <summary>
        /// Creates the valut directory in LEO
        /// </summary>
        /// <param name="valutDir"></param>
        /// <returns></returns>
        private async Task<string> CreateDirectory(string valutDir)
        {
            string directoryId = string.Empty;
            try
            {
                LogFileWriter.LogMessage($"Checking for existing synced directory for this machine...");
                //Check if the valut is already registered in Leo
                List<LeoDirectoryInfo> directoriesInfo = await LeoClient.GetDirectoryInfoAsync(LeoAIDataUtilities.GetFormattedMacAddress());
                if (directoriesInfo != null && directoriesInfo.Count > 0)
                {
                    //Valut already registered
                    var dir = directoriesInfo.FirstOrDefault(d => d.Uri.Equals(valutDir, StringComparison.OrdinalIgnoreCase));
                    if (dir != null)
                    {
                        directoryId = dir.Id;
                        LogFileWriter.LogMessage($"Found existing synced directory for path: '{valutDir}'. Using ID: {directoryId}");
                    }
                    else
                    {
                        LogFileWriter.LogMessage($"No synced directory found for path: '{valutDir}'. Creating a new one.");
                        directoryId = await LeoClient.CreateDirectoryAsync(LeoAIDataUtilities.GetFormattedMacAddress(), valutDir);
                        LogFileWriter.LogMessage($"Created new synced directory with path: '{valutDir}'. New ID: {directoryId}");
                    }
                }
                else
                {
                    //Valut not registered yet
                    LogFileWriter.LogMessage($"No existing synced directories found for this machine. Creating new one with path: {valutDir}");
                    directoryId = await LeoClient.CreateDirectoryAsync(LeoAIDataUtilities.GetFormattedMacAddress(), valutDir);
                }

                if (!string.IsNullOrEmpty(directoryId))
                {
                    dirInfo = await LeoClient.GetSyncMetadataAsync(directoryId);
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError(ex.StackTrace);
            }

            return directoryId;
        }

        /// <summary>
        /// Update supported files into Leo AI
        /// </summary>
        /// <param name="filesInfo">List of file data to upload</param>
        /// <param name="dirId">Directory ID</param>
        /// <param name="vaultDir">Vault directory path</param>
        private async Task<Dictionary<string, string>> UpdateFilesToLeoAI(List<FileData> filesInfo, string dirId, string vaultDir)
        {
            try
            {
                // This method is now a simple uploader. It assumes the calling method has already
                // handled any required deletions for updates/moves.

                // Sort files by dependency depth to ensure children are created before parents
                var sortedFiles = filesInfo.OrderBy(f => GetDependencyDepth(f, filesInfo)).ToList();

                foreach (var pInfo in sortedFiles)
                {
                    // Always treat the file as a new creation.
                    LogFileWriter.LogMessage($"Uploading file to server: {pInfo.file}");
                    Dictionary<string, string> childerenInfo = await GetChildInfo(pInfo, dirId, vaultDir);
                    var result = await LeoClient.CreateFileAsync(dirId, vaultDir, pInfo.file, childerenInfo);
                    if (result != null)
                    {
                        // Add the newly created file to our cache and local dependency info
                        AddFileToCache(ConvertToFileMetadata(result));
                        if (!LeoFilesInformation.ContainsKey(pInfo.file))
                        {
                            LeoFilesInformation.Add(pInfo.file, result.ComponentId);
                        }
                        else
                        {
                            LeoFilesInformation[pInfo.file] = result.ComponentId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError(ex.StackTrace);
            }
            return LeoFilesInformation;
        }

        /// <summary>
        /// Calculates the dependency depth of a file. Files with no dependencies have depth 0.
        /// </summary>
        private int GetDependencyDepth(FileData file, List<FileData> allFiles, Dictionary<string, int> memo = null)
        {
            if (memo == null) memo = new Dictionary<string, int>();
            if (memo.ContainsKey(file.file)) return memo[file.file];
            if (file.dependencies == null || file.dependencies.Count == 0) return 0;

            int maxChildDepth = 0;
            foreach (var dep in file.dependencies)
            {
                var childFile = allFiles.FirstOrDefault(f => f.file == dep.filePath);
                if (childFile != null)
                {
                    maxChildDepth = Math.Max(maxChildDepth, GetDependencyDepth(childFile, allFiles, memo));
                }
            }

            int depth = 1 + maxChildDepth;
            memo[file.file] = depth;
            return depth;
        }

        /// <summary>
        /// Gets the required child information of the file
        /// </summary>
        /// <param name="pInfo"></param>
        /// <param name="dirId"></param>
        /// <returns></returns>
        private async Task<Dictionary<string, string>> GetChildInfo(FileData pInfo, string dirId, string vaultDir)
        {
            Dictionary<string, string> childerenInfo = new Dictionary<string, string>();
            if (pInfo.dependencies != null && pInfo.dependencies.Count > 0)
            {
                foreach (var child in pInfo.dependencies)
                {
                    // The child.filePath from SOLIDWORKS is the full absolute path, 
                    // so we can use it directly to lookup in LeoFilesInformation
                    LogFileWriter.LogMessage($"Looking up dependency: '{child.filePath}' in LeoFilesInformation");

                    if (LeoFilesInformation.ContainsKey(child.filePath))
                    {
                        var componentId = LeoFilesInformation[child.filePath];
                        // Calculate the relative path from vault root for this dependency
                        string relativePath = SecureApiClient.GetRelativePath(vaultDir, child.filePath);
                        string normalizedPath = SecureApiClient.NormalizeFilePathForApi(relativePath);

                        // Get the actual checksum of the dependency file
                        string checkSum = GetFileChecksum(child.filePath);

                        // The API expects: checkSum -> relative file path
                        childerenInfo.Add(checkSum, normalizedPath);
                        LogFileWriter.LogMessage($"Found dependency '{child.filePath}' with checkSum: {checkSum}, relative path: {normalizedPath}");
                    }
                    else
                    {
                        // This case should ideally not happen if files are processed in order of dependency.
                        // It indicates a child part/assembly that wasn't in the original list.
                        // We create it here as a fallback.
                        LogFileWriter.LogMessage($"Warning: Child dependency '{child.filePath}' not found in pre-processed list. Creating it on-the-fly.");
                        var result = await LeoClient.CreateFileAsync(dirId, vaultDir, child.filePath, null);
                        if (result != null)
                        {
                            // Calculate the relative path from vault root for this dependency
                            string relativePath = SecureApiClient.GetRelativePath(vaultDir, child.filePath);
                            string normalizedPath = SecureApiClient.NormalizeFilePathForApi(relativePath);

                            // Get the actual checksum of the dependency file
                            string checkSum = GetFileChecksum(child.filePath);

                            childerenInfo.Add(checkSum, normalizedPath);
                            // Add to cache and local tracking
                            AddFileToCache(ConvertToFileMetadata(result));
                            LeoFilesInformation[child.filePath] = result.ComponentId;
                            LogFileWriter.LogMessage($"Created dependency '{child.filePath}' with checkSum: {checkSum}, relative path: {normalizedPath}");
                        }
                    }
                }
            }

            return childerenInfo;
        }

        /// <summary>
        /// Gets the checksum of a file using the same algorithm as LeoFileInfo
        /// </summary>
        /// <param name="filePath">Full path to the file</param>
        /// <returns>Checksum string</returns>
        private string GetFileChecksum(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    LogFileWriter.LogError($"File not found for checksum calculation: {filePath}");
                    return string.Empty;
                }

                // Use the same method as LeoFileInfo to ensure consistency
                LeoFileInfo.LeoFileInformation fileInfo = LeoFileInfo.GetFileInfo(filePath);
                return fileInfo.CheckSum;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error calculating checksum for file '{filePath}': {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Analyzes the differences between local files and server sync metadata to determine what changes need to be made
        /// </summary>
        /// <param name="localFiles">List of local files</param>
        /// <param name="serverSyncData">Server sync metadata</param>
        /// <param name="vaultDir">Vault directory path</param>
        /// <returns>Categorized sync changes</returns>
        private SyncChanges AnalyzeSyncChanges(List<FileData> localFiles, SyncMetadataResponse serverSyncData, string vaultDir)
        {
            var syncChanges = new SyncChanges();

            try
            {
                // Create a set of server file paths for quick lookup
                var serverFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var serverFileMap = new Dictionary<string, SyncMetadataFile>(StringComparer.OrdinalIgnoreCase);

                if (serverSyncData?.Files != null)
                {
                    foreach (var serverFile in serverSyncData.Files)
                    {
                        var normalizedPath = NormalizePath(serverFile.FilePathInDirectory);
                        serverFiles.Add(normalizedPath);
                        serverFileMap[normalizedPath] = serverFile;

                        // Log first few server files for debugging
                        if (serverFiles.Count <= 5)
                        {
                            LogFileWriter.LogMessage($"Server file: '{normalizedPath}' (Original: '{serverFile.FilePathInDirectory}')");
                        }
                    }
                }

                LogFileWriter.LogMessage($"Server has {serverFiles.Count} files");

                // Deduplicate local files by relative path to prevent duplicate processing
                var uniqueLocalFiles = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
                foreach (var localFile in localFiles)
                {
                    var relativePath = SecureApiClient.GetRelativePath(vaultDir, localFile.file);
                    var normalizedPath = NormalizePath(relativePath);

                    // Only keep the first occurrence of each unique path
                    if (!uniqueLocalFiles.ContainsKey(normalizedPath))
                    {
                        uniqueLocalFiles[normalizedPath] = localFile;

                        // Log first few local files for debugging
                        if (uniqueLocalFiles.Count <= 10)
                        {
                            LogFileWriter.LogMessage($"Local file: '{normalizedPath}' (Original: '{localFile.file}')");
                        }
                    }
                    else
                    {
                        LogFileWriter.LogMessage($"Duplicate local file skipped: '{normalizedPath}' (Original: '{localFile.file}')");
                    }
                }

                LogFileWriter.LogMessage($"Local vault has {uniqueLocalFiles.Count} unique files (deduplicated from {localFiles.Count} total)");

                // Debug path comparison for first few files
                LogFileWriter.LogMessage("=== PATH COMPARISON DEBUG ===");
                int debugCount = 0;
                foreach (var kvp in uniqueLocalFiles)
                {
                    if (debugCount >= 5) break;
                    var normalizedPath = kvp.Key;
                    var hasMatch = serverFiles.Contains(normalizedPath);
                    LogFileWriter.LogMessage($"Local: '{normalizedPath}' -> Server match: {hasMatch}");
                    debugCount++;
                }
                LogFileWriter.LogMessage("=== END PATH COMPARISON DEBUG ===");

                // Analyze each unique local file
                foreach (var kvp in uniqueLocalFiles)
                {
                    var normalizedPath = kvp.Key;
                    var localFile = kvp.Value;

                    if (serverFiles.Contains(normalizedPath))
                    {
                        // File exists on server - check if it's modified
                        var serverFile = serverFileMap[normalizedPath];

                        // For now, assume files are unchanged if they exist on both sides
                        // In the future, we could compare checksums or modification dates
                        LogFileWriter.LogMessage($"File unchanged: {normalizedPath}");
                        // Don't add to any sync changes list - unchanged files don't need processing
                    }
                    else
                    {
                        // File doesn't exist on server - it's new
                        LogFileWriter.LogMessage($"New file detected: {normalizedPath}");
                        syncChanges.NewFiles.Add(localFile);
                    }
                }

                // Check for files that exist on server but not locally (deleted files)
                foreach (var serverFile in serverFileMap.Values)
                {
                    var normalizedPath = NormalizePath(serverFile.FilePathInDirectory);
                    if (!uniqueLocalFiles.ContainsKey(normalizedPath))
                    {
                        LogFileWriter.LogMessage($"Deleted file detected: {normalizedPath}");
                        syncChanges.DeletedFiles.Add(serverFile);
                    }
                }

                LogFileWriter.LogMessage("Sync analysis completed successfully");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage($"Error during sync analysis: {ex.Message}");
                throw;
            }

            return syncChanges;
        }

        /// <summary>
        /// Decodes a URL-encoded path segment from the API.
        /// </summary>
        private string DecodePathFromApi(string encodedPath)
        {
            if (string.IsNullOrEmpty(encodedPath)) return encodedPath;
            return Uri.UnescapeDataString(encodedPath);
        }

        /// <summary>
        /// Handles file deletion by removing the file from Leo AI
        /// </summary>
        /// <param name="poCmd"></param>
        /// <param name="ppoData"></param>
        private async Task HandleFileDeleted(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage("File delete hook called.");

            var initResult = await InitializeClientAndCache(poCmd);
            if (!initResult.success)
            {
                LogFileWriter.LogError("Could not initialize Leo client or cache. Aborting delete operation.");
                return;
            }
            string directoryId = initResult.directoryId;
            string vaultDir = initResult.vaultDir;
            SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

            foreach (var data in ppoData)
            {
                try
                {
                    string filePath = data.mbsStrData1; // For PostDelete, the path is in mbsStrData1
                    if (string.IsNullOrEmpty(filePath)) continue;

                    LogFileWriter.LogMessage($"Processing deletion for file: {filePath}");

                    string relativePath = SecureApiClient.GetRelativePath(vaultDir, filePath);
                    string normalizedPath = NormalizePath(relativePath);

                    // Use the new method to get the file ID, with fallback to refresh the cache
                    string componentId = await GetFileIdWithCacheFallback(normalizedPath, directoryId);

                    if (string.IsNullOrEmpty(componentId))
                    {
                        LogFileWriter.LogMessage($"Could not find ComponentId for deleted file '{normalizedPath}' even after cache refresh. It might have been out of sync or already deleted.");
                        continue; // Skip if not found on server
                    }

                    bool success = await LeoClient.DeleteFileAsync(directoryId, componentId, normalizedPath);

                    if (success)
                    {
                        LogFileWriter.LogMessage($"Successfully notified Leo AI of file deletion: {filePath}");
                        RemoveFileFromCache(normalizedPath); // Keep cache in sync
                    }
                    else
                    {
                        LogFileWriter.LogError($"Failed to notify Leo AI of file deletion: {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Error processing file deletion: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the directory ID for the vault
        /// </summary>
        /// <param name="vaultDir"></param>
        /// <returns></returns>
        private async Task<string> GetDirectoryId(string vaultDir)
        {
            try
            {
                List<LeoDirectoryInfo> directoriesInfo = await LeoClient.GetDirectoryInfoAsync(LeoAIDataUtilities.GetFormattedMacAddress());
                if (directoriesInfo != null && directoriesInfo.Count > 0)
                {
                    return directoriesInfo.FirstOrDefault()?.Id;
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in GetDirectoryId: {ex.Message}");
            }
            return string.Empty;
        }

        /// <summary>
        /// Handles file move by updating the file location in Leo AI and removing the old location
        /// </summary>
        /// <param name="poCmd"></param>
        /// <param name="ppoData"></param>
        private async Task HandleFileMoved(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                LogFileWriter.LogMessage("HandleFileMoved method called");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success)
                {
                    return;
                }
                string directoryId = initResult.directoryId;
                string vaultDir = initResult.vaultDir;
                SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

                // Process each moved file
                foreach (var cmdData in ppoData)
                {
                    string oldFilePath = cmdData.mbsStrData1;
                    string newFilePath = cmdData.mbsStrData2;
                    LogFileWriter.LogMessage($"Processing move: Old='{oldFilePath}' -> New='{newFilePath}'");

                    // 1. Upload the new file.
                    List<FileData> fileStructure = pdmHelper.GetFileStructure(newFilePath);
                    if (fileStructure != null && fileStructure.Count > 0)
                    {
                        LogFileWriter.LogMessage($"Uploading moved file structure to new location: {newFilePath}");
                        await UpdateFilesToLeoAI(fileStructure, directoryId, vaultDir);
                    }

                    // 2. Delete the old file from the server
                    string oldRelativePath = NormalizePath(SecureApiClient.GetRelativePath(vaultDir, oldFilePath));
                    if (_pathToServerFileCache.TryGetValue(oldRelativePath, out var componentId))
                    {
                        LogFileWriter.LogMessage($"Deleting old record for moved file: '{oldRelativePath}'");
                        if (await LeoClient.DeleteFileAsync(directoryId, componentId, oldRelativePath))
                        {
                            RemoveFileFromCache(oldRelativePath);
                        }
                    }
                }

                LogFileWriter.LogMessage("HandleFileMoved method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in HandleFileMoved: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Handles file copy by uploading the new file to Leo AI
        /// </summary>
        /// <param name="poCmd"></param>
        /// <param name="ppoData"></param>
        private async Task HandleFileCopied(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage("File copy hook called.");

            var initResult = await InitializeClientAndCache(poCmd);
            if (!initResult.success)
            {
                LogFileWriter.LogError("Could not initialize Leo client or cache. Aborting copy operation.");
                return;
            }
            string directoryId = initResult.directoryId;
            string vaultDir = initResult.vaultDir;
            SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

            foreach (var data in ppoData)
            {
                // For PostCopy, mbsStrData1 is the source file path and mbsStrData2 is the destination.
                string sourceFilePath = data.mbsStrData1;
                string destFilePath = data.mbsStrData2;

                LogFileWriter.LogMessage($"Processing copy from '{sourceFilePath}' to '{destFilePath}'");

                try
                {
                    // We only care about uploading the new destination file
                    var fileInfo = await LeoClient.CreateFileAsync(directoryId, vaultDir, destFilePath);
                    if (fileInfo != null)
                    {
                        LogFileWriter.LogMessage($"Successfully created copied file in Leo: {destFilePath}");
                        AddFileToCache(ConvertToFileMetadata(fileInfo));
                    }
                    else
                    {
                        LogFileWriter.LogError($"Failed to create copied file in Leo: {destFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Error processing file copy for '{destFilePath}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles a file check-in event. Assumes the file content has changed and replaces it on the server.
        /// </summary>
        private async Task HandleFileCheckIn(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            LogFileWriter.LogMessage("File check-in hook called.");

            var initResult = await InitializeClientAndCache(poCmd);
            if (!initResult.success)
            {
                return;
            }
            string directoryId = initResult.directoryId;
            string vaultDir = initResult.vaultDir;
            SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

            foreach (var cmdData in ppoData)
            {
                string filePath = cmdData.mbsStrData1;
                string relativePath = NormalizePath(SecureApiClient.GetRelativePath(vaultDir, filePath));

                // Get the component ID for the old file version (for deletion later)
                string oldComponentId = null;
                if (_pathToServerFileCache.TryGetValue(relativePath, out oldComponentId))
                {
                    LogFileWriter.LogMessage($"File check-in: Found existing version of '{relativePath}' with component ID: {oldComponentId}");
                }

                // 1. Upload the new file version FIRST
                List<FileData> fileStructure = pdmHelper.GetFileStructure(filePath);
                if (fileStructure != null && fileStructure.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading new version for checked-in file: {filePath}");
                    await UpdateFilesToLeoAI(fileStructure, directoryId, vaultDir);

                    // 2. Only after successful upload, delete the old file version if it existed
                    if (!string.IsNullOrEmpty(oldComponentId))
                    {
                        LogFileWriter.LogMessage($"File check-in: Deleting old version of '{relativePath}' after successful upload of new version.");
                        if (await LeoClient.DeleteFileAsync(directoryId, oldComponentId, relativePath))
                        {
                            LogFileWriter.LogMessage($"Successfully deleted old version of '{relativePath}'");
                            // Note: Cache is already updated by UpdateFilesToLeoAI with the new file info
                        }
                        else
                        {
                            LogFileWriter.LogError($"Failed to delete old version of '{relativePath}'. New version uploaded but old version remains.");
                            // Note: The new version is still available, so the check-in succeeded
                        }
                    }
                }
                else
                {
                    LogFileWriter.LogError($"Failed to get file structure for check-in: {filePath}. Check-in operation aborted - old version remains intact.");
                }
            }
            LogFileWriter.LogMessage("HandleFileCheckIn method finished");
        }

        /// <summary>
        /// Handles an "undo check-out" event, which can include a file rename.
        /// </summary>
        private async Task HandleFileUndoCheckOut(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                LogFileWriter.LogMessage("HandleFileUndoCheckOut method called");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success)
                {
                    return;
                }
                string directoryId = initResult.directoryId;
                string vaultDir = initResult.vaultDir;
                SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

                foreach (var cmdData in ppoData)
                {
                    string oldFilePath = cmdData.mbsStrData1;
                    string newFilePath = cmdData.mbsStrData2;

                    // If newFilePath is empty or same as old, it's not a rename, so do nothing.
                    if (string.IsNullOrEmpty(newFilePath) || oldFilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        LogFileWriter.LogMessage($"Undo checkout for '{oldFilePath}' with no name change. No action needed.");
                        continue;
                    }

                    // This is a rename. Treat it like a move.
                    LogFileWriter.LogMessage($"Undo checkout with rename detected. Old: '{oldFilePath}', New: '{newFilePath}'");

                    // 1. Upload the new file location
                    List<FileData> fileStructure = pdmHelper.GetFileStructure(newFilePath);
                    if (fileStructure != null && fileStructure.Count > 0)
                    {
                        LogFileWriter.LogMessage($"Uploading renamed file structure for: {newFilePath}");
                        await UpdateFilesToLeoAI(fileStructure, directoryId, vaultDir);
                    }

                    // 2. Delete the old file record from the server
                    string oldRelativePath = NormalizePath(SecureApiClient.GetRelativePath(vaultDir, oldFilePath));
                    if (_pathToServerFileCache.TryGetValue(oldRelativePath, out var componentId))
                    {
                        LogFileWriter.LogMessage($"Deleting old record for renamed file: '{oldRelativePath}'");
                        if (await LeoClient.DeleteFileAsync(directoryId, componentId, oldRelativePath))
                        {
                            RemoveFileFromCache(oldRelativePath);
                        }
                    }
                }
                LogFileWriter.LogMessage("HandleFileUndoCheckOut method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in HandleFileUndoCheckOut: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        private async Task HandleFileRename(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                LogFileWriter.LogMessage("HandleFileRename method called");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success)
                {
                    return;
                }
                string directoryId = initResult.directoryId;
                string vaultDir = initResult.vaultDir;
                SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

                foreach (var cmdData in ppoData)
                {
                    var vault = poCmd.mpoVault as IEdmVault5;
                    IEdmFolder5 parentFolder = vault.GetObject(EdmObjectType.EdmObject_Folder, cmdData.mlObjectID2) as IEdmFolder5;
                    if (parentFolder == null)
                    {
                        LogFileWriter.LogError($"Could not retrieve parent folder (ID: {cmdData.mlObjectID2}) for rename operation.");
                        continue;
                    }

                    string oldFilePath = Path.Combine(parentFolder.LocalPath, cmdData.mbsStrData1);
                    string newFilePath = Path.Combine(parentFolder.LocalPath, cmdData.mbsStrData2);
                    LogFileWriter.LogMessage($"Processing rename: Old='{oldFilePath}' -> New='{newFilePath}'");

                    // This is a rename. Treat it like a move.
                    await MoveFileOnServer(directoryId, vaultDir, pdmHelper, oldFilePath, newFilePath);
                }
                LogFileWriter.LogMessage("HandleFileRename method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in HandleFileRename: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        private async Task HandleFolderRename(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                LogFileWriter.LogMessage("HandleFolderRename method called");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success) return;

                foreach (var cmdData in ppoData)
                {
                    var vault = poCmd.mpoVault as IEdmVault5;
                    IEdmFolder5 parentFolder = vault.GetObject(EdmObjectType.EdmObject_Folder, cmdData.mlObjectID2) as IEdmFolder5;
                    if (parentFolder == null)
                    {
                        LogFileWriter.LogError($"Could not retrieve parent folder (ID: {cmdData.mlObjectID2}) for folder rename.");
                        continue;
                    }

                    string oldFolderPath = Path.Combine(parentFolder.LocalPath, cmdData.mbsStrData1);
                    string newFolderPath = Path.Combine(parentFolder.LocalPath, cmdData.mbsStrData2);

                    await HandleRecursiveMove(oldFolderPath, newFolderPath, poCmd);
                }
                LogFileWriter.LogMessage("HandleFolderRename method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in HandleFolderRename: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        // Generic method to handle directory operations with different file operation functions
        private async Task HandleDirectoryOperation(EdmCmd poCmd, EdmCmdData[] ppoData, string operationName,
            Func<string, string, string, string, SolidWorksPdmHelper, string[]> getFilesFunc,
            Func<string, string, string, SolidWorksPdmHelper, string, Task> fileOperationFunc)
        {
            try
            {
                LogFileWriter.LogMessage($"{operationName} method called");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success)
                {
                    LogFileWriter.LogError($"Could not initialize Leo client or cache. Aborting {operationName.ToLower()} operation.");
                    return;
                }
                string directoryId = initResult.directoryId;
                string vaultDir = initResult.vaultDir;
                SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

                foreach (var cmdData in ppoData)
                {
                    try
                    {
                        // Get the files to process using the provided function
                        var filesToProcess = getFilesFunc(cmdData.mbsStrData1, cmdData.mbsStrData2, directoryId, vaultDir, pdmHelper);

                        foreach (var filePath in filesToProcess)
                        {
                            LogFileWriter.LogMessage($"Processing {operationName.ToLower()} file: {filePath}");

                            // Execute the file operation using the provided function
                            await fileOperationFunc(directoryId, vaultDir, filePath, pdmHelper, cmdData.mbsStrData1);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Error processing {operationName.ToLower()} for folder '{cmdData.mbsStrData1}': {ex.Message}");
                    }
                }
                LogFileWriter.LogMessage($"{operationName} method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in {operationName}: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        private async Task HandleFolderMove(EdmCmd poCmd, EdmCmdData[] ppoData)
        {
            try
            {
                LogFileWriter.LogMessage("HandleFolderMove method called");
                var initResult = await InitializeClientAndCache(poCmd);
                if (!initResult.success) return;

                foreach (var cmdData in ppoData)
                {
                    await HandleRecursiveMove(cmdData.mbsStrData1, cmdData.mbsStrData2, poCmd);
                }
                LogFileWriter.LogMessage("HandleFolderMove method finished");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in HandleFolderMove: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }





        private async Task HandleRecursiveMove(string oldFolderPath, string newFolderPath, EdmCmd poCmd)
        {
            var initResult = await InitializeClientAndCache(poCmd);
            if (!initResult.success)
            {
                LogFileWriter.LogError("Could not initialize Leo client or cache. Aborting recursive move.");
                return;
            }
            string directoryId = initResult.directoryId;
            string vaultDir = initResult.vaultDir;
            SolidWorksPdmHelper pdmHelper = initResult.pdmHelper;

            LogFileWriter.LogMessage($"Starting recursive move from '{oldFolderPath}' to '{newFolderPath}'");

            try
            {
                // For PostMoveFolder, the folder has already been moved, so we need to get files from the NEW location
                // and calculate what their old paths would have been
                var filesToMove = Directory.GetFiles(newFolderPath, "*.*", SearchOption.AllDirectories);

                foreach (var newFilePath in filesToMove)
                {
                    // Calculate what the old path would have been
                    string oldFilePath = newFilePath.Replace(newFolderPath, oldFolderPath);
                    LogFileWriter.LogMessage($"Processing moved file: '{oldFilePath}' -> '{newFilePath}'");
                    await MoveFileOnServer(directoryId, vaultDir, pdmHelper, oldFilePath, newFilePath);
                }

                LogFileWriter.LogMessage("Finished recursive move.");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error during recursive move: {ex.Message}");
            }
        }

        private async Task MoveFileOnServer(string directoryId, string vaultDir, SolidWorksPdmHelper pdmHelper, string oldFilePath, string newFilePath)
        {
            string oldRelativePath = SecureApiClient.GetRelativePath(vaultDir, oldFilePath);
            string newRelativePath = SecureApiClient.GetRelativePath(vaultDir, newFilePath);
            string normalizedOldPath = NormalizePath(oldRelativePath);
            string normalizedNewPath = NormalizePath(newRelativePath);

            LogFileWriter.LogMessage($"Attempting to move file on server from '{normalizedOldPath}' to '{normalizedNewPath}'");

            // 1. Get the component ID for the old file (for deletion later)
            string componentId = await GetFileIdWithCacheFallback(normalizedOldPath, directoryId);
            if (string.IsNullOrEmpty(componentId))
            {
                LogFileWriter.LogMessage($"Cannot move file: component ID for '{normalizedOldPath}' not found.");
                return;
            }

            // 2. Create the new file record FIRST
            var fileInfo = await LeoClient.CreateFileAsync(directoryId, vaultDir, newFilePath, null);
            if (fileInfo != null)
            {
                LogFileWriter.LogMessage($"Successfully created new file record: '{normalizedNewPath}'");

                // 3. Only after successful creation, delete the old file record
                if (await LeoClient.DeleteFileAsync(directoryId, componentId, normalizedOldPath))
                {
                    LogFileWriter.LogMessage($"Successfully deleted old file record: '{normalizedOldPath}'");

                    // 4. Update cache: remove old entry and add new one
                    RemoveFileFromCache(normalizedOldPath);
                    AddFileToCache(ConvertToFileMetadata(fileInfo));
                }
                else
                {
                    LogFileWriter.LogError($"Failed to delete old file record during move: '{normalizedOldPath}'. New file created but old record remains.");
                    // Note: We still have the new file created, so the move partially succeeded
                    // The old file record will remain, but the new file is available
                    AddFileToCache(ConvertToFileMetadata(fileInfo));
                }
            }
            else
            {
                LogFileWriter.LogError($"Failed to create new file record for move: '{normalizedNewPath}'. Move operation aborted - old file remains intact.");
                // The old file remains untouched, so no data loss occurred
            }
        }

        private static string NormalizePath(string path)
        {
            return path?.Replace('\\', '/');
        }


        /// <summary>
        /// Helper method to reduce boilerplate in event handlers. Initializes the API client and ensures the cache is populated.
        /// </summary>
        private async Task<(bool success, string directoryId, string vaultDir, SolidWorksPdmHelper pdmHelper)> InitializeClientAndCache(EdmCmd poCmd)
        {
            string directoryId = null;
            string vaultDir = null;
            SolidWorksPdmHelper pdmHelper = null;

            try
            {
                var vault = poCmd.mpoVault as IEdmVault5;
                if (vault == null)
                {
                    LogFileWriter.LogError("Failed to get vault object.");
                    return (false, null, null, null);
                }
                vaultDir = vault.RootFolderPath;
                pdmHelper = new SolidWorksPdmHelper(vault);

                LeoAuthConfig authConfig = ReadAuthConfig();
                if (authConfig == null)
                {
                    LogFileWriter.LogError("Failed to read auth config.");
                    return (false, null, null, null);
                }

                LeoClient = new SecureApiClient(authConfig.ApiKey, authConfig.ProjectId);
                if (LeoClient == null)
                {
                    LogFileWriter.LogError("Failed to initialize Leo API client.");
                    return (false, null, null, null);
                }

                string macAddress = LeoAIDataUtilities.GetMacAddress();
                var directories = await LeoClient.GetDirectoryInfoAsync(macAddress);
                string localVaultDir = vaultDir; // Copy out parameter to local variable for lambda
                directoryId = directories?.FirstOrDefault(d => d.Uri.Equals(localVaultDir, StringComparison.OrdinalIgnoreCase))?.Id;

                if (string.IsNullOrEmpty(directoryId))
                {
                    LogFileWriter.LogError("Failed to create a new directory in Leo. Aborting.");
                    return (false, null, null, null);
                }

                await EnsureCacheIsPopulated(directoryId);
                return (true, directoryId, vaultDir, pdmHelper);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error during client initialization: {ex.Message}");
                return (false, null, null, null);
            }
        }

        private LeoAICadDataClient.Utilities.FileInfo ConvertToFileInfo(SyncMetadataFile file)
        {
            if (file == null) return null;
            return new LeoAICadDataClient.Utilities.FileInfo
            {
                ComponentId = file.ComponentId,
                FilePathInDirectory = file.FilePathInDirectory,
                CheckSum = file.CheckSum,
                mimeType = file.MimeType
            };
        }

        /// <summary>
        /// Safely uploads vault data by extracting COM data on main thread first
        /// </summary>
        /// <param name="edmVault">The vault to upload data from</param>
        /// <param name="waitForCompletion">If true, blocks until upload completes (needed for install events)</param>
        private void SafeUploadData(IEdmVault5 edmVault, bool waitForCompletion = false)
        {
            try
            {
                // Extract all needed data from COM object on main thread
                string vaultDir = edmVault.RootFolderPath;
                string vaultName = edmVault.Name;

                LogFileWriter.LogMessage($"SafeUploadData method called for vault: {vaultName}");
                LogFileWriter.LogMessage($"Vault directory: {vaultDir}");

                // This is a full, one-time sync. The cache should be cleared before starting.
                Task.Run(async () => await ClearCacheSafely()).Wait();

                // Create SolidWorksPdmHelper on main thread
                SolidWorksPdmHelper pdmHelper = new SolidWorksPdmHelper(edmVault);
                pdmHelper.ProcessFolders(edmVault);

                // Get the file structure on main thread
                List<FileData> topFileStructure = pdmHelper.FilesInfo;
                LogFileWriter.LogMessage($"Found {topFileStructure.Count} files in vault");

                // Handle async operations based on whether we need to wait for completion
                if (waitForCompletion)
                {
                    // For install operations, block until completion to prevent PDM from unloading the add-in
                    try
                    {
                        Task.Run(async () =>
                        {
                            await UploadVaultDataAsync(vaultDir, vaultName, topFileStructure);
                            LogFileWriter.LogMessage("SafeUploadData method finished successfully");
                        }).Wait();
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Exception in SafeUploadData sync operation: {ex.Message}");
                        LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
                    }
                }
                else
                {
                    // For regular operations, run in background
                    Task.Run(async () =>
                    {
                        try
                        {
                            await UploadVaultDataAsync(vaultDir, vaultName, topFileStructure);
                            LogFileWriter.LogMessage("SafeUploadData method finished successfully");
                        }
                        catch (Exception ex)
                        {
                            LogFileWriter.LogError($"Exception in SafeUploadData async operation: {ex.Message}");
                            LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Exception in SafeUploadData: {ex.Message}");
                LogFileWriter.LogError($"StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Async method that uploads vault data without using COM objects
        /// Uses intelligent sync comparison to only upload what has changed
        /// </summary>
        /// <param name="vaultDir">Vault root directory path</param>
        /// <param name="vaultName">Vault name</param>
        /// <param name="filesInfo">List of files to upload</param>
        private async Task UploadVaultDataAsync(string vaultDir, string vaultName, List<FileData> filesInfo)
        {
            LogFileWriter.LogMessage("UploadVaultDataAsync method called for initial sync.");

            // 1. Initialize Descope Auth
            LeoAuthConfig authConfig = ReadAuthConfig();
            if (authConfig == null)
            {
                LogFileWriter.LogError("Failed to load authentication configuration. Aborting file upload.");
                return;
            }

            var descopeApiKey = authConfig.ApiKey;
            var descopeProjectId = authConfig.ProjectId;

            LogFileWriter.LogMessage("Descope Auth initialized");

            // Initialize LeoApiClient
            LeoClient = new SecureApiClient(descopeApiKey, descopeProjectId);
            LogFileWriter.LogMessage("LeoApiClient initialized");

            // CreateDirectory
            string directoryId = await CreateDirectory(vaultDir);
            if (string.IsNullOrEmpty(directoryId))
            {
                LogFileWriter.LogError("Failed to create directory");
                return;
            }

            LogFileWriter.LogMessage($"Directory created with ID: {directoryId}");

            // Get current sync metadata from server
            LogFileWriter.LogMessage("Getting current sync metadata from server...");
            SyncMetadataResponse serverSyncData = await LeoClient.GetSyncMetadataAsync(directoryId);

            // Compare local files with server state and categorize changes
            var syncChanges = AnalyzeSyncChanges(filesInfo, serverSyncData, vaultDir);

            LogFileWriter.LogMessage($"Sync analysis complete:");
            LogFileWriter.LogMessage($"  New files: {syncChanges.NewFiles.Count}");
            LogFileWriter.LogMessage($"  Modified files: {syncChanges.ModifiedFiles.Count}");
            LogFileWriter.LogMessage($"  Moved files: {syncChanges.MovedFiles.Count}");
            LogFileWriter.LogMessage($"  Deleted files: {syncChanges.DeletedFiles.Count}");

            // Process changes in the correct order:
            // 1. Upload new files (CAD files first, then assemblies, then documents)
            if (syncChanges.NewFiles.Count > 0)
            {
                LogFileWriter.LogMessage("Processing new files...");

                // CAD files with dependencies (SOLIDWORKS assemblies)
                var newAssemblies = syncChanges.NewFiles.Where(f => f.mimeType == "application/x-sldasm").ToList();

                // CAD files without dependencies (parts, STEP)
                var newCadFiles = syncChanges.NewFiles.Where(f =>
                    f.mimeType == "application/x-sldprt" ||
                    f.mimeType == "model/step").ToList();

                // Document files
                var newDocuments = syncChanges.NewFiles.Where(f =>
                    f.mimeType == "text/plain" ||
                    f.mimeType == "application/pdf" ||
                    f.mimeType == "application/msword" ||
                    f.mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document").ToList();

                // Upload in dependency order: CAD files first, then assemblies, then documents
                if (newCadFiles.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {newCadFiles.Count} new CAD files (parts, STEP)...");
                    await UpdateFilesToLeoAI(newCadFiles, directoryId, vaultDir);
                }

                if (newAssemblies.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {newAssemblies.Count} new assemblies...");
                    await UpdateFilesToLeoAI(newAssemblies, directoryId, vaultDir);
                }

                if (newDocuments.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {newDocuments.Count} new documents...");
                    await UpdateFilesToLeoAI(newDocuments, directoryId, vaultDir);
                }
            }

            // 2. Upload moved files (same order as new files)
            if (syncChanges.MovedFiles.Count > 0)
            {
                LogFileWriter.LogMessage("Processing moved files...");

                var movedAssemblies = syncChanges.MovedFiles.Where(f => f.mimeType == "application/x-sldasm").ToList();
                var movedCadFiles = syncChanges.MovedFiles.Where(f =>
                    f.mimeType == "application/x-sldprt" ||
                    f.mimeType == "model/step").ToList();
                var movedDocuments = syncChanges.MovedFiles.Where(f =>
                    f.mimeType == "text/plain" ||
                    f.mimeType == "application/pdf" ||
                    f.mimeType == "application/msword" ||
                    f.mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document").ToList();

                if (movedCadFiles.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {movedCadFiles.Count} moved CAD files...");
                    await UpdateFilesToLeoAI(movedCadFiles, directoryId, vaultDir);
                }

                if (movedAssemblies.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {movedAssemblies.Count} moved assemblies...");
                    await UpdateFilesToLeoAI(movedAssemblies, directoryId, vaultDir);
                }

                if (movedDocuments.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {movedDocuments.Count} moved documents...");
                    await UpdateFilesToLeoAI(movedDocuments, directoryId, vaultDir);
                }
            }

            // 3. Upload modified files (same order as new files)
            if (syncChanges.ModifiedFiles.Count > 0)
            {
                LogFileWriter.LogMessage("Processing modified files...");

                var modifiedAssemblies = syncChanges.ModifiedFiles.Where(f => f.mimeType == "application/x-sldasm").ToList();
                var modifiedCadFiles = syncChanges.ModifiedFiles.Where(f =>
                    f.mimeType == "application/x-sldprt" ||
                    f.mimeType == "model/step").ToList();
                var modifiedDocuments = syncChanges.ModifiedFiles.Where(f =>
                    f.mimeType == "text/plain" ||
                    f.mimeType == "application/pdf" ||
                    f.mimeType == "application/msword" ||
                    f.mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document").ToList();

                if (modifiedCadFiles.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {modifiedCadFiles.Count} modified CAD files...");
                    await UpdateFilesToLeoAI(modifiedCadFiles, directoryId, vaultDir);
                }

                if (modifiedAssemblies.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {modifiedAssemblies.Count} modified assemblies...");
                    await UpdateFilesToLeoAI(modifiedAssemblies, directoryId, vaultDir);
                }

                if (modifiedDocuments.Count > 0)
                {
                    LogFileWriter.LogMessage($"Uploading {modifiedDocuments.Count} modified documents...");
                    await UpdateFilesToLeoAI(modifiedDocuments, directoryId, vaultDir);
                }
            }

            // 4. Delete files that no longer exist locally
            if (syncChanges.DeletedFiles.Count > 0)
            {
                LogFileWriter.LogMessage($"Deleting {syncChanges.DeletedFiles.Count} files from server...");

                foreach (var deletedFile in syncChanges.DeletedFiles)
                {
                    // Use the original sync metadata for deletion - don't get fresh data as it may include newly created files
                    string pathForDelete = NormalizePath(deletedFile.FilePathInDirectory);
                    LogFileWriter.LogMessage($"Attempting to delete file using path from original sync metadata: {pathForDelete}");
                    bool deleteSuccess = await LeoClient.DeleteFileAsync(directoryId, deletedFile.ComponentId, pathForDelete);
                    if (deleteSuccess)
                    {
                        LogFileWriter.LogMessage($"Successfully deleted: {pathForDelete}");
                        // Remove from cache to keep it in sync
                        RemoveFileFromCache(pathForDelete);
                    }
                    else
                    {
                        LogFileWriter.LogError($"Failed to delete: {pathForDelete}");
                    }
                }
            }

            LeoFilesInformation.Clear();
            LogFileWriter.LogMessage("Sync process completed successfully");
        }

        /// <summary>
        /// Determines if a file should be processed based on its extension
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>True if the file should be processed</returns>
        private bool IsProcessableFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();

            // SOLIDWORKS files (excluding drawings)
            if (extension == ".sldprt" || extension == ".sldasm")
                return true;

            // Generic CAD formats
            if (extension == ".step" || extension == ".stp")
                return true;

            // Parasolid files - temporarily disabled until API MIME type is confirmed
            // if (extension == ".x_t" || extension == ".xt")
            //     return true;

            // Document formats
            if (extension == ".txt" || extension == ".pdf")
                return true;

            // Microsoft Word formats
            if (extension == ".doc" || extension == ".docx")
                return true;

            return false;
        }

        /// <summary>
        /// Determines if a file has dependencies (i.e., is a SOLIDWORKS assembly)
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>True if the file may have dependencies</returns>
        private bool FileHasDependencies(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            // Only SOLIDWORKS assemblies have dependencies (drawings no longer supported)
            return extension == ".sldasm";
        }

        private async Task ClearCacheSafely()
        {
            // Wait for any ongoing cache refresh to complete before clearing
            while (true)
            {
                lock (_cacheLock)
                {
                    if (!_isRefreshingCache)
                    {
                        _pathToServerFileCache.Clear();
                        _isCachePopulated = false;
                        LogFileWriter.LogMessage("Cache cleared safely.");
                        return;
                    }
                }

                LogFileWriter.LogMessage("Waiting for cache refresh to complete before clearing...");
                await Task.Delay(50); // Short delay before checking again
            }
        }

        /// <summary>
        /// Registers vault installation in the registry for tracking
        /// </summary>
        private void RegisterVaultInstallation(string vaultName, string vaultPath)
        {
            try
            {
                string registryPath = @"SOFTWARE\LeoAI\PDM-AddIn\InstalledVaults";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath))
                {
                    if (key != null)
                    {
                        key.SetValue(vaultName, vaultPath);
                        LogFileWriter.LogMessage($"Registered vault installation in registry: {vaultName} -> {vaultPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to register vault installation in registry: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes vault installation from the registry after successful cleanup
        /// </summary>
        private void UnregisterVaultInstallation(string vaultName)
        {
            try
            {
                string registryPath = @"SOFTWARE\LeoAI\PDM-AddIn\InstalledVaults";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(vaultName, false);
                        LogFileWriter.LogMessage($"Removed vault installation from registry: {vaultName}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to remove vault installation from registry: {ex.Message}");
            }
        }


    }
}
