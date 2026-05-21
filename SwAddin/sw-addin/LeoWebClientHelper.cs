using Newtonsoft.Json;
using sw_addin.Data;
using sw_addin.Logs;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace sw_addin
{
	public class LeoWebClientHelper
	{
		private const string LOCALHOST_URL = "http://localhost:4000/receive-data";
		private const int REQUEST_TIMEOUT_MS = 5000;
		private const string LOCALHOSTUI_URL = "http://localhost:4000/unminized";
		private const string PARTR_REPLACEMENT_URL = "http://localhost:4000/partReplacement";
		private const string ASSEMBLY_INSPECTION_URL = "http://localhost:4000/assemblyInspection";

		/// <summary>
		/// Sends the Provided measurement data
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static async Task SendMeasurementData(SwMeasurementData data, bool isUIrealted = false)
		{
			try
			{
				//serialize into json format
				string json = JsonConvert.SerializeObject(data);

				LogFileWriter.Write($"Leo AI : Measure Data Json Content {json}");
				var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

				using (var httpClient = new HttpClient())
				{
					httpClient.Timeout = TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS);

					string endPoint = LOCALHOST_URL;

					if (isUIrealted)
					{
						endPoint = LOCALHOSTUI_URL;
					}
					HttpResponseMessage response = await httpClient.PostAsync(endPoint, content);

					if (response.IsSuccessStatusCode)
					{
						LogFileWriter.Write($"Leo AI : Responce send successfully to - {endPoint} with timout - {REQUEST_TIMEOUT_MS}");
						//Measurement data sent successfully to localhost application.";
					}
					else
					{
						LogFileWriter.Write($"Leo AI : Failed to send measurement data to - {endPoint} with timout - {REQUEST_TIMEOUT_MS}");
						//"Failed to send measurement data. Status code: {response.StatusCode};
					}
				}
			}
			catch (HttpRequestException ex)
			{
				LogFileWriter.Write($"Leo AI : Network Error {ex.Message} , Result - {ex.HResult}");
				//Network Error
				//Ensure your local application is running and listening on the specified port.");
			}
			catch (TaskCanceledException)
			{
				LogFileWriter.Write($"Leo AI :  Request to localhost timed out");
				//Request to localhost timed out. Ensure your local application is responsive.;
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI :  Error {ex.Message} ");
				//Error sending measurement data: {ex.Message};
			}
		}

		/// <summary>
		/// Sends part replacement request to Leo desktop app
		/// </summary>
		/// <param name="filePath">Path to the part file to replace</param>
		/// <returns></returns>
		public static async Task SendPartReplacementRequest(string filePath)
		{
			try
			{
				if (string.IsNullOrEmpty(filePath))
				{
					LogFileWriter.Write($"Leo AI : Part replacement request failed - file path is empty");
					return;
				}

				// Create request payload
				var requestData = new
				{
					filePath = filePath
				};

				// Serialize into JSON format
				string json = JsonConvert.SerializeObject(requestData);
				LogFileWriter.Write($"Leo AI : Part Replacement Request Json Content: {json}");

				var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

				using (var httpClient = new HttpClient())
				{
					httpClient.Timeout = TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS);

					HttpResponseMessage response = await httpClient.PostAsync(PARTR_REPLACEMENT_URL, content);

					if (response.IsSuccessStatusCode)
					{
						LogFileWriter.Write($"Leo AI : Part replacement request sent successfully to - {PARTR_REPLACEMENT_URL} with timeout - {REQUEST_TIMEOUT_MS}");
					}
					else
					{
						LogFileWriter.Write($"Leo AI : Failed to send part replacement request to - {PARTR_REPLACEMENT_URL}. Status code: {response.StatusCode}");
					}
				}
			}
			catch (HttpRequestException ex)
			{
				LogFileWriter.Write($"Leo AI : Network Error sending part replacement request: {ex.Message}, Result - {ex.HResult}");
			}
			catch (TaskCanceledException)
			{
				LogFileWriter.Write($"Leo AI : Part replacement request to localhost timed out");
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : Error sending part replacement request: {ex.Message}");
			}
		}

		/// <summary>
		/// Sends assembly inspection request to Leo desktop app
		/// </summary>
		/// <param name="filePath">Path to the assembly file to inspect</param>
		/// <returns></returns>
		public static async Task SendAssemblyInspectionRequest(string filePath)
		{
			try
			{
				if (string.IsNullOrEmpty(filePath))
				{
					LogFileWriter.Write($"Leo AI : Assembly inspection request failed - file path is empty");
					return;
				}

				var requestData = new
				{
					filePath = filePath
				};

				string json = JsonConvert.SerializeObject(requestData);
				LogFileWriter.Write($"Leo AI : Assembly Inspection Request Json Content: {json}");

				var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

				using (var httpClient = new HttpClient())
				{
					httpClient.Timeout = TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS);

					HttpResponseMessage response = await httpClient.PostAsync(ASSEMBLY_INSPECTION_URL, content);

					if (response.IsSuccessStatusCode)
					{
						LogFileWriter.Write($"Leo AI : Assembly inspection request sent successfully to - {ASSEMBLY_INSPECTION_URL} with timeout - {REQUEST_TIMEOUT_MS}");
					}
					else
					{
						LogFileWriter.Write($"Leo AI : Failed to send assembly inspection request to - {ASSEMBLY_INSPECTION_URL}. Status code: {response.StatusCode}");
					}
				}
			}
			catch (HttpRequestException ex)
			{
				LogFileWriter.Write($"Leo AI : Network Error sending assembly inspection request: {ex.Message}, Result - {ex.HResult}");
			}
			catch (TaskCanceledException)
			{
				LogFileWriter.Write($"Leo AI : Assembly inspection request to localhost timed out");
			}
			catch (Exception ex)
			{
				LogFileWriter.Write($"Leo AI : Error sending assembly inspection request: {ex.Message}");
			}
		}

	}
}
