using Newtonsoft.Json;
using sw_addin.Logs;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace sw_addin
{
	internal class LeoWebServerListener
	{
		private HttpListener listener;
		private Thread listenerThread;
		private SwHelper solidWorksHelper;
		private const string LOCALHOST_RECIEVER_URL = "http://localhost:4100/";

		public LeoWebServerListener(SwHelper solidWorksHelper)
		{
			this.solidWorksHelper = solidWorksHelper;
			LogFileWriter.Write($"Leo AI : LOCALHOST_RECIEVER_URL  :{LOCALHOST_RECIEVER_URL} ");
		}

		/// <summary>
		/// Starts the Http Listner to look for the specified
		/// </summary>
		public void StartListener()
		{
			listener = new HttpListener();
			listener.Prefixes.Add(LOCALHOST_RECIEVER_URL);
			//start the listener on a separate thread
			listenerThread = new Thread(new ThreadStart(HandleIncomingConnections));
			listenerThread.IsBackground = true; // Make sure this thread will not block SolidWorks from closing
			listenerThread.Start();
			LogFileWriter.Write($"Leo AI : Listerner started in the background. ");
		}

		/// <summary>
		/// Looks for the incoming data to the specified local port
		/// </summary>
		private void HandleIncomingConnections()
		{
			try
			{
				LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Starting listener on thread: {Thread.CurrentThread.ManagedThreadId}");
				listener.Start();
				
				while (true)
				{
					try
					{
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Waiting for request...");
						// Wait for a request
						HttpListenerContext context = listener.GetContext();
						HttpListenerRequest request = context.Request;
						HttpListenerResponse response = context.Response;

						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Request received. Method: {request.HttpMethod}, URL: {request.Url}");

						// Read  the incoming data
						string receivedData = new System.IO.StreamReader(request.InputStream, request.ContentEncoding).ReadToEnd();
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Received data length: {receivedData?.Length ?? 0}");
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Received data content: {receivedData}");

						//Conver Json to class
						FileDownloadInfo fileDownloadInfo = null;
						try
						{
							fileDownloadInfo = JsonConvert.DeserializeObject<FileDownloadInfo>(receivedData);
							LogFileWriter.Write($"Leo AI : HandleIncomingConnections - JSON deserialized successfully");
						}
						catch (Exception jsonEx)
						{
							LogFileWriter.Write($"Leo AI : HandleIncomingConnections - JSON deserialization failed: {jsonEx.Message}");
							throw;
						}

						if (fileDownloadInfo == null)
						{
							LogFileWriter.Write($"Leo AI : HandleIncomingConnections - FileDownloadInfo is null after deserialization");
						}
						else
						{
							LogFileWriter.Write($"Leo AI : HandleIncomingConnections - FileDownloadInfo properties:");
							LogFileWriter.Write($"Leo AI :   - DownloadPath: {fileDownloadInfo.DownloadPath ?? "null"}");
							LogFileWriter.Write($"Leo AI :   - PlaceLikeComponentTitle: {fileDownloadInfo.PlaceLikeComponentTitle ?? "null"}");
							LogFileWriter.Write($"Leo AI :   - PlaceLikeFilePath: {fileDownloadInfo.PlaceLikeFilePath ?? "null"}");
							LogFileWriter.Write($"Leo AI :   - ReplaceExisting: {fileDownloadInfo.ReplaceExisting}");
						}

						// Accessing the download path
						string downloadedFilePath = fileDownloadInfo?.DownloadPath;
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Downloaded file path: {downloadedFilePath ?? "null"}");

						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Calling ProcessFile...");
						ProcessFile(fileDownloadInfo);
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - ProcessFile completed");

						// Respond with a confirmation message
						string responseString = "<html><body><h1>Data Received</h1></body></html>";
						byte[] buffer = Encoding.UTF8.GetBytes(responseString);
						response.ContentLength64 = buffer.Length;

						// Write the response
						System.IO.Stream output = response.OutputStream;
						output.Write(buffer, 0, buffer.Length);
						output.Close();
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Response sent successfully");
					}
					catch (Exception requestEx)
					{
						LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Error processing request: {requestEx.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : HandleIncomingConnections - Fatal error in listener loop: {ex.Message}");
			}
		}

		/// <summary>
		/// Process Downloaded file liek open/insert into active model
		/// </summary>
		/// <param name="fileDownloadInfo"></param>
		private void ProcessFile(FileDownloadInfo fileDownloadInfo)
		{
			try
			{
				LogFileWriter.Write($"Leo AI : ProcessFile - START on thread: {Thread.CurrentThread.ManagedThreadId}");
				
				if (fileDownloadInfo == null)
				{
					LogFileWriter.Write($"Leo AI : ProcessFile - ERROR: fileDownloadInfo is null");
					return;
				}

				string downloadedFilePath = fileDownloadInfo.DownloadPath;
				LogFileWriter.Write($"Leo AI : ProcessFile - DownloadPath: {downloadedFilePath ?? "null"}");
				
				LogFileWriter.Write($"Leo AI : ProcessFile - Getting active document type...");
				DocType currentDocType = solidWorksHelper.ActiveDocType();
				LogFileWriter.Write($"Leo AI : ProcessFile - Active document type: {currentDocType}");

				// Check if this is a part or assembly replacement request
				bool hasTitle = !string.IsNullOrEmpty(fileDownloadInfo.PlaceLikeComponentTitle);
				bool hasFilePath = !string.IsNullOrEmpty(fileDownloadInfo.PlaceLikeFilePath);
				bool hasDownloadPath = !string.IsNullOrEmpty(downloadedFilePath);
				bool isSLDPRT = hasDownloadPath && Path.GetExtension(downloadedFilePath).Equals(".SLDPRT", StringComparison.OrdinalIgnoreCase);
				bool isSLDASM = hasDownloadPath && Path.GetExtension(downloadedFilePath).Equals(".SLDASM", StringComparison.OrdinalIgnoreCase);
				bool isAssembly = currentDocType == DocType.Assembly;

				LogFileWriter.Write($"Leo AI : ProcessFile - Replacement check:");
				LogFileWriter.Write($"Leo AI :   - Has PlaceLikeComponentTitle: {hasTitle} ({fileDownloadInfo.PlaceLikeComponentTitle ?? "null"})");
				LogFileWriter.Write($"Leo AI :   - Has PlaceLikeFilePath: {hasFilePath} ({fileDownloadInfo.PlaceLikeFilePath ?? "null"})");
				LogFileWriter.Write($"Leo AI :   - Has DownloadPath: {hasDownloadPath}");
				LogFileWriter.Write($"Leo AI :   - Is .SLDPRT file: {isSLDPRT}");
				LogFileWriter.Write($"Leo AI :   - Is .SLDASM file: {isSLDASM}");
				LogFileWriter.Write($"Leo AI :   - Is Assembly document: {isAssembly}");

				bool isPartReplacement = hasTitle && hasFilePath && hasDownloadPath && isSLDPRT && isAssembly;
				bool isAssemblyReplacement = hasTitle && hasFilePath && hasDownloadPath && isSLDASM && isAssembly;
				LogFileWriter.Write($"Leo AI : ProcessFile - Is part replacement: {isPartReplacement}");
				LogFileWriter.Write($"Leo AI : ProcessFile - Is assembly replacement: {isAssemblyReplacement}");

				if (isAssemblyReplacement)
				{
					LogFileWriter.Write($"Leo AI : ProcessFile - ASSEMBLY REPLACEMENT REQUEST DETECTED");
					
					bool replaceExisting = fileDownloadInfo.ReplaceExisting;
					LogFileWriter.Write($"Leo AI : ProcessFile - ReplaceExisting: {replaceExisting}");
					
					LogFileWriter.Write($"Leo AI : ProcessFile - Calling ReplaceAssemblyInAssembly...");
					
					try
					{
						bool result = solidWorksHelper.ReplaceAssemblyInAssembly(
							fileDownloadInfo.PlaceLikeComponentTitle,
							fileDownloadInfo.PlaceLikeFilePath,
							downloadedFilePath,
							replaceExisting);
						LogFileWriter.Write($"Leo AI : ProcessFile - ReplaceAssemblyInAssembly returned: {result}");
					}
					catch (Exception replaceEx)
					{
						LogFileWriter.Write($"Leo AI : ProcessFile - CRASH in ReplaceAssemblyInAssembly: {replaceEx.Message}");
						throw;
					}
				}
				else if (isPartReplacement)
				{
					LogFileWriter.Write($"Leo AI : ProcessFile - PART REPLACEMENT REQUEST DETECTED");
					
					// Get replaceExisting flag (defaults to false if not specified)
					bool replaceExisting = fileDownloadInfo.ReplaceExisting;
					LogFileWriter.Write($"Leo AI : ProcessFile - ReplaceExisting: {replaceExisting}");
					
					LogFileWriter.Write($"Leo AI : ProcessFile - Calling ReplacePartInAssembly...");
					
					try
					{
						bool result = solidWorksHelper.ReplacePartInAssembly(
							fileDownloadInfo.PlaceLikeComponentTitle,
							fileDownloadInfo.PlaceLikeFilePath,
							downloadedFilePath,
							replaceExisting);
						LogFileWriter.Write($"Leo AI : ProcessFile - ReplacePartInAssembly returned: {result}");
					}
					catch (Exception replaceEx)
					{
						LogFileWriter.Write($"Leo AI : ProcessFile - CRASH in ReplacePartInAssembly: {replaceEx.Message}");
						throw;
					}
				}
				else if (solidWorksHelper.IsSolidWorksFile(downloadedFilePath))
				{
					LogFileWriter.Write($"Leo AI : ProcessFile - Standard SolidWorks file insertion");
					//Solidworks Native file support (SLDPRT/SLDASM) 
					if (currentDocType == DocType.Part || currentDocType == DocType.Empty)
					{
						LogFileWriter.Write($"Leo AI : ProcessFile - Opening document (Part/Empty context)");
						solidWorksHelper.OpenDocument(downloadedFilePath);
					}
					else if (currentDocType == DocType.Assembly)
					{
						LogFileWriter.Write($"Leo AI : ProcessFile - Inserting into assembly");
						solidWorksHelper.Insert(downloadedFilePath);
					}
				}
				else
				{//None - native file support (Step/STP/IGES)
					LogFileWriter.Write($"Leo AI : ProcessFile - Non-native file insertion");
					solidWorksHelper.OpenInsertComponentDialog(downloadedFilePath);
					// if (currentDocType == DocType.Part || currentDocType == DocType.Empty)
					// {
					// 	LogFileWriter.Write($"Leo AI : ProcessFile - Loading file (Part/Empty context)");
					// 	solidWorksHelper.LoadFile(downloadedFilePath);
					// }
					// else if (currentDocType == DocType.Assembly)
					// {
					// 	LogFileWriter.Write($"Leo AI : ProcessFile - Inserting non-native file into assembly");
					// 	solidWorksHelper.InsertNonNativeFile(downloadedFilePath);
					// }
				}

				LogFileWriter.Write($"Leo AI : ProcessFile - END (successful)");
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : ProcessFile - FATAL ERROR: {ex.Message}");
				throw; // Re-throw to be caught by HandleIncomingConnections
			}
		}
		

		/// <summary>
		/// Stops the Listner
		/// </summary>
		public void StopListener()
		{
			LogFileWriter.Write($"Leo AI : Stopping web server listener.");
			//stop and close the listner
			listener.Stop();
			listener.Close();
			listenerThread.Abort();
		}
	}

	/// <summary>
	/// Downloaded File information
	/// </summary>
	public class FileDownloadInfo
	{
		public string DownloadPath { get; set; }
		public string PlaceLikeComponentTitle { get; set; }
		public string PlaceLikeFilePath { get; set; }
		public bool ReplaceExisting { get; set; } = false;
	}
}
