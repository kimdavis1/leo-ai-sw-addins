using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//using static System.Net.Mime.MediaTypeNames;
using System.Windows.Forms;
using WixToolset.Dtf.WindowsInstaller;

namespace Bundle.Core.CustomAction
{
    public class CustomActions
    {
        [CustomAction]
        public static ActionResult CustomAction1(Session session)
        {
            session.Log("Begin CustomAction1");

            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult ValidateFile(Session session)
        {
            try
            {
                
                // Get the path selected in the UI
                string filePath = session["SELECTED_FILE"];
                session.Log("Validating SELECTED_FILE: " + filePath);
                filePath = CleanFilePath(filePath);
                session["SELECTED_FILE"]= filePath; // Cleaned path back to session

                // Check: Is it empty or missing?
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    MessageBox.Show("No file was selected. Please select a valid JSON file.", "File Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return ActionResult.Success;
                }

                // Check: Does the file exist?
                if (!File.Exists(filePath))
                {
                    MessageBox.Show($"The selected file does not exist:\n{filePath}", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return ActionResult.Success;
                }

                // Optional: Check file extension
                if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("The selected file must be a .json file.", "Invalid Format", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return ActionResult.Success;
                }

                // Optional: Check if it's empty
                if (new FileInfo(filePath).Length == 0)
                {
                    MessageBox.Show("The selected JSON file is empty.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return ActionResult.Success;
                }

                session["VALID_SELECTED_FILE"] = "1";
                // Success!
                session.Log("File validated successfully.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unexpected error validating file:\n" + ex.Message, "Validation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                session.Log("Exception in ValidateFile: " + ex);
                return ActionResult.Failure;
            }
        }


        private static string CleanFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return filePath;

            // Remove surrounding quotes if present
            if ((filePath.StartsWith("\"") && filePath.EndsWith("\"")) ||
                (filePath.StartsWith("'") && filePath.EndsWith("'")))
            {
                filePath = filePath.Substring(1, filePath.Length - 2);
            }

            // Remove trailing backslashes (but preserve if it's a root directory like C:\)
            while (filePath.Length > 3 && filePath.EndsWith("\\"))
            {
                filePath = filePath.TrimEnd('\\');
            }

            // Handle double backslashes in the middle of the path
            while (filePath.Contains("\\\\"))
            {
                filePath = filePath.Replace("\\\\", "\\");
            }

            // Trim any whitespace
            filePath = filePath.Trim();

            return filePath;
        }

        [CustomAction]
        public static ActionResult ShowJsonBrowse(Session session)
        {
            try
            {
                
                session.Log("Begin OpenFileChooser Custom Action");
                var task = new Thread(() => GetFile(session));
                task.SetApartmentState(ApartmentState.STA);
                task.Start();
                task.Join();
                session.Log("End OpenFileChooser Custom Action");
            }
            catch (Exception ex)
            {
                session.Log("Exception occurred as Message: {0}\r\n StackTrace: {1}", ex.Message, ex.StackTrace);
                return ActionResult.Failure;
            }
            return ActionResult.Success;
        }

        private static void GetFile(Session session)
        {
            var fileDialog = new OpenFileDialog 
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (fileDialog.ShowDialog() == DialogResult.OK) 
            {
                session["SELECTED_FILE"] = "";
                session["SELECTED_FILE"] = fileDialog.FileName;
            }
        }


        [CustomAction]
        public static ActionResult ShowCleanupPopup(Session session)
        {
            
            session.Log("Begin ShowCleanupPopup");
            string installFolder = session.CustomActionData["INSTALLFOLDER"];
            var cleanFolder = installFolder.TrimEnd('\\');
            Environment.SetEnvironmentVariable("LEO_AUTH_KEY", null, EnvironmentVariableTarget.Machine);

            try
            {
                string exePath = Path.Combine(cleanFolder, "LeoAIUnsync.exe");
                session.Log($"Looking for exe at: {exePath}");

                if (!File.Exists(exePath))
                {
                    session.Log($"LeoAIUnsync.exe not found at: {exePath}");
                    return ActionResult.Success;
                }

                session.Log("Launching LeoAIUnsync.exe directly");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = installFolder,
                    UseShellExecute = true,  // This allows console window to appear
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        session.Log("LeoAIUnsync.exe launched successfully, waiting for completion...");
                        process.WaitForExit(); // This will now wait for the actual process
                        session.Log($"LeoAIUnsync.exe completed with exit code: {process.ExitCode}");
                        
                        // Show final uninstaller message
                        MessageBox.Show(
                            "IMPORTANT: To complete the uninstall process, you need to manually remove the Leo AI PDM Add-in " +
                            "from the vault using the PDM Administrator tool.",
                            "Uninstall Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else
                    {
                        session.Log("Failed to launch LeoAIUnsync.exe");
                    }
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"ERROR in ShowCleanupPopup: {ex}");
                return ActionResult.Success;
            }
        }



        //[CustomAction]
        //public static ActionResult ShowCleanupPopup(Session session)
        //{

        //   System.Diagnostics.Debugger.Launch();
        //    session.Log("Begin ShowCleanupPopup");
        //    string installFolder = session.CustomActionData["INSTALLFOLDER"];
        //    var cleanFolder = installFolder.TrimEnd('\\');

        //    Environment.SetEnvironmentVariable("LEO_AUTH_KEY", null, EnvironmentVariableTarget.Machine);


        //    try
        //    {
        //        string exePath = Path.Combine(cleanFolder, "LeoAIUnsync.exe");
        //        session.Log($"Looking for exe at: {exePath}");

        //        if (!File.Exists(exePath))
        //        {
        //            session.Log($"LoadAddIn.exe not found at: {exePath}");
        //            return ActionResult.Success;
        //        }

        //        session.Log("Launching interactive LeoAIUnsync.exe via cmd");




        //        ProcessStartInfo startInfo = new ProcessStartInfo
        //        {
        //            FileName = "cmd.exe",
        //            Arguments = $"/c start \"\" \"{exePath}\"",
        //            WorkingDirectory = installFolder,
        //            UseShellExecute = false,        // This avoids the environment error
        //            CreateNoWindow = false,
        //            WindowStyle = ProcessWindowStyle.Normal
        //        };

        //        Process process = Process.Start(startInfo);
        //        process?.WaitForExit(); // Wait for the process to complete

        //        if (process != null)
        //        {
        //            session.Log("LeoAIUnsync.exe.exe launched via cmd successfully");
        //        }
        //        else
        //        {
        //            session.Log("Failed to launch LeoAIUnsync.exe.exe via cmd");
        //        }

        //        return ActionResult.Success;
        //    }
        //    catch (Exception ex)
        //    {
        //        session.Log($"ERROR in LaunchInteractiveApp: {ex}");
        //        return ActionResult.Success;
        //    }









        //    //            try
        //    //            {
        //    //                string message = @"PDM Add-in Cleanup Required

        //    //The LeoAI PDM Add-in has been uninstalled, but may still appear in the PDM Administration tool.

        //    //To completely remove the add-in:

        //    //1. Open SolidWorks PDM Administration
        //    //2. Navigate to Add-ins section
        //    //3. Find 'LeoAI PDM Add-in'
        //    //4. Right-click and select 'Remove' or 'Delete'
        //    //5. Restart PDM services if necessary

        //    //This is a limitation of the PDM system and requires manual intervention.

        //    //Contact support if you need assistance.";

        //    //                MessageBox.Show(message, "PDM Add-in Cleanup Instructions",
        //    //                              MessageBoxButtons.OK, MessageBoxIcon.Information);

        //    //                return ActionResult.Success;
        //    //            }
        //    //            catch (Exception ex)
        //    //            {
        //    //                session.Log("Error showing cleanup popup: " + ex.Message);
        //    //                return ActionResult.Success; // Don't fail uninstall
        //    //            }
        //}

        [CustomAction]
        public static ActionResult LaunchInteractiveApp(Session session)
        {
            session.Log("Begin LaunchInteractiveApp");
            string installFolder = session["INSTALLFOLDER"];
            var cleanFolder = installFolder.TrimEnd('\\');

            try
            {
                string exePath = Path.Combine(installFolder, "LoadAddIn.exe");
                session.Log($"Looking for exe at: {exePath}");

                if (!File.Exists(exePath))
                {
                    session.Log($"LoadAddIn.exe not found at: {exePath}");
                    return ActionResult.Success;
                }

                session.Log("Launching interactive LoadAddIn.exe via cmd");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"LoadAddIn\" \"{exePath}\" \"{cleanFolder}\"",
                    WorkingDirectory = installFolder,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process process = Process.Start(startInfo);

                if (process != null)
                {
                    session.Log("LoadAddIn.exe launched via cmd successfully");
                }
                else
                {
                    session.Log("Failed to launch LoadAddIn.exe via cmd");
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"ERROR in LaunchInteractiveApp: {ex}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult CopySelectedJson(Session session)
        {
            
            session.Log("Begin CopySelectedJson");

            string sourcePath = session.CustomActionData["SELECTED_FILE"];
            string installFolder = session.CustomActionData["INSTALLFOLDER"];
            string fullPath = Path.Combine(installFolder, sourcePath);


            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                session.Log("No valid JSONFILE to copy.");
                return ActionResult.Failure;
            }

            Environment.SetEnvironmentVariable("LEO_AUTH_KEY", fullPath, EnvironmentVariableTarget.Machine);


            try
            {
                string destPath = Path.Combine(installFolder, Path.GetFileName(sourcePath));
                session.Log($"Copying from '{sourcePath}' to '{destPath}'");
                File.Copy(sourcePath, destPath, overwrite: true);
                session.Log("JSON copy succeeded");

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"ERROR copying JSON: {ex}");
                return ActionResult.Failure;
            }
        }

    }
}
