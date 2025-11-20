using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LeoAICadDataClient;
using Newtonsoft.Json;

namespace LeoApiVerifier
{
    /// <summary>
    /// Command-line tool to verify file status on Leo server
    /// Usage: LeoApiVerifier.exe <apiKeyJsonPath> <vaultRootPath> <relativeFilePath1[;relativeFilePath2;...]> <timeoutMinutes> <pollIntervalSeconds> [requestTimeoutSeconds]
    /// Multiple files can be separated by semicolon
    /// Returns exit code 0 if all COMPLETE, 1 if any IN_ERROR, 2 if timeout, 3 if error
    /// </summary>
    class Program
    {
        const int DEFAULT_REQUEST_TIMEOUT_SECONDS = 30;

        static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length < 5)
                {
                    Console.WriteLine("Usage: LeoApiVerifier.exe <apiKeyJsonPath> <vaultRootPath> <relativeFilePath1[;file2;...]> <timeoutMinutes> <pollIntervalSeconds> [requestTimeoutSeconds]");
                    return 3;
                }

                string apiKeyJsonPath = args[0];
                string vaultRootPath = args[1];
                string relativeFilePathsArg = args[2];

                // Debug: print all args
                Console.WriteLine($"[DEBUG] Args count: {args.Length}");
                for (int i = 0; i < args.Length; i++)
                {
                    Console.WriteLine($"[DEBUG] args[{i}] = '{args[i]}'");
                }

                if (!int.TryParse(args[3], out int timeoutMinutes))
                {
                    Console.WriteLine($"[ERROR] Invalid timeout value: '{args[3]}'. Expected integer.");
                    return 3;
                }

                if (!int.TryParse(args[4], out int pollIntervalSeconds))
                {
                    Console.WriteLine($"[ERROR] Invalid poll interval value: '{args[4]}'. Expected integer.");
                    return 3;
                }

                int requestTimeoutSeconds = DEFAULT_REQUEST_TIMEOUT_SECONDS;
                if (args.Length >= 6 && int.TryParse(args[5], out int customTimeout))
                {
                    requestTimeoutSeconds = customTimeout;
                }

                // Parse multiple file paths (semicolon-separated)
                var relativeFilePaths = relativeFilePathsArg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                if (relativeFilePaths.Length == 0)
                {
                    Console.WriteLine("[ERROR] No file paths provided");
                    return 3;
                }

                Console.WriteLine($"[INFO] Verifying {relativeFilePaths.Length} file(s)");
                foreach (var path in relativeFilePaths)
                {
                    Console.WriteLine($"  - {path}");
                }
                Console.WriteLine($"[INFO] Vault root path: {vaultRootPath}");
                Console.WriteLine($"[INFO] Total timeout: {timeoutMinutes} minutes");
                Console.WriteLine($"[INFO] Poll interval: {pollIntervalSeconds} seconds");
                Console.WriteLine($"[INFO] Request timeout: {requestTimeoutSeconds} seconds");

                // Load API key from JSON file
                if (!File.Exists(apiKeyJsonPath))
                {
                    Console.WriteLine($"[ERROR] API key file not found: {apiKeyJsonPath}");
                    return 3;
                }

                string apiKeyJson = File.ReadAllText(apiKeyJsonPath);
                var apiKeyData = JsonConvert.DeserializeObject<ApiKeyData>(apiKeyJson);

                if (string.IsNullOrEmpty(apiKeyData?.ApiKey) || string.IsNullOrEmpty(apiKeyData?.ProjectId))
                {
                    Console.WriteLine("[ERROR] Invalid API key JSON format. Expected: {\"apiKey\":\"...\", \"projectId\":\"...\"}");
                    return 3;
                }

                Console.WriteLine("[INFO] API key loaded successfully");

                // Create API client
                var leoClient = new SecureApiClient(apiKeyData.ApiKey, apiKeyData.ProjectId);

                // Get directory ID from vault path
                Console.WriteLine("[INFO] Resolving directory ID from vault path...");
                string macAddress = GetFormattedMacAddress();
                var directories = await leoClient.GetDirectoryInfoAsync(macAddress);

                if (directories == null || directories.Count == 0)
                {
                    Console.WriteLine("[ERROR] No directories found for this machine");
                    return 3;
                }

                var directory = directories.FirstOrDefault(d => d.Uri.Equals(vaultRootPath, StringComparison.OrdinalIgnoreCase));
                if (directory == null)
                {
                    Console.WriteLine($"[ERROR] Directory not found for vault path: {vaultRootPath}");
                    Console.WriteLine($"[INFO] Available directories:");
                    foreach (var dir in directories)
                    {
                        Console.WriteLine($"  - {dir.Uri} (ID: {dir.Id})");
                    }
                    return 3;
                }

                string directoryId = directory.Id;
                Console.WriteLine($"[INFO] Directory ID resolved: {directoryId}");

                // Track status for each file
                var fileStatuses = relativeFilePaths.ToDictionary(path => path, path => (string)null);
                var startTime = DateTime.Now;
                var totalTimeout = TimeSpan.FromMinutes(timeoutMinutes);

                // Poll for status
                while ((DateTime.Now - startTime) < totalTimeout)
                {
                    bool allComplete = true;
                    bool anyError = false;

                    // Check each file with individual request timeout
                    foreach (var relativeFilePath in relativeFilePaths)
                    {
                        if (fileStatuses[relativeFilePath] == "COMPLETE" || fileStatuses[relativeFilePath] == "IN_ERROR")
                        {
                            continue; // Skip files that already reached final state
                        }

                        try
                        {
                            // Check with request timeout
                            var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeoutSeconds));
                            var fileInfoTask = leoClient.GetFileInfoByPathAsync(directoryId, relativeFilePath);

                            // Wait with timeout
                            if (await Task.WhenAny(fileInfoTask, Task.Delay(requestTimeoutSeconds * 1000, requestCts.Token)) == fileInfoTask)
                            {
                                var fileInfo = await fileInfoTask;

                                if (fileInfo == null)
                                {
                                    Console.WriteLine($"[WARN] File not found on server: {relativeFilePath}");
                                    allComplete = false;
                                }
                                else
                                {
                                    string status = fileInfo.ParentStatus ?? "UNKNOWN";
                                    fileStatuses[relativeFilePath] = status;

                                    Console.WriteLine($"[INFO] {relativeFilePath}: {status}");

                                    if (status == "COMPLETE")
                                    {
                                        // File complete - continue checking others
                                    }
                                    else if (status == "IN_ERROR")
                                    {
                                        Console.WriteLine($"[ERROR] File processing failed: {relativeFilePath}");
                                        anyError = true;
                                    }
                                    else
                                    {
                                        allComplete = false;
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[WARN] Request timeout ({requestTimeoutSeconds}s) for {relativeFilePath} - will retry");
                                allComplete = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARN] API call failed for {relativeFilePath}: {ex.Message}");
                            allComplete = false;
                        }
                    }

                    // Check if we're done
                    if (anyError)
                    {
                        Console.WriteLine($"[RESULT] At least one file failed processing");
                        return 1; // Error
                    }

                    if (allComplete)
                    {
                        Console.WriteLine($"[SUCCESS] All {relativeFilePaths.Length} file(s) processing completed");
                        return 0; // Success
                    }

                    // Wait before next poll
                    if ((DateTime.Now - startTime) < totalTimeout)
                    {
                        Console.WriteLine($"[INFO] Waiting {pollIntervalSeconds} seconds before next check...");
                        Thread.Sleep(pollIntervalSeconds * 1000);
                    }
                }

                Console.WriteLine($"[TIMEOUT] Files did not reach COMPLETE status within {timeoutMinutes} minutes");
                Console.WriteLine($"[INFO] Final statuses:");
                foreach (var kvp in fileStatuses)
                {
                    Console.WriteLine($"  - {kvp.Key}: {kvp.Value ?? "NOT_FOUND"}");
                }
                return 2; // Timeout
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL] {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 3; // Error
            }
        }

        static string GetFormattedMacAddress()
        {
            try
            {
                var networkInterface = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                        && nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);

                if (networkInterface != null)
                {
                    string macAddress = networkInterface.GetPhysicalAddress().ToString();
                    // Format as XX:XX:XX:XX:XX:XX
                    return string.Join(":", Enumerable.Range(0, macAddress.Length / 2)
                        .Select(i => macAddress.Substring(i * 2, 2)));
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public class ApiKeyData
    {
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }

        [JsonProperty("projectId")]
        public string ProjectId { get; set; }
    }
}
