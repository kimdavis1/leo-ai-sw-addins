using LeoAICadDataClient.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LeoAICadDataClient
{
	public class LeoApiService
	{
		private readonly LeoAIWebClient _leoApiClient;

		public LeoApiService(string apiToken)
		{
			_leoApiClient = new LeoAIWebClient(apiToken);
		}

		public async Task<bool> Login()
		{
			try
			{
				string response = await _leoApiClient.LoginAsync();
				return response.ToLower() == "ok";
			}
			catch (Exception ex)
			{
				Logger.Error($"Login failed: {ex.Message}");
				return false;
			}
		}

		public static string GetRelativePath(string rootPath, string targetPath)
		{
			Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ? rootPath : rootPath + Path.DirectorySeparatorChar);
			Uri targetUri = new Uri(targetPath);
			Uri relativeUri = rootUri.MakeRelativeUri(targetUri);

			string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
			return relativePath.Replace('/', Path.DirectorySeparatorChar);
		}

		public async Task<string> CreateDirectory(string machId, string folder)
		{
			try
			{
				string folderResponse = await _leoApiClient.CreateFolderAsync(machId, folder);

				if (!string.IsNullOrEmpty(folderResponse))
				{
					ProjectData project = JsonConvert.DeserializeObject<ProjectData>(folderResponse);
					return project.Id; 
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Failed to create directory: {ex.Message}");
			}

			return string.Empty;
		}

		public async Task<Utilities.FileInfo> CreateFile(string dirId, string filePath, Dictionary<string, string> childInfos = null)
		{
			try
			{
				LeoFileInfo.LeoFileInformation fInfo = LeoFileInfo.GetFileInfo(filePath);
				string mimeType = LeoAIMemeType.GetMemeType(filePath);
				
				List<ChildData> childDatas = new List<ChildData>();
				if (childInfos != null)
				{
					foreach (var kvp in childInfos)
					{
						childDatas.Add(new ChildData(kvp.Value, kvp.Key));
					}
				}

				string fileResponse = await _leoApiClient.CreateFileAsync(dirId, mimeType, fInfo.CheckSum, fInfo.Base64EncodedFile, Path.GetFileName(filePath), childDatas);
				
				if (!string.IsNullOrEmpty(fileResponse))
				{
					return JsonConvert.DeserializeObject<Utilities.FileInfo>(fileResponse);
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"Failed to create file: {ex.Message}");
			}

			return null;
		}
	}
}
