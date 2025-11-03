using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EPDM.Interop.epdm;
using LeoAICadDataClient;

namespace LeoAISwPdmAddIn
{
    internal class SolidWorksPdmHelper
    {
        private IEdmVault5 swPdmVault;

        // Use ConcurrentBag for thread-safe parallel operations
        public List<FileData> FilesInfo { get; set; }

        public SolidWorksPdmHelper(IEdmVault5 edmVault)
        {
            swPdmVault = edmVault;
        }

        public List<ChildData> GetReferencedFiles(IEdmReference10 Reference, string ProjectName = "")
        {
            List<ChildData> referencedFiles = new List<ChildData>();
            try
            {
                // For now, we'll skip the reference processing as the API methods are not available
                // in the current version of the PDM API. This functionality can be added later
                // when the correct API methods are identified.
                LogFileWriter.LogMessage("Reference processing skipped - API methods not available in current PDM version");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error getting referenced files: {ex.Message}");
            }
            return referencedFiles;
        }

        private bool IsProcessableFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".sldprt" ||
                   extension == ".sldasm" ||
                   extension == ".step" ||
                   extension == ".stp" ||
                   extension == ".prt" ||
                   extension == ".asm" ||
                   extension == ".ipt" ||
                   extension == ".iam" ||
                   extension == ".x_t" ||
                   extension == ".xt" ||
                   extension == ".txt" ||
                   extension == ".pdf" ||
                   extension == ".doc" ||
                   extension == ".docx";
        }

        public string GetDocType(string swFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(swFilePath))
                    return "UNKNOWN";

                string extension = Path.GetExtension(swFilePath).ToLower();
                switch (extension)
                {
                    case ".sldprt":
                        return "PART";
                    case ".sldasm":
                        return "ASSEMBLY";
                    case ".slddrw":
                        return "DRAWING";
                    case ".step":
                    case ".stp":
                        return "PART";
                default:
                        return "DOCUMENT";
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error determining document type for {swFilePath}: {ex.Message}");
                return "UNKNOWN";
        }
        }

        public bool ProcessFolders(IEdmVault5 edmVault)
        {
            try
            {
                FilesInfo = new List<FileData>();
                IEdmFolder5 rootFolder = edmVault.RootFolder;

                // Step 1: Collect all folders (must be sequential due to COM threading)
                List<IEdmFolder5> allFolders = new List<IEdmFolder5>();
                CollectAllFolders(rootFolder, allFolders);
                LogFileWriter.LogMessage($"Found {allFolders.Count} folders to process");

                // Step 2: Process folders in parallel
                ConcurrentBag<FileData> concurrentResults = new ConcurrentBag<FileData>();

                Parallel.ForEach(allFolders, folder =>
                {
                    try
                    {
                        ProcessSingleFolder(folder, edmVault, concurrentResults);
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Error processing folder in parallel: {ex.Message}");
                    }
                });

                // Convert ConcurrentBag to List
                FilesInfo = concurrentResults.ToList();
                LogFileWriter.LogMessage($"Processed {FilesInfo.Count} files from {allFolders.Count} folders");

                return true;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error processing folders: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Recursively collect all folders (must run on main thread due to COM)
        /// </summary>
        private void CollectAllFolders(IEdmFolder5 folder, List<IEdmFolder5> folderList)
        {
            try
            {
                // Skip LeoAI_TaskData folder (contains auth configuration - should never be synced)
                string folderPath = folder.LocalPath;
                if (!string.IsNullOrEmpty(folderPath) && folderPath.Contains("\\LeoAI_TaskData"))
                {
                    LogFileWriter.LogDebug($"Skipping metadata folder: {folderPath}");
                    return; // Don't add this folder or recurse into it
                }

                folderList.Add(folder);

                IEdmPos5 subFolderPos = folder.GetFirstSubFolderPosition();
                while (!subFolderPos.IsNull)
                {
                    IEdmFolder5 subFolder = folder.GetNextSubFolder(subFolderPos);
                    CollectAllFolders(subFolder, folderList);
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error collecting folders: {ex.Message}");
            }
        }

        /// <summary>
        /// Process files in a single folder (thread-safe, can run in parallel)
        /// </summary>
        private void ProcessSingleFolder(IEdmFolder5 folder, IEdmVault5 vault, ConcurrentBag<FileData> results)
        {
            try
            {
                // Step 1: Collect all file references sequentially (COM requirement)
                List<Tuple<IEdmFile5, string>> filesToProcess = new List<Tuple<IEdmFile5, string>>();
                int totalFilesInFolder = 0;
                int nullPathCount = 0;
                int filteredOutCount = 0;

                // Get folder's vault logical path for constructing file paths
                string folderPath = folder.LocalPath;

                IEdmPos5 pos = folder.GetFirstFilePosition();
                while (!pos.IsNull)
                {
                    IEdmFile5 file = folder.GetNextFile(pos);
                    totalFilesInFolder++;

                    // CRITICAL FIX: Construct vault logical path instead of using GetLocalPath()
                    // GetLocalPath() returns null for files not in local view (most files on task host)
                    // This was causing 95%+ of files to be skipped!
                    string filePath = null;
                    if (!string.IsNullOrEmpty(folderPath) && !string.IsNullOrEmpty(file.Name))
                    {
                        filePath = System.IO.Path.Combine(folderPath, file.Name);
                    }

                    if (string.IsNullOrEmpty(filePath))
                    {
                        nullPathCount++;
                        LogFileWriter.LogWarning($"Skipping file with null path in folder: {folderPath ?? "unknown"}");
                        continue;
                    }

                    if (!IsProcessableFile(filePath))
                    {
                        filteredOutCount++;
                        continue;
                    }

                    filesToProcess.Add(Tuple.Create(file, filePath));
                }

                // Log enumeration statistics for this folder
                if (totalFilesInFolder > 0)
                {
                    LogFileWriter.LogDebug($"Folder '{folderPath}': {totalFilesInFolder} total files, {nullPathCount} null paths, {filteredOutCount} filtered out, {filesToProcess.Count} to process");
                }

                // Step 2: Process all files in parallel
                Parallel.ForEach(filesToProcess, fileInfo =>
                {
                    try
                    {
                        IEdmFile5 file = fileInfo.Item1;
                        string filePath = fileInfo.Item2;

                        var fileData = new FileData
                        {
                            file = filePath,
                            mimeType = LeoAIMemeType.GetMemeType(filePath),
                            checkSum = null,
                            children = new List<ChildData>()
                        };

                        if (GetDocType(filePath).ToUpper() == "ASSEMBLY")
                        {
                            try
                            {
                                IEdmReference10 reference = file as IEdmReference10;
                                if (reference != null)
                                {
                                    fileData.children = GetReferencedFiles(reference);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogFileWriter.LogError($"Error getting references for {file.Name}: {ex.Message}");
                            }
                        }

                        results.Add(fileData);
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Error processing individual file: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error processing folder files: {ex.Message}");
            }
        }
    }
}
