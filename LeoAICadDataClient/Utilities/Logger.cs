using System;
using System.IO;
using System.Diagnostics;

namespace LeoAICadDataClient.Utilities
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
    /// Simple logging utility for LeoAICadDataClient
    /// </summary>
    public static class Logger
    {
        private static readonly string logFilePath;
        private static readonly object lockObject = new object();
        private static LogLevel _currentLogLevel = LogLevel.Error; // Default to Error level
        private static bool _logLevelInitialized = false;

        static Logger()
        {
            string tempPath = Path.GetTempPath();
            logFilePath = Path.Combine(tempPath, "Logging", "LeoAICadDataClient_Logfile.log");
            
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
                    string envLogLevel = LeoAIDataUtilities.ReadEnvVariableByName("LEO_LOG_LEVEL", false);
                    
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
                WriteToLog("INFO", $"LeoAICadDataClient log level initialized to: {_currentLogLevel}");
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
        /// Logs a debug message (only shown when LEO_LOG_LEVEL=DEBUG)
        /// </summary>
        /// <param name="message">Debug message to log</param>
        public static void Debug(string message)
        {
            if (ShouldLog(LogLevel.Debug))
            {
                WriteToLog("DEBUG", message);
            }
        }

        /// <summary>
        /// Logs an informational message (shown when LEO_LOG_LEVEL=DEBUG or INFO)
        /// </summary>
        /// <param name="message">Message to log</param>
        public static void Info(string message)
        {
            if (ShouldLog(LogLevel.Info))
            {
                WriteToLog("INFO", message);
            }
        }

        /// <summary>
        /// Logs a warning message (shown when LEO_LOG_LEVEL=DEBUG, INFO, or WARNING)
        /// </summary>
        /// <param name="message">Warning message to log</param>
        public static void Warning(string message)
        {
            if (ShouldLog(LogLevel.Warning))
            {
                WriteToLog("WARNING", message);
            }
        }

        /// <summary>
        /// Logs an error message (always shown regardless of log level)
        /// </summary>
        /// <param name="message">Error message to log</param>
        public static void Error(string message)
        {
            if (ShouldLog(LogLevel.Error))
            {
                WriteToLog("ERROR", message);
            }
        }

        /// <summary>
        /// Forces a log entry regardless of current log level (use sparingly)
        /// </summary>
        /// <param name="logType">Type of log message</param>
        /// <param name="message">Message content</param>
        public static void ForceLog(string logType, string message)
        {
            WriteToLog(logType, message);
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
        /// Writes a message to the log file
        /// </summary>
        /// <param name="logType">Type of log message</param>
        /// <param name="message">Message content</param>
        private static void WriteToLog(string logType, string message)
        {
            try
            {
                var logDirectory = Path.GetDirectoryName(logFilePath);
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                lock (lockObject)
                {
                    using (StreamWriter writer = new StreamWriter(logFilePath, true))
                    {
                        writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logType}] {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Cannot log the logging error (to avoid infinite recursion)
                // In a production environment, you might want to consider fallback options
            }
        }

        /// <summary>
        /// Gets the full path to the log file
        /// </summary>
        public static string LogFilePath => logFilePath;
        
        /// <summary>
        /// Opens the log file in the default text editor
        /// </summary>
        public static void OpenLogFile()
        {
            try
            {
                if (File.Exists(logFilePath))
                {
                    Process.Start(logFilePath);
                }
                else
                {
                    // Create the file if it doesn't exist, then open it
                    WriteToLog("INFO", "Log file created");
                    Process.Start(logFilePath);
                }
            }
            catch (Exception ex)
            {
                // Since we can't log this error, we'll simply ignore it
            }
        }
    }
}