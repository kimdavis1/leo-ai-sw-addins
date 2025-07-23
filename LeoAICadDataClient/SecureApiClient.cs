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

        public SecureApiClient(string apiKey, string projectId)
        {
            _projectId = projectId;
            _apiKey = apiKey;
            _httpClient = new HttpClient { BaseAddress = new Uri(_baseApiUrl) };
            _httpClient.DefaultRequestHeaders.ExpectContinue = false; // Explicitly disable Expect: 100-continue
            Logger.Info("SecureApiClient initialized with standard HttpClient.");
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

        public async Task<LeoAICadDataClient.Utilities.FileInfo> CreateFileAsync(string directoryId, string vaultPath, string filePath, Dictionary<string, string> childInfos = null)
        {
            await RefreshTokenIfRequiredAsync();
            try
            {
                Logger.Info($"Attempting to create file: {filePath} in directory: {directoryId}");
                LeoFileInfo.LeoFileInformation fInfo = LeoFileInfo.GetFileInfo(filePath);
                string relativePath = GetRelativePath(vaultPath, filePath);
                string memeType = LeoAIMemeType.GetMemeType(filePath);

                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StringContent(memeType), "mimeType");
                    content.Add(new StringContent(fInfo.CheckSum), "checkSum");
                    content.Add(new StringContent(NormalizeFilePathForApi(relativePath)), "filePathInDirectory");

                    var fileBytes = Convert.FromBase64String(fInfo.Base64EncodedFile);
                    content.Add(new ByteArrayContent(fileBytes), "file", Path.GetFileName(filePath));

                    if (childInfos != null && childInfos.Count > 0)
                    {
                        var childDatas = childInfos.Select(kvp => new ChildData(kvp.Key, kvp.Value)).ToList();
                        content.Add(new StringContent(JsonConvert.SerializeObject(childDatas)), "dependencies");
                    }

                    var response = await _httpClient.PostAsync($"api/v1/synced-directories/{directoryId}/files", content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Info($"Successfully created file: {filePath}");
                        return JsonConvert.DeserializeObject<LeoAICadDataClient.Utilities.FileInfo>(responseString);
                    }
                    else
                    {
                        Logger.Error($"Failed to create file: {filePath}. Status: {response.StatusCode}, Response: {responseString}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in CreateFile: {ex.Message}");
                Logger.Error($"StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        public static string GetRelativePath(string rootPath, string targetPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ? rootPath : rootPath + Path.DirectorySeparatorChar);
            Uri targetUri = new Uri(targetPath);
            Uri relativeUri = rootUri.MakeRelativeUri(targetUri);

            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
            string finalPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            Logger.Info($"Calculated relative path: '{finalPath}' from root: '{rootPath}' and target: '{targetPath}'");
            return finalPath;
        }

        public static string NormalizeFilePathForApi(string filePath)
        {
            return filePath.Replace('\\', '/');
        }

        public async Task<string> CreateDirectoryAsync(string machineId, string uri)
        {
            await RefreshTokenIfRequiredAsync();
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
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CreateDirectory failed: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<List<LeoDirectoryInfo>> GetDirectoryInfoAsync(string machineId)
        {
            await RefreshTokenIfRequiredAsync();
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
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in GetDirectoryInfo: {ex.Message}");
                return null;
            }
        }

        public async Task<LeoAICadDataClient.Utilities.FileInfo> GetFileInfoByPathAsync(string directoryId, string relativePath, SyncMetadataResponse cachedSyncMetadata = null)
        {
            try
            {
                Logger.Info($"GetFileInfoByPath: Attempting to find file '{relativePath}' in directory '{directoryId}'");

                var syncMetadata = cachedSyncMetadata ?? await GetSyncMetadataAsync(directoryId);

                if (syncMetadata == null || syncMetadata.Files == null)
                {
                    Logger.Error($"GetFileInfoByPath: Could not retrieve sync metadata for directory '{directoryId}'.");
                    return null;
                }

                string normalizedApiRelativePath = NormalizeFilePathForApi(relativePath);
                var syncFile = syncMetadata.Files.FirstOrDefault(f => NormalizeFilePathForApi(f.FilePathInDirectory) == normalizedApiRelativePath);

                if (syncFile == null)
                {
                    Logger.Info($"GetFileInfoByPath: File '{normalizedApiRelativePath}' not found in directory '{directoryId}'.");
                    return null;
                }
                else
                {
                    Logger.Info($"GetFileInfoByPath: Successfully found file '{normalizedApiRelativePath}'.");
                    return new LeoAICadDataClient.Utilities.FileInfo
                    {
                        ComponentId = syncFile.ComponentId,
                        FilePathInDirectory = syncFile.FilePathInDirectory,
                        CheckSum = syncFile.CheckSum,
                        mimeType = syncFile.MimeType
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in GetFileInfoByPath: {ex.Message}");
                return null;
            }
        }

        public async Task<SyncMetadataResponse> GetSyncMetadataAsync(string directoryId)
        {
            await RefreshTokenIfRequiredAsync();
            try
            {
                Logger.Info($"GetSyncMetadata: Fetching sync metadata for directory {directoryId}");
                var response = await _httpClient.GetAsync($"api/v1/synced-directories/{directoryId}/files/sync-metadata");
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
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GetSyncMetadata: Exception occurred: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string directoryId, string fileId, string filePathInDirectory)
        {
            await RefreshTokenIfRequiredAsync();
            try
            {
                string normalizedPath = NormalizeFilePathForApi(filePathInDirectory);
                string encodedFilePath = Uri.EscapeDataString(normalizedPath);
                string requestUri = $"api/v1/synced-directories/{directoryId}/files/{fileId}?filePathInDirectory={encodedFilePath}";

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
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in DeleteFile: {ex.Message}");
                Logger.Error($"StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<LeoAICadDataClient.Utilities.FileInfo> UpdateFileLocationAsync(string directoryId, string vaultPath, string filePath, Dictionary<string, string> childInfos = null)
        {
            await RefreshTokenIfRequiredAsync();
            try
            {
                Logger.Info($"Attempting to update file location: {filePath} in directory: {directoryId}");
                LeoFileInfo.LeoFileInformation fInfo = LeoFileInfo.GetFileInfo(filePath);
                string relativePath = GetRelativePath(vaultPath, filePath);
                string mimeType = LeoAIMemeType.GetMemeType(filePath);

                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StringContent(mimeType), "mimeType");
                    content.Add(new StringContent(fInfo.CheckSum), "checkSum");
                    content.Add(new StringContent(NormalizeFilePathForApi(relativePath)), "filePathInDirectory");

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
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in UpdateFileLocation: {ex.Message}");
                Logger.Error($"StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        public async Task<bool> DeleteDirectoryAsync(string directoryId)
        {
            await RefreshTokenIfRequiredAsync();
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
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in DeleteDirectory: {ex.Message}");
                Logger.Error($"StackTrace: {ex.StackTrace}");
                return false;
            }
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