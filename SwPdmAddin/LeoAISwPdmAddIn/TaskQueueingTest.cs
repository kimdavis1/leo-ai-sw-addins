using EPDM.Interop.epdm;
using System;

namespace LeoAISwPdmAddIn
{
    /// <summary>
    /// Proof-of-concept test to verify programmatic task queueing works in PDM
    /// This MUST be tested before implementing the client-server split architecture
    /// </summary>
    public static class TaskQueueingTest
    {
        /// <summary>
        /// Tests whether we can programmatically queue tasks from a non-task add-in
        /// Call this method from EdmCmd_InstallAddIn or a menu command to run the test
        /// Check the log file for results
        /// </summary>
        /// <param name="vault">The vault to test with</param>
        public static void TestProgrammaticTaskQueue(IEdmVault5 vault)
        {
            try
            {
                LogFileWriter.LogMessage("=================================");
                LogFileWriter.LogMessage("=== TESTING PROGRAMMATIC TASK QUEUEING ===");
                LogFileWriter.LogMessage("=================================");

                // Cast to IEdmVault11 to access newer APIs
                IEdmVault11 vault11 = vault as IEdmVault11;
                if (vault11 == null)
                {
                    LogFileWriter.LogError("FAILED: Cannot cast vault to IEdmVault11");
                    return;
                }

                // Test 1: Can we get IEdmTaskMgr using CreateUtility?
                LogFileWriter.LogMessage("Step 1 - Attempting to create IEdmTaskMgr...");

                object taskMgrObj = null;
                try
                {
                    // Try using EdmUtil_TaskMgr enum value
                    // Based on other CreateUtility examples in the codebase
                    taskMgrObj = vault11.CreateUtility(EdmUtility.EdmUtil_TaskMgr);
                }
                catch (Exception ex1)
                {
                    LogFileWriter.LogError($"Failed to create TaskMgr with EdmUtil_TaskMgr: {ex1.Message}");

                    // If that fails, try casting the vault itself
                    try
                    {
                        LogFileWriter.LogMessage("Attempting alternative method...");
                        // Some utilities might be accessed differently
                        taskMgrObj = vault11;
                    }
                    catch (Exception ex2)
                    {
                        LogFileWriter.LogError($"Alternative method also failed: {ex2.Message}");
                        return;
                    }
                }

                if (taskMgrObj == null)
                {
                    LogFileWriter.LogError("FAILED: taskMgrObj is null");
                    return;
                }

                LogFileWriter.LogMessage($"Step 1 - SUCCESS: Created object of type {taskMgrObj.GetType().Name}");

                // Test 2: Can we cast to IEdmTaskMgr?
                LogFileWriter.LogMessage("Step 2 - Attempting to cast to IEdmTaskMgr...");
                IEdmTaskMgr taskMgr = taskMgrObj as IEdmTaskMgr;

                if (taskMgr == null)
                {
                    LogFileWriter.LogError($"FAILED: Cannot cast {taskMgrObj.GetType().Name} to IEdmTaskMgr");
                    LogFileWriter.LogMessage("Testing if object supports GetTask method...");

                    // Check what interfaces it implements
                    Type objType = taskMgrObj.GetType();
                    LogFileWriter.LogMessage($"Object type: {objType.FullName}");
                    LogFileWriter.LogMessage("Supported interfaces:");
                    foreach (Type iface in objType.GetInterfaces())
                    {
                        LogFileWriter.LogMessage($"  - {iface.Name}");
                    }

                    return;
                }

                LogFileWriter.LogMessage("Step 2 - SUCCESS: Cast to IEdmTaskMgr succeeded");

                // Test 3: Can we find a task by name?
                // NOTE: This requires a task to already be configured in the PDM Administration tool
                string testTaskName = "Leo AI Sync Task";  // Or use any existing task name
                LogFileWriter.LogMessage($"Step 3 - Attempting to find task '{testTaskName}'...");

                IEdmTask task = null;
                try
                {
                    task = taskMgr.GetTask(testTaskName);
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"GetTask threw exception: {ex.Message}");
                    LogFileWriter.LogMessage("NOTE: A task with this name must be configured in PDM Admin for this test to succeed");
                    return;
                }

                if (task == null)
                {
                    LogFileWriter.LogMessage($"Step 3 - Task '{testTaskName}' not found (this is expected if task hasn't been configured yet)");
                    LogFileWriter.LogMessage("To complete this test:");
                    LogFileWriter.LogMessage("  1. Configure a task add-in in PDM Administration");
                    LogFileWriter.LogMessage("  2. Name it 'Leo AI Sync Task' (or update the test code)");
                    LogFileWriter.LogMessage("  3. Run this test again");
                    LogFileWriter.LogMessage("");
                    LogFileWriter.LogMessage("PARTIAL SUCCESS: IEdmTaskMgr.GetTask() method exists and can be called");
                    return;
                }

                LogFileWriter.LogMessage($"Step 3 - SUCCESS: Found task '{testTaskName}'");

                // Test 4: Can we create a task instance?
                LogFileWriter.LogMessage("Step 4 - Attempting to create task instance...");

                IEdmTaskInstance instance = null;
                try
                {
                    instance = task.CreateInstance();
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"CreateInstance threw exception: {ex.Message}");
                    return;
                }

                if (instance == null)
                {
                    LogFileWriter.LogError("FAILED: CreateInstance returned null");
                    return;
                }

                LogFileWriter.LogMessage("Step 4 - SUCCESS: Task instance created");

                // Test 5: Can we set variables on the instance?
                LogFileWriter.LogMessage("Step 5 - Attempting to set task variables...");
                try
                {
                    instance.SetVar("Operation", "TestOperation");
                    instance.SetVar("TestData", "Hello from proof-of-concept test");
                    instance.SetVar("Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"SetVar threw exception: {ex.Message}");
                    return;
                }

                LogFileWriter.LogMessage("Step 5 - SUCCESS: Variables set successfully");

                // Test 6: Can we queue the task with a file list?
                LogFileWriter.LogMessage("Step 6 - Attempting to queue task with file list...");

                try
                {
                    // Create a minimal file list for testing
                    // Using dummy IDs - in real code, these would be actual file/folder IDs
                    EdmSelItem2[] testFiles = new EdmSelItem2[1];
                    testFiles[0].mlID = 1;  // Dummy file ID - replace with real ID in production
                    testFiles[0].mlParentID = 1;  // Dummy folder ID
                    testFiles[0].meType = EdmObjectType.EdmObject_File;

                    // Queue the task
                    instance.QueueEx(testFiles, null);
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"QueueEx threw exception: {ex.Message}");
                    LogFileWriter.LogError($"Exception type: {ex.GetType().Name}");
                    LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                    return;
                }

                LogFileWriter.LogMessage("Step 6 - SUCCESS: QueueEx() succeeded");

                // SUCCESS!
                LogFileWriter.LogMessage("");
                LogFileWriter.LogMessage("=================================");
                LogFileWriter.LogMessage("=== TEST PASSED: Programmatic task queueing WORKS! ===");
                LogFileWriter.LogMessage("=================================");
                LogFileWriter.LogMessage("");
                LogFileWriter.LogMessage("CONCLUSION: The planned client-server architecture is FEASIBLE!");
                LogFileWriter.LogMessage("You can proceed with implementation using:");
                LogFileWriter.LogMessage("  - IEdmTaskMgr taskMgr = vault.CreateUtility(EdmUtility.EdmUtil_TaskMgr)");
                LogFileWriter.LogMessage("  - IEdmTask task = taskMgr.GetTask(\"Leo AI Sync Task\")");
                LogFileWriter.LogMessage("  - IEdmTaskInstance instance = task.CreateInstance()");
                LogFileWriter.LogMessage("  - instance.SetVar(name, value) to pass data");
                LogFileWriter.LogMessage("  - instance.QueueEx(fileList, null) to queue for execution");
                LogFileWriter.LogMessage("");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage("");
                LogFileWriter.LogMessage("=================================");
                LogFileWriter.LogError($"=== TEST FAILED: {ex.Message} ===");
                LogFileWriter.LogMessage("=================================");
                LogFileWriter.LogError($"Exception type: {ex.GetType().Name}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                LogFileWriter.LogMessage("");
                LogFileWriter.LogMessage("CONCLUSION: Programmatic task queueing may NOT work as expected.");
                LogFileWriter.LogMessage("Consider using Alternative Architecture (Option B) from IMPLEMENTATION_PLAN.md");
                LogFileWriter.LogMessage("  - Scheduled tasks with file marking");
                LogFileWriter.LogMessage("  - Or direct server communication approach");
            }
        }

        /// <summary>
        /// Alternative test method that uses a real file from the vault
        /// Call this if the basic test passes and you want to test with actual file IDs
        /// </summary>
        /// <param name="vault">The vault to test with</param>
        /// <param name="testFilePath">Path to an actual file in the vault</param>
        public static void TestWithRealFile(IEdmVault5 vault, string testFilePath)
        {
            try
            {
                LogFileWriter.LogMessage($"=== TESTING WITH REAL FILE: {testFilePath} ===");

                // Get the file object
                IEdmFile5 file = vault.GetFileFromPath(testFilePath);
                if (file == null)
                {
                    LogFileWriter.LogError($"File not found: {testFilePath}");
                    return;
                }

                int fileId = file.ID;
                IEdmFolder5 folder = file.GetFirstFolderPosition();
                if (folder == null)
                {
                    LogFileWriter.LogError("Could not get folder for file");
                    return;
                }
                int folderId = folder.ID;

                LogFileWriter.LogMessage($"File ID: {fileId}, Folder ID: {folderId}");

                // Now run the queueing test with real IDs
                IEdmVault11 vault11 = vault as IEdmVault11;
                var taskMgr = vault11.CreateUtility(EdmUtility.EdmUtil_TaskMgr) as IEdmTaskMgr;
                var task = taskMgr.GetTask("Leo AI Sync Task");
                var instance = task.CreateInstance();

                instance.SetVar("Operation", "Upload");
                instance.SetVar("FilePath", testFilePath);

                EdmSelItem2[] fileList = new EdmSelItem2[1];
                fileList[0].mlID = fileId;
                fileList[0].mlParentID = folderId;
                fileList[0].meType = EdmObjectType.EdmObject_File;

                instance.QueueEx(fileList, null);

                LogFileWriter.LogMessage("=== TEST WITH REAL FILE PASSED ===");
                LogFileWriter.LogMessage("Check the Task List in PDM to see if the task was queued!");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Test with real file failed: {ex.Message}");
            }
        }
    }
}
