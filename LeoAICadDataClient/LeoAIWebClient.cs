using LeoAICadDataClient.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace LeoAICadDataClient
{
	public class LeoAIWebClient
	{	
		private readonly HttpClient _httpClient;
		private const string BASE_API_URL = "https://api.getleo.ai/";

		public LeoAIWebClient(string apiToken)
		{
			_httpClient = new HttpClient { BaseAddress = new Uri(BASE_API_URL) };
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
		}

		public async Task<string> LoginAsync(string username = "", string password = "")
		{
			try
			{
				HttpResponseMessage httpResponse = await _httpClient.GetAsync("/");
				httpResponse.EnsureSuccessStatusCode();
				
				if (httpResponse.IsSuccessStatusCode)
				{
					string responseContent = await httpResponse.Content.ReadAsStringAsync();
					return "OK";
				}
				else
				{
					return "Failed";
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Login failed: {ex.Message}");
				return "Failed";
			}
		}

		public async Task<string> CreateFolderAsync(string machineId, string uri)
		{
			try
			{
				var directoryData = new DirectoryData
				{
					machineId = machineId,
					uri = uri
				};

				string jsonData = JsonConvert.SerializeObject(directoryData);
				var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
				var response = await _httpClient.PostAsync("api/v1/synced-directories", content);
				
				if (response.IsSuccessStatusCode)
				{
					return await response.Content.ReadAsStringAsync();
				}
				else
				{
					Logger.Error($"Failed to create folder. Status: {response.StatusCode}");
					return string.Empty;
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Exception creating folder: {ex.Message}");
				return string.Empty;
			}
		}

		public async Task<string> CreateFileAsync(string directoryId, string mimeType, string checkSum, string file, string filePathInDirectory, List<ChildData> dependencies)
		{
			try
			{
				using (var content = new MultipartFormDataContent())
				{
					content.Add(new StringContent(mimeType), "mimeType");
					content.Add(new StringContent(checkSum), "checkSum");
					content.Add(new StringContent(filePathInDirectory), "filePathInDirectory");

					var fileBytes = Convert.FromBase64String(file);
					content.Add(new ByteArrayContent(fileBytes), "file", System.IO.Path.GetFileName(filePathInDirectory));

					if (dependencies != null && dependencies.Count > 0)
					{
						content.Add(new StringContent(JsonConvert.SerializeObject(dependencies)), "dependencies");
					}

					var response = await _httpClient.PostAsync($"api/v1/synced-directories/{directoryId}/files", content);
					
					if (response.IsSuccessStatusCode)
					{
						return await response.Content.ReadAsStringAsync();
					}
					else
					{
						Logger.Error($"Failed to create file. Status: {response.StatusCode}");
						return string.Empty;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Exception creating file: {ex.Message}");
				return string.Empty;
			}
		}

		public async Task<string> GetFolderInformationAsync(string directoryId)
		{
			try
			{
				var response = await _httpClient.GetAsync($"api/v1/synced-directories/{directoryId}");
				return await response.Content.ReadAsStringAsync();
			}
			catch (Exception ex)
			{
				Logger.Error($"Exception getting folder information: {ex.Message}");
				return string.Empty;
			}
		}
	}

	public class DirectoryData
	{
		public string machineId { get; set; }
		public string uri { get; set; }
	}

	public class FileData
	{
		public string mimeType { get; set; }
		public string checkSum { get; set; }
		public string file { get; set; }
		public string filePathInDirectory { get; set; }
		public List<ChildData> dependencies { get; set; } = new List<ChildData>();
		public List<ChildData> children { get; set; } = new List<ChildData>();
	}

	public class ChildData
	{
		public ChildData(string cSum, string fPath)
		{
			checkSum = cSum;
			filePath = fPath;
		}

		public ChildData()
		{
		}

		public string checkSum { get; set; }
		public string filePath { get; set; }
		public string mimeType { get; set; }
	}

	public class ProjectData
	{
		public string Id { get; set; }
		public string Uri { get; set; }
		public string MachineId { get; set; }
		public bool WorkingDirectory { get; set; }
	}
}
