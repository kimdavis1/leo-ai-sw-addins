using System;
using System.IO;
using System.IO.Hashing;

namespace LeoAICadDataClient
{
	public class LeoFileInfo
	{
		/// <summary>
		/// Reads file and encodes it to Base64 for upload
		/// Takes fileID_version as parameter instead of calculating checksum
		/// </summary>
		public static LeoFileInformation GetFileForUpload(string filePath, string fileIdVersion)
		{
			byte[] fileBytes;

			// Read file with FileShare.ReadWrite to allow access even if file is open in SOLIDWORKS
			using (System.IO.FileStream fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
			{
				fileBytes = new byte[fs.Length];
				int totalRead = 0;
				while (totalRead < fileBytes.Length)
				{
					int bytesRead = fs.Read(fileBytes, totalRead, fileBytes.Length - totalRead);
					if (bytesRead == 0)
						throw new System.IO.EndOfStreamException($"Could not read entire file: {filePath}");
					totalRead += bytesRead;
				}
			}

			// Encode file to Base64
			string base64EncodedFile = Convert.ToBase64String(fileBytes);

			return new LeoFileInformation
			{
				CheckSum = fileIdVersion,  // Use provided FileID_Version identifier
				Base64EncodedFile = base64EncodedFile
			};
		}

		/// <summary>
		/// Data class for file information
		/// CheckSum field now stores FileID_Version identifier instead of actual checksum
		/// </summary>
		public class LeoFileInformation
		{
			public string CheckSum { get; set; }  // Now stores "fileID_version" format
			public string Base64EncodedFile { get; set; }
		}
	}

	public class LeoAIMemeType
	{
		public static string GetMemeType(string filePath)
		{
			string fileType = Path.GetExtension(filePath);
			string memeType = string.Empty;
			switch (fileType.ToLower())
			{
				case ".sldprt":
					{
						memeType = "application/x-sldprt";
						break;
					}
				case ".sldasm":
					{
						memeType = "application/x-sldasm";
						break;
					}
				case ".step":
				case ".stp":
					{
						memeType = "model/step";
						break;
					}
				// Creo part files
				case ".prt":
					{
						memeType = "application/x-creo-part";
						break;
					}
				// Creo assembly files
				case ".asm":
					{
						memeType = "application/x-creo-assembly";
						break;
					}
				// Inventor part files
				case ".ipt":
					{
						memeType = "application/vnd.autodesk.inventor.part";
						break;
					}
				// Inventor assembly files
				case ".iam":
					{
						memeType = "application/vnd.autodesk.inventor.assembly";
						break;
					}
				// Parasolid files - MIME type not confirmed by API yet
				case ".x_t":
				case ".xt":
					{
						memeType = "application/x-parasolid";
						break;
					}
				case ".txt":
					{
						memeType = "text/plain";
						break;
					}
				case ".pdf":
					{
						memeType = "application/pdf";
						break;
					}
				case ".doc":
					{
						memeType = "application/msword";
						break;
					}
				case ".docx":
					{
						memeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
						break;
					}
				default:
					{
						memeType = "application/octet-stream";
						break;
					}
			}
			return memeType;
		}
	}
}
