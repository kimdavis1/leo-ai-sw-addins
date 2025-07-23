using System;
using System.IO;
using System.Reflection;

namespace LeoAISwPdmAddIn
{
	/// <summary>
	/// Log levels for controlling logging output
	/// </summary>
	public enum LogLevel
	{
		Debug = 0,    // Most verbose - all messages
		Info = 1,     // Information messages and above
		Warning = 2,  // Warning messages and above
		Error = 3     // Only error messages
	}

	/// <summary>
	/// Log file stating Work flow of the operations we perform
	/// </summary>
	public static class LogFileWriter
	{
		private static readonly string logFilePath;
		private static readonly object lockObject = new object();
		private static LogLevel _currentLogLevel = LogLevel.Error; // Default to Error level
		private static bool _logLevelInitialized = false;

		static LogFileWriter()
		{
			string tempPath = System.IO.Path.GetTempPath();
			logFilePath = Path.Combine(tempPath, "Logging","LeoAISWPDMAddIn_Logfile.log");
			// Ensure the directory exists
			var logDirectory = Path.GetDirectoryName(logFilePath);
			if (!Directory.Exists(logDirectory))
			{
				Directory.CreateDirectory(logDirectory);
			}
		}

		/// <summary>
		/// Gets the current log level from environment variable or returns default (Error)
		/// </summary>
		private static LogLevel GetCurrentLogLevel()
		{
			if (!_logLevelInitialized)
			{
				try
				{
					// Check for environment variable LEO_LOG_LEVEL
					// Can be set to: DEBUG, INFO, WARNING, ERROR
					// If not set or invalid, defaults to ERROR
					string envLogLevel = LeoAICadDataClient.Utilities.LeoAIDataUtilities.ReadEnvVariableByName("LEO_LOG_LEVEL", false);
					
					if (!string.IsNullOrEmpty(envLogLevel))
					{
						switch (envLogLevel.ToUpper().Trim())
						{
							case "DEBUG":
								_currentLogLevel = LogLevel.Debug;
								break;
							case "INFO":
								_currentLogLevel = LogLevel.Info;
								break;
							case "WARNING":
							case "WARN":
								_currentLogLevel = LogLevel.Warning;
								break;
							case "ERROR":
								_currentLogLevel = LogLevel.Error;
								break;
							default:
								_currentLogLevel = LogLevel.Error; // Default fallback
								break;
						}
					}
					else
					{
						_currentLogLevel = LogLevel.Error; // Default when no env var is set
					}
				}
				catch
				{
					_currentLogLevel = LogLevel.Error; // Fallback on any error
				}
				
				_logLevelInitialized = true;
				
				// Log the current log level (this will always be written since it's at startup)
				WriteLog("INFO", $"Log level initialized to: {_currentLogLevel}");
			}
			
			return _currentLogLevel;
		}

		/// <summary>
		/// Checks if a message should be logged based on current log level
		/// </summary>
		private static bool ShouldLog(LogLevel messageLevel)
		{
			return messageLevel >= GetCurrentLogLevel();
		}

		/// <summary>
		/// Log's a debug message (only shown when LEO_LOG_LEVEL=DEBUG)
		/// </summary>
		/// <param name="message"></param>
		public static void LogDebug(string message)
		{
			if (ShouldLog(LogLevel.Debug))
			{
				WriteLog("DEBUG", message);
			}
		}

		/// <summary>
		/// Log's a normal information message (shown when LEO_LOG_LEVEL=DEBUG or INFO)
		/// </summary>
		/// <param name="message"></param>
		public static void LogMessage(string message)
		{
			if (ShouldLog(LogLevel.Info))
			{
				WriteLog("INFO", message);
			}
		}

		/// <summary>
		/// Log's an information message (alias for LogMessage for clarity)
		/// </summary>
		/// <param name="message"></param>
		public static void LogInfo(string message)
		{
			LogMessage(message);
		}

		/// <summary>
		/// Log's a warning message (shown when LEO_LOG_LEVEL=DEBUG, INFO, or WARNING)
		/// </summary>
		/// <param name="message"></param>
		public static void LogWarning(string message)
		{
			if (ShouldLog(LogLevel.Warning))
			{
				WriteLog("WARNING", message);
			}
		}

		/// <summary>
		/// Log a message of type Error (always shown regardless of log level)
		/// </summary>
		/// <param name="message"></param>
		public static void LogError(string message)
		{
			if (ShouldLog(LogLevel.Error))
			{
				WriteLog("ERROR", message);
			}
		}

		/// <summary>
		/// Forces a log entry regardless of current log level (use sparingly)
		/// </summary>
		/// <param name="logType"></param>
		/// <param name="message"></param>
		public static void ForceLog(string logType, string message)
		{
			WriteLog(logType, message);
		}

		/// <summary>
		/// Gets the current log level as a string (for diagnostics)
		/// </summary>
		/// <returns></returns>
		public static string GetCurrentLogLevelString()
		{
			return GetCurrentLogLevel().ToString();
		}

		/// <summary>
		/// Writes respective message to log file
		/// </summary>
		/// <param name="logType"></param>
		/// <param name="message"></param>
		private static void WriteLog(string logType, string message)
		{
			try
			{
				var logDirectory = Path.GetDirectoryName(logFilePath);
				if (!Directory.Exists(logDirectory))//create directory if not exists
				{
					Directory.CreateDirectory(logDirectory);
				}
				lock (lockObject)
				{
					using (StreamWriter writer = new StreamWriter(logFilePath, true))
					{
						writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logType}] {message + "\n"}");
					}
				}
			}
			catch (Exception)
			{
				// Silently ignore logging errors to prevent infinite loops
			}
		}
	}
}
