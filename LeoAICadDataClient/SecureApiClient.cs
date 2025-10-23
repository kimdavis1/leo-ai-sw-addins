namespace LeoAICadDataClient
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;
    using LeoAICadDataClient.Utilities;
    using Newtonsoft.Json;

    public class SecureApiClient
    {
        private readonly string _baseApiUrl = "https://api.getleo.ai/"; //"http://localhost:8000/";
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _projectId;
        private string _jwtToken;
        private const int MaxRetries = 5;
        private const int InitialRetryDelayMs = 1000;

        public SecureApiClient(string apiKey, string projectId)
        {
            _projectId = projectId;
            _apiKey = apiKey;
            _httpClient = new HttpClient { BaseAddress = new Uri(_baseApiUrl) };
            _httpClient.DefaultRequestHeaders.ExpectContinue = false; // Explicitly disable Expect: 100-continue
            Logger.Info("SecureApiClient initialized with standard HttpClient.");

            // Initialize Sentry for API error tracking
            SentryApiErrorHandler.Initialize("Production");
        }

        /// <summary>
        /// Creates a SecureApiClient from a config file containing ApiKey and ProjectId
        /// </summary>
        /// <param name="configFilePath">Path to LeoAuthKey.json file</param>
        /// <returns>Initialized SecureApiClient</returns>
        public static SecureApiClient CreateFromConfigFile(string configFilePath)
        {
            if (string.IsNullOrEmpty(configFilePath))
            {
                throw new ArgumentNullException(nameof(configFilePath), "Config file path cannot be null or empty");
            }

            if (!File.Exists(configFilePath))
            {
                throw new FileNotFoundException($"Auth config not found: {configFilePath}");
            }

            Logger.Info($"Reading auth config from: {configFilePath}");
            string json = File.ReadAllText(configFilePath);
            var config = JsonConvert.DeserializeObject<LeoAuthConfig>(json);

            if (config == null || string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ProjectId))
            {
                throw new Exception($"Invalid auth config in {configFilePath} - missing ApiKey or ProjectId");
            }

            Logger.Info($"Auth config loaded successfully - ProjectId: {config.ProjectId}");
            return new SecureApiClient(config.ApiKey, config.ProjectId);
        }

        /// <summary>
        /// Creates a SecureApiClient by searching for config in standard locations
        /// Priority: 1) Provided path, 2) Default installation folder
        /// </summary>
        /// <param name="vaultConfigPath">Optional path to vault config file</param>
        /// <returns>Initialized SecureApiClient</returns>
        public static SecureApiClient CreateFromStandardLocations(string vaultConfigPath = null)
        {
            // Try vault config path first if provided
            if (!string.IsNullOrEmpty(vaultConfigPath) && File.Exists(vaultConfigPath))
            {
                Logger.Info($"Using vault config path: {vaultConfigPath}");
                return CreateFromConfigFile(vaultConfigPath);
            }

            // Try environment variable
            string envPath = LeoAIDataUtilities.ReadEnvVariableByName("LEO_AUTH_KEY", false);
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            {
                Logger.Info($"Using config from environment variable: {envPath}");
                return CreateFromConfigFile(envPath);
            }

            // Fallback to default installation folder
            string defaultPath = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.json");
            if (File.Exists(defaultPath))
            {
                Logger.Info($"Using config from default installation folder: {defaultPath}");
                return CreateFromConfigFile(defaultPath);
            }

            // No config found
            throw new FileNotFoundException(
                "Leo AI authentication configuration not found!\n\n" +
                "Please place the LeoAuthKey.json file in one of the following locations:\n" +
                "1. Vault location: <vault_root>/LeoAI_TaskData/LeoAuthKey.json\n" +
                "2. Default location: C:\\Program Files\\LeoAISwPdmAddIn\\LeoAuthKey.json\n" +
                "3. Custom location specified in LEO_AUTH_KEY environment variable");
        }

        /// <summary>
        /// Authentication configuration model
        /// </summary>
        private class LeoAuthConfig
        {
            public string ApiKey { get; set; }
            public string ProjectId { get; set; }
        }

        public void SetJwtToken(string token)
        {
            _jwtToken = token;
            // Clear previous auth headers
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _httpClient.DefaultRequestHeaders.Remove("X-API-Key");

            if (!string.IsNullOrEmpty(_jwtToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }
            Logger.Info("Auth headers set on HttpClient.");
        }

        private async Task RefreshTokenIfRequiredAsync()
        {
            try
            {
                bool isTokenValid = JwtAuthHelper.ValidateJwtToken(_jwtToken, _apiKey, _projectId);
                if (!isTokenValid)
                {
                    Logger.Info("Token is not valid, attempting to refresh.");
                    var descopeClient = new DescopeClient(_projectId, "https://api.descope.com");

                    var tokenTask = descopeClient.ExchangeTokenAsync(_apiKey);
                    if (await Task.WhenAny(tokenTask, Task.Delay(10000)) == tokenTask)
                    {
                        string newJwtToken = await tokenTask;
                        if (!string.IsNullOrEmpty(newJwtToken))
                        {
                            _jwtToken = newJwtToken;
                            Logger.Info("Token refreshed successfully.");
                        }
                        else
                        {
                            Logger.Error("Failed to refresh token, new token is null or empty.");
                        }
                    }
                    else
                    {
                        Logger.Error("Token refresh timed out after 10 seconds.");
                    }
                }
                SetJwtToken(_jwtToken);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing token: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes an async function with retry logic for rate limit errors (HTTP 429)
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
        {
            int retryDelay = InitialRetryDelayMs;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex)
                {
                    // Check if it's a rate limit error (HTTP 429)
                    bool isRateLimit = ex.Message.Contains("429") ||
                                      ex.Message.ToLower().Contains("rate limit") ||
                                      ex.Message.ToLower().Contains("too many requests");

                    if (isRateLimit && attempt < MaxRetries)
                    {
                        // Exponential backoff for rate limits
                        Logger.Info($"Rate limit hit for {operationName}, retrying in {retryDelay}ms (attempt {attempt + 1}/{MaxRetries})");
                        await Task.Delay(retryDelay);
                        retryDelay *= 2; // Exponential backoff
                        continue;
                    }

                    // For all other errors or exhausted retries, re-throw
                    throw;
                }
            }

            // Should never reach here, but compiler requires return
            throw new Exception($"Failed to execute {operationName} after {MaxRetries} retries");
        }

        /// <summary>
        /// Creates a file in Leo AI with separate logical and physical paths.
        /// logicalFilePath: The path shown to user/API (relative path calculated from this)
        /// actualFilePath: The actual file system path to read file content from (can be archive path or temp path)
        /// externalId: The unique PDM file ID
        /// </summary>
        public async Task<LeoAICadDataClient.Utilities.FileInfo> CreateFileAsync(string directoryId, string vaultPath, string logicalFilePath, string actualFilePath, string externalId, Dictionary<string, string> childInfos = null)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"Attempting to create file: {logicalFilePath} (reading from: {actualFilePath}) in directory: {directoryId}");
                    LeoFileInfo.LeoFileInformation fInfo = LeoFileInfo.GetFileInfo(actualFilePath); // Read from actual path
                    string relativePath = GetRelativePath(vaultPath, logicalFilePath); // Use logical path for API
                    string memeType = LeoAIMemeType.GetMemeType(logicalFilePath); // Use logical path for extension

                    // Log API request parameters
                    Logger.Info($"[API CALL] CreateFile: path={NormalizeFilePathForApi(relativePath)}, checksum={fInfo.CheckSum}, mimeType={memeType}, externalId={externalId}, hasFileContent=true");
                    if (childInfos != null && childInfos.Count > 0)
                    {
                        Logger.Info($"[API CALL] CreateFile dependencies: {JsonConvert.SerializeObject(childInfos)}");
                    }

                    using (var content = new MultipartFormDataContent())
                    {
                        content.Add(new StringContent(memeType), "mimeType");
                        content.Add(new StringContent(fInfo.CheckSum), "checkSum");
                        content.Add(new StringContent(NormalizeFilePathForApi(relativePath)), "filePathInDirectory");
                        content.Add(new StringContent(externalId), "externalId");

                        var fileBytes = Convert.FromBase64String(fInfo.Base64EncodedFile);
                        content.Add(new ByteArrayContent(fileBytes), "file", Path.GetFileName(logicalFilePath));

                        if (childInfos != null && childInfos.Count > 0)
                        {
                            var childDatas = childInfos.Select(kvp => new ChildData(kvp.Key, kvp.Value)).ToList();
                            content.Add(new StringContent(JsonConvert.SerializeObject(childDatas)), "dependencies");
                        }

                        var response = await _httpClient.PostAsync($"api/v1/synced-directories/{directoryId}/files", content);
                        var responseString = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            Logger.Info($"Successfully created file: {logicalFilePath}");
                            return JsonConvert.DeserializeObject<LeoAICadDataClient.Utilities.FileInfo>(responseString);
                        }
                        else
                        {
                            Logger.Error($"Failed to create file: {logicalFilePath}. Status: {response.StatusCode}, Response: {responseString}");

                            // Capture unexpected API errors to Sentry (excluding rate limits which are handled separately)
                            if ((int)response.StatusCode != 429)
                            {
                                SentryApiErrorHandler.CaptureApiError("CreateFile", (int)response.StatusCode, responseString,
                                    new Dictionary<string, string> { { "file", logicalFilePath }, { "directoryId", directoryId } });
                            }

                            if ((int)response.StatusCode == 429)
                            {
                                throw new Exception($"Rate limit (429): {responseString}");
                            }
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in CreateFile: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "CreateFile" },
                        { "file", logicalFilePath },
                        { "directoryId", directoryId }
                    });

                    throw;
                }
            }, $"CreateFile({Path.GetFileName(logicalFilePath)})");
        }

        public static string GetRelativePath(string rootPath, string targetPath)
        {
            // Use Path-based calculation instead of URI-based to avoid issues with spaces and special characters
            // Normalize paths to ensure consistent comparison
            string normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedTarget = Path.GetFullPath(targetPath);

            // Check if target starts with root
            if (normalizedTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Remove root from target to get relative path
                string relativePath = normalizedTarget.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Logger.Info($"Calculated relative path: '{relativePath}' from root: '{rootPath}' and target: '{targetPath}'");
                return relativePath;
            }
            else
            {
                // If target doesn't start with root, return the target as-is (shouldn't happen in normal operation)
                Logger.Info($"Target path does not start with root path. Root: '{rootPath}', Target: '{targetPath}'");
                return targetPath;
            }
        }

        public static string NormalizeFilePathForApi(string filePath)
        {
            return filePath.Replace('\\', '/');
        }

        public async Task<string> CreateDirectoryAsync(string machineId, string uri)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    if (!LeoAIDataUtilities.IsValidMacAddressFormat(machineId))
                    {
                        Logger.Error($"Invalid MAC address format: {machineId}. Expected format: XX:XX:XX:XX:XX:XX or XX-XX-XX-XX-XX-XX");
                        return string.Empty;
                    }

                    Logger.Info($"Creating directory for machine: {machineId}, uri: {uri}");
                    var jsonPayload = JsonConvert.SerializeObject(new { machineId, uri });
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync("api/v1/synced-directories", content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var project = JsonConvert.DeserializeObject<ProjectData>(responseString);
                        Logger.Info($"Directory created successfully with ID: {project.Id}");
                        return project.Id;
                    }
                    else
                    {
                        Logger.Error($"Failed to create directory. Status: {response.StatusCode}, Response: {responseString}");

                        // Capture unexpected API errors to Sentry
                        if ((int)response.StatusCode != 429)
                        {
                            SentryApiErrorHandler.CaptureApiError("CreateDirectory", (int)response.StatusCode, responseString,
                                new Dictionary<string, string> { { "machineId", machineId }, { "uri", uri } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        return string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"CreateDirectory failed: {ex.Message}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "CreateDirectory" },
                        { "machineId", machineId },
                        { "uri", uri }
                    });

                    throw;
                }
            }, $"CreateDirectory({machineId})");
        }

        public async Task<List<LeoDirectoryInfo>> GetDirectoryInfoAsync(string machineId)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"GetDirectoryInfo: Starting to fetch directory info for machine {machineId}");
                    var response = await _httpClient.GetAsync("api/v1/synced-directories");
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonConvert.DeserializeObject<List<LeoDirectoryInfo>>(responseString);
                    }
                    else
                    {
                        Logger.Error($"GetDirectoryInfo failed. Status: {response.StatusCode}, Response: {responseString}");

                        // Capture unexpected API errors to Sentry
                        if ((int)response.StatusCode != 429)
                        {
                            SentryApiErrorHandler.CaptureApiError("GetDirectoryInfo", (int)response.StatusCode, responseString,
                                new Dictionary<string, string> { { "machineId", machineId } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in GetDirectoryInfo: {ex.Message}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "GetDirectoryInfo" },
                        { "machineId", machineId }
                    });

                    throw;
                }
            }, $"GetDirectoryInfo({machineId})");
        }

        public async Task<LeoAICadDataClient.Utilities.FileInfo> GetFileInfoByPathAsync(string directoryId, string relativePath)
        {
            try
            {
                Logger.Info($"GetFileInfoByPath: Attempting to find file '{relativePath}' in directory '{directoryId}'");

                // Fetch specific file using filepath_in_directory query parameter
                string normalizedPath = NormalizeFilePathForApi(relativePath);
                var syncMetadata = await GetSyncMetadataAsync(directoryId, normalizedPath);

                if (syncMetadata == null || syncMetadata.Files == null || syncMetadata.Files.Count == 0)
                {
                    Logger.Info($"GetFileInfoByPath: File '{normalizedPath}' not found in directory '{directoryId}'.");
                    return null;
                }

                // Should return exactly one file
                var file = syncMetadata.Files.FirstOrDefault();
                Logger.Info($"GetFileInfoByPath: Successfully found file '{normalizedPath}'.");

                var fileInfo = new LeoAICadDataClient.Utilities.FileInfo
                {
                    ComponentId = file.ComponentId,
                    FilePathInDirectory = file.FilePathInDirectory,
                    CheckSum = file.CheckSum,
                    mimeType = file.MimeType,
                    ParentStatus = file.ParentStatus
                };

                Logger.Info($"[API RESPONSE] GetFileInfoByPath: componentId={fileInfo.ComponentId}, checksum={fileInfo.CheckSum}, parentStatus={fileInfo.ParentStatus}, path={fileInfo.FilePathInDirectory}");
                return fileInfo;
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in GetFileInfoByPath: {ex.Message}");
                return null;
            }
        }

        public async Task<SyncMetadataResponse> GetSyncMetadataAsync(string directoryId)
        {
            return await GetSyncMetadataAsync(directoryId, null);
        }

        public async Task<SyncMetadataResponse> GetSyncMetadataAsync(string directoryId, string filepathInDirectory)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    string url = $"api/v1/synced-directories/{directoryId}/files/sync-metadata";

                    if (!string.IsNullOrEmpty(filepathInDirectory))
                    {
                        url += $"?filepath_in_directory={Uri.EscapeDataString(filepathInDirectory)}";
                        Logger.Info($"GetSyncMetadata: Fetching sync metadata for specific file '{filepathInDirectory}' in directory {directoryId}");
                    }
                    else
                    {
                        Logger.Info($"GetSyncMetadata: Fetching all sync metadata for directory {directoryId}");
                    }

                    var response = await _httpClient.GetAsync(url);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        // Log the raw JSON response to help diagnose parsing issues
                        Logger.Info($"GetSyncMetadata: Raw JSON response: {responseString}");
                        return JsonConvert.DeserializeObject<SyncMetadataResponse>(responseString);
                    }
                    else
                    {
                        Logger.Error($"GetSyncMetadata failed. Status: {response.StatusCode}, Body: {responseString}");

                        // Capture unexpected API errors to Sentry
                        if ((int)response.StatusCode != 429)
                        {
                            SentryApiErrorHandler.CaptureApiError("GetSyncMetadata", (int)response.StatusCode, responseString,
                                new Dictionary<string, string> { { "directoryId", directoryId }, { "filepath", filepathInDirectory ?? "all" } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetSyncMetadata: Exception occurred: {ex.Message}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "GetSyncMetadata" },
                        { "directoryId", directoryId },
                        { "filepath", filepathInDirectory ?? "all" }
                    });

                    throw;
                }
            }, $"GetSyncMetadata({directoryId}, {filepathInDirectory ?? "all"})");
        }

        public async Task<bool> DeleteFileAsync(string directoryId, string filePathInDirectory)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    string normalizedPath = NormalizeFilePathForApi(filePathInDirectory);
                    string encodedFilePath = Uri.EscapeDataString(normalizedPath);
                    string requestUri = $"api/v1/synced-directories/{directoryId}/files?filePathInDirectory={encodedFilePath}";

                    Logger.Info($"[API CALL] DeleteFile: path={normalizedPath}, directoryId={directoryId}");
                    Logger.Info($"Sending DELETE request to: {requestUri}");

                    var response = await _httpClient.DeleteAsync(requestUri);

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Info($"Successfully deleted file: {normalizedPath}");
                        return true;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Logger.Error($"Failed to delete file: {normalizedPath}. Status: {response.StatusCode}, Response: {errorContent}");

                        // Capture unexpected API errors to Sentry
                        if ((int)response.StatusCode != 429)
                        {
                            SentryApiErrorHandler.CaptureApiError("DeleteFile", (int)response.StatusCode, errorContent,
                                new Dictionary<string, string> { { "file", normalizedPath }, { "directoryId", directoryId } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {errorContent}");
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in DeleteFile: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "DeleteFile" },
                        { "file", filePathInDirectory },
                        { "directoryId", directoryId }
                    });

                    throw;
                }
            }, $"DeleteFile({filePathInDirectory})");
        }

        /// <summary>
        /// Updates file location (move/rename) - sends checksum to identify the file but NOT the file content
        /// Backend uses checksum to attach new path to existing file
        /// checksum: The checksum from the OLD file location (to identify which file to update)
        /// externalId: The unique PDM file ID
        /// </summary>
        public async Task<LeoAICadDataClient.Utilities.FileInfo> UpdateFileLocationAsync(string directoryId, string vaultPath, string filePath, string checksum, string externalId, Dictionary<string, string> childInfos = null)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"Attempting to update file location: {filePath} in directory: {directoryId}");

                    // For move/rename: use the provided checksum (from old file) to identify the file on server
                    // Do NOT calculate new checksum - we already compared checksums earlier
                    string relativePath = GetRelativePath(vaultPath, filePath);
                    string mimeType = LeoAIMemeType.GetMemeType(filePath);

                    // Log API request parameters
                    Logger.Info($"[API CALL] UpdateFileLocation: path={NormalizeFilePathForApi(relativePath)}, checksum={checksum}, mimeType={mimeType}, externalId={externalId}, hasFileContent=false");
                    if (childInfos != null && childInfos.Count > 0)
                    {
                        Logger.Info($"[API CALL] UpdateFileLocation dependencies: {JsonConvert.SerializeObject(childInfos)}");
                    }

                    using (var content = new MultipartFormDataContent())
                    {
                        content.Add(new StringContent(mimeType), "mimeType");
                        content.Add(new StringContent(checksum), "checkSum");
                        content.Add(new StringContent(NormalizeFilePathForApi(relativePath)), "filePathInDirectory");
                        content.Add(new StringContent(externalId), "externalId");

                        // NOTE: We send checkSum so backend can identify which file to attach the new path to
                        // We do NOT send file content (no ByteArrayContent with Base64EncodedFile)

                        if (childInfos != null && childInfos.Count > 0)
                        {
                            var childDatas = childInfos.Select(kvp => new ChildData(kvp.Key, kvp.Value)).ToList();
                            content.Add(new StringContent(JsonConvert.SerializeObject(childDatas)), "dependencies");
                        }

                        var response = await _httpClient.PostAsync($"api/v1/synced-directories/{directoryId}/files", content);
                        var responseString = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            Logger.Info($"Successfully updated file location: {filePath}");
                            return JsonConvert.DeserializeObject<LeoAICadDataClient.Utilities.FileInfo>(responseString);
                        }
                        else
                        {
                            Logger.Error($"Failed to update file location: {filePath}. Status: {response.StatusCode}, Response: {responseString}");

                            // Capture unexpected API errors to Sentry
                            if ((int)response.StatusCode != 429)
                            {
                                SentryApiErrorHandler.CaptureApiError("UpdateFileLocation", (int)response.StatusCode, responseString,
                                    new Dictionary<string, string> { { "file", filePath }, { "directoryId", directoryId }, { "checksum", checksum } });
                            }

                            if ((int)response.StatusCode == 429)
                            {
                                throw new Exception($"Rate limit (429): {responseString}");
                            }
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in UpdateFileLocation: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "UpdateFileLocation" },
                        { "file", filePath },
                        { "directoryId", directoryId }
                    });

                    throw;
                }
            }, $"UpdateFileLocation({Path.GetFileName(filePath)})");
        }

        public async Task<bool> DeleteDirectoryAsync(string directoryId)
        {
            await RefreshTokenIfRequiredAsync();

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"Attempting to delete directory: {directoryId}");
                    var response = await _httpClient.DeleteAsync($"api/v1/synced-directories/{directoryId}");

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Info($"Successfully deleted directory: {directoryId}");
                        return true;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Logger.Error($"Failed to delete directory: {directoryId}. Status: {response.StatusCode}, Response: {errorContent}");

                        // Capture unexpected API errors to Sentry
                        if ((int)response.StatusCode != 429)
                        {
                            SentryApiErrorHandler.CaptureApiError("DeleteDirectory", (int)response.StatusCode, errorContent,
                                new Dictionary<string, string> { { "directoryId", directoryId } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {errorContent}");
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in DeleteDirectory: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Capture exception to Sentry
                    SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "DeleteDirectory" },
                        { "directoryId", directoryId }
                    });

                    throw;
                }
            }, $"DeleteDirectory({directoryId})");
        }
    }

    public class SyncMetadataResponse
    {
        [JsonProperty("directoryId")]
        public string DirectoryId { get; set; }

        [JsonProperty("files")]
        public List<SyncMetadataFile> Files { get; set; }
    }

    public class SyncMetadataFile
    {
        [JsonProperty("componentId")]
        public string ComponentId { get; set; }

        [JsonProperty("fileStored")]
        public bool FileStored { get; set; }

        [JsonProperty("parentStatus")]
        public string ParentStatus { get; set; }

        [JsonProperty("checkSum")]
        public string CheckSum { get; set; }

        [JsonProperty("filePathInDirectory")]
        public string FilePathInDirectory { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("childrenStatuses")]
        public Newtonsoft.Json.Linq.JToken ChildrenStatuses { get; set; }
    }
}