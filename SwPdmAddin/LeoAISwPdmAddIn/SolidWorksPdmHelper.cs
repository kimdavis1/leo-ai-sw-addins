using System;
using System.Collections.Generic;
using System.IO;
using EPDM.Interop.epdm;
using LeoAICadDataClient;

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
                        // Store file with null checksum - will be calculated later by caller if needed
                        // This is because checksum calculation requires archive access which
                        // the helper class doesn't have (it's in LeoAiSyncTask)
                        var fileData = new FileData
                        {
                            file = filePath,
                            mimeType = LeoAIMemeType.GetMemeType(filePath),
                            checkSum = null, // Will be calculated by caller using archive-first approach
                            children = new List<ChildData>()
                        };

                        if (GetDocType(filePath).ToUpper() == "ASSEMBLY")
                        {
                            try
                            {
                                // Use as operator for safer casting - returns null if interface not supported
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
