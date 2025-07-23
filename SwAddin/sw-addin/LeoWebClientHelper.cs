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

	}
}
