using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EPDM.Interop.epdm;
using LeoAICadDataClient;
using LeoAICadDataClient.Utilities;
using LeoAISwPdmAddIn.ErrorTracking;

namespace LeoAISwPdmAddIn
{
    /// <summary>
    /// Metadata for a single file/folder operation
    /// </summary>
    public class OperationMetadata
    {
        public string Id { get; set; }
        public string Operation { get; set; }      // "Rename", "Move", "Upload", "Delete"
        public string OldPath { get; set; }
        public string NewPath { get; set; }
        public int FileID { get; set; }
        public int FolderID { get; set; }
        public string Status { get; set; }         // "in-work" or "ready"
        public long Timestamp { get; set; }
        public Dictionary<string, string> AdditionalData { get; set; }
    }

    /// <summary>
    /// User session metadata containing all operations for a user session
    /// </summary>
    public class UserSessionMetadata
    {
        public string SessionId { get; set; }
        public List<OperationMetadata> Operations { get; set; }
        public long LastModified { get; set; }
    }

    /// <summary>
    /// Shared sync operations that can be called from both client add-in and task add-in
    /// </summary>
    public static class SharedSyncOperations
    {
        public static async Task OnOperationRunAsync(IEdmVault11 vault, string operationJson, SecureApiClient leoClient, string directoryId)
        {
            LogFileWriter.LogMessage("=== OnOperationRun: Starting sync operation (EVENT MODE - local-first) ===");

            OperationMetadata operation = null;
            try
            {
                LogFileWriter.LogMessage($"Vault: {vault.Name}");
                LogFileWriter.LogMessage($"Vault Root Path: {vault.RootFolderPath}");

                SentryErrorHandler.AddBreadcrumb("Starting sync operation", "sync", new Dictionary<string, string>
                {
                    { "vault", vault.Name },
                    { "mode", "event" }
                });

                operation = Newtonsoft.Json.JsonConvert.DeserializeObject<OperationMetadata>(operationJson);

                if (operation == null)
                {
                    LogFileWriter.LogError("Failed to deserialize operation JSON");
                    return;
                }

                LogFileWriter.LogMessage($"=== Processing operation ===");
                LogFileWriter.LogMessage($"  Type: {operation.Operation}");
                LogFileWriter.LogMessage($"  ID: {operation.Id}");
                LogFileWriter.LogMessage($"  OldPath: {operation.OldPath ?? "N/A"}");
                LogFileWriter.LogMessage($"  NewPath: {operation.NewPath ?? "N/A"}");
                LogFileWriter.LogMessage($"  Timestamp: {operation.Timestamp}");

                // Add breadcrumb with operation details
                SentryErrorHandler.AddOperationBreadcrumb(
                    operation.Operation,
                    $"Processing {operation.Operation} operation",
                    new Dictionary<string, string>
                    {
                        { "operation_type", operation.Operation },
                        { "file", operation.NewPath ?? operation.OldPath ?? "unknown" }
                    }
                );

                // Event operations use local-first mode (tryArchiveFirst = false)
                bool tryArchiveFirst = false;

                switch (operation.Operation)
                {
                    case "Add":
                    case "Upload":
                        SentryErrorHandler.AddOperationBreadcrumb("Upload", "Starting upload operation");
                        await ProcessUploadOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "Delete":
                        await ProcessDeleteOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "Move":
                        await ProcessMoveOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "Rename":
                        await ProcessRenameOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "Copy":
                        await ProcessCopyOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "AddFolder":
                        await ProcessAddFolderOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "DeleteFolder":
                        await ProcessDeleteFolderOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "MoveFolder":
                        await ProcessMoveFolderOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    case "RenameFolder":
                        await ProcessRenameFolderOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
                        break;

                    default:
                        LogFileWriter.LogWarning($"Unknown operation type: {operation.Operation} - skipping");
                        break;
                }

                LogFileWriter.LogMessage($"Operation completed successfully");
            }
            catch (Exception ex)
            {
                // Unwrap AggregateException to get the real error
                Exception realException = ex;
                if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
                {
                    realException = aggEx.InnerException ?? aggEx.InnerExceptions[0];
                    LogFileWriter.LogError($"OnOperationRun failed (AggregateException unwrapped): {realException.Message}");
                }
                else
                {
                    LogFileWriter.LogError($"OnOperationRun failed: {ex.Message}");
                }

                LogFileWriter.LogError($"Stack trace: {realException.StackTrace}");

                // Log inner exceptions if present
                if (realException.InnerException != null)
                {
                    LogFileWriter.LogError($"Inner exception: {realException.InnerException.Message}");
                    LogFileWriter.LogError($"Inner exception stack trace: {realException.InnerException.StackTrace}");
                }

                SentryErrorHandler.CaptureException(realException, new Dictionary<string, string>
                {
                    { "operation", "OnOperationRun" },
                    { "operation_type", operation?.Operation ?? "unknown" },
                    { "vault", vault?.Name ?? "unknown" }
                });
                throw;
            }
        }

        #region Sync Operations (New Format - OperationMetadata)

        /// <summary>
        /// Process an Upload operation from metadata
        /// Checks if file exists on server, compares checksums, and handles accordingly
        /// Always checks before upload regardless of operation type (upload, rename, move, replace, etc)
        /// </summary>
        private static async Task ProcessUploadOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessUploadOperation: {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("Upload operation missing NewPath");
            }

            // Check if file is processable
            if (!IsProcessableFile(operation.NewPath))
            {
                LogFileWriter.LogMessage($"Skipping upload - file not processable: {operation.NewPath}");
                return;
            }

            // Check if file exists in vault using PDM API (not local view)
            if (!FileExistsInVault(vault, operation.NewPath))
            {
                throw new FileNotFoundException($"File not found in vault: {operation.NewPath}");
            }

            string relativePath = GetRelativePath(vault.RootFolderPath, operation.NewPath);
            LogFileWriter.LogMessage($"Processing upload for: {relativePath}");

            // Optimistic upload: Try to upload directly, handle conflicts if they occur
            // UpdateFilesToLeoAI will handle the 409 conflict check internally
            LogFileWriter.LogMessage($"Uploading: {relativePath}");
            await UpdateFilesToLeoAI(vault, new[] { operation.NewPath }, vault.RootFolderPath, leoClient, directoryId, tryArchiveFirst);

            LogFileWriter.LogMessage($"Upload completed: {relativePath}");
        }

        /// <summary>
        /// Process a Delete operation from metadata
        /// </summary>
        private static async Task ProcessDeleteOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessDeleteOperation: {operation.OldPath}");

            if (string.IsNullOrEmpty(operation.OldPath))
            {
                throw new Exception("Delete operation missing OldPath");
            }

            // Check if file is processable
            if (!IsProcessableFile(operation.OldPath))
            {
                LogFileWriter.LogMessage($"Skipping delete - file not processable: {operation.OldPath}");
                return;
            }

            string relativePath = GetRelativePath(vault.RootFolderPath, operation.OldPath);
            LogFileWriter.LogMessage($"Deleting from server: {relativePath}");

            bool deleted = await leoClient.DeleteFileAsync(directoryId, relativePath);
            if (deleted)
            {
                LogFileWriter.LogMessage($"Delete completed: {relativePath}");
            }
            else
            {
                LogFileWriter.LogMessage($"Delete returned false: {relativePath}");
            }
        }

        /// <summary>
        /// Process a Rename operation from metadata
        /// Upload new path first, then delete old path
        /// </summary>
        private static async Task ProcessRenameOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessRenameOperation: {operation.OldPath} → {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.OldPath) || string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("Rename operation missing OldPath or NewPath");
            }

            // Check if file is processable
            if (!IsProcessableFile(operation.NewPath))
            {
                LogFileWriter.LogMessage($"Skipping rename - file not processable: {operation.NewPath}");
                return;
            }

            // Get full paths using FileID and FolderID from operation metadata
            // For RENAME, the paths stored are just filenames, so we need to reconstruct full paths
            // Both old and new files are in the same folder (rename doesn't change folder)
            string newFullPath = null;
            string oldFullPath = null;

            if (operation.FileID > 0 && operation.FolderID > 0)
            {
                try
                {
                    IEdmFile5 file = (IEdmFile5)vault.GetObject(EdmObjectType.EdmObject_File, operation.FileID);
                    IEdmFolder5 folder = (IEdmFolder5)vault.GetObject(EdmObjectType.EdmObject_Folder, operation.FolderID);

                    if (file != null && folder != null)
                    {
                        // Construct full paths using folder.LocalPath + filename
                        // Don't use GetLocalPath() - it returns null for files not in local view
                        newFullPath = System.IO.Path.Combine(folder.LocalPath, file.Name);
                        oldFullPath = System.IO.Path.Combine(folder.LocalPath, operation.OldPath);
                        LogFileWriter.LogMessage($"Constructed new path from FileID/FolderID: {newFullPath}");
                        LogFileWriter.LogMessage($"Constructed old path from FileID/FolderID: {oldFullPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogWarning($"Failed to get file/folder from PDM API: {ex.Message}");
                }
            }

            // Fallback: If we couldn't get paths from FileID/FolderID, use operation paths
            if (string.IsNullOrEmpty(newFullPath))
            {
                newFullPath = EnsureFullPath(vault, operation.NewPath);
                LogFileWriter.LogMessage($"Using EnsureFullPath fallback for new path: {newFullPath}");
            }

            if (string.IsNullOrEmpty(oldFullPath))
            {
                oldFullPath = EnsureFullPath(vault, operation.OldPath);
                LogFileWriter.LogMessage($"Using EnsureFullPath fallback for old path: {oldFullPath}");
            }

            // Convert both paths to relative paths
            string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldFullPath);
            string newRelativePath = GetRelativePath(vault.RootFolderPath, newFullPath);

            // Execute the common rename/move logic
            await ExecuteRenameOrMoveOperation(vault, oldRelativePath, newRelativePath, newFullPath, leoClient, directoryId, tryArchiveFirst, "Rename");
        }

        /// <summary>
        /// Process a Move operation from metadata
        /// Upload new path first, then delete old path
        /// For MOVE operations, the paths in metadata are already full vault-relative paths from client
        /// </summary>
        private static async Task ProcessMoveOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessMoveOperation: {operation.OldPath} → {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.OldPath) || string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("Move operation missing OldPath or NewPath");
            }

            // Check if file is processable
            if (!IsProcessableFile(operation.NewPath))
            {
                LogFileWriter.LogMessage($"Skipping move - file not processable: {operation.NewPath}");
                return;
            }

            // For MOVE operations, the paths stored in metadata are full absolute paths
            // (client add-in gets full paths from PDM event data for moves)
            // We need to convert them to relative paths and ensure they're proper full paths

            // Convert paths - they might be absolute or relative
            string oldFullPath = EnsureFullPath(vault, operation.OldPath);
            string newFullPath = EnsureFullPath(vault, operation.NewPath);

            // Convert to relative paths for server API
            string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldFullPath);
            string newRelativePath = GetRelativePath(vault.RootFolderPath, newFullPath);

            // Execute the common rename/move logic
            await ExecuteRenameOrMoveOperation(vault, oldRelativePath, newRelativePath, newFullPath, leoClient, directoryId, tryArchiveFirst, "Move");
        }

        /// <summary>
        /// Common implementation for executing rename/move operations
        /// Upload new path first, then delete old path
        /// </summary>
        private static async Task ExecuteRenameOrMoveOperation(
            IEdmVault11 vault,
            string oldRelativePath,
            string newRelativePath,
            string newFullPath,
            SecureApiClient leoClient,
            string directoryId,
            bool tryArchiveFirst,
            string operationType)
        {
            LogFileWriter.LogMessage($"{operationType} file: {oldRelativePath} -> {newRelativePath}");

            // Build mapping for this single file operation (new relative path -> old relative path)
            Dictionary<string, string> moveMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            moveMap[newRelativePath] = oldRelativePath;

            // Create processedFiles set for this operation (thread-safe)
            ConcurrentDictionary<string, byte> processedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            // Create processing set for circular dependency detection (thread-safe)
            ConcurrentDictionary<string, byte> processingFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            // Create dependency cache to avoid redundant PDM API calls (thread-safe)
            ConcurrentDictionary<string, Dictionary<int, List<string>>> dependencyCache = new ConcurrentDictionary<string, Dictionary<int, List<string>>>(StringComparer.OrdinalIgnoreCase);

            // Step 1: Upload file with new path
            if (FileExistsInVault(vault, newFullPath))
            {
                LogFileWriter.LogMessage($"Uploading file with new path using move context: {newRelativePath}");

                // Use UploadFileWithDependencies with moveMap to handle dependencies
                await UploadFileWithDependencies(vault, newFullPath, vault.RootFolderPath, processedFiles, processingFiles, dependencyCache, moveMap, leoClient, directoryId, tryArchiveFirst);

                LogFileWriter.LogMessage($"Upload completed: {newRelativePath}");
            }
            else
            {
                LogFileWriter.LogMessage($"Warning: File not found in vault: {newFullPath}");
            }

            // Step 2: Delete old path from server
            LogFileWriter.LogMessage($"Deleting old path from server: {oldRelativePath}");
            bool deleted = await leoClient.DeleteFileAsync(directoryId, oldRelativePath);
            if (deleted)
            {
                LogFileWriter.LogMessage($"Deleted old path: {oldRelativePath}");
            }
            else
            {
                LogFileWriter.LogMessage($"Delete returned false for old path: {oldRelativePath}");
            }

            LogFileWriter.LogMessage($"{operationType} completed: {oldRelativePath} → {newRelativePath}");
        }


        /// <summary>
        /// Process a Copy operation from metadata
        /// The copied file is a new independent file - just check if destination exists and upload
        /// </summary>
        private static async Task ProcessCopyOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessCopyOperation: {operation.OldPath} → {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("Copy operation missing NewPath");
            }

            string newRelativePath = GetRelativePath(vault.RootFolderPath, operation.NewPath);

            // Check if destination file exists in vault
            if (!FileExistsInVault(vault, operation.NewPath))
            {
                LogFileWriter.LogMessage($"Warning: Destination file not found in vault: {operation.NewPath}");
                return;
            }

            // Optimistic upload: Try to upload the copied file directly
            // If it conflicts (409), UpdateFilesToLeoAI will handle it
            await UpdateFilesToLeoAI(vault, new[] { operation.NewPath }, vault.RootFolderPath, leoClient, directoryId, tryArchiveFirst);
            LogFileWriter.LogMessage($"Copy completed: {newRelativePath}");
        }

        /// <summary>
        /// Process an AddFolder operation - uploads all files in the folder
        /// </summary>
        private static async Task ProcessAddFolderOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessAddFolderOperation: {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("AddFolder operation missing NewPath");
            }

            // Get folder from vault
            string folderFullPath = EnsureFullPath(vault, operation.NewPath);
            IEdmFolder5 folder = vault.GetFolderFromPath(folderFullPath);

            if (folder == null)
            {
                LogFileWriter.LogMessage($"Warning: Folder not found in vault: {operation.NewPath}");
                return;
            }

            // Get all files in folder (recursively)
            List<string> filesToUpload = new List<string>();
            EnumerateFolderFilesRecursive(folder, folder.LocalPath, folder.LocalPath, new List<string>(), filesToUpload);

            LogFileWriter.LogMessage($"Found {filesToUpload.Count} files in folder to upload");

            // Upload each file
            foreach (string filePath in filesToUpload)
            {
                try
                {
                    await UpdateFilesToLeoAI(vault, new[] { filePath }, vault.RootFolderPath, leoClient, directoryId, tryArchiveFirst);
                    string relativePath = GetRelativePath(vault.RootFolderPath, filePath);
                    LogFileWriter.LogMessage($"Uploaded file from new folder: {relativePath}");
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");
                    // Continue with other files
                }
            }

            LogFileWriter.LogMessage($"AddFolder completed: {operation.NewPath} ({filesToUpload.Count} files)");
        }

        /// <summary>
        /// Process a DeleteFolder operation - deletes all files in the folder from server
        /// </summary>
        private static async Task ProcessDeleteFolderOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessDeleteFolderOperation: {operation.OldPath}");

            if (string.IsNullOrEmpty(operation.OldPath))
            {
                throw new Exception("DeleteFolder operation missing OldPath");
            }

            string folderRelativePath = GetRelativePath(vault.RootFolderPath, operation.OldPath);
            LogFileWriter.LogMessage($"Deleting all files in folder from server: {folderRelativePath}");

            // Get all files from server that are in this folder path
            var serverData = await leoClient.GetSyncMetadataAsync(directoryId);
            List<string> filesToDelete = new List<string>();

            if (serverData?.Files != null)
            {
                foreach (var serverFile in serverData.Files)
                {
                    // Check if file path starts with folder path (including subfolder files)
                    if (serverFile.FilePathInDirectory.StartsWith(folderRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        filesToDelete.Add(serverFile.FilePathInDirectory);
                    }
                }
            }

            LogFileWriter.LogMessage($"Found {filesToDelete.Count} files in folder to delete from server");

            // Delete each file
            int deleted = 0;
            foreach (string relativePath in filesToDelete)
            {
                try
                {
                    bool success = await leoClient.DeleteFileAsync(directoryId, relativePath);
                    if (success)
                    {
                        deleted++;
                        LogFileWriter.LogMessage($"Deleted file from deleted folder: {relativePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to delete file {relativePath}: {ex.Message}");
                    // Continue with other files
                }
            }

            LogFileWriter.LogMessage($"DeleteFolder completed: {operation.OldPath} ({deleted}/{filesToDelete.Count} files deleted)");
        }

        /// <summary>
        /// Process a MoveFolder operation - uploads files to new paths and deletes old paths
        /// </summary>
        private static async Task ProcessMoveFolderOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessMoveFolderOperation: {operation.OldPath} → {operation.NewPath}");

            if (string.IsNullOrEmpty(operation.OldPath) || string.IsNullOrEmpty(operation.NewPath))
            {
                throw new Exception("MoveFolder operation missing OldPath or NewPath");
            }

            // Get new folder from vault
            string newFolderFullPath = EnsureFullPath(vault, operation.NewPath);
            IEdmFolder5 newFolder = vault.GetFolderFromPath(newFolderFullPath);

            if (newFolder == null)
            {
                LogFileWriter.LogMessage($"Warning: New folder not found in vault: {operation.NewPath}");
                return;
            }

            // Get all files in new folder location
            List<string> oldFilePaths = new List<string>();
            List<string> newFilePaths = new List<string>();

            string oldFolderPath = EnsureFullPath(vault, operation.OldPath);
            string newFolderPath = newFolder.LocalPath;

            EnumerateFolderFilesRecursive(newFolder, oldFolderPath, newFolderPath, oldFilePaths, newFilePaths);

            LogFileWriter.LogMessage($"Found {newFilePaths.Count} files to move");

            // Build mapping of files being moved (new relative path -> old relative path)
            // This is used to identify if a dependency is part of this folder move operation
            Dictionary<string, string> moveMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < newFilePaths.Count; i++)
            {
                string oldRelPath = GetRelativePath(vault.RootFolderPath, oldFilePaths[i]);
                string newRelPath = GetRelativePath(vault.RootFolderPath, newFilePaths[i]);
                moveMap[newRelPath] = oldRelPath;
            }

            // IMPORTANT: Share processedFiles across all files in this folder move
            // This prevents re-processing dependencies that were already handled
            ConcurrentDictionary<string, byte> processedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            // Create processing set for circular dependency detection (thread-safe)
            ConcurrentDictionary<string, byte> processingFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            // Create dependency cache to avoid redundant PDM API calls (thread-safe)
            ConcurrentDictionary<string, Dictionary<int, List<string>>> dependencyCache = new ConcurrentDictionary<string, Dictionary<int, List<string>>>(StringComparer.OrdinalIgnoreCase);

            // Process each file: upload new path, delete old path
            int processed = 0;
            for (int i = 0; i < newFilePaths.Count; i++)
            {
                try
                {
                    string oldFilePath = oldFilePaths[i];
                    string newFilePath = newFilePaths[i];

                    string oldRelativePath = GetRelativePath(vault.RootFolderPath, oldFilePath);
                    string newRelativePath = GetRelativePath(vault.RootFolderPath, newFilePath);

                    LogFileWriter.LogMessage($"Moving file: {oldFilePath} -> {newFilePath}");

                    // Upload file with new path using move context
                    // Pass move map and shared processedFiles so dependencies can be identified and tracked
                    await UploadFileWithDependencies(vault, newFilePath, vault.RootFolderPath, processedFiles, processingFiles, dependencyCache, moveMap, leoClient, directoryId, tryArchiveFirst);
                    LogFileWriter.LogMessage($"Uploaded file to new location: {newRelativePath}");

                    // Delete old path
                    bool deleted = await leoClient.DeleteFileAsync(directoryId, oldRelativePath);
                    if (deleted)
                    {
                        LogFileWriter.LogMessage($"Deleted old file path: {oldRelativePath}");
                    }

                    processed++;
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to move file {oldFilePaths[i]}: {ex.Message}");
                    // Continue with other files
                }
            }

            LogFileWriter.LogMessage($"MoveFolder completed: {operation.OldPath} → {operation.NewPath} ({processed}/{newFilePaths.Count} files moved)");
        }

        /// <summary>
        /// Process a RenameFolder operation - same as MoveFolder
        /// </summary>
        private static async Task ProcessRenameFolderOperation(IEdmVault11 vault, OperationMetadata operation, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            LogFileWriter.LogMessage($"ProcessRenameFolderOperation: {operation.OldPath} → {operation.NewPath}");
            // Rename and Move folder operations are identical in implementation
            await ProcessMoveFolderOperation(vault, operation, leoClient, directoryId, tryArchiveFirst);
        }

        /// <summary>
        /// Process a CompleteSync operation - syncs entire vault with server
        /// </summary>
        public static async Task ProcessCompleteSyncOperation(IEdmVault11 vault, IEdmTaskInstance taskInstance, SecureApiClient leoClient, string directoryId)
        {
            LogFileWriter.LogMessage("ProcessCompleteSyncOperation: Starting full vault sync (ARCHIVE-FIRST mode)");

            // Complete sync uses archive-first mode for performance
            bool tryArchiveFirst = true;

            try
            {
                try
                {
                    taskInstance.SetProgressPos(30, "Enumerating vault files...");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }

                // Get all files from vault using SolidWorksPdmHelper
                SolidWorksPdmHelper pdmHelper = new SolidWorksPdmHelper(vault);
                pdmHelper.ProcessFolders(vault);
                List<FileData> vaultFiles = pdmHelper.FilesInfo;
                LogFileWriter.LogMessage($"Found {vaultFiles.Count} files in vault");

                try
                {
                    taskInstance.SetProgressPos(35, "Calculating checksums...");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }

                // Calculate checksums for all vault files using archive-first approach
                // Use streaming checksums for memory efficiency with large files
                // PARALLEL: Checksum calculation is I/O bound and benefits from parallel processing

                // Phase 1: Collect file info sequentially (COM requirement)
                var fileInfoList = new List<Tuple<FileData, string, bool>>();
                foreach (var vaultFile in vaultFiles)
                {
                    try
                    {
                        string fullPath = EnsureFullPath(vault, vaultFile.file);

                        // Just add the file path - no need to get readable path for identifier generation
                        fileInfoList.Add(Tuple.Create(vaultFile, fullPath, false));
                    }
                    catch (Exception csEx)
                    {
                        LogFileWriter.LogError($"Failed to process file {vaultFile.file}: {csEx.Message}");
                        // Leave checksum as null - will be treated as changed
                    }
                }

                // Phase 2: Get FileID_Version identifiers sequentially (COM requirement - must be single-threaded)
                int identifierProgress = 0;
                bool cancellationRequested = false;

                try
                {
                    foreach (var fileInfo in fileInfoList)
                    {
                        // Check for cancellation
                        if (cancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            var vaultFile = fileInfo.Item1;
                            var fullPath = fileInfo.Item2;

                            // Get FileID_Version identifier (fast, no file I/O)
                            var fileInfoData = GetUniqueIdentifier(vault, fullPath);
                            vaultFile.checkSum = fileInfoData.CheckSum;  // Now contains "fileID_version"
                        }
                        catch (Exception csEx)
                        {
                            LogFileWriter.LogError($"Failed to get identifier for {fileInfo.Item1.file}: {csEx.Message}");
                            // Leave checksum as null - will be treated as changed
                        }

                        // Progress tracking
                        identifierProgress++;

                        // Report progress every 100 files to reduce overhead
                        if (identifierProgress % 100 == 0)
                        {
                            // Check for cancellation
                            if (IsTaskCancelled(taskInstance))
                            {
                                LogFileWriter.LogMessage("Task cancelled by user during identifier generation");
                                cancellationRequested = true;
                                break;
                            }

                            try
                            {
                                int progress = 35 + (int)((identifierProgress / (float)vaultFiles.Count) * 5);
                                taskInstance.SetProgressPos(progress, $"Generated identifiers for {identifierProgress}/{vaultFiles.Count} files");
                            }
                            catch (Exception progEx)
                            {
                                LogFileWriter.LogError($"Failed to update progress status: {progEx.Message}");
                                // Continue - progress update failure shouldn't stop the sync
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Error during identifier generation: {ex.Message}");
                    // Continue with what we have - partial identifiers are okay
                }

                // Check if cancellation was requested
                if (cancellationRequested)
                {
                    throw new OperationCanceledException("Task cancelled by user during identifier generation");
                }

                try
                {
                    taskInstance.SetProgressPos(40, "Getting server state...");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }

                // Get all files from server using pagination
                var serverFilesList = await leoClient.GetAllSyncMetadataAsync(directoryId);
                Dictionary<string, SyncMetadataFile> serverFiles = new Dictionary<string, SyncMetadataFile>(StringComparer.OrdinalIgnoreCase);
                if (serverFilesList != null)
                {
                    foreach (var file in serverFilesList)
                    {
                        try
                        {
                            serverFiles[file.FilePathInDirectory] = file;
                        }
                        catch (Exception serverEx)
                        {
                            LogFileWriter.LogError($"Failed to process server file {file?.FilePathInDirectory ?? "unknown"}: {serverEx.Message}");
                            // Continue processing remaining server files
                        }
                    }
                }
                LogFileWriter.LogMessage($"Found {serverFiles.Count} files on server");

                try
                {
                    taskInstance.SetProgressPos(50, "Comparing vault and server state...");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }

                // Build sets for comparison
                HashSet<string> vaultPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var vaultFile in vaultFiles)
                {
                    try
                    {
                        string relativePath = GetRelativePath(vault.RootFolderPath, vaultFile.file);
                        vaultPaths.Add(relativePath);
                    }
                    catch (Exception pathEx)
                    {
                        LogFileWriter.LogError($"Failed to process vault file path {vaultFile?.file ?? "unknown"}: {pathEx.Message}");
                        // Continue processing remaining vault files
                    }
                }

                // Track files that already exist on server (to avoid re-processing dependencies)
                HashSet<string> alreadyOnServer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Find files to upload (in vault but not on server, or changed)
                List<string> newFilesToUpload = new List<string>();
                List<string> modifiedFilesToUpload = new List<string>();
                foreach (var vaultFile in vaultFiles)
                {
                    try
                    {
                        string relativePath = GetRelativePath(vault.RootFolderPath, vaultFile.file);
                        if (!serverFiles.ContainsKey(relativePath))
                        {
                            newFilesToUpload.Add(vaultFile.file);
                            LogFileWriter.LogMessage($"New file to upload: {relativePath}");
                        }
                        else
                        {
                            // Check if file changed (compare checksum) or has IN_ERROR status
                            var serverFile = serverFiles[relativePath];

                            // Check if file has IN_ERROR status - force reupload even if checksum matches
                            if (serverFile.ParentStatus == "IN_ERROR")
                            {
                                modifiedFilesToUpload.Add(vaultFile.file);
                                LogFileWriter.LogMessage($"Modified file to upload: {relativePath} (IN_ERROR status - forcing reupload)");
                            }
                            // Only compare if we successfully calculated checksum
                            else if (!string.IsNullOrEmpty(vaultFile.checkSum) && serverFile.CheckSum != vaultFile.checkSum)
                            {
                                modifiedFilesToUpload.Add(vaultFile.file);
                                LogFileWriter.LogMessage($"Modified file to upload: {relativePath} (server: {serverFile.CheckSum}, vault: {vaultFile.checkSum})");
                            }
                            else if (string.IsNullOrEmpty(vaultFile.checkSum))
                            {
                                // Couldn't calculate checksum - treat as modified to be safe
                                modifiedFilesToUpload.Add(vaultFile.file);
                                LogFileWriter.LogMessage($"Modified file to upload (no checksum): {relativePath}");
                            }
                            else
                            {
                                // Checksums match - skip upload but track as already on server
                                // This is important for dependency tracking: if file A depends on file B,
                                // and B already exists on server with correct checksum, we don't want to
                                // re-upload B when processing A's dependencies
                                alreadyOnServer.Add(relativePath);
                                LogFileWriter.LogMessage($"File unchanged (checksums match): {relativePath}");
                            }
                        }
                    }
                    catch (Exception compEx)
                    {
                        LogFileWriter.LogError($"Failed to compare file {vaultFile?.file ?? "unknown"}: {compEx.Message}");
                        // Treat as modified to be safe - better to re-upload than miss a change
                        if (vaultFile?.file != null)
                        {
                            modifiedFilesToUpload.Add(vaultFile.file);
                        }
                    }
                }

                // Find files to delete (on server but not in vault - vault is master)
                List<string> filesToDelete = new List<string>();
                foreach (var serverPath in serverFiles.Keys)
                {
                    try
                    {
                        if (!vaultPaths.Contains(serverPath))
                        {
                            filesToDelete.Add(serverPath);
                            LogFileWriter.LogMessage($"File to delete from server: {serverPath}");
                        }
                    }
                    catch (Exception delEx)
                    {
                        LogFileWriter.LogError($"Failed to process server file for deletion {serverPath ?? "unknown"}: {delEx.Message}");
                        // Continue processing remaining files
                    }
                }

                int totalFilesToUpload = newFilesToUpload.Count + modifiedFilesToUpload.Count;
                LogFileWriter.LogMessage($"New files: {newFilesToUpload.Count}, Modified files: {modifiedFilesToUpload.Count}, Files to delete: {filesToDelete.Count}");

                // Delete modified files first (before uploading new versions)
                if (modifiedFilesToUpload.Count > 0)
                {
                    try
                    {
                        taskInstance.SetProgressPos(55, $"Deleting {modifiedFilesToUpload.Count} modified files before re-upload...");
                    }
                    catch (Exception progEx)
                    {
                        LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                    }

                    // NOTE: Delete kept sequential - Parallel.ForEach with async doesn't properly await
                    int deletedModified = 0;
                    foreach (var filePath in modifiedFilesToUpload)
                    {
                        // Check for cancellation every 10 deletes
                        if (deletedModified % 10 == 0 && IsTaskCancelled(taskInstance))
                        {
                            LogFileWriter.LogMessage("Task cancelled by user during modified files deletion");
                            throw new OperationCanceledException("Task cancelled by user");
                        }

                        try
                        {
                            string relativePath = GetRelativePath(vault.RootFolderPath, filePath);
                            bool deleteSuccess = await leoClient.DeleteFileAsync(directoryId, relativePath).ConfigureAwait(false);
                            if (deleteSuccess)
                            {
                                deletedModified++;
                                LogFileWriter.LogMessage($"Deleted modified file before re-upload: {relativePath}");
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"Warning: Failed to delete modified file: {relativePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogFileWriter.LogError($"Error deleting modified file {filePath}: {ex.Message}");
                            // Continue with upload anyway
                        }
                    }
                    LogFileWriter.LogMessage($"Deleted {deletedModified} modified files before re-upload");
                }

                // Upload all files (new + modified)
                List<string> allFilesToUpload = new List<string>();
                allFilesToUpload.AddRange(newFilesToUpload);
                allFilesToUpload.AddRange(modifiedFilesToUpload);

                try
                {
                    taskInstance.SetProgressPos(60, $"Uploading {totalFilesToUpload} files...");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }
                int uploaded = 0;
                int uploadTotal = totalFilesToUpload > 0 ? totalFilesToUpload : 1;

                LogFileWriter.LogMessage($"Initial sync: {alreadyOnServer.Count} files already on server with matching checksums (will be used for dependency tracking)");

                // NOTE: Uploads are kept sequential due to complex dependency handling
                // UpdateFilesToLeoAI creates a local uploadedFiles HashSet which would cause
                // race conditions if multiple threads upload files with shared dependencies
                foreach (var filePath in allFilesToUpload)
                {
                    // Check for cancellation every 10 uploads
                    if (uploaded % 10 == 0 && IsTaskCancelled(taskInstance))
                    {
                        LogFileWriter.LogMessage("Task cancelled by user during file upload");
                        throw new OperationCanceledException("Task cancelled by user");
                    }

                    try
                    {
                        // Pass alreadyOnServer set so dependencies that exist on server aren't re-uploaded
                        await UpdateFilesToLeoAI(vault, new[] { filePath }, vault.RootFolderPath, leoClient, directoryId, tryArchiveFirst, alreadyOnServer).ConfigureAwait(false);
                        uploaded++;

                        // Report progress every 100 files instead of every file to reduce overhead
                        if (uploaded % 100 == 0 || uploaded == totalFilesToUpload)
                        {
                            try
                            {
                                int progress = 60 + (int)((uploaded / (float)uploadTotal) * 20);
                                taskInstance.SetProgressPos(progress, $"Uploaded {uploaded}/{totalFilesToUpload} files");
                            }
                            catch (Exception progEx)
                            {
                                LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");
                        // Continue with next file instead of failing entire sync
                    }
                }
                LogFileWriter.LogMessage($"Uploaded {uploaded} files");

                // Delete files
                try
                {
                    taskInstance.SetProgressPos(80, $"Deleting {filesToDelete.Count} files from server...");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }

                // NOTE: Delete kept sequential - Parallel.ForEach with async doesn't properly await
                int deleted = 0;
                int deleteTotal = filesToDelete.Count > 0 ? filesToDelete.Count : 1;
                foreach (var relativePath in filesToDelete)
                {
                    // Check for cancellation every 10 deletes
                    if (deleted % 10 == 0 && IsTaskCancelled(taskInstance))
                    {
                        LogFileWriter.LogMessage("Task cancelled by user during file deletion");
                        throw new OperationCanceledException("Task cancelled by user");
                    }

                    try
                    {
                        bool success = await leoClient.DeleteFileAsync(directoryId, relativePath).ConfigureAwait(false);
                        if (success)
                        {
                            deleted++;
                            LogFileWriter.LogMessage($"Deleted from server: {relativePath}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"Failed to delete from server: {relativePath}");
                        }

                        // Report progress every 100 files instead of every file to reduce overhead
                        if (deleted % 100 == 0 || deleted == filesToDelete.Count)
                        {
                            try
                            {
                                int progress = 80 + (int)((deleted / (float)deleteTotal) * 10);
                                taskInstance.SetProgressPos(progress, $"Deleted {deleted}/{filesToDelete.Count} files");
                            }
                            catch (Exception progEx)
                            {
                                LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Error deleting file {relativePath}: {ex.Message}");
                        // Continue with next file instead of failing entire sync
                    }
                }
                LogFileWriter.LogMessage($"Deleted {deleted} files from server");

                try
                {
                    taskInstance.SetProgressPos(100, "Complete sync finished");
                }
                catch (Exception progEx)
                {
                    LogFileWriter.LogError($"Failed to update progress: {progEx.Message}");
                }

                // Report success with statistics
                int totalAttempted = totalFilesToUpload + filesToDelete.Count;
                int totalSucceeded = uploaded + deleted;
                int totalFailed = totalAttempted - totalSucceeded;

                string statusMessage = $"Complete sync finished: {uploaded}/{totalFilesToUpload} uploaded, {deleted}/{filesToDelete.Count} deleted";
                if (totalFailed > 0)
                {
                    statusMessage += $" ({totalFailed} failed)";
                    LogFileWriter.LogMessage($"ProcessCompleteSyncOperation: Completed with errors - {statusMessage}");
                    try
                    {
                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, statusMessage);
                    }
                    catch (Exception statusEx)
                    {
                        LogFileWriter.LogError($"Failed to set task status: {statusEx.Message}");
                    }
                }
                else
                {
                    LogFileWriter.LogMessage($"ProcessCompleteSyncOperation: Completed successfully - {statusMessage}");
                    try
                    {
                        taskInstance.SetStatus(EdmTaskStatus.EdmTaskStat_DoneOK, 0, statusMessage);
                    }
                    catch (Exception statusEx)
                    {
                        LogFileWriter.LogError($"Failed to set task status: {statusEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"ProcessCompleteSyncOperation failed: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                SentryErrorHandler.CaptureException(ex, new Dictionary<string, string>
                {
                    { "operation", "ProcessCompleteSyncOperation" },
                    { "vault", vault?.Name ?? "unknown" },
                    { "directory_id", directoryId ?? "unknown" }
                }, Sentry.SentryLevel.Fatal);
                throw;
            }
        }

        public static async Task<string> GetOrCreateDirectoryId(SecureApiClient leoClient, string vaultPath)
        {
            LogFileWriter.LogMessage($"Getting/creating directory for vault: {vaultPath}");

            string macAddress = LeoAIDataUtilities.GetFormattedMacAddress();
            var directories = await leoClient.GetDirectoryInfoAsync(macAddress);
            var directory = directories.FirstOrDefault(d => d.Uri.Equals(vaultPath, StringComparison.OrdinalIgnoreCase));

            if (directory != null)
            {
                LogFileWriter.LogMessage($"Directory exists: {directory.Id}");
                return directory.Id;
            }

            LogFileWriter.LogMessage("Directory not found, creating new directory");
            string directoryId = await leoClient.CreateDirectoryAsync(macAddress, vaultPath);
            LogFileWriter.LogMessage($"Created directory: {directoryId}");

            return directoryId;
        }

        #endregion

        #region Sync Operations
        private static void EnumerateFolderFilesRecursive(IEdmFolder5 folder, string oldBasePath, string newBasePath, List<string> oldFilePaths, List<string> newFilePaths)
        {
            // Get all files in current folder
            IEdmPos5 pos = folder.GetFirstFilePosition();
            while (!pos.IsNull)
            {
                IEdmFile5 file = folder.GetNextFile(pos);
                string filePath = file.GetLocalPath(folder.ID);

                // Only process SLDPRT, SLDASM, SLDDRW files
                if (!string.IsNullOrEmpty(filePath) && IsProcessableFile(filePath))
                {
                    // Calculate old path by replacing new base with old base
                    string relativePath = filePath.Substring(newBasePath.Length).TrimStart('\\', '/');
                    string oldFilePath = Path.Combine(oldBasePath, relativePath);

                    oldFilePaths.Add(oldFilePath);
                    newFilePaths.Add(filePath);
                }
            }

            // Process subfolders recursively
            IEdmPos5 subPos = folder.GetFirstSubFolderPosition();
            while (!subPos.IsNull)
            {
                IEdmFolder5 subFolder = folder.GetNextSubFolder(subPos);
                EnumerateFolderFilesRecursive(subFolder, oldBasePath, newBasePath, oldFilePaths, newFilePaths);
            }
        }

        private static bool IsProcessableFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToUpper();

            // Skip SolidWorks lock files (temporary files that start with ~$)
            if (fileName.StartsWith("~$") && (ext == ".SLDPRT" || ext == ".SLDASM"))
            {
                return false;
            }

            return ext == ".SLDPRT" ||
                   ext == ".SLDASM" ||
                   ext == ".STEP" ||
                   ext == ".STP" ||
                   ext == ".PRT" ||
                   ext == ".ASM" ||
                   ext == ".IPT" ||
                   ext == ".IAM" ||
                   ext == ".X_T" ||
                   ext == ".XT" ||
                   ext == ".TXT" ||
                   ext == ".PDF" ||
                   ext == ".DOC" ||
                   ext == ".DOCX";
        }

        #endregion

        #region Helper Methods (copied from SwPdmAddinMain)

        /// <summary>
        /// Gets all file dependencies (references) for a given file
        /// Returns dependencies organized by level (depth in dependency tree)
        /// Level 0 = direct dependencies, Level 1 = dependencies of dependencies, etc.
        /// Higher levels (deeper in tree) should be processed first as they have no subdependencies
        /// Uses recursive traversal of the PDM reference tree
        /// </summary>
        private static Dictionary<int, List<string>> GetFileDependencies(IEdmVault11 vault, string filePath, string vaultRootPath)
        {
            // Dictionary: level -> list of file paths at that level
            // Also track which files we've seen to avoid duplicates
            Dictionary<int, List<string>> dependenciesByLevel = new Dictionary<int, List<string>>();
            HashSet<string> seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Ensure we have full path for GetFileFromPath
                string fullPath = EnsureFullPath(vault, filePath);

                // Get file object from vault
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);

                if (file == null || folder == null)
                {
                    LogFileWriter.LogMessage($"GetFileDependencies: File not found: {fullPath}");
                    return dependenciesByLevel;
                }

                // Get the reference tree for this file
                IEdmReference10 referenceTree = (IEdmReference10)file.GetReferenceTree(folder.ID);

                if (referenceTree == null)
                {
                    LogFileWriter.LogMessage($"GetFileDependencies: No reference tree for: {filePath}");
                    return dependenciesByLevel;
                }

                // Recursively traverse the reference tree to collect dependency paths by level
                TraverseReferences(vault, referenceTree, "", 0, true, vaultRootPath, dependenciesByLevel, seenFiles);

                int totalCount = dependenciesByLevel.Values.Sum(list => list.Count);
                LogFileWriter.LogMessage($"GetFileDependencies: Found {totalCount} dependencies across {dependenciesByLevel.Count} levels for {filePath}");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"GetFileDependencies failed for {filePath}: {ex.Message}");
                // Return empty dictionary - file will be uploaded without dependencies
            }

            return dependenciesByLevel;
        }

        /// <summary>
        /// Recursively traverses the PDM reference tree to extract all dependency paths organized by level
        /// Filters out invalid references like weldment cut-list items
        /// </summary>
        private static void TraverseReferences(IEdmVault11 vault, IEdmReference10 reference, string projectName, int level, bool isTop, string vaultRootPath, Dictionary<int, List<string>> dependenciesByLevel, HashSet<string> seenFiles)
        {
            try
            {
                if (isTop)
                {
                    // This is the root - skip adding it, just traverse children
                    IEdmPos5 pos = reference.GetFirstChildPosition3(projectName, true, true, (int)EdmRefFlags.EdmRef_File, "", 0);

                    while (!pos.IsNull)
                    {
                        IEdmReference10 childRef = (IEdmReference10)reference.GetNextChild(pos);
                        if (childRef != null && !string.IsNullOrEmpty(childRef.FoundPath))
                        {
                            // Skip invalid references (e.g., weldment cut-list items, broken references)
                            if (IsValidFileReference(childRef.FoundPath) && IsProcessableFile(childRef.FoundPath))
                            {
                                string relativePath = GetRelativePath(vaultRootPath, childRef.FoundPath);

                                // Add dependency if not already seen
                                if (!seenFiles.Contains(relativePath))
                                {
                                    seenFiles.Add(relativePath);

                                    // Add to the appropriate level list
                                    if (!dependenciesByLevel.ContainsKey(level))
                                    {
                                        dependenciesByLevel[level] = new List<string>();
                                    }
                                    dependenciesByLevel[level].Add(relativePath);

                                    LogFileWriter.LogMessage($"  Dependency (level {level}): {relativePath}");

                                    // Recursively traverse this child's dependencies (ONLY if not seen before)
                                    TraverseReferences(vault, childRef, projectName, level + 1, false, vaultRootPath, dependenciesByLevel, seenFiles);
                                }
                                else
                                {
                                    LogFileWriter.LogDebug($"  Skipping already seen file (circular reference) at level {level}: {relativePath}");
                                }
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"  Skipping invalid/non-processable reference (level {level}): {childRef.FoundPath}");
                            }
                        }
                    }
                }
                else
                {
                    // Process children of this reference
                    IEdmPos5 pos = reference.GetFirstChildPosition3(projectName, false, true, (int)EdmRefFlags.EdmRef_File, "", 0);

                    while (!pos.IsNull)
                    {
                        IEdmReference10 childRef = (IEdmReference10)reference.GetNextChild(pos);
                        if (childRef != null && !string.IsNullOrEmpty(childRef.FoundPath))
                        {
                            // Skip invalid references (e.g., weldment cut-list items, broken references)
                            if (IsValidFileReference(childRef.FoundPath) && IsProcessableFile(childRef.FoundPath))
                            {
                                string relativePath = GetRelativePath(vaultRootPath, childRef.FoundPath);

                                // Add dependency if not already seen
                                if (!seenFiles.Contains(relativePath))
                                {
                                    seenFiles.Add(relativePath);

                                    // Add to the appropriate level list
                                    if (!dependenciesByLevel.ContainsKey(level))
                                    {
                                        dependenciesByLevel[level] = new List<string>();
                                    }
                                    dependenciesByLevel[level].Add(relativePath);

                                    LogFileWriter.LogMessage($"  Dependency (level {level}): {relativePath}");

                                    // Recursively traverse this child's dependencies (ONLY if not seen before)
                                    TraverseReferences(vault, childRef, projectName, level + 1, false, vaultRootPath, dependenciesByLevel, seenFiles);
                                }
                                else
                                {
                                    LogFileWriter.LogDebug($"  Skipping already seen file (circular reference) at level {level}: {relativePath}");
                                }
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"  Skipping invalid/non-processable reference (level {level}): {childRef.FoundPath}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"TraverseReferences failed at level {level}: {ex.Message}");
                LogFileWriter.LogDebug($"  Continuing without dependencies from this level");
                // Don't re-throw - continue with dependencies collected so far
            }
        }

        /// <summary>
        /// Checks if a file reference is valid (has a file extension and is not a cut-list item)
        /// </summary>
        private static bool IsValidFileReference(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            try
            {
                // Check for illegal path characters first (before calling Path methods)
                char[] invalidPathChars = Path.GetInvalidPathChars();
                if (filePath.IndexOfAny(invalidPathChars) >= 0)
                {
                    LogFileWriter.LogDebug($"Skipping reference with illegal path characters: {filePath}");
                    return false;
                }

                // Check if it has a file extension
                string extension = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(extension))
                {
                    // No extension = likely a cut-list item or broken reference
                    return false;
                }

                // Filter out known invalid references
                string fileName = Path.GetFileName(filePath);

                // Skip weldment cut-list items (typically named like "Cut-List-Item1", "Cut-List-Item2", etc.)
                if (fileName.StartsWith("Cut-List-Item", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch (ArgumentException ex)
            {
                // Path.GetExtension or Path.GetFileName threw due to invalid characters
                LogFileWriter.LogWarning($"Path validation failed for reference: {filePath} - {ex.Message}");
                return false; // Skip this reference
            }
        }

        /// <summary>
        /// Deletes a metadata file from vault
        /// </summary>
        private static void DeleteMetadataFile(IEdmVault11 vault, IEdmFile5 file, IEdmFolder5 folder)
        {
            try
            {
                if (file == null || folder == null)
                {
                    LogFileWriter.LogError("Cannot delete metadata file: file or folder is null");
                    return;
                }

                // Delete the file from vault using folder.DeleteFile
                folder.DeleteFile(0, file.ID, true); // window handle, file ID, permanently delete
                LogFileWriter.LogMessage($"Deleted metadata file from vault: {file.Name}");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error deleting metadata file: {ex.Message}");
                throw;
            }
        }

        private static async Task UpdateFilesToLeoAI(IEdmVault11 vault, string[] filePaths, string vaultRootPath, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst, HashSet<string> alreadyOnServer = null)
        {
            LogFileWriter.LogMessage($"Updating {filePaths.Length} files to Leo AI");

            // Track uploaded files to avoid re-uploading dependencies multiple times
            // Start with files that are already on server (from initial sync comparison)
            // Use thread-safe ConcurrentDictionary for parallel uploads
            ConcurrentDictionary<string, byte> uploadedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            if (alreadyOnServer != null)
            {
                foreach (var path in alreadyOnServer)
                {
                    uploadedFiles.TryAdd(path, 0);
                }
            }

            // Create processing set for circular dependency detection (thread-safe)
            ConcurrentDictionary<string, byte> processingFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            // Create dependency cache to avoid redundant PDM API calls (thread-safe)
            ConcurrentDictionary<string, Dictionary<int, List<string>>> dependencyCache = new ConcurrentDictionary<string, Dictionary<int, List<string>>>(StringComparer.OrdinalIgnoreCase);

            // Upload each file with its dependencies (dependencies uploaded first, recursively)
            foreach (string filePath in filePaths)
            {
                try
                {
                    await UploadFileWithDependencies(vault, filePath, vaultRootPath, uploadedFiles, processingFiles, dependencyCache, null, leoClient, directoryId, tryArchiveFirst);
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");
                    SentryErrorHandler.CaptureException(ex, new Dictionary<string, string>
                    {
                        { "operation", "UpdateFilesToLeoAI" },
                        { "file_path", filePath },
                        { "vault_root", vaultRootPath }
                    });
                    throw; // Propagate error to fail the operation
                }
            }

            LogFileWriter.LogMessage($"Uploaded {uploadedFiles.Count} unique files (including dependencies)");
        }


        /// <summary>
        /// Recursively uploads a file and all its dependencies
        /// Dependencies are uploaded first before the file that depends on them
        /// Uses uploadedFiles set to track what's been uploaded and avoid duplicates
        /// Uses processingFiles set to prevent circular dependency loops
        /// Uses dependencyCache to avoid redundant PDM API calls
        /// moveMap (optional): new relative path -> old relative path for files being moved/renamed
        /// </summary>
        private static async Task UploadFileWithDependencies(IEdmVault11 vault, string filePath, string vaultRootPath, ConcurrentDictionary<string, byte> uploadedFiles, ConcurrentDictionary<string, byte> processingFiles, ConcurrentDictionary<string, Dictionary<int, List<string>>> dependencyCache, Dictionary<string, string> moveMap, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            string relativePath = GetRelativePath(vaultRootPath, filePath);

            // Check if already uploaded - if so, skip everything (no need to process dependencies or upload)
            if (uploadedFiles.ContainsKey(relativePath))
            {
                LogFileWriter.LogMessage($"File already uploaded, skipping: {relativePath}");
                return;
            }

            // Check if this file is currently being processed (circular dependency prevention)
            // TryAdd returns false if key already exists - this is an atomic check-and-add operation
            if (!processingFiles.TryAdd(relativePath, 0))
            {
                LogFileWriter.LogWarning($"Circular dependency detected, skipping: {relativePath}");
                return;
            }

            try
            {
                LogFileWriter.LogMessage($"=== Processing file: {relativePath} ===");

                // Get dependencies for this file organized by level (depth in dependency tree)
                // Use cache if available to avoid redundant PDM API calls
                // GetOrAdd is atomic - only one thread will call GetFileDependencies for a given key
                Dictionary<int, List<string>> dependenciesByLevel = dependencyCache.GetOrAdd(
                    relativePath,
                    key => GetFileDependencies(vault, filePath, vaultRootPath)
                );

                if (dependencyCache.ContainsKey(relativePath))
                {
                    LogFileWriter.LogMessage($"Using cached/computed dependencies for: {relativePath}");
                }

                // If file has dependencies, upload them first (level by level, deepest first)
                int totalDependencies = dependenciesByLevel.Values.Sum(list => list.Count);
            if (totalDependencies > 0)
            {
                LogFileWriter.LogMessage($"File has {totalDependencies} dependencies across {dependenciesByLevel.Count} levels - uploading dependencies level-by-level (deepest first)");

                // Process levels from highest (deepest) to lowest (direct dependencies)
                // This ensures dependencies are uploaded before files that depend on them
                var sortedLevels = dependenciesByLevel.Keys.OrderByDescending(level => level).ToList();

                foreach (var level in sortedLevels)
                {
                    var filesAtLevel = dependenciesByLevel[level];
                    LogFileWriter.LogMessage($"Processing level {level} ({filesAtLevel.Count} files) in parallel");

                    // Process all files at this level in parallel
                    var uploadTasks = new List<Task>();

                    foreach (var depRelativePath in filesAtLevel)
                    {
                        // Skip self-reference (file depends on itself - corrupted PDM data)
                        if (depRelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            LogFileWriter.LogWarning($"  [Level {level}] Skipping self-referencing dependency: {depRelativePath}");
                            continue;
                        }

                        // Skip if already uploaded
                        if (uploadedFiles.ContainsKey(depRelativePath))
                        {
                            LogFileWriter.LogMessage($"  [Level {level}] Dependency already uploaded, skipping: {depRelativePath}");
                            continue;
                        }

                        // Check if this dependency is part of a move/rename operation
                        bool isDependencyBeingMoved = moveMap != null && moveMap.ContainsKey(depRelativePath);

                        // Convert relative path back to full path
                        string depFullPath = Path.Combine(vaultRootPath, depRelativePath.Replace('/', Path.DirectorySeparatorChar));

                        if (isDependencyBeingMoved)
                        {
                            string oldDepRelativePath = moveMap[depRelativePath];
                            LogFileWriter.LogMessage($"  [Level {level}] Dependency is being moved: {oldDepRelativePath} -> {depRelativePath}");
                        }

                        // Create upload task for this dependency
                        var uploadTask = Task.Run(async () =>
                        {
                            try
                            {
                                // Recursively process this dependency (will check checksum and choose upload method)
                                await UploadFileWithDependencies(vault, depFullPath, vaultRootPath, uploadedFiles, processingFiles, dependencyCache, moveMap, leoClient, directoryId, tryArchiveFirst);
                            }
                            catch (Exception ex)
                            {
                                LogFileWriter.LogError($"[Level {level}] Failed to upload dependency {depRelativePath}: {ex.Message}");
                                LogFileWriter.LogMessage($"Continuing without dependency: {depRelativePath}");
                            }
                        });

                        uploadTasks.Add(uploadTask);
                    }

                    // Wait for all files at this level to complete before moving to next level
                    if (uploadTasks.Count > 0)
                    {
                        await Task.WhenAll(uploadTasks);
                        LogFileWriter.LogMessage($"Level {level} completed ({uploadTasks.Count} parallel uploads)");
                    }
                }
            }

            // Build dependency dictionary with FileID_Version identifiers for files that were uploaded
            // Dictionary format: Key = fileID_version, Value = relative path from assembly (for ChildData constructor)
            Dictionary<string, string> dependenciesWithIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Flatten all dependency paths from all levels
            var allDependencyPaths = dependenciesByLevel.Values.SelectMany(list => list).ToList();

            foreach (var depRelativePath in allDependencyPaths)
            {
                // Only include dependencies that were successfully uploaded
                if (uploadedFiles.ContainsKey(depRelativePath))
                {
                    try
                    {
                        // Get FileID_Version identifier from uploaded file
                        string depFullPath = Path.Combine(vaultRootPath, depRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var depFileInfo = GetUniqueIdentifier(vault, depFullPath);
                        string fileIdVersion = depFileInfo.CheckSum;  // Contains "fileID_version"

                        if (!string.IsNullOrEmpty(fileIdVersion))
                        {
                            // Calculate relative path from assembly to dependency
                            string relativePathToAssembly = CalculateRelativePath(relativePath, depRelativePath);

                            // Key = fileID_version, Value = relative path from assembly
                            dependenciesWithIds[fileIdVersion] = relativePathToAssembly;

                            LogFileWriter.LogDebug($"Dependency [{fileIdVersion}]: {relativePathToAssembly}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue - skip this dependency
                        LogFileWriter.LogError($"Failed to get identifier for dependency {depRelativePath}: {ex.Message}");
                    }
                }
            }

            // Check if this file itself is being moved
            bool isFileBeingMoved = moveMap != null && moveMap.ContainsKey(relativePath);

            if (isFileBeingMoved)
            {
                // Optimistic move: Try UpdateFileLocationAsync first without checking server
                string oldRelativePath = moveMap[relativePath];
                string currentChecksum = null;
                string externalId = "";

                // Get current file identifier for move operation
                try
                {
                    string fullPath = EnsureFullPath(vault, filePath);

                    // Get FileID_Version identifier (no file I/O needed)
                    var fileInfo = GetUniqueIdentifier(vault, fullPath);
                    currentChecksum = fileInfo.CheckSum;  // Contains "fileID_version"
                    externalId = currentChecksum;  // externalId is now just fileID_version
                }
                catch (Exception ex)
                {
                    LogFileWriter.LogError($"Error getting file identifier for move: {ex.Message}");
                }

                // Try optimistic update (without file content)
                LogFileWriter.LogMessage($"Attempting optimistic UpdateFileLocationAsync with checksum: {currentChecksum}, externalId: {externalId}");
                var updateResult = await leoClient.UpdateFileLocationAsync(directoryId, vaultRootPath, filePath, currentChecksum, externalId, dependenciesWithIds);

                if (updateResult != null)
                {
                    // Success - file location updated
                    uploadedFiles.TryAdd(relativePath, 0);
                    LogFileWriter.LogMessage($"File location updated successfully: {relativePath}");
                }
                else
                {
                    // Update failed - check NEW path on server and handle accordingly
                    LogFileWriter.LogMessage($"UpdateFileLocationAsync returned null - checking NEW path on server: {relativePath}");

                    try
                    {
                        var newFileOnServer = await leoClient.GetFileInfoByPathAsync(directoryId, relativePath);

                        if (newFileOnServer != null)
                        {
                            LogFileWriter.LogMessage($"NEW path exists on server with checksum: {newFileOnServer.CheckSum}, status: {newFileOnServer.ParentStatus}");

                            // Check if file at new path needs to be deleted and reuploaded
                            bool needsDelete = false;
                            string reason = "";

                            if (newFileOnServer.ParentStatus == "IN_ERROR")
                            {
                                needsDelete = true;
                                reason = "file has IN_ERROR status";
                                LogFileWriter.LogMessage($"NEW path has IN_ERROR status - will delete and reupload");
                            }
                            else if (newFileOnServer.CheckSum != currentChecksum)
                            {
                                needsDelete = true;
                                reason = "checksum differs";
                                LogFileWriter.LogMessage($"NEW path checksum differs (server: {newFileOnServer.CheckSum}, current: {currentChecksum}) - will delete and reupload");
                            }

                            if (needsDelete)
                            {
                                LogFileWriter.LogMessage($"Deleting NEW path ({reason}): {relativePath}");
                                bool deleted = await leoClient.DeleteFileAsync(directoryId, relativePath);
                                if (deleted)
                                {
                                    LogFileWriter.LogMessage($"Deleted NEW path: {relativePath}");
                                }
                                else
                                {
                                    LogFileWriter.LogMessage($"Delete returned false for NEW path: {relativePath}");
                                }
                            }

                            // Do full upload with file content
                            LogFileWriter.LogMessage($"Performing full upload to NEW path");
                            bool uploadSuccess = await UploadSingleFile(vault, filePath, vaultRootPath, dependenciesWithIds, leoClient, directoryId, tryArchiveFirst);

                            if (uploadSuccess)
                            {
                                uploadedFiles.TryAdd(relativePath, 0);
                                LogFileWriter.LogMessage($"File uploaded to NEW path: {relativePath}");
                            }
                            else
                            {
                                LogFileWriter.LogError($"File upload failed: {relativePath}");
                            }
                        }
                        else
                        {
                            // NEW path doesn't exist on server - do full upload
                            LogFileWriter.LogMessage($"NEW path not found on server - doing full upload");
                            bool uploadSuccess = await UploadSingleFile(vault, filePath, vaultRootPath, dependenciesWithIds, leoClient, directoryId, tryArchiveFirst);

                            if (uploadSuccess)
                            {
                                uploadedFiles.TryAdd(relativePath, 0);
                                LogFileWriter.LogMessage($"File uploaded to NEW path: {relativePath}");
                            }
                            else
                            {
                                LogFileWriter.LogError($"File upload failed: {relativePath}");
                            }
                        }

                        // After successful upload to NEW path, delete OLD path
                        LogFileWriter.LogMessage($"Deleting OLD path from server: {oldRelativePath}");
                        bool oldDeleted = await leoClient.DeleteFileAsync(directoryId, oldRelativePath);
                        if (oldDeleted)
                        {
                            LogFileWriter.LogMessage($"Successfully deleted OLD path: {oldRelativePath}");
                        }
                        else
                        {
                            LogFileWriter.LogMessage($"Delete returned false for OLD path (might be 404 - file didn't exist): {oldRelativePath}");
                        }
                    }
                    catch (Exception checkEx)
                    {
                        LogFileWriter.LogError($"Error handling failed UpdateFileLocationAsync: {checkEx.Message}");
                    }
                }
            }
            else
            {
                // Normal upload flow
                bool uploadSuccess = await UploadSingleFile(vault, filePath, vaultRootPath, dependenciesWithIds, leoClient, directoryId, tryArchiveFirst);

                // Mark as uploaded (even if skipped, because it exists on server)
                if (uploadSuccess)
                {
                    uploadedFiles.TryAdd(relativePath, 0);
                    LogFileWriter.LogMessage($"File upload completed (or skipped if unchanged): {relativePath}");
                }
                else
                {
                    LogFileWriter.LogError($"File upload failed: {relativePath}");
                }
            }
            }
            finally
            {
                // Remove from processing set when done (successful or not)
                byte removedValue;
                processingFiles.TryRemove(relativePath, out removedValue);
            }
        }

        /// <summary>
        /// Uploads a single file with proper error handling and dependency information
        /// ALWAYS checks if file exists on server first, compares identifiers, and deletes old version if needed
        /// Deletes temp file immediately after upload if it was copied to local view
        /// Returns true if file was uploaded or already exists (skipped), false if failed
        /// </summary>
        private static async Task<bool> UploadSingleFile(IEdmVault11 vault, string filePath, string vaultRootPath, Dictionary<string, string> dependencies, SecureApiClient leoClient, string directoryId, bool tryArchiveFirst)
        {
            string relativePath = GetRelativePath(vaultRootPath, filePath);
            LogFileWriter.LogMessage($"Uploading file: {relativePath}");

            // Add breadcrumb for file upload start
            SentryErrorHandler.AddOperationBreadcrumb(
                "UploadFile",
                $"Starting upload: {relativePath}",
                new Dictionary<string, string>
                {
                    { "file", relativePath },
                    { "has_dependencies", dependencies != null && dependencies.Count > 0 ? "yes" : "no" }
                }
            );

            string actualFilePath = null;
            bool needsCleanup = false;

            try
            {
                // Ensure we have full path for GetFileFromPath
                string fullPath = EnsureFullPath(vault, filePath);

                // Get readable file path (archive or temp copy)
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);
                string externalId = "";

                if (file != null && folder != null)
                {
                    SentryErrorHandler.AddOperationBreadcrumb("UploadFile", "Getting readable file path");
                    (actualFilePath, needsCleanup) = GetReadableFilePath(vault, filePath, folder.ID, tryArchiveFirst);

                    // Check if file was found (GetReadableFilePath returns null for files not in vault)
                    if (actualFilePath == null)
                    {
                        LogFileWriter.LogMessage($"Skipping file not in vault: {relativePath}");
                        return false; // Skip this file
                    }

                    // Build externalId: fileID_version (no checksum needed)
                    var idInfo = GetUniqueIdentifier(vault, fullPath);
                    externalId = idInfo.CheckSum;  // Contains "fileID_version"

                    LogFileWriter.LogMessage($"File path for upload: {actualFilePath} (cleanup: {needsCleanup}, externalId: {externalId})");
                }
                else
                {
                    // Fallback: use the provided path directly
                    actualFilePath = filePath;
                    LogFileWriter.LogMessage($"Could not get file from vault, using path directly: {actualFilePath}");
                }

                // Optimistic upload: Try to upload directly first
                // Only check server if we get a 409 conflict
                LogFileWriter.LogMessage($"Attempting optimistic upload: {relativePath}");
                SentryErrorHandler.AddOperationBreadcrumb("UploadFile", "Attempting optimistic upload");

                SentryErrorHandler.AddOperationBreadcrumb(
                    "UploadFile",
                    "Calling API to create file",
                    new Dictionary<string, string>
                    {
                        { "file", relativePath },
                        { "dependency_count", dependencies?.Count.ToString() ?? "0" }
                    }
                );

                var fileInfo = await leoClient.CreateFileAsync(directoryId, vaultRootPath, filePath, actualFilePath, externalId, dependencies);

                // If upload failed, check if it's a conflict (409) and handle it
                if (fileInfo == null)
                {
                    LogFileWriter.LogMessage($"Initial upload returned null - checking if file exists on server");
                    SentryErrorHandler.AddOperationBreadcrumb("UploadFile", "Upload returned null - checking server");

                    try
                    {
                        var serverFile = await leoClient.GetFileInfoByPathAsync(directoryId, relativePath);

                        if (serverFile != null)
                        {
                            LogFileWriter.LogMessage($"File exists on server with identifier: {serverFile.CheckSum}");
                            SentryErrorHandler.AddOperationBreadcrumb("UploadFile", "File found on server - comparing identifiers");

                            // Get local file identifier (FileID_Version)
                            var localFileInfo = GetUniqueIdentifier(vault, fullPath);
                            string localIdentifier = localFileInfo.CheckSum;  // Contains "fileID_version"
                            LogFileWriter.LogMessage($"Local file identifier: {localIdentifier}");

                            // Check if file needs reupload
                            bool needsReupload = false;
                            string reason = "";

                            if (serverFile.ParentStatus == "IN_ERROR")
                            {
                                needsReupload = true;
                                reason = "file has IN_ERROR status";
                                LogFileWriter.LogMessage($"File has IN_ERROR status - forcing reupload");
                            }
                            else if (serverFile.CheckSum != localIdentifier)
                            {
                                needsReupload = true;
                                reason = "version changed";
                            }

                            if (!needsReupload && serverFile.CheckSum == localIdentifier)
                            {
                                LogFileWriter.LogMessage($"File unchanged (identifiers match, no errors) - treating as success");
                                SentryErrorHandler.AddOperationBreadcrumb("UploadFile", "File unchanged - skipping reupload");
                                // File already exists with same version - treat as success
                                fileInfo = new LeoAICadDataClient.Utilities.FileInfo { ComponentId = serverFile.ComponentId };
                            }
                            else if (needsReupload)
                            {
                                LogFileWriter.LogMessage($"File changed ({reason}) - deleting old version before reupload");
                                SentryErrorHandler.AddOperationBreadcrumb("UploadFile", $"Deleting old version: {reason}");

                                // Delete the old version first
                                bool deleted = await leoClient.DeleteFileAsync(directoryId, relativePath);
                                if (deleted)
                                {
                                    LogFileWriter.LogMessage($"Deleted old version: {relativePath} - retrying upload");
                                    // Retry upload after deletion
                                    fileInfo = await leoClient.CreateFileAsync(directoryId, vaultRootPath, filePath, actualFilePath, externalId, dependencies);
                                }
                                else
                                {
                                    LogFileWriter.LogError($"Failed to delete old version: {relativePath}");
                                }
                            }
                        }
                        else
                        {
                            LogFileWriter.LogError($"Upload failed and file not found on server: {relativePath}");
                        }
                    }
                    catch (Exception checkEx)
                    {
                        LogFileWriter.LogError($"Error checking server file after failed upload: {checkEx.Message}");
                    }
                }

                if (fileInfo != null)
                {
                    LogFileWriter.LogMessage($"File synced to server: {relativePath} (ID: {fileInfo.ComponentId})");
                    SentryErrorHandler.AddOperationBreadcrumb("UploadFile", $"Upload successful: {relativePath}");

                    if (dependencies != null && dependencies.Count > 0)
                    {
                        LogFileWriter.LogMessage($"  With {dependencies.Count} dependencies attached");
                    }
                }

                // Delete temp file immediately after successful upload
                // Only delete if we copied it from archive (needsCleanup = true)
                if (needsCleanup && !string.IsNullOrEmpty(actualFilePath))
                {
                    try
                    {
                        DeleteTempFile(actualFilePath);
                        LogFileWriter.LogMessage($"Deleted temp file after upload: {actualFilePath}");
                    }
                    catch (Exception delEx)
                    {
                        LogFileWriter.LogError($"Failed to delete temp file {actualFilePath}: {delEx.Message}");
                    }
                }

                // Upload successful
                return true;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to upload file {filePath}: {ex.Message}");

                // Capture detailed error context to Sentry
                var errorContext = new Dictionary<string, string>
                {
                    { "operation", "UploadSingleFile" },
                    { "file_path", filePath },
                    { "relative_path", relativePath },
                    { "directory_id", directoryId },
                    { "has_dependencies", dependencies != null && dependencies.Count > 0 ? "yes" : "no" },
                    { "dependency_count", dependencies?.Count.ToString() ?? "0" },
                    { "used_temp_file", needsCleanup ? "yes" : "no" },
                    { "error_type", ex.GetType().Name }
                };

                // Add API error details if available
                if (ex.InnerException != null)
                {
                    errorContext.Add("inner_error", ex.InnerException.Message);
                }

                SentryErrorHandler.CaptureException(ex, errorContext);

                // Still try to clean up temp file even if upload failed
                if (needsCleanup && !string.IsNullOrEmpty(actualFilePath))
                {
                    try
                    {
                        DeleteTempFile(actualFilePath);
                        LogFileWriter.LogMessage($"Deleted temp file after failed upload: {actualFilePath}");
                    }
                    catch (Exception delEx)
                    {
                        LogFileWriter.LogError($"Failed to delete temp file {actualFilePath}: {delEx.Message}");
                    }
                }

                throw new Exception($"Failed to sync file {relativePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the archive root path for the vault from:
        /// 1. Environment variable LEOAI_PDM_ARCHIVE_ROOT (user override)
        /// 2. Windows Registry (ArchiveServer\Vaults\{vaultName}\ArchiveTable - using ArchiveTable0 path, removing \0 suffix)
        /// 3. Default location
        /// All paths are validated for existence before returning
        /// </summary>
        private static string GetArchiveRootPath(string vaultName)
        {
            try
            {
                // Option 1: Check environment variable first (allows user override for non-standard installs)
                string envOverride = Environment.GetEnvironmentVariable("LEOAI_PDM_ARCHIVE_ROOT");
                if (!string.IsNullOrEmpty(envOverride))
                {
                    string envPath = Path.Combine(envOverride, vaultName);
                    if (Directory.Exists(envPath))
                    {
                        LogFileWriter.LogMessage($"Archive root from environment variable: {envPath}");
                        return envPath;
                    }
                    else
                    {
                        LogFileWriter.LogMessage($"Environment variable path does not exist: {envPath}, trying next option");
                    }
                }

                // Option 2: Try reading from registry: ArchiveServer\Vaults\{vaultName}\ArchiveTable
                string registryPath = $@"SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise\ArchiveServer\Vaults\{vaultName}\ArchiveTable";
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        // Try ArchiveTable0 first (most common)
                        object archiveTable0 = key.GetValue("ArchiveTable0");
                        if (archiveTable0 != null)
                        {
                            string archiveTablePath = archiveTable0.ToString();
                            // Remove the \0 suffix to get the base archive path
                            string archivePath = archiveTablePath.TrimEnd('\\', '0');

                            if (Directory.Exists(archivePath))
                            {
                                LogFileWriter.LogMessage($"Archive root from registry ArchiveTable0: {archivePath}");
                                return archivePath;
                            }
                            else
                            {
                                LogFileWriter.LogMessage($"Registry ArchiveTable0 path does not exist: {archivePath}, trying next option");
                            }
                        }
                    }
                }

                // Option 3: Fallback to default location
                string defaultPath = $@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS PDM\Data\{vaultName}";
                if (Directory.Exists(defaultPath))
                {
                    LogFileWriter.LogMessage($"Using default archive path: {defaultPath}");
                    return defaultPath;
                }
                else
                {
                    LogFileWriter.LogMessage($"Default path does not exist: {defaultPath}");
                    LogFileWriter.LogMessage($"WARNING: No valid archive path found. Files will be copied using GetFileCopy.");
                    return null; // Return null to indicate no archive path found - will fall back to GetFileCopy
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error reading archive path from registry: {ex.Message}");
                return null; // Return null to fall back to GetFileCopy
            }
        }

        /// <summary>
        /// Constructs the actual archive file path based on File ID and Version.
        /// Archive structure: {ArchiveRoot}\{LastHexDigit}\{FileID_8DigitHex}\{Version_8DigitHex}.{Extension}
        /// Example: test_pro\0\00000010\00000002.SLDPRT for FileID 16 version 2
        /// </summary>
        private static string GetArchiveFilePath(IEdmVault11 vault, IEdmFile5 file, string logicalFilePath)
        {
            try
            {
                string archiveRoot = GetArchiveRootPath(vault.Name);
                if (string.IsNullOrEmpty(archiveRoot))
                {
                    LogFileWriter.LogMessage("No archive root found, cannot construct archive path");
                    return null;
                }

                int fileID = file.ID;
                int currentVersion = file.CurrentVersion;

                string hexID = fileID.ToString("X8"); // Convert to 8-digit hexadecimal (padded with zeros)
                string hexVersion = currentVersion.ToString("X8"); // Version as 8-digit hex
                string lastHexDigit = hexID.Substring(hexID.Length - 1); // Last hex digit determines subfolder
                string extension = Path.GetExtension(logicalFilePath);

                // Structure: {ArchiveRoot}\{LastHexDigit}\{FileID_8DigitHex}\{Version_8DigitHex}.{Extension}
                string archivePath = Path.Combine(archiveRoot, lastHexDigit, hexID, $"{hexVersion}{extension}");

                LogFileWriter.LogMessage($"Archive path construction:");
                LogFileWriter.LogMessage($"  Logical file: {logicalFilePath}");
                LogFileWriter.LogMessage($"  File ID: {fileID} (hex: {hexID})");
                LogFileWriter.LogMessage($"  Current version: {currentVersion} (hex: {hexVersion})");
                LogFileWriter.LogMessage($"  Archive root: {archiveRoot}");
                LogFileWriter.LogMessage($"  Constructed path: {archivePath}");
                LogFileWriter.LogMessage($"  File exists: {File.Exists(archivePath)}");

                return archivePath;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to construct archive path: {ex.Message}");
                LogFileWriter.LogError($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Gets the best available file path for reading - tries archive path first, falls back to GetFileCopy if needed.
        /// Returns: (actualFilePath, needsCleanup)
        /// </summary>
        private static (string filePath, bool needsCleanup) GetReadableFilePath(IEdmVault11 vault, string logicalPath, int folderID, bool tryArchiveFirst = false)
        {
            try
            {
                // Ensure we have full path for GetFileFromPath
                string fullPath = EnsureFullPath(vault, logicalPath);

                // Get file object from vault
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);

                if (file == null)
                {
                    LogFileWriter.LogWarning($"File not found in vault (skipping): {logicalPath}");
                    LogFileWriter.LogDebug($"  This may be a configuration-specific, analysis, or temporary file (e.g., with ^ character)");
                    // Return null to signal caller to skip this file
                    return (null, false);
                }

                if (folder == null)
                {
                    LogFileWriter.LogWarning($"Could not find folder for file in vault (skipping): {logicalPath}");
                    return (null, false);
                }

                // Use the folder ID from the file object, not the parameter
                // This ensures we have the correct folder for renamed/moved files
                int actualFolderID = folder.ID;

                if (tryArchiveFirst)
                {
                    // COMPLETE SYNC MODE: Try archive path first (best option - no copy needed)
                    try
                    {
                        string archivePath = GetArchiveFilePath(vault, file, logicalPath);
                        if (archivePath != null && File.Exists(archivePath))
                        {
                            LogFileWriter.LogMessage($"[ARCHIVE-FIRST] File found in archive: {archivePath}");
                            return (archivePath, false); // No cleanup needed
                        }
                    }
                    catch (Exception archEx)
                    {
                        LogFileWriter.LogError($"[ARCHIVE-FIRST] Failed to access archive for {logicalPath}: {archEx.Message}");
                        // Continue to GetFileCopy fallback
                    }

                    LogFileWriter.LogMessage($"[ARCHIVE-FIRST] File not in archive, using GetFileCopy to temp location");
                }
                else
                {
                    // EVENT MODE: Try local view first
                    try
                    {
                        string localPath = file.GetLocalPath(actualFolderID);
                        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                        {
                            LogFileWriter.LogMessage($"[LOCAL-FIRST] File found in local view: {localPath}");
                            return (localPath, false); // No cleanup needed
                        }
                    }
                    catch (Exception localEx)
                    {
                        LogFileWriter.LogError($"[LOCAL-FIRST] Failed to access local view for {logicalPath}: {localEx.Message}");
                        // Continue to GetFileCopy fallback
                    }

                    LogFileWriter.LogMessage($"[LOCAL-FIRST] File not in local view, using GetFileCopy to temp location");
                }

                // Fallback: Copy file to temp location using PDM API
                // This is the most robust method as it works even with corrupted metadata

                // Create a unique temp subdirectory for this file to avoid naming conflicts
                // Try multiple temp directory locations for robustness
                string uniqueTempDir = null;

                    try
                    {
                        // Option 1: System drive root (C:\Temp\LeoAI_PDM\{GUID}) - no spaces, short path
                        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory); // Usually C:\
                        string tempBase1 = Path.Combine(systemDrive, "Temp", "LeoAI_PDM");

                        if (!Directory.Exists(tempBase1))
                        {
                            Directory.CreateDirectory(tempBase1);
                        }

                        // Use "N" format GUID (no hyphens) for maximum compatibility
                        uniqueTempDir = Path.Combine(tempBase1, Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(uniqueTempDir);

                        LogFileWriter.LogDebug($"Using system temp directory: {uniqueTempDir}");
                    }
                    catch (Exception ex1)
                    {
                        LogFileWriter.LogWarning($"Failed to create system temp directory: {ex1.Message}");

                        try
                        {
                            // Option 2: User temp directory (current behavior fallback)
                            string tempBase2 = Path.Combine(Path.GetTempPath(), "LeoAI_PDM");
                            if (!Directory.Exists(tempBase2))
                            {
                                Directory.CreateDirectory(tempBase2);
                            }

                            uniqueTempDir = Path.Combine(tempBase2, Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(uniqueTempDir);

                            LogFileWriter.LogDebug($"Using user temp directory: {uniqueTempDir}");
                        }
                        catch (Exception ex2)
                        {
                            LogFileWriter.LogError($"All temp directory creation attempts failed:");
                            LogFileWriter.LogError($"  System temp error: {ex1.Message}");
                            LogFileWriter.LogError($"  User temp error: {ex2.Message}");

                            SentryErrorHandler.CaptureException(ex2, new Dictionary<string, string>
                            {
                                { "operation", "CreateTempDirectory" },
                                { "system_temp_error", ex1.Message },
                                { "user_temp_path", Path.GetTempPath() },
                                { "file_being_processed", logicalPath },
                                { "file_id", file.ID.ToString() }
                            });

                            throw new Exception($"Cannot create temp directory for GetFileCopy: {ex2.Message}", ex2);
                        }
                    }

                    LogFileWriter.LogMessage($"GetFileCopy for '{logicalPath}' - created temp dir: '{uniqueTempDir}'");

                    // GetFileCopy signature: GetFileCopy(int hwnd, ref object version, ref object pathOrFolderID, int flags, string newName)
                    // Folder paths must be terminated by a backslash (per PDM API documentation)
                    string folderPathWithBackslash = uniqueTempDir.TrimEnd('\\') + "\\";
                    LogFileWriter.LogMessage($"GetFileCopy for '{logicalPath}' - calling with folder path: '{folderPathWithBackslash}'");
                    LogFileWriter.LogDebug($"  File.ID: {file.ID}, File.CurrentVersion: {file.CurrentVersion}");

                    // Parameters: version (0 for latest), path or folder ID (path with backslash), flags, new name (empty)
                    object version = 0;
                    object folderPath = folderPathWithBackslash;

                    try
                    {
                        file.GetFileCopy(0, ref version, ref folderPath, (int)EdmGetCmdFlags.Egcf_Nothing, "");
                    }
                    catch (System.Runtime.InteropServices.COMException comEx)
                    {
                        LogFileWriter.LogError($"GetFileCopy COM error: HRESULT 0x{comEx.ErrorCode:X8}, Message: {comEx.Message}");
                        LogFileWriter.LogError($"  File ID: {file.ID}, Version: {file.CurrentVersion}");
                        LogFileWriter.LogError($"  Temp folder: {folderPathWithBackslash}");

                        SentryErrorHandler.CaptureException(comEx, new Dictionary<string, string>
                        {
                            { "operation", "GetFileCopy" },
                            { "file_path", logicalPath },
                            { "file_id", file.ID.ToString() },
                            { "version", file.CurrentVersion.ToString() },
                            { "temp_folder", folderPathWithBackslash },
                            { "hresult", $"0x{comEx.ErrorCode:X8}" }
                        });

                        throw;
                    }

                    // File will be created with its original name in the temp directory
                    string fileName = Path.GetFileName(logicalPath);
                    string tempFilePath = Path.Combine(uniqueTempDir, fileName);
                    LogFileWriter.LogMessage($"GetFileCopy succeeded - checking for file at: '{tempFilePath}'");

                    if (!File.Exists(tempFilePath))
                    {
                        throw new Exception($"GetFileCopy succeeded but temp file not found: {tempFilePath}");
                    }

                LogFileWriter.LogMessage($"Copied to temp: {tempFilePath}");
                return (tempFilePath, true); // Cleanup needed
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to get readable file path for {logicalPath}: {ex.Message}");

                // Capture generic error if not already captured
                if (!(ex is System.Runtime.InteropServices.COMException))
                {
                    var errorContext = new Dictionary<string, string>
                    {
                        { "operation", "GetReadableFilePath" },
                        { "file_path", logicalPath },
                        { "folder_id", folderID.ToString() },
                        { "error_type", ex.GetType().Name }
                    };

                    SentryErrorHandler.CaptureException(ex, errorContext);
                }

                throw;
            }
        }

        /// <summary>
        /// Safely deletes a temporary file
        /// </summary>
        private static void DeleteTempFile(string tempFilePath)
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                    LogFileWriter.LogMessage($"Deleted temp file: {tempFilePath}");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage($"Warning: Could not delete temp file {tempFilePath}: {ex.Message}");
                // Don't throw - temp file cleanup failure is not critical
            }
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relative.Replace(Path.DirectorySeparatorChar, '/');
            }
            return fullPath.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Checks if a file exists in the PDM vault (not in local view, but in vault archive/database)
        /// This is safer than File.Exists() which only checks local view
        /// Handles both full paths and relative paths/filenames by constructing full path if needed
        /// </summary>
        private static bool FileExistsInVault(IEdmVault11 vault, string filePath)
        {
            try
            {
                string fullPath = filePath;

                // If filePath is not absolute (just filename or relative path), construct full path
                if (!Path.IsPathRooted(filePath))
                {
                    fullPath = Path.Combine(vault.RootFolderPath, filePath);
                    LogFileWriter.LogMessage($"Constructed full path: {fullPath} from relative path: {filePath}");
                }

                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(fullPath, out folder);
                bool exists = file != null;

                if (exists)
                {
                    LogFileWriter.LogMessage($"File exists in vault: {fullPath} (ID: {file.ID})");
                }
                else
                {
                    LogFileWriter.LogMessage($"File not found in vault: {fullPath}");
                }

                return exists;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error checking file existence in vault for {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensures a file path is absolute (fully qualified) and uses the task host's vault root
        /// Handles paths from different vault views (client local view vs task host local view)
        /// If the path is relative, constructs full path by combining with task host's vault root
        /// If the path is absolute but from a different vault view, extracts relative portion and recombines with task host's vault root
        /// </summary>
        private static string EnsureFullPath(IEdmVault11 vault, string filePath)
        {
            string taskHostVaultRoot = vault.RootFolderPath;

            // If path is not rooted, it's relative - just combine with vault root
            if (!Path.IsPathRooted(filePath))
            {
                string fullPath = Path.Combine(taskHostVaultRoot, filePath);
                LogFileWriter.LogMessage($"EnsureFullPath: Converted relative path '{filePath}' to absolute path '{fullPath}'");
                return fullPath;
            }

            // Path is absolute - check if it starts with task host's vault root
            if (filePath.StartsWith(taskHostVaultRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Path already uses task host's vault root - return as-is
                LogFileWriter.LogMessage($"EnsureFullPath: Path already uses task host vault root: '{filePath}'");
                return filePath;
            }

            // Path is absolute but uses different vault root (e.g., from client's local view)
            // Need to extract the vault-relative portion and recombine with task host's vault root
            LogFileWriter.LogMessage($"EnsureFullPath: Path uses different vault root: '{filePath}', task host root: '{taskHostVaultRoot}'");

            // Try to find vault-relative path by looking for common vault structure
            // Strategy: Use PDM API to get file by path, then reconstruct using task host's vault root
            try
            {
                // Get file from vault using the provided path
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(filePath, out folder);

                if (file != null && folder != null)
                {
                    // Success - construct path using task host's vault root
                    // NOTE: Don't use GetLocalPath() here - it returns null for files not in local view
                    // Instead, use folder.LocalPath + file.Name which works for all files
                    string taskHostPath = System.IO.Path.Combine(folder.LocalPath, file.Name);
                    LogFileWriter.LogMessage($"EnsureFullPath: Resolved via PDM API to task host path: '{taskHostPath}'");
                    return taskHostPath;
                }
                else
                {
                    LogFileWriter.LogMessage($"EnsureFullPath: PDM API returned null - trying path extraction fallback");
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogMessage($"EnsureFullPath: PDM API lookup failed: {ex.Message} - trying path extraction fallback");
            }

            // Fallback: Extract vault-relative path by removing known vault root patterns
            // Common pattern: C:\test_pro\ or C:\Users\...\test_pro\
            // We need to find where the vault name portion starts
            string vaultName = vault.Name;
            int vaultNameIndex = filePath.IndexOf(vaultName, StringComparison.OrdinalIgnoreCase);

            if (vaultNameIndex >= 0)
            {
                // Extract everything after "vaultName\"
                int relativeStartIndex = vaultNameIndex + vaultName.Length;
                if (relativeStartIndex < filePath.Length && (filePath[relativeStartIndex] == '\\' || filePath[relativeStartIndex] == '/'))
                {
                    relativeStartIndex++; // Skip the separator
                }

                if (relativeStartIndex < filePath.Length)
                {
                    string relativePath = filePath.Substring(relativeStartIndex);
                    string reconstructedPath = Path.Combine(taskHostVaultRoot, relativePath);
                    LogFileWriter.LogMessage($"EnsureFullPath: Extracted relative path '{relativePath}', reconstructed as '{reconstructedPath}'");
                    return reconstructedPath;
                }
            }

            // Last resort: use path as-is and hope for the best
            LogFileWriter.LogMessage($"EnsureFullPath: Could not normalize path - using as-is: '{filePath}'");
            return filePath;
        }

        /// <summary>
        /// Checks if the task has been cancelled by the user
        /// </summary>
        private static bool IsTaskCancelled(IEdmTaskInstance taskInstance)
        {
            if (taskInstance == null)
                return false;

            try
            {
                // Check task status - user clicked cancel or task is already cancelled
                EdmTaskStatus status = taskInstance.GetStatus();
                return status == EdmTaskStatus.EdmTaskStat_CancelPending ||
                       status == EdmTaskStatus.EdmTaskStat_DoneCancelled;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to check task cancellation status: {ex.Message}");
                return false; // Assume not cancelled if we can't check
            }
        }

        /// <summary>
        /// Gets unique identifier for a PDM file based on FileID and Version
        /// Replaces checksum-based identification with stable PDM metadata
        /// </summary>
        /// <param name="vault">PDM vault instance</param>
        /// <param name="filePath">Full path to file in vault</param>
        /// <returns>LeoFileInformation with UniqueId in format "fileID_version"</returns>
        private static LeoFileInfo.LeoFileInformation GetUniqueIdentifier(IEdmVault11 vault, string filePath)
        {
            try
            {
                IEdmFolder5 folder;
                IEdmFile5 file = vault.GetFileFromPath(filePath, out folder);

                if (file == null)
                {
                    throw new FileNotFoundException($"File not found in vault: {filePath}");
                }

                // Build unique identifier from PDM's stable FileID and Version
                string uniqueId = $"{file.ID}_{file.CurrentVersion}";

                LogFileWriter.LogDebug($"Unique identifier for {Path.GetFileName(filePath)}: {uniqueId}");

                return new LeoFileInfo.LeoFileInformation
                {
                    CheckSum = uniqueId  // Reuse CheckSum field to store FileID_Version
                };
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to get unique identifier for {filePath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates relative path from assembly file to dependency file
        /// Both paths should be vault-root-relative initially
        /// </summary>
        /// <param name="assemblyVaultRelPath">Assembly path relative to vault root (e.g., "folder1/folder2/assembly.sldasm")</param>
        /// <param name="dependencyVaultRelPath">Dependency path relative to vault root (e.g., "folder1/part.sldprt")</param>
        /// <returns>Relative path from assembly to dependency (e.g., "../part.sldprt")</returns>
        private static string CalculateRelativePath(string assemblyVaultRelPath, string dependencyVaultRelPath)
        {
            // Normalize separators to forward slash and trim leading slashes
            string normAssembly = assemblyVaultRelPath.Replace('\\', '/').TrimStart('/');
            string normDependency = dependencyVaultRelPath.Replace('\\', '/').TrimStart('/');

            string[] assemblyParts = normAssembly.Split('/');
            string[] dependencyParts = normDependency.Split('/');

            // Find common ancestor folder depth (exclude filename from comparison)
            int commonDepth = 0;
            int maxCheck = Math.Min(assemblyParts.Length - 1, dependencyParts.Length - 1);

            for (int i = 0; i < maxCheck; i++)
            {
                if (assemblyParts[i].Equals(dependencyParts[i], StringComparison.OrdinalIgnoreCase))
                    commonDepth++;
                else
                    break;
            }

            // Calculate how many levels up from assembly directory to common ancestor
            int levelsUp = (assemblyParts.Length - 1) - commonDepth;

            // Build relative path
            List<string> pathParts = new List<string>();

            // Add ".." for each level up
            for (int i = 0; i < levelsUp; i++)
            {
                pathParts.Add("..");
            }

            // Add path segments from common ancestor to dependency
            for (int i = commonDepth; i < dependencyParts.Length; i++)
            {
                pathParts.Add(dependencyParts[i]);
            }

            string result = string.Join("/", pathParts);
            LogFileWriter.LogDebug($"Relative path from {Path.GetFileName(assemblyVaultRelPath)} to {Path.GetFileName(dependencyVaultRelPath)}: {result}");

            return result;
        }

        #endregion
    }
}
