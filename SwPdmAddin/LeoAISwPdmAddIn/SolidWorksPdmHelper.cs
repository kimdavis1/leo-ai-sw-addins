using System;
using System.Collections.Generic;
using System.IO;
using EPDM.Interop.epdm;
using LeoAICadDataClient;
using LeoAICadDataClient.Utilities;

namespace LeoAISwPdmAddIn
{
    internal class SolidWorksPdmHelper
    {
        private IEdmVault5 swPdmVault;

        public List<FileData> FilesInfo { get; set; }

        public SolidWorksPdmHelper(IEdmVault5 edmVault)
        {
            swPdmVault = edmVault;
        }

        public List<FileData> GetFileStructure(string swFilePath)
        {
            FilesInfo = new List<FileData>();
            try
            {
                string currDocType = GetDocType(swFilePath);
                switch (currDocType.ToUpper())
                {
                    case "ASSEMBLY":
                        GetAssemblyInfo(swFilePath);
                        break;
                    case "PART":
                        GetPartInfo(swFilePath);
                        break;
                    case "DRAWING":
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error getting file structure for {swFilePath}: {ex.Message}");
            }
            return FilesInfo;
        }

        public FileData GetSingleFileData(string swFilePath)
        {
            try
            {
                if (!IsProcessableFile(swFilePath))
                {
                    return null;
                }

                IEdmFolder5 parentFolder;
                IEdmFile5 file = swPdmVault.GetFileFromPath(swFilePath, out parentFolder);
                if (file == null)
                {
                    LogFileWriter.LogError($"Failed to get file from PDM: {swFilePath}");
                    return null;
                }

                var fileData = new FileData
                {
                    file = swFilePath,
                    mimeType = LeoAIMemeType.GetMemeType(swFilePath),
                    children = new List<ChildData>()
                };

                return fileData;
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error getting single file data for {swFilePath}: {ex.Message}");
                return null;
            }
        }

        private void GetPartInfo(string swFilePath)
        {
            try
            {
                if (!IsProcessableFile(swFilePath))
                {
                    return;
                }

                var partFileData = new FileData
                {
                    file = swFilePath,
                    mimeType = LeoAIMemeType.GetMemeType(swFilePath),
                    children = new List<ChildData>()
                };

                FilesInfo.Add(partFileData);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error processing part file {swFilePath}: {ex.Message}");
            }
        }

        private void GetAssemblyInfo(string swFilePath)
        {
            try
            {
                if (!IsProcessableFile(swFilePath))
                {
                    return;
                }

                IEdmFolder5 parentFolder;
                IEdmFile5 file = swPdmVault.GetFileFromPath(swFilePath, out parentFolder);
                if (file == null)
                {
                    LogFileWriter.LogError($"Failed to get assembly file from PDM: {swFilePath}");
                    return;
                }

                var assemblyFileData = new FileData
                {
                    file = swFilePath,
                    mimeType = LeoAIMemeType.GetMemeType(swFilePath),
                    children = new List<ChildData>()
                };

                IEdmReference10 reference = (IEdmReference10)file;
                if (reference != null)
                {
                    assemblyFileData.children = GetReferencedFiles(reference);
                }

                FilesInfo.Add(assemblyFileData);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error processing assembly file {swFilePath}: {ex.Message}");
            }
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
                   extension == ".slddrw" || 
                   extension == ".step" || 
                   extension == ".stp" || 
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
                ListFoldersAndFiles(rootFolder, edmVault);
            return true;
        }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error processing folders: {ex.Message}");
                return false;
            }
        }

        private void ListFoldersAndFiles(IEdmFolder5 folder, IEdmVault5 vault)
        {
            try
            {
                IEdmPos5 pos = folder.GetFirstFilePosition();
                while (!pos.IsNull)
                {
                    IEdmFile5 file = folder.GetNextFile(pos);
                        string filePath = file.GetLocalPath(folder.ID);

                    if (!string.IsNullOrEmpty(filePath) && IsProcessableFile(filePath))
                    {
                        var fileData = new FileData
                        {
                            file = filePath,
                            mimeType = LeoAIMemeType.GetMemeType(filePath),
                            children = new List<ChildData>()
                        };

                        if (GetDocType(filePath).ToUpper() == "ASSEMBLY")
                        {
                            IEdmReference10 reference = (IEdmReference10)file;
                            if (reference != null)
                            {
                                fileData.children = GetReferencedFiles(reference);
                            }
                        }

                        FilesInfo.Add(fileData);
                    }
                        }

                IEdmPos5 subFolderPos = folder.GetFirstSubFolderPosition();
                while (!subFolderPos.IsNull)
                {
                    IEdmFolder5 subFolder = folder.GetNextSubFolder(subFolderPos);
                    ListFoldersAndFiles(subFolder, vault);
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Error listing folder contents: {ex.Message}");
            }
        }
    }
}
