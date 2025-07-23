using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace LeoAICadDataClient.Utilities
{
	public class LeoAIDataUtilities
	{
		public static string ReadEnvVariableByName(string variableName, bool isUser = true)
		{
			EnvironmentVariableTarget envVarTarget = !isUser ? EnvironmentVariableTarget.Machine: EnvironmentVariableTarget.User;
			// Set environment variable at the Machine (system) level
			string variableValue = Environment.GetEnvironmentVariable(variableName, envVarTarget); 

			return variableValue;
		}

		/// <summary>
		/// Gets Mac address of the machine
		/// </summary>
		/// <returns></returns>
		public static string GetMacAddress()
		{
			return NetworkInterface
					.GetAllNetworkInterfaces()
					.Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
												nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
					.Select(nic => nic.GetPhysicalAddress().ToString())
					.FirstOrDefault();
		}

		/// <summary>
		/// Gets Mac address of the machine in colon-separated format (e.g., 70:C9:4E:43:7E:CC)
		/// </summary>
		/// <returns></returns>
		public static string GetFormattedMacAddress()
		{
			string rawMac = GetMacAddress();
			return FormatMacAddress(rawMac);
		}

		/// <summary>
		/// Formats a MAC address string to colon-separated format
		/// </summary>
		/// <param name="macAddress">Raw MAC address (e.g., "70C94E437ECC")</param>
		/// <returns>Formatted MAC address (e.g., "70:C9:4E:43:7E:CC")</returns>
		public static string FormatMacAddress(string macAddress)
		{
			if (string.IsNullOrEmpty(macAddress))
				return string.Empty;

			// Remove any existing separators
			string cleanMac = macAddress.Replace(":", "").Replace("-", "").Replace(" ", "");

			// Validate length (should be 12 characters for a MAC address)
			if (cleanMac.Length != 12)
				return macAddress; // Return original if invalid

			// Insert colons every 2 characters
			return string.Join(":", Enumerable.Range(0, 6)
				.Select(i => cleanMac.Substring(i * 2, 2)));
		}

		/// <summary>
		/// Validates if a MAC address is in the correct format
		/// </summary>
		/// <param name="macAddress">MAC address to validate</param>
		/// <returns>True if valid format</returns>
		public static bool IsValidMacAddressFormat(string macAddress)
		{
			if (string.IsNullOrEmpty(macAddress))
				return false;

			const string MACHINE_ID_REGEX = @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$";
			return Regex.IsMatch(macAddress, MACHINE_ID_REGEX);
		}
	}

	public class JwtSession
	{
		public string SessionJwt { get; set; }
	}

	public class LeoDirectoryInfo
	{
		public string Id { get; set; }
		public string TenantId { get; set; }
		public string Uri { get; set; }
		public string MachineId { get; set; }
		public bool WorkingDirectory { get; set; }
		public List<object> SyncedComponents { get; set; }
	}

	public class ProjectData
	{
		public string Id { get; set; }
		public string Uri { get; set; }
		public string MachineId { get; set; }
		public bool WorkingDirectory { get; set; }
	}

	public class FileInfo
	{
		public string ComponentId { get; set; }
		public string FilePathInDirectory { get; set; }
		public string CheckSum { get; set; }
		public string mimeType { get; set; }
	}
}
