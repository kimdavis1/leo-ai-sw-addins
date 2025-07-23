using Newtonsoft.Json;
using sw_addin.Logs;
using System;
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
			listener.Start();
			while (true)
			{
				// Wait for a request
				HttpListenerContext context = listener.GetContext();
				HttpListenerRequest request = context.Request;
				HttpListenerResponse response = context.Response;

				// Read  the incoming data
				string receivedData = new System.IO.StreamReader(request.InputStream, request.ContentEncoding).ReadToEnd();

				LogFileWriter.Write($"Leo AI :  Listener Response - {receivedData} ");

				//Conver Json to class
				FileDownloadInfo fileDownloadInfo = JsonConvert.DeserializeObject<FileDownloadInfo>(receivedData);
				// Accessing the download path
				string downloadedFilePath = fileDownloadInfo.DownloadPath;

				LogFileWriter.Write($"Leo AI :  Downloaded file path - {downloadedFilePath} ");

				ProcessFile(downloadedFilePath);

				// Respond with a confirmation message
				string responseString = "<html><body><h1>Data Received</h1></body></html>";
				byte[] buffer = Encoding.UTF8.GetBytes(responseString);
				response.ContentLength64 = buffer.Length;

				// Write the response
				System.IO.Stream output = response.OutputStream;
				output.Write(buffer, 0, buffer.Length);
				output.Close();
			}
		}

		/// <summary>
		/// Process Downloaded file liek open/insert into active model
		/// </summary>
		/// <param name="downloadedFilePath"></param>
		private void ProcessFile(string downloadedFilePath)
		{
			try
			{
				LogFileWriter.Write($"Leo AI : Downloade File Process Start.");
				DocType currentDocType = solidWorksHelper.ActiveDocType();
				if (solidWorksHelper.IsSolidWorksFile(downloadedFilePath))
				{
					LogFileWriter.Write($"Leo AI : Downloade File is Solidworks File.");
					//Solidworks Native file support (SLDPRT/SLDASM) 
					if (currentDocType == DocType.Part || currentDocType == DocType.Empty)
					{
						LogFileWriter.Write($"Leo AI : Current active Doc is a Part or No Doc.");
						solidWorksHelper.OpenDocument(downloadedFilePath);
					}
					else if (currentDocType == DocType.Assembly)
					{
						LogFileWriter.Write($"Leo AI : Current active Doc is an Assembly.");
						solidWorksHelper.Insert(downloadedFilePath);
					}
				}
				else
				{//None - native file support (Step/STP/IGES)

					LogFileWriter.Write($"Leo AI : Downloade File is not a Solidworks File.");
					if (currentDocType == DocType.Part || currentDocType == DocType.Empty)
					{
						LogFileWriter.Write($"Leo AI : Current active Doc is a Part or No Doc.");
						solidWorksHelper.LoadFile(downloadedFilePath);
					}
					else if (currentDocType == DocType.Assembly)
					{
						LogFileWriter.Write($"Leo AI : Current active Doc is an Assembly.");
						solidWorksHelper.InsertNonNativeFile(downloadedFilePath);
					}
				}

				LogFileWriter.Write($"Leo AI : Downloade File Process End.");
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : Downloade File Process Error {ex.Message}.");
				///
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
	}
}
