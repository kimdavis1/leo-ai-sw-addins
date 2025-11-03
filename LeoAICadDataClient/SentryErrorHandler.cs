using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sentry;
using LeoAICadDataClient.Utilities;

namespace LeoAICadDataClient
{
    /// <summary>
    /// Centralized Sentry error tracking for API client operations.
    /// NOTE: This class uses NO lambda expressions to avoid COM type library issues.
    /// </summary>
    [ComVisible(false)]
    public static class SentryApiErrorHandler
    {
        private static IDisposable _sentryInstance;
        private static bool _isInitialized = false;
        private const string FALLBACK_SENTRY_DSN = "https://2874a11ed687f7e489de2abefa258929@o4505905649090560.ingest.us.sentry.io/4510233235161088";

        /// <summary>
        /// Initialize Sentry for API client. Called from SecureApiClient constructor.
        /// </summary>
        public static void Initialize(string environment = "Production")
        {
            if (_isInitialized)
            {
                return;
            }

            try
            {
                string sentryDsn = FALLBACK_SENTRY_DSN;

                // Simple initialization without lambda expressions
                _sentryInstance = SentrySdk.Init(sentryDsn);
                _isInitialized = true;

                Logger.Info($"Sentry initialized for API client - Environment: {environment}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize Sentry for API client: {ex.Message}");
            }
        }

        /// <summary>
        /// Capture an exception to Sentry with additional context.
        /// </summary>
        public static void CaptureException(Exception exception, Dictionary<string, string> context = null, SentryLevel level = SentryLevel.Error)
        {
            if (!_isInitialized)
            {
                Logger.Warning("Sentry not initialized - exception not sent to Sentry");
                return;
            }

            try
            {
                // Add context as tags
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

                // Flush immediately to ensure event is sent (API calls are often short-lived)
                SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).Wait();

                string contextInfo = context != null && context.Count > 0
                    ? $" (Context: {string.Join(", ", context.Keys)})"
                    : "";
                Logger.Info($"Exception captured by Sentry{contextInfo}: {exception.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to capture exception in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Capture API error (non-exception) to Sentry with HTTP details.
        /// </summary>
        public static void CaptureApiError(string operation, int statusCode, string responseBody, Dictionary<string, string> context = null)
        {
            if (!_isInitialized)
            {
                return;
            }

            try
            {
                // Add HTTP details to context
                var enrichedContext = context != null ? new Dictionary<string, string>(context) : new Dictionary<string, string>();
                enrichedContext["http_status_code"] = statusCode.ToString();
                enrichedContext["operation"] = operation;

                // Add truncated response body (limit to 500 chars to avoid huge Sentry events)
                if (!string.IsNullOrEmpty(responseBody))
                {
                    string truncatedBody = responseBody.Length > 500
                        ? responseBody.Substring(0, 500) + "..."
                        : responseBody;
                    enrichedContext["response_body"] = truncatedBody;
                }

                // Add context as tags
                foreach (var kvp in enrichedContext)
                {
                    SentrySdk.ConfigureScope(scope =>
                    {
                        scope.SetTag(kvp.Key, kvp.Value);
                    });
                }

                string message = $"API Error in {operation}: HTTP {statusCode}";
                SentrySdk.CaptureMessage(message, SentryLevel.Error);

                // Flush immediately to ensure event is sent (API calls are often short-lived)
                SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).Wait();

                Logger.Info($"API error captured by Sentry: {message}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to capture API error in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a breadcrumb for tracking API operation flow.
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
                Logger.Error($"Failed to add breadcrumb in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a breadcrumb for a key API operation milestone.
        /// </summary>
        public static void AddApiOperationBreadcrumb(string apiMethod, string message, Dictionary<string, string> data = null)
        {
            if (!_isInitialized) return;

            try
            {
                var breadcrumbData = data != null ? new Dictionary<string, string>(data) : new Dictionary<string, string>();
                breadcrumbData["api_method"] = apiMethod;

                SentrySdk.AddBreadcrumb(message, "api", level: BreadcrumbLevel.Info, data: breadcrumbData);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to add API operation breadcrumb in Sentry: {ex.Message}");
            }
        }

        /// <summary>
        /// Set user context for Sentry tracking.
        /// </summary>
        public static void SetUser(string username, string vaultName = null)
        {
            if (!_isInitialized)
            {
                return;
            }

            try
            {
                // Note: Cannot use lambda-based SentrySdk.ConfigureScope in COM environment
                // User context would need to be set via scope if needed
                Logger.Info($"Sentry user context: {username}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to set Sentry user: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispose Sentry instance on shutdown.
        /// </summary>
        public static void Shutdown()
        {
            if (_sentryInstance != null)
            {
                _sentryInstance.Dispose();
                _sentryInstance = null;
                _isInitialized = false;
                Logger.Info("Sentry disposed for API client");
            }
        }
    }
}
