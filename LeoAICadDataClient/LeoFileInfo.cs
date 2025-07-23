using System;
using System.IO;
using System.IO.Hashing;
using LeoAICadDataClient.Utilities;

namespace LeoAICadDataClient
{
	public class LeoFileInfo
	{
		// This should match the UNIVERSAL_FILE_HASHING_SEED from your Electron app
		private const long UNIVERSAL_FILE_HASHING_SEED = 0; // Replace with your actual seed value
		
		public static LeoFileInformation GetFileInfo(string filePath)
		{
			// Read file as byte array
			byte[] fileBytes = File.ReadAllBytes(filePath);

			// Compute xxHash64 checksum with hex output (to match Electron app)
			string checkSum = ComputeXXHash64Hex(fileBytes);

			// Encode file to Base64
			string base64EncodedFile = Convert.ToBase64String(fileBytes);

			LeoFileInformation info = new LeoFileInformation()
			{
				CheckSum = checkSum,
				Base64EncodedFile = base64EncodedFile
			};
		
			return info;
		}

		static string ComputeXXHash64Hex(byte[] data)
		{
			// Use Microsoft's System.IO.Hashing with xxHash64 and custom seed
			var xxHash = new XxHash64(UNIVERSAL_FILE_HASHING_SEED);
			xxHash.Append(data);
			byte[] hash = xxHash.GetHashAndReset();
			
			// Convert 8-byte array to 64-bit number, then to hex (like JavaScript .toString(16))
			ulong hashValue = BitConverter.ToUInt64(hash, 0);
			string result = hashValue.ToString("x"); // "x" for lowercase hex, equivalent to .toString(16)
			
			return result;
		}

		public class LeoFileInformation
		{
			public string CheckSum { get; set; }
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
				// Parasolid files - MIME type not confirmed by API yet
				// case ".x_t":
				// case ".xt":
				// 	{
				// 		memeType = "application/x-parasolid";
				// 		break;
				// 	}
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
