using System;
using System.IO;
using System.IO.Hashing;

namespace LeoAICadDataClient
{
	public class LeoFileInfo
	{
		// This should match the UNIVERSAL_FILE_HASHING_SEED from your Electron app
		private const long UNIVERSAL_FILE_HASHING_SEED = 0; // Replace with your actual seed value

		// Buffer size for streaming file reads (80KB - good balance between memory and performance)
		private const int STREAM_BUFFER_SIZE = 81920;

		/// <summary>
		/// Gets file info with checksum calculated by streaming (memory-efficient for large files).
		/// Use this for CompleteSync operations to avoid loading entire files into memory.
		/// </summary>
		public static LeoFileInformation GetFileInfoStreaming(string filePath)
		{
			// Compute checksum by streaming file
			string checkSum = ComputeXXHash64HexStreaming(filePath);

			// For streaming mode, we don't load the entire file into memory for Base64
			// Base64 encoding will be done separately when needed for upload
			LeoFileInformation info = new LeoFileInformation()
			{
				CheckSum = checkSum,
				Base64EncodedFile = null  // Not loaded in streaming mode
			};

			return info;
		}

		/// <summary>
		/// Original method - loads entire file into memory.
		/// Use this for event-based operations where we need the Base64 content immediately.
		/// </summary>
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

		/// <summary>
		/// Compute xxHash64 by streaming file in chunks (memory-efficient).
		/// </summary>
		static string ComputeXXHash64HexStreaming(string filePath)
		{
			using (var stream = File.OpenRead(filePath))
			{
				var xxHash = new XxHash64(UNIVERSAL_FILE_HASHING_SEED);
				byte[] buffer = new byte[STREAM_BUFFER_SIZE];
				int bytesRead;

				while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					xxHash.Append(buffer.AsSpan(0, bytesRead));
				}

				byte[] hash = xxHash.GetHashAndReset();

				// Convert 8-byte array to 64-bit number, then to hex (like JavaScript .toString(16))
				ulong hashValue = BitConverter.ToUInt64(hash, 0);
				string result = hashValue.ToString("x"); // "x" for lowercase hex, equivalent to .toString(16)

				return result;
			}
		}

		/// <summary>
		/// Original method - computes hash from byte array in memory.
		/// </summary>
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
