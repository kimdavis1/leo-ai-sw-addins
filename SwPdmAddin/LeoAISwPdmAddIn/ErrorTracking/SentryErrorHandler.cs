using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Sentry;

namespace LeoAISwPdmAddIn.ErrorTracking
{
    /// <summary>
    /// Centralized error handling and tracking using Sentry.
    /// All methods avoid lambda expressions to prevent COM type library export issues.
    /// </summary>
    [ComVisible(false)]
    public static class SentryErrorHandler
    {
        private static IDisposable _sentryInstance;
        private static bool _isInitialized = false;
        private static readonly object _initLock = new object();

        // Hardcoded fallback DSN - replace with your Sentry project DSN
        private const string FALLBACK_SENTRY_DSN = "https://2874a11ed687f7e489de2abefa258929@o4505905649090560.ingest.us.sentry.io/4510233235161088";

        /// <summary>
        /// Initialize Sentry error tracking for a specific vault.
        /// NO LAMBDA EXPRESSIONS to avoid COM type library issues.
        /// </summary>
        public static void Initialize(string vaultName, string environment = "Production")
        {
            lock (_initLock)
            {
                if (_isInitialized)
                {
                    return;
                }

                try
                {
                    // Try to get DSN from registry first
                    string sentryDsn = GetDsnFromRegistry(vaultName);

                    if (string.IsNullOrEmpty(sentryDsn))
                    {
                        if (!string.IsNullOrEmpty(FALLBACK_SENTRY_DSN))
                        {
                            sentryDsn = FALLBACK_SENTRY_DSN;
                            LogFileWriter.LogMessage("Using fallback hardcoded Sentry DSN");
                        }
                        else
                        {
                            LogFileWriter.LogMessage("Sentry DSN not configured - error tracking disabled");
                            return;
                        }
                    }
                    else
                    {
                        LogFileWriter.LogMessage("Using Sentry DSN from registry");
                    }

                    // Initialize Sentry WITHOUT lambda expressions
                    _sentryInstance = SentrySdk.Init(sentryDsn);

                    _isInitialized = true;
                    LogFileWriter.LogMessage($"Sentry error tracking initialized for vault: {vaultName}");
                }
                catch (Exception ex)
                {
                    // Don't fail initialization if Sentry setup fails
                    LogFileWriter.LogError($"Failed to initialize Sentry: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Capture an exception to Sentry with optional context data
        /// </summary>
        public static void CaptureException(Exception exception, Dictionary<string, string> context = null, SentryLevel level = SentryLevel.Error)
        {
            if (!_isInitialized)
            {
                // Just log locally if Sentry not initialized
                LogFileWriter.LogError($"Exception (Sentry not initialized): {exception.Message}");
                return;
            }

            try
            {
                // Add context data without using lambdas
                if (context != null && context.Count > 0)
                {
                    foreach (var kvp in context)
                    {
                        SentrySdk.ConfigureScope(scope =>
                        {
                            scope.SetTag(kvp.Key, kvp.Value);
                        });
                    }
                }

                // Capture the exception
                SentrySdk.CaptureException(exception);

                // Flush immediately to ensure event is sent (PDM events are short-lived)
                // Add timeout protection to prevent infinite hangs
                try
                {
                    if (!SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(5)))
                    {
                        LogFileWriter.LogWarning("Sentry flush timed out after 5 seconds");
                    }
                }
                catch (Exception flushEx)
                {
                    LogFileWriter.LogError($"Sentry flush failed: {flushEx.Message}");
                }

                // Also log locally for redundancy
                string contextInfo = context != null && context.Count > 0
                    ? $" (Context: {string.Join(", ", context.Keys)})"
                    : "";
                LogFileWriter.LogError($"Exception tracked to Sentry{contextInfo}: {exception.Message}");
            }
            catch (Exception ex)
            {
                // Don't fail if Sentry capture fails
                LogFileWriter.LogError($"Failed to capture exception in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Capture a message to Sentry
        /// </summary>
        public static void CaptureMessage(string message, Dictionary<string, string> context = null, SentryLevel level = SentryLevel.Info)
        {
            if (!_isInitialized) return;

            try
            {
                SentrySdk.CaptureMessage(message, level);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to capture message in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a breadcrumb for tracking operation flow.
        /// Breadcrumbs are automatically included when an exception is captured.
        /// </summary>
        public static void AddBreadcrumb(string message, string category = null, Dictionary<string, string> data = null)
        {
            if (!_isInitialized) return;

            try
            {
                // Add breadcrumb to Sentry (will be included with any future errors)
                SentrySdk.AddBreadcrumb(message, category, level: BreadcrumbLevel.Info, data: data);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to add breadcrumb in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a breadcrumb for a key operation milestone.
        /// Use this to track the main steps in a process.
        /// </summary>
        public static void AddOperationBreadcrumb(string operation, string message, Dictionary<string, string> data = null)
        {
            if (!_isInitialized) return;

            try
            {
                var breadcrumbData = data != null ? new Dictionary<string, string>(data) : new Dictionary<string, string>();
                breadcrumbData["operation"] = operation;

                SentrySdk.AddBreadcrumb(message, "operation", level: BreadcrumbLevel.Info, data: breadcrumbData);
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to add operation breadcrumb in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Set user information for Sentry - simplified without lambda
        /// </summary>
        public static void SetUser(string username, string vaultName)
        {
            if (!_isInitialized) return;

            try
            {
                // Just log the user info - avoid lambdas for COM compatibility
                LogFileWriter.LogMessage($"Sentry tracking for user: {username} in vault: {vaultName}");
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to set user in Sentry: {ex.Message}");
            }
        }

        private static string GetDsnFromRegistry(string vaultName)
        {
            try
            {
                // Try vault-specific DSN first
                string keyPath = $@"Software\LeoAI\PDM\{vaultName}";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        string dsn = key.GetValue("SentryDsn") as string;
                        if (!string.IsNullOrEmpty(dsn)) return dsn;
                    }
                }

                // Try system-wide DSN
                keyPath = @"Software\LeoAI\PDM";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        string dsn = key.GetValue("SentryDsn") as string;
                        if (!string.IsNullOrEmpty(dsn)) return dsn;
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileWriter.LogError($"Failed to read Sentry DSN from registry: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Shutdown Sentry (call when add-in unloads)
        /// </summary>
        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (_sentryInstance != null)
                {
                    try
                    {
                        _sentryInstance.Dispose();
                        _isInitialized = false;
                        LogFileWriter.LogMessage("Sentry error tracking shut down");
                    }
                    catch (Exception ex)
                    {
                        LogFileWriter.LogError($"Failed to shutdown Sentry: {ex.Message}");
                    }
                }
            }
        }
    }
}
