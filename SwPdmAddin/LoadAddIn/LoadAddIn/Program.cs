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

                // Hard-coded add-in DLL paths
                //basePath = @"C:\Program Files\LeoAISwPdmAddIn\";
                var addinFiles = new List<string>
                {
                    Path.Combine(basePath, "LeoAISwPdmAddIn.dll"),
                    Path.Combine(basePath, "LeoAICadDataClient.dll"),
                    Path.Combine(basePath, "EPDM.Interop.epdm.dll"),

                };

                // Verify all files exist
                foreach (var file in addinFiles)
                {
                    if (!File.Exists(file))
                    {
                        Console.WriteLine($"❌ Missing file: {file}");
                        return;
                    }
                }

                // Show sync process information
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("IMPORTANT: Sync Process Information");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine("After confirming to SolidWorks PDM to add the Leo AI add-in,");
                Console.WriteLine("it will immediately start a sync process for all files.");
                Console.WriteLine();
                Console.WriteLine("In large systems with many files, this could take even a few hours.");
                Console.WriteLine("Please don't close this window until it finishes.");
                Console.WriteLine();
                Console.WriteLine("You can keep track of the progress in the Leo Admin Dashboard");
                Console.WriteLine("in the Leo AI application.");
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

                    string fileList = string.Join("\n", addinFiles);
                    var addinMgr = (IEdmAddInMgr8)vaultObj;
                    addinMgr.AddAddIns(fileList,
                        (int)EdmAddAddInFlags.EdmAddin_AddAllFilesToOneAddIn,
                        null);

                    Console.WriteLine("✅ Add-in installed successfully.");
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
