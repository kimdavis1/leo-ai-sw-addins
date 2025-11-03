using System;
using System.IO;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;
using LeoAICadDataClient;

namespace LeoAISwPdmAddIn
{
    /// <summary>
    /// Leo AI Sync Task Add-in - Executes complete sync on task host (server)
    /// This add-in runs on the designated task host and has access to API keys
    /// </summary>
    [ComVisible(true)]
    [Guid("8F7A3B2C-9E4D-4A1F-B6C5-2D8E3F1A4B7C")]
    public class LeoAiSyncTask : IEdmAddIn5
    {
        private const string TASK_NAME = "Leo AI Sync Task";
        private SecureApiClient _leoClient;
        private string _directoryId;
        private int _maxRetries = 3;

        public void GetAddInInfo(ref EdmAddInInfo poInfo, IEdmVault5 poVault, IEdmCmdMgr5 poCmdMgr)
        {
            LogFileWriter.LogDebug("=== LeoAiSyncTask.GetAddInInfo called ===");

            try
            {
                poInfo.mbsAddInName = TASK_NAME;
                poInfo.mbsCompany = "Leo AI";
                poInfo.mbsDescription = "Syncs PDM files with Leo AI server (runs on task host)";
                poInfo.mlAddInVersion = 1;
                poInfo.mlRequiredVersionMajor = 17;
                poInfo.mlRequiredVersionMinor = 0;

                poCmdMgr.AddHook(EdmCmdType.EdmCmd_TaskSetup);
                poCmdMgr.AddHook(EdmCmdType.EdmCmd_TaskRun);

                LogFileWriter.LogInfo($"Task add-in registered: {TASK_NAME}");
                LogFileWriter.LogDebug("Registered for EdmCmd_TaskSetup and EdmCmd_TaskRun");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"GetAddInInfo failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        public void OnCmd(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            LogFileWriter.LogDebug($"=== LeoAiSyncTask.OnCmd called - CmdType: {poCmd.meCmdType} ===");

            try
            {
                switch (poCmd.meCmdType)
                {
                    case EdmCmdType.EdmCmd_TaskSetup:
                        LogFileWriter.LogDebug("Handling EdmCmd_TaskSetup");
                        OnTaskSetup(ref poCmd, ref ppoData);
                        break;

                    case EdmCmdType.EdmCmd_TaskRun:
                        LogFileWriter.LogInfo("Handling EdmCmd_TaskRun");
                        OnTaskRun(ref poCmd, ref ppoData);
                        break;

                    default:
                        LogFileWriter.LogWarning($"Unhandled command type: {poCmd.meCmdType}");
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
            LogFileWriter.LogDebug("=== OnTaskSetup: Configuring task properties ===");

            try
            {
                IEdmTaskProperties taskProps = (IEdmTaskProperties)poCmd.mpoExtra;
                taskProps.TaskFlags = 0;
                _maxRetries = taskProps.RetryCount;

                LogFileWriter.LogDebug($"Task ID: {taskProps.TaskID}");
                LogFileWriter.LogDebug($"Task Name: {taskProps.TaskName}");
                LogFileWriter.LogDebug($"Task GUID: {taskProps.TaskGUID}");
                LogFileWriter.LogInfo($"Task Retry Count: {_maxRetries}");
                LogFileWriter.LogDebug("Task configured: no scheduling, no manual launch (client-triggered only)");
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
            LogFileWriter.LogMessage("=== OnTaskRun: Starting complete sync operation ===");

            IEdmTaskInstance taskInstance = null;
            IEdmVault11 vault = null;

            try
            {
                taskInstance = (IEdmTaskInstance)poCmd.mpoExtra;
                taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_Running);
                taskInstance.SetProgressRange(100, 0, "Starting Leo AI complete sync...");

                LogFileWriter.LogMessage($"Task Instance ID: {taskInstance.ID}");
                LogFileWriter.LogMessage($"Task Instance GUID: {taskInstance.InstanceGUID}");
                LogFileWriter.LogMessage($"Task Name: {taskInstance.TaskName}");

                vault = (IEdmVault11)poCmd.mpoVault;
                LogFileWriter.LogMessage($"Vault: {vault.Name}");
                LogFileWriter.LogMessage($"Vault Root Path: {vault.RootFolderPath}");

                // IMPORTANT: Run in Task.Run to avoid STA thread deadlock with Descope auth
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        taskInstance.SetProgressPos(10, "Initializing Leo AI client...");

                        // SecureApiClient will auto-detect .txt (encrypted) or .json (legacy) format
                        string configPath = Path.Combine(vault.RootFolderPath, "LeoAI_TaskData", "LeoAuthKey.txt");
                        _leoClient = SecureApiClient.CreateFromStandardLocations(configPath);
                        LogFileWriter.LogMessage("SecureApiClient initialized");

                        string dirPath = $"swpdm/{vault.Name}";
                        _directoryId = await SharedSyncOperations.GetOrCreateDirectoryId(_leoClient, dirPath).ConfigureAwait(false);
                        LogFileWriter.LogMessage($"Directory ID: {_directoryId}");

                        taskInstance.SetProgressPos(20, "Running complete sync...");

                        await SharedSyncOperations.ProcessCompleteSyncOperation(vault, taskInstance, _leoClient, _directoryId).ConfigureAwait(false);

                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, "Complete sync finished successfully");
                        LogFileWriter.LogMessage("=== OnTaskRun: Complete sync finished successfully ===");
                    }
                    catch (OperationCanceledException ex)
                    {
                        LogFileWriter.LogMessage($"OnTaskRun cancelled by user: {ex.Message}");
                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneCancelled, 0, "Task cancelled by user");
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"OnTaskRun failed: {ex.Message}");
                        LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, $"Sync failed: {ex.Message}");
                    }
                }).Wait(); // Wait for completion before returning from OnTaskRun
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"OnTaskRun outer exception: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");

                if (taskInstance != null)
                {
                    taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneFailed, 0, $"Sync failed: {ex.Message}");
                }
            }
        }
    }
}
