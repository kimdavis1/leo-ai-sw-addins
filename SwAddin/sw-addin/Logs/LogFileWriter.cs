using System;
using System.IO;

namespace sw_addin.Logs
{
	public class LogFileWriter
	{

		// Get current date and time
		//private string currentDateTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
	
		private static string LogFilePath = Path.Combine(Path.GetTempPath(), "Logging", $"SWLeoAIAddIn_{DateTime.Now.ToString("yyyy-MM-dd")}.log"); // @"C:\Users\dev\Desktop\Temp\SolidWorksAddinLog.txt";

		/// <summary>
		/// Writes the specified message to the Log file
		/// </summary>
		/// <param name="message"></param>
		public static void Write(string message)
		{
			try
			{
				// Ensure the directory exists
				string logDirectory = Path.GetDirectoryName(LogFilePath);
				if (!Directory.Exists(logDirectory))
				{
					//create the directroy
					Directory.CreateDirectory(logDirectory);
				}

				// Append to log file
				File.AppendAllText(LogFilePath, $"{DateTime.Now}: {message}\n");
			}
			catch (Exception)
			{
				//Exception
				//Unable to write message to log file
			}
		}

		/// <summary>
		/// Log entire measure data
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="obj"></param>
		/// <param name="indentLevel"></param>
		public static void LogMeasureProperties<T>(T obj,  int indentLevel = 0)
		{
			if (obj == null) return;

			Write("Leo AI: Measure Data Start:");

			// Prepare the log file
			using (StreamWriter writer = new StreamWriter(LogFilePath, append: true))
			{
				Type type = obj.GetType();
				string indent = new string('\t', indentLevel);

				writer.WriteLine($"{indent}Class: {type.Name}");
				foreach (var property in type.GetProperties())
				{
					try
					{
						object value = property.GetValue(obj);

						if (value == null)
						{
							writer.WriteLine($"{indent}\t{property.Name}: null");
						}
						else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
						{
							writer.WriteLine($"{indent}\t{property.Name}:");
							LogMeasureProperties(value, indentLevel + 1);
						}
						else
						{
							writer.WriteLine($"{indent}\t{property.Name}: {value}");
						}
					}
					catch(Exception ex) 
					{
						Write($"Leo AI : Measure Data Info - {ex.Message}");
					}
				}

				writer.WriteLine();
			}

			Write("Leo AI: Measure Data End:");
		}
	}
}
