using System;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using LeoAICadDataClient;
using LeoAICadDataClient.Utilities;
using EPDM.Interop.epdm;
using System.Threading.Tasks;

namespace LeoAISwPdmAddIn
{
    /// <summary>
    /// Standalone uninstaller for Leo AI PDM Add-in
    /// This tool allows users to unsync vaults from the Leo AI server
    /// </summary>
    public class LeoAIUnsync
    {
        // Add LeoAuthConfig definition for use in this file
        public class LeoAuthConfig {
            public string ApiKey { get; set; }
            public string ProjectId { get; set; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            try
            {
                PrintOpeningMessage();
                
                // Clean up local files first
                CleanupLocalFiles();
                
                var vaultNames = GetRegisteredVaults();
                if (vaultNames.Count == 0)
                {
                    Console.WriteLine("No vaults registered on this machine.");
                    PrintCompletionMessage();
                    return;
                }
                var selectedVaults = GetUserVaultSelection(vaultNames);
                if (selectedVaults.Count == 0)
                {
                    Console.WriteLine("No valid vaults selected. Exiting.");
                    PrintCompletionMessage();
                    return;
                }
                var selectedVaultPaths = GetVaultRootPathsFromRegistry(selectedVaults);
                if (selectedVaultPaths.Count == 0)
                {
                    Console.WriteLine("No valid vault root paths found. Exiting.");
                    PrintCompletionMessage();
                    return;
                }
                if (!ConfirmOperation(selectedVaults))
                {
                    Console.WriteLine("Operation cancelled.");
                    PrintCompletionMessage();
                    return;
                }
                var authConfig = ReadAuthConfig();
                if (authConfig == null)
                {
                    Console.WriteLine("No valid authentication configuration found. Cannot proceed.");
                    PrintCompletionMessage();
                    return;
                }

                var directories = GetLeoAIDirectories(authConfig);
                if (directories == null)
                {
                    PrintCompletionMessage();
                    return;
                }
                
                if (directories.Count == 0)
                {
                    Console.WriteLine("No synced directories found on Leo AI server for this machine.");
                    Console.WriteLine("This means no vaults have been synced to Leo AI yet, or they were already unsynced.");
                    PrintCompletionMessage();
                    return;
                }
                await UnsyncVaults(selectedVaultPaths, directories, authConfig);
                PrintCompletionMessage();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in uninstaller: {ex.Message}", "Leo AI Uninstaller Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                PrintCompletionMessage();
            }
        }

        // Prints the opening message and warnings
        static void PrintOpeningMessage()
        {
            Console.WriteLine("Leo AI PDM Add-in Vault Unsync Tool");
            Console.WriteLine("====================================");
            Console.WriteLine();
            Console.WriteLine("This tool will UNSYNC a selected PDM vault from the Leo AI server.");
            Console.WriteLine();
            Console.WriteLine("WARNING: This action is IRREVERSIBLE and will DELETE the entire index Leo AI has created for the synced data.");
            Console.WriteLine("All users in your organization will lose access to these files through the Leo AI application.");
            Console.WriteLine("Leo AI will no longer know about these files or their contents, so they will not appear in search results or answers.");
            Console.WriteLine();
            Console.WriteLine("This action DOES NOT affect the regular operation of your PDM vault. You and your users will retain normal access to files using PDM.");
            Console.WriteLine();
            Console.WriteLine("You can select one or more vaults to unsync (or all). To unsync others later, rerun this tool.");
            Console.WriteLine();
            Console.WriteLine("Note: You can also unsync files using the Leo AI Admin Dashboard in the Leo AI application.");
            Console.WriteLine();
        }

        // Gets the user's vault selection
        static List<string> GetUserVaultSelection(List<string> vaultNames)
        {
            Console.WriteLine("Available vaults:");
            for (int i = 0; i < vaultNames.Count; i++)
                Console.WriteLine($"  {i + 1}. {vaultNames[i]}");
            Console.WriteLine("  0. UNSYNC ALL vaults");
            Console.WriteLine();
            Console.Write("Enter vault numbers to unsync (comma-separated, or 0 for all): ");
            var input = Console.ReadLine();
            var selectedVaults = new List<string>();
            if (input.Trim() == "0")
            {
                selectedVaults.AddRange(vaultNames);
            }
            else
            {
                var indices = input.Split(',').Select(s => s.Trim()).Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
                foreach (var idx in indices)
                {
                    if (idx > 0 && idx <= vaultNames.Count)
                        selectedVaults.Add(vaultNames[idx - 1]);
                }
            }
            return selectedVaults;
        }

        // Loads the root paths for the selected vaults from the registry
        static List<string> GetVaultRootPathsFromRegistry(List<string> selectedVaults)
        {
            var selectedVaultPaths = new List<string>();
            foreach (var vault in selectedVaults)
            {
                var path = GetVaultRootPathFromRegistry(vault);
                if (!string.IsNullOrEmpty(path))
                {
                    selectedVaultPaths.Add(path);
                }
                else
                {
                    Console.WriteLine($"[!] Could not find root path for vault '{vault}' in registry. Attempting to get it from vault...");
                    var vaultPath = GetVaultRootPathFromVault(vault);
                    if (!string.IsNullOrEmpty(vaultPath))
                    {
                        selectedVaultPaths.Add(vaultPath);
                        Console.WriteLine($"[OK] Retrieved vault root path from vault: {vaultPath}");
                    }
                    else
                    {
                        Console.WriteLine($"[FAIL] Could not get root path for vault '{vault}' from registry or vault. Skipping.");
                    }
                }
            }
            return selectedVaultPaths;
        }

        // Confirms the operation with the user
        static bool ConfirmOperation(List<string> selectedVaults)
        {
            Console.WriteLine();
            Console.WriteLine("You are about to UNSYNC the following vault(s) from Leo AI:");
            foreach (var v in selectedVaults) Console.WriteLine($"- {v}");
            Console.WriteLine();
            Console.Write("Are you sure? This cannot be undone! (y/N): ");
            var confirm = Console.ReadLine();
            return (confirm?.ToLower() == "y" || confirm?.ToLower() == "yes");
        }

        // Performs the unsync operation for each selected vault path
        static async Task UnsyncVaults(List<string> selectedVaultPaths, List<LeoDirectoryInfo> directories, LeoAuthConfig authConfig)
        {
            foreach (var vaultPath in selectedVaultPaths)
            {
                var dir = directories.FirstOrDefault(d => d.Uri.Equals(vaultPath, StringComparison.OrdinalIgnoreCase));
                if (dir == null)
                {
                    Console.WriteLine($"[!] No synced directory found for vault path '{vaultPath}'. Skipping.");
                    continue;
                }
                Console.WriteLine($"Deleting Leo AI index for vault path '{vaultPath}' (directory: {dir.Uri})...");
                var leoClient = new SecureApiClient(authConfig.ApiKey, authConfig.ProjectId);
                bool success = await leoClient.DeleteDirectoryAsync(dir.Id);
                if (success)
                {
                    Console.WriteLine($"[OK] Successfully unsynced '{vaultPath}' from Leo AI.");
                    // Remove from registry after successful server deletion
                    RemoveVaultFromRegistry(vaultPath);
                }
                else
                {
                    Console.WriteLine($"[FAIL] Failed to unsync '{vaultPath}'.");
                }
            }
        }

        // Prints the completion message
        static void PrintCompletionMessage()
        {
            Console.WriteLine();
            Console.WriteLine("Operation complete.");
            Console.WriteLine();
            Console.WriteLine("IMPORTANT: To complete the uninstall process, you need to manually remove the Leo AI PDM Add-in");
            Console.WriteLine("from the vault using the PDM Administrator tool.");
            Console.WriteLine();
            Console.WriteLine("Press Enter to close the program...");
            Console.ReadKey();
        }

        // Reuse from LoadAddIn
        static List<string> GetRegisteredVaults()
        {
            var list = new List<string>();
            try
            {
                IEdmVault5 vault5 = new EPDM.Interop.epdm.EdmVault5();
                IEdmVault8 vault8 = (IEdmVault8)vault5;
                vault8.GetVaultViews(out EPDM.Interop.epdm.EdmViewInfo[] views, false);
                foreach (var view in views)
                {
                    if (!string.IsNullOrEmpty(view.mbsVaultName))
                        list.Add(view.mbsVaultName);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error enumerating vaults: {e.Message}");
            }
            return list;
        }

        // Helper: Get vault root path from registry (InstalledVaults)
        static string GetVaultRootPathFromRegistry(string vaultName)
        {
            try
            {
                string registryPath = @"SOFTWARE\\LeoAI\\PDM-AddIn\\InstalledVaults";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, false))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(vaultName) as string;
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading registry for vault '{vaultName}': {ex.Message}");
            }
            return null;
        }

        // Helper: Get vault root path from vault object (fallback when registry is missing)
        static string GetVaultRootPathFromVault(string vaultName)
        {
            try
            {
                IEdmVault5 vault5 = new EPDM.Interop.epdm.EdmVault5();
                
                // First try with default admin credentials
                try
                {
                    vault5.Login("admin", "", vaultName);
                    string rootPath = vault5.RootFolderPath;
                    return rootPath;
                }
                catch (Exception)
                {
                    // If default login fails, prompt for credentials
                    Console.WriteLine($"\nCould not access vault '{vaultName}' with default credentials.");
                    Console.WriteLine("Please provide PDM credentials to access the vault:");
                    
                    Console.Write("Enter PDM username (press Enter for 'admin'): ");
                    string user = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(user)) user = "admin";

                    Console.Write("Enter PDM password: ");
                    string password = ReadPassword();
                    
                    if (string.IsNullOrWhiteSpace(password))
                    {
                        Console.WriteLine("Password cannot be empty. Skipping vault.");
                        return null;
                    }

                    // Try login with provided credentials
                    vault5.Login(user, password, vaultName);
                    string rootPath = vault5.RootFolderPath;
                    Console.WriteLine($"✅ Successfully accessed vault '{vaultName}'");
                    return rootPath;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting vault root path for '{vaultName}': {ex.Message}");
                return null;
            }
        }

        // Exact auth config loading logic from SwPdmAddinMain
        private static LeoAuthConfig ReadAuthConfig()
        {
            try
            {
                string configFilePath = null;
                // First, try to read the path from environment variable
                string envPath = LeoAIDataUtilities.ReadEnvVariableByName("LEO_AUTH_KEY", false);
                if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                {
                    configFilePath = envPath;
                }
                else
                {
                    // Fallback to default location
                    string defaultPath = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.json");
                    if (File.Exists(defaultPath))
                    {
                        configFilePath = defaultPath;
                    }
                }
                if (string.IsNullOrEmpty(configFilePath))
                {
                    Console.WriteLine("Leo AI authentication configuration not found!\n\n" +
                        "Please place the auth.json file in one of the following locations:\n" +
                        "1. Default location: C:\\Program Files\\LeoAISwPdmAddIn\\auth.json\n" +
                        "2. Custom location specified in LEO_AUTH_KEY environment variable\n\n" +
                        "The auth.json file should contain:\n" +
                        "{\n  \"ApiKey\": \"your-api-key\",\n  \"ProjectId\": \"your-project-id\"\n}\n\n" +
                        "You can get the authentication keys from the Leo AI Admin Dashboard\n(available in Leo Business/Enterprise accounts).");
                    return null;
                }
                string jsonContent = File.ReadAllText(configFilePath);
                LeoAuthConfig config = ParseAuthConfig(jsonContent);
                if (config == null || string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ProjectId))
                {
                    Console.WriteLine($"Invalid Leo AI authentication configuration in file: {configFilePath}\n\n" +
                        "The auth.json file should contain:\n" +
                        "{\n  \"ApiKey\": \"your-api-key\",\n  \"ProjectId\": \"your-project-id\"\n}\n\n" +
                        "Both ApiKey and ProjectId are required and cannot be empty.\n" +
                        "You can get the authentication keys from the Leo AI Admin Dashboard\n(available in Leo Business/Enterprise accounts).");
                    return null;
                }
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading Leo AI authentication configuration: {ex.Message}\n\n" +
                    "Please ensure the auth.json file is properly formatted:\n" +
                    "{\n  \"ApiKey\": \"your-api-key\",\n  \"ProjectId\": \"your-project-id\"\n}\n\n" +
                    "You can get the authentication keys from the Leo AI Admin Dashboard\n(available in Leo Business/Enterprise accounts).");
                return null;
            }
        }
        private static LeoAuthConfig ParseAuthConfig(string jsonContent)
        {
            try
            {
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
                return new LeoAuthConfig { ApiKey = dict["ApiKey"], ProjectId = dict["ProjectId"] };
            }
            catch { return null; }
        }

        // Handles all Leo AI API interactions and error handling
        static List<LeoDirectoryInfo> GetLeoAIDirectories(LeoAuthConfig authConfig)
        {
            var leoClient = new SecureApiClient(authConfig.ApiKey, authConfig.ProjectId);
            string macAddress = LeoAIDataUtilities.GetFormattedMacAddress();
            
            Console.WriteLine("Connecting to Leo AI server...");
            List<LeoDirectoryInfo> directories = null;
            try
            {
                directories = leoClient.GetDirectoryInfoAsync(macAddress).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to Leo AI server: {ex.Message}");
                if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
                {
                    Console.WriteLine("This appears to be an authentication error. Please check:");
                    Console.WriteLine("1. Your API key is valid and not expired");
                    Console.WriteLine("2. Your Project ID is correct");
                    Console.WriteLine("3. You have the necessary permissions for this project");
                }
                else if (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
                {
                    Console.WriteLine("The API endpoint was not found. Please check your Project ID.");
                }
                else if (ex.Message.Contains("timeout") || ex.Message.Contains("connection"))
                {
                    Console.WriteLine("Network connection error. Please check your internet connection.");
                }
                return null;
            }

            if (directories == null)
            {
                Console.WriteLine("Failed to retrieve directory information from Leo AI server.");
                Console.WriteLine("This could be due to:");
                Console.WriteLine("- Invalid or expired API key - please get a new one from the Leo AI Admin Dashboard");
                Console.WriteLine("- Network connectivity issues - please check your internet connection");
                Console.WriteLine("- Server-side problems");
                return null;
            }
            
            return directories;
        }

        // Clean up local files (copied from LeoAICleanupUtility)
        static void CleanupLocalFiles()
        {
            try
            {
                Console.WriteLine("Cleaning up local files...");
                
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string addInDataDir = Path.Combine(appDataPath, "LeoAI-PDM-AddIn");
                
                if (Directory.Exists(addInDataDir))
                {
                    Directory.Delete(addInDataDir, true);
                    Console.WriteLine($"Deleted local data directory: {addInDataDir}");
                }
                else
                {
                    Console.WriteLine("No local data directory found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up local files: {ex.Message}");
            }
        }

        // Helper method to read password without displaying characters
        static string ReadPassword()
        {
            string password = "";
            ConsoleKeyInfo key;
            
            do
            {
                key = Console.ReadKey(true);
                
                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);
            
            Console.WriteLine();
            return password;
        }

        // Remove specific vault from registry after successful unsync
        static void RemoveVaultFromRegistry(string vaultPath)
        {
            try
            {
                string registryPath = @"SOFTWARE\LeoAI\PDM-AddIn\InstalledVaults";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, true))
                {
                    if (key != null)
                    {
                        // Find the vault name by matching the path
                        string vaultNameToRemove = null;
                        foreach (string vaultName in key.GetValueNames())
                        {
                            var storedPath = key.GetValue(vaultName) as string;
                            if (storedPath != null && storedPath.Equals(vaultPath, StringComparison.OrdinalIgnoreCase))
                            {
                                vaultNameToRemove = vaultName;
                                break;
                            }
                        }
                        
                        if (vaultNameToRemove != null)
                        {
                            key.DeleteValue(vaultNameToRemove, false);
                            Console.WriteLine($"Removed vault '{vaultNameToRemove}' from registry.");
                        }
                        else
                        {
                            Console.WriteLine($"Could not find registry entry for vault path '{vaultPath}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing vault from registry: {ex.Message}");
            }
        }
    }
} 