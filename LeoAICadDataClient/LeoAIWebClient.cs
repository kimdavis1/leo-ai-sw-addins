using System.Collections.Generic;

namespace LeoAICadDataClient
{
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

		public string checkSum { get; set; }
		public string filePath { get; set; }
	}

	public class ProjectData
	{
		public string Id { get; set; }
		public string Uri { get; set; }
		public string MachineId { get; set; }
		public bool WorkingDirectory { get; set; }
	}
}
