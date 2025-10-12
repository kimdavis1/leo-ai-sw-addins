using System;
using System.IO;
using System.Collections.Generic;
using EPDM.Interop.epdm;
using System.Linq;
using System.Runtime.InteropServices;

namespace LoadAddIn
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string user;
            string password;

            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: LoadAddIn.exe <basePath>");
                return;
            }
            string basePath = args[0].TrimEnd('\\') + "\\";
            Console.WriteLine($"Using base path: {basePath}");

            try
            {
                // Prompt for credentials
                Console.Write("Enter PDM username (press Enter for 'admin'): ");
                user = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(user)) user = "admin";

                Console.Write("Enter PDM password: ");
                password = ReadPassword();
               // if (string.IsNullOrWhiteSpace(password)) password = "prem";

                
                var vaultNames = GetRegisteredVaults();
                if (vaultNames.Count == 0)
                {
                    Console.WriteLine("No vaults registered on this machine.");
                    return;
                }

               
                Console.WriteLine("Available vaults:");
                for (int i = 0; i < vaultNames.Count; i++)
                    Console.WriteLine($"  {i + 1}. {vaultNames[i]}");
                Console.WriteLine("  0. Connect to ALL vaults");
                Console.Write("Select vault by number: ");
                if (!int.TryParse(Console.ReadLine(), out int choice)) choice = -1;

                var selectedVaults = new List<string>();
                if (choice == 0)
                    selectedVaults.AddRange(vaultNames);
                else if (choice > 0 && choice <= vaultNames.Count)
                    selectedVaults.Add(vaultNames[choice - 1]);
                else
                {
                    Console.WriteLine("Invalid selection.");
                    return;
                }

                // Task add-in files (installed first)
                // NOTE: COM DLL must be FIRST in the list!
                // PDM scans the first DLL for IEdmAddIn5 interface
                var taskAddinFiles = new List<string>
                {
                    Path.Combine(basePath, "LeoAISwPdmTaskAddIn.dll"),
                    Path.Combine(basePath, "LeoAICadDataClient.dll"),
                    Path.Combine(basePath, "EPDM.Interop.epdm.dll")
                };

                // Client add-in files (installed last)
                // NOTE: COM DLL must be FIRST in the list!
                var clientAddinFiles = new List<string>
                {
                    Path.Combine(basePath, "LeoAISwPdmAddIn.dll"),
                    Path.Combine(basePath, "LeoAICadDataClient.dll"),
                    Path.Combine(basePath, "EPDM.Interop.epdm.dll")
                };

                // Verify all required files exist
                var allFiles = taskAddinFiles.Concat(clientAddinFiles).Distinct();
                foreach (var file in allFiles)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine($"❌ Missing file: {file}");
                        return;
                    }
                }

                // Show installation information
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("IMPORTANT: Installation Process");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine("This installer will add both the client and task add-ins.");
                Console.WriteLine();
                Console.WriteLine("After installation, you MUST complete the task setup in");
                Console.WriteLine("PDM Administration before triggering the initial sync.");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine("\nPress Enter to continue with the installation...");
                Console.ReadLine();

                
                foreach (var vName in selectedVaults)
                {
                    Console.WriteLine($"\n--- Processing vault: {vName} ---");
                    var vaultObj = new EdmVault5();
                    vaultObj.Login(user, password, vName);
                    Console.WriteLine("✅ Login successful.");
                    Console.WriteLine($"Vault root path: {vaultObj.RootFolderPath}");

                    var addinMgr = (IEdmAddInMgr5)vaultObj;

                    // Step 1: Install TASK add-in FIRST
                    Console.WriteLine( $"\n[1/2] Installing task add-in... from files: " + string.Join(", ", taskAddinFiles));
                    string taskFileList = string.Join("\n", taskAddinFiles);
                    Console.WriteLine($"DEBUG: File list being sent to AddAddIns (separated by newlines):");
                    Console.WriteLine($"---START---");
                    Console.WriteLine(taskFileList);
                    Console.WriteLine($"---END---");
                    Console.WriteLine($"DEBUG: Flag value: {(int)EdmAddAddInFlags.EdmAddin_AddAllFilesToOneAddIn}");

                    // Try with empty string instead of null for third parameter
                    Console.WriteLine("DEBUG: Attempting AddAddIns call...");
                    try
                    {
                        addinMgr.AddAddIns(taskFileList,
                            (int)EdmAddAddInFlags.EdmAddin_AddAllFilesToOneAddIn,
                            "");
                        Console.WriteLine("✅ Task add-in installed: LeoAISwPdmTaskAddIn");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DEBUG: Exception details: {ex.GetType().Name}");
                        Console.WriteLine($"DEBUG: Message: {ex.Message}");
                        if (ex is COMException comEx)
                        {
                            Console.WriteLine($"DEBUG: HRESULT: 0x{comEx.ErrorCode:X}");
                        }
                        throw;
                    }

                    // Step 2: Install CLIENT add-in LAST
                    Console.WriteLine( $"\n[2/2] Installing client add-in... from files: " + string.Join(", ", clientAddinFiles));
                    string clientFileList = string.Join("\n", clientAddinFiles);
                    addinMgr.AddAddIns(clientFileList,
                        (int)EdmAddAddInFlags.EdmAddin_AddAllFilesToOneAddIn,
                        null);
                    Console.WriteLine("✅ Client add-in installed: LeoAISwPdmAddIn");

                    Console.WriteLine($"\n🎉 Add-ins installed successfully for vault: {vName}");
                    Console.WriteLine("\n" + new string('=', 70));
                    Console.WriteLine("⚠️  MANUAL STEPS REQUIRED:");
                    Console.WriteLine(new string('=', 70));
                    Console.WriteLine("STEP 1 - Configure Task in PDM Administration:");
                    Console.WriteLine("  1. Open PDM Administration and connect to the vault");
                    Console.WriteLine("  2. Navigate to: Task Host Computers");
                    Console.WriteLine($"  3. Add this computer as a task host: {Environment.MachineName}");
                    Console.WriteLine("  4. Navigate to: Tasks");
                    Console.WriteLine("  5. You should see 'Leo AI Sync Task' - open it");
                    Console.WriteLine($"  6. Assign it to host: {Environment.MachineName}");
                    Console.WriteLine();
                    Console.WriteLine("STEP 2 - Trigger Initial Sync:");
                    Console.WriteLine("  7. Open PDM Client (Windows Explorer with vault view)");
                    Console.WriteLine("  8. Right-click on any file in the vault");
                    Console.WriteLine("  9. Select 'Leo AI' → 'Complete Sync' from the context menu");
                    Console.WriteLine("  10. Wait for the initial sync to complete");
                    Console.WriteLine(new string('=', 70));
                }
            }
            catch (COMException ex)
            {
                Console.WriteLine($"❌ COM Error: 0x{ex.ErrorCode:X} - {ex.Message}");
                HandleComError((uint)ex.ErrorCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        
        static List<string> GetRegisteredVaults()
        {
            var list = new List<string>();

            try
            {
                // Instantiate as IEdmVault8
                IEdmVault5 vault5 = new EdmVault5();
                IEdmVault8 vault8 = (IEdmVault8)vault5;

              
                vault8.GetVaultViews(out EdmViewInfo[] views, false);

                // Extract names
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

        // Securely read password input
        static string ReadPassword()
        {
            var pwd = new Stack<char>();
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && pwd.Count > 0)
                {
                    Console.Write("\b \b");
                    pwd.Pop();
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    Console.Write("*");
                    pwd.Push(key.KeyChar);
                }
            }
            Console.WriteLine();
            return new string(pwd.Reverse().ToArray());
        }

        // Handle common COM errors
        static void HandleComError(uint code)
        {
            switch (code)
            {
                case 0x80040154:
                    Console.WriteLine("💡 DLL not registered or COM class missing.");
                    break;
                case 0x80070005:
                    Console.WriteLine("💡 Access denied - try running as administrator.");
                    break;
                case 0x8007007E:
                    Console.WriteLine("💡 Module not found - check dependencies.");
                    break;
                default:
                    Console.WriteLine("💡 Check PDM Administration tool for more details.");
                    break;
            }
        }
    }
}
