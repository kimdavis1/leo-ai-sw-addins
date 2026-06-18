namespace LeoAICadDataClient
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using LeoAICadDataClient.Utilities;
    using Newtonsoft.Json;

    public class SecureApiClient
    {
        private readonly string _baseApiUrl = "https://api.getleo.ai/"; //"http://localhost:8000/";
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _projectId;
        private string _jwtToken;
        private readonly SemaphoreSlim _tokenRefreshLock = new SemaphoreSlim(1, 1);
        private const int MaxRetries = 5;
        // 5s baseline applies when the server's 429 response carries no retryAfterMs — most
        // commonly the nginx ingress limit (no JSON body, no retryAfterMs field). In production,
        // nginx is configured at 10 r/s per client IP with a burst of 50, so a 5s wait refills
        // the entire burst capacity. The previous 1s start was too aggressive for nginx and
        // caused retry storms. Server-supplied retryAfterMs (leo-api) is honored separately
        // and is not affected by this constant.
        // Sequence with current MaxRetries=5: 5s -> 10s -> 20s -> 40s -> 60s (clamped) ~= 135s
        // total wait before the call is reported to Sentry as exhausted.
        private const int InitialRetryDelayMs = 5000;
        // 60s matches leo-api's largest legitimate rate-limit window (the IP-based limit on
        // GET /synced-directories -- see leo-api/src/api/v1/synced-directories.ts). The hot
        // metadata endpoint uses a 3s window, so values that high are pathological; the cap
        // is defensive headroom rather than a frequently-hit ceiling.
        private const int MaxRetryDelayMs = 60_000;
        // Small buffer beyond the server's retryAfterMs so we don't race the bucket reset by a millisecond.
        private const int RetryAfterBufferMs = 100;
        private static readonly Regex RetryAfterMsRegex = new Regex(@"""retryAfterMs""\s*:\s*(\d+)", RegexOptions.Compiled);
        private static readonly Random _retryJitter = new Random();

        public SecureApiClient(string apiKey, string projectId)
        {
            _projectId = projectId;
            _apiKey = apiKey;

            Logger.Info("Initializing SecureApiClient");

            // Just use default system settings
            var handler = new HttpClientHandler
            {
                UseDefaultCredentials = true
            };

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_baseApiUrl),
                Timeout = TimeSpan.FromSeconds(120)
            };
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            Logger.Info("SecureApiClient initialized successfully");

            // Initialize Sentry for API error tracking
            SentryApiErrorHandler.Initialize("Production");
        }

        /// <summary>
        /// Creates a SecureApiClient from a config file containing ApiKey and ProjectId
        /// Supports both:
        /// - .txt files (encrypted format - preferred)
        /// - .json files (plaintext format - for dev/backward compatibility)
        /// If .txt decryption fails, automatically tries .json with same base name
        /// </summary>
        /// <param name="configFilePath">Path to LeoAuthKey.txt or LeoAuthKey.json file</param>
        /// <returns>Initialized SecureApiClient</returns>
        public static SecureApiClient CreateFromConfigFile(string configFilePath)
        {
            if (string.IsNullOrEmpty(configFilePath))
            {
                throw new ArgumentNullException(nameof(configFilePath), "Config file path cannot be null or empty");
            }

            if (!File.Exists(configFilePath))
            {
                throw new FileNotFoundException($"Auth config not found: {configFilePath}");
            }

            Logger.Info($"Reading auth config from: {configFilePath}");
            string json = null;
            string actualFilePath = configFilePath;

            // Determine file format based on extension
            if (configFilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                // New encrypted format - try to decrypt first
                Logger.Info("Detected encrypted .txt format - decrypting...");
                try
                {
                    json = AuthFileEncryption.DecryptFromFile(configFilePath);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to decrypt .txt file: {ex.Message}");

                    // Fallback: try .json with same base name (for dev purposes)
                    string jsonPath = Path.ChangeExtension(configFilePath, ".json");
                    if (File.Exists(jsonPath))
                    {
                        Logger.Info($"Attempting fallback to plaintext .json file: {jsonPath}");
                        try
                        {
                            json = File.ReadAllText(jsonPath);
                            actualFilePath = jsonPath;
                            Logger.Info("Successfully loaded plaintext .json fallback");
                        }
                        catch (Exception jsonEx)
                        {
                            Logger.Error($"Fallback to .json also failed: {jsonEx.Message}");
                            throw new Exception($"Failed to decrypt .txt file and fallback .json file also failed. Original error: {ex.Message}", ex);
                        }
                    }
                    else
                    {
                        Logger.Error($"No .json fallback file found at: {jsonPath}");
                        throw new Exception($"Failed to decrypt .txt file and no .json fallback found at {jsonPath}. Error: {ex.Message}", ex);
                    }
                }
            }
            else if (configFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy plaintext format - read directly
                Logger.Info("Detected legacy .json format - reading plaintext...");
                json = File.ReadAllText(configFilePath);
            }
            else
            {
                // Unknown extension - try to read as plaintext first, then try decrypt
                Logger.Info($"Unknown file extension '{Path.GetExtension(configFilePath)}' - attempting to read as plaintext...");
                try
                {
                    json = File.ReadAllText(configFilePath);
                }
                catch
                {
                    Logger.Info("Plaintext read failed - attempting to decrypt...");
                    json = AuthFileEncryption.DecryptFromFile(configFilePath);
                }
            }

            var config = JsonConvert.DeserializeObject<LeoAuthConfig>(json);

            if (config == null || string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ProjectId))
            {
                throw new Exception($"Invalid auth config in {actualFilePath} - missing ApiKey or ProjectId");
            }

            Logger.Info($"Auth config loaded successfully from {actualFilePath} - ProjectId: {config.ProjectId}");
            return new SecureApiClient(config.ApiKey, config.ProjectId);
        }

        /// <summary>
        /// Creates a SecureApiClient by searching for config in standard locations
        /// Priority: 1) Provided path, 2) Environment variable, 3) Default installation folder
        /// Looks for both .txt (encrypted) and .json (plaintext) files
        /// If provided path is .txt and doesn't exist, also checks for .json with same base name
        /// </summary>
        /// <param name="vaultConfigPath">Optional path to vault config file (can be .txt or .json)</param>
        /// <returns>Initialized SecureApiClient</returns>
        public static SecureApiClient CreateFromStandardLocations(string vaultConfigPath = null)
        {
            // Try vault config path first if provided
            if (!string.IsNullOrEmpty(vaultConfigPath))
            {
                if (File.Exists(vaultConfigPath))
                {
                    Logger.Info($"Using vault config path: {vaultConfigPath}");
                    return CreateFromConfigFile(vaultConfigPath);
                }

                // If .txt doesn't exist, try .json with same base name (for dev purposes)
                if (vaultConfigPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    string jsonPath = Path.ChangeExtension(vaultConfigPath, ".json");
                    if (File.Exists(jsonPath))
                    {
                        Logger.Info($"Vault .txt not found, using .json fallback: {jsonPath}");
                        return CreateFromConfigFile(jsonPath);
                    }
                }
            }

            // Try environment variable
            string envPath = LeoAIDataUtilities.ReadEnvVariableByName("LEO_AUTH_KEY", false);
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            {
                Logger.Info($"Using config from environment variable: {envPath}");
                return CreateFromConfigFile(envPath);
            }

            // Fallback to default installation folder - try .txt first (new encrypted format), then .json (plaintext)
            string defaultTxtPath = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.txt");
            if (File.Exists(defaultTxtPath))
            {
                Logger.Info($"Using config from default installation folder: {defaultTxtPath}");
                return CreateFromConfigFile(defaultTxtPath);
            }

            string defaultJsonPath = Path.Combine(@"C:\Program Files\LeoAISwPdmAddIn", "LeoAuthKey.json");
            if (File.Exists(defaultJsonPath))
            {
                Logger.Info($"Using config from default installation folder (plaintext): {defaultJsonPath}");
                return CreateFromConfigFile(defaultJsonPath);
            }

            // No config found
            throw new FileNotFoundException(
                "Leo AI authentication configuration not found!\n\n" +
                "Please place the LeoAuthKey.txt or LeoAuthKey.json file in one of the following locations:\n" +
                "1. Vault location: <vault_root>/LeoAI_TaskData/LeoAuthKey.txt (or .json)\n" +
                "2. Default location: C:\\Program Files\\LeoAISwPdmAddIn\\LeoAuthKey.txt (or .json)\n" +
                "3. Custom location specified in LEO_AUTH_KEY environment variable");
        }

        /// <summary>
        /// Authentication configuration model
        /// </summary>
        private class LeoAuthConfig
        {
            public string ApiKey { get; set; }
            public string ProjectId { get; set; }
        }

        /// <summary>
        /// Stores the current Descope JWT. Auth headers are NOT applied to the shared
        /// <see cref="_httpClient"/> here — they are attached per-request in
        /// <see cref="ApplyAuthHeaders"/>. Mutating <see cref="System.Net.Http.Headers.HttpRequestHeaders"/>
        /// <paramref name="token"/> is valid and means "fall back to the X-API-Key credential".
        /// </summary>
        public void SetJwtToken(string token)
        {
            _jwtToken = token;
        }

        /// <summary>
        /// Attaches the auth credential to a single outgoing request, read atomically from the current
        /// token state. Prefers the Descope JWT (Bearer) and falls back to the static API key (X-API-Key).
        /// If neither is available the request is NOT sent.
        /// </summary>
        private void ApplyAuthHeaders(HttpRequestMessage request)
        {
            // Single atomic reference read — string assignment is atomic, so a concurrent refresh
            // swapping _jwtToken can only ever hand us a complete old-or-new value, never a torn one.
            string jwt = _jwtToken;
            if (!string.IsNullOrEmpty(jwt))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            }
            else if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Add("X-API-Key", _apiKey);
            }
            else
            {
                throw new InvalidOperationException(
                    "SecureApiClient has no usable credential: both the JWT and the API key are empty. " +
                    "Auth initialization had not completed before the request was issued.");
            }
        }

        /// <summary>
        /// Sends a request with the auth credential applied per-request (see <see cref="ApplyAuthHeaders"/>)
        /// instead of relying on the shared client's default headers, eliminating the race where a concurrent
        /// token refresh could drop the header off an in-flight request. The caller owns
        /// <paramref name="content"/> and remains responsible for disposing it; the request only holds a
        /// reference to it.
        /// </summary>
        private Task<HttpResponseMessage> SendWithAuthAsync(HttpMethod method, string requestUri, HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, requestUri);
            if (content != null)
            {
                request.Content = content;
            }
            ApplyAuthHeaders(request);
            return _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Refreshes the Descope JWT when required and stores it via <see cref="SetJwtToken"/>.
        /// Auth headers are applied per-request in <see cref="ApplyAuthHeaders"/>.
        /// </summary>
        /// <param name="force">
        /// When <c>true</c>, bypasses the local <see cref="JwtAuthHelper.ValidateJwtToken"/> check and
        /// always exchanges for a fresh token. This is used after the server rejects a token with a 401
        /// <c>isAuthTokenExpired</c> response: a Descope JWT can be revoked or force-expired server-side
        /// (session revocation, force-logout) while still appearing valid locally by its <c>exp</c> claim,
        /// so the local check alone would never trigger a refresh in that case.
        /// </param>
        private async Task RefreshTokenIfRequiredAsync(bool force = false)
        {
            // Prevent concurrent token refreshes - only one thread should refresh at a time
            await _tokenRefreshLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Double-check token validity after acquiring lock (another thread may have already refreshed).
                // A forced refresh skips the local check entirely — the server told us the token is bad even
                // though it still looks valid locally.
                bool isTokenValid = !force && JwtAuthHelper.ValidateJwtToken(_jwtToken, _apiKey, _projectId);
                if (!isTokenValid)
                {
                    Logger.Info(force
                        ? "Forcing token refresh (server rejected current token with 401)."
                        : "Token is not valid, attempting to refresh.");
                    var descopeClient = new DescopeClient(_projectId, "https://api.descope.com");

                    var tokenTask = descopeClient.ExchangeTokenAsync(_apiKey);
                    if (await Task.WhenAny(tokenTask, Task.Delay(10000)).ConfigureAwait(false) == tokenTask)
                    {
                        string newJwtToken = await tokenTask.ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(newJwtToken))
                        {
                            _jwtToken = newJwtToken;
                            Logger.Info("Token refreshed successfully.");
                        }
                        else
                        {
                            Logger.Error("Failed to refresh token, new token is null or empty.");
                        }
                    }
                    else
                    {
                        Logger.Error("Token refresh timed out after 10 seconds.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing token: {ex.Message}");
            }
            finally
            {
                _tokenRefreshLock.Release();
            }
        }

        /// <summary>
        /// Executes an async function with retry logic for rate limit errors (HTTP 429).
        /// Honors the server-supplied <c>retryAfterMs</c> from the response body when present,
        /// and falls back to exponential backoff otherwise. Adds jitter so concurrent workers
        /// don't retry in lockstep.
        ///
        /// Sentry capture policy: transient 429s that succeed on retry are NOT captured — only
        /// 429s that exhaust the retry budget reach Sentry, tagged with <c>retry_exhausted=true</c>.
        /// The inner catches in each public method are expected to skip <c>CaptureException</c> when
        /// <see cref="IsRateLimitException"/> returns true, leaving the decision to this method.
        ///
        /// Auth-expiry policy (HTTP 401 <c>isAuthTokenExpired</c>): mirrors the 429 policy but with a
        /// single retry. On the first such 401 we force a token refresh (<see cref="RefreshTokenIfRequiredAsync"/>
        /// with <c>force: true</c>) and retry the operation exactly once — a second 401 after a fresh token is a
        /// genuine auth failure, not a stale token. The inner catches skip <c>CaptureException</c> for
        /// <see cref="IsAuthTokenExpiredException"/>, so the single Sentry report on a real failure is emitted
        /// here, tagged with <c>auth_retry_exhausted=true</c>. A 401 that succeeds after the refresh produces
        /// no Sentry event at all — this is the noise reduction the fix targets.
        /// </summary>
        internal async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
        {
            int exponentialDelay = InitialRetryDelayMs;
            bool authRefreshRetried = false;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    bool isRateLimit = IsRateLimitException(ex);
                    bool isAuthExpired = IsAuthTokenExpiredException(ex);

                    // 401 isAuthTokenExpired: the server rejected a token that still looked valid locally
                    // (revoked / force-expired server-side). Force a fresh token and retry the operation
                    // ONCE. Guarded by authRefreshRetried so a persistent 401 can't loop.
                    if (isAuthExpired && !authRefreshRetried)
                    {
                        authRefreshRetried = true;
                        Logger.Info($"Auth token rejected by server (401) in {operationName} — forcing token refresh and retrying once");
                        await RefreshTokenIfRequiredAsync(force: true).ConfigureAwait(false);
                        // The one-shot auth retry must not consume a rate-limit attempt slot — otherwise a
                        // first 401 arriving on the final iteration (after the 429 budget is spent) would exit
                        // the loop before the refreshed token is ever tried. The authRefreshRetried guard above
                        // makes this strictly one-shot, so decrementing here cannot loop (worst case is
                        // MaxRetries + 2 total iterations).
                        attempt--;
                        continue;
                    }

                    if (isAuthExpired)
                    {
                        // Already retried once with a fresh token and still 401 → genuine auth failure.
                        // The inner catch suppressed this (mirroring the 429 policy), so report it here, once.
                        Logger.Error($"Auth token refresh did not fix 401 in {operationName} — giving up and reporting to Sentry");
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", operationName },
                            { "auth_retry_exhausted", "true" },
                        });
                        throw;
                    }

                    if (isRateLimit && attempt < MaxRetries)
                    {
                        int delay = ComputeRateLimitDelayMs(ex.Message, exponentialDelay, out bool usedServerValue);
                        Logger.Info($"Rate limit hit for {operationName}, retrying in {delay}ms (attempt {attempt + 1}/{MaxRetries}, source={(usedServerValue ? "server" : "backoff")})");
                        await Task.Delay(delay).ConfigureAwait(false);
                        // Advance the exponential backoff regardless — only used on the next retry if the server doesn't supply a value.
                        exponentialDelay = Math.Min(exponentialDelay * 2, MaxRetryDelayMs);
                        continue;
                    }

                    if (isRateLimit)
                    {
                        // Exhausted the retry budget — this is the first time we report the 429 to Sentry.
                        // All earlier transient 429s on this call were suppressed by the inner catches.
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", operationName },
                            { "retry_exhausted", "true" },
                            { "retry_attempts", (MaxRetries + 1).ToString() },
                        });
                    }

                    // Non-rate-limit errors were already captured by the inner catch; rate-limit
                    // exhaustion was captured just above. Either way, let it bubble.
                    throw;
                }
            }

            // Should never reach here, but compiler requires return
            throw new Exception($"Failed to execute {operationName} after {MaxRetries} retries");
        }

        /// <summary>
        /// Returns true when <paramref name="ex"/> represents an HTTP 429 response from the API.
        /// Shared between <see cref="ExecuteWithRetryAsync"/> and the inner catches in each public
        /// method so the suppression decision stays in sync.
        /// </summary>
        internal static bool IsRateLimitException(Exception ex)
        {
            if (ex == null || string.IsNullOrEmpty(ex.Message)) return false;
            return ex.Message.Contains("429") ||
                   ex.Message.ToLower().Contains("rate limit") ||
                   ex.Message.ToLower().Contains("too many requests");
        }

        /// <summary>
        /// Returns true when <paramref name="ex"/> represents a leo-api HTTP 401 "auth token expired"
        /// response. The inner catches in each public method throw an exception whose message embeds the
        /// response body (e.g. <c>{"error":"Authorization token expired","isAuthTokenExpired":true}</c>),
        /// so this matches on either marker. Shared between <see cref="ExecuteWithRetryAsync"/> (which
        /// forces a token refresh and retries once) and the inner catches (which skip
        /// <c>CaptureException</c> for this class so the single report happens centrally).
        /// </summary>
        internal static bool IsAuthTokenExpiredException(Exception ex)
        {
            if (ex == null || string.IsNullOrEmpty(ex.Message)) return false;
            return ex.Message.Contains("isAuthTokenExpired") ||
                   ex.Message.Contains("Authorization token expired");
        }

        /// <summary>
        /// Detects the leo-api "auth token expired" signal directly from an HTTP response: status 401 with
        /// a body carrying <c>isAuthTokenExpired</c> or <c>Authorization token expired</c>. Used by each
        /// public method's non-success branch to decide whether to throw a retryable auth-expiry exception
        /// (so <see cref="ExecuteWithRetryAsync"/> can refresh + retry) instead of reporting and returning.
        /// Other 401s (e.g. a bad API key) do NOT match and keep their existing report-and-return behavior —
        /// retrying those would loop without ever succeeding.
        /// </summary>
        internal static bool IsAuthTokenExpiredResponse(int statusCode, string responseBody)
        {
            if (statusCode != 401 || string.IsNullOrEmpty(responseBody)) return false;
            return responseBody.Contains("isAuthTokenExpired") ||
                   responseBody.Contains("Authorization token expired");
        }

        /// <summary>
        /// Picks a retry delay for a 429: server-supplied <c>retryAfterMs</c> (plus a small buffer)
        /// when present in the response body, otherwise the current exponential backoff value.
        /// Clamps to <see cref="MaxRetryDelayMs"/>.
        ///
        /// Jitter is applied differently in each case:
        /// <list type="bullet">
        ///   <item>Server-supplied: positive-only jitter (0..+15%). The server's value is a hard
        ///   floor — retrying earlier than that guarantees another 429.</item>
        ///   <item>Exponential fallback: symmetric jitter (±15%). No hard floor; spreading both
        ///   directions helps break lockstep retries across concurrent workers.</item>
        /// </list>
        /// </summary>
        internal static int ComputeRateLimitDelayMs(string exceptionMessage, int exponentialFallbackMs, out bool usedServerValue)
        {
            int? serverRetryMs = ParseRetryAfterMs(exceptionMessage);
            usedServerValue = serverRetryMs.HasValue;

            int baseDelay;
            int jitterLow;
            int jitterHighExclusive;

            if (serverRetryMs.HasValue)
            {
                baseDelay = Math.Min(serverRetryMs.Value + RetryAfterBufferMs, MaxRetryDelayMs);
                int jitterRange = Math.Max(1, baseDelay * 15 / 100);
                jitterLow = 0;
                jitterHighExclusive = jitterRange + 1;
            }
            else
            {
                baseDelay = Math.Min(exponentialFallbackMs, MaxRetryDelayMs);
                int jitterRange = Math.Max(1, baseDelay * 15 / 100);
                jitterLow = -jitterRange;
                jitterHighExclusive = jitterRange + 1;
            }

            int jitter;
            lock (_retryJitter)
            {
                jitter = _retryJitter.Next(jitterLow, jitterHighExclusive);
            }

            // 50ms floor matters only for the exponential branch with tiny fallbacks; the server
            // branch is already floor-protected by the buffer and positive-only jitter.
            return Math.Max(50, baseDelay + jitter);
        }

        /// <summary>
        /// Extracts the <c>retryAfterMs</c> field from a JSON body embedded in the exception message
        /// (e.g. <c>"Rate limit (429): {\"error\":\"...\",\"retryAfterMs\":2699}"</c>).
        /// Returns <c>null</c> when the field is absent — typical for 429s that originate outside the
        /// leo-api rate limiter (e.g. the monolith advisory-lock collision).
        /// </summary>
        internal static int? ParseRetryAfterMs(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var match = RetryAfterMsRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int ms))
            {
                return ms;
            }
            return null;
        }

        /// <summary>
        /// Creates a file in Leo AI with separate logical and physical paths.
        /// logicalFilePath: The path shown to user/API (relative path calculated from this)
        /// actualFilePath: The actual file system path to read file content from (can be archive path or temp path)
        /// externalId: The unique PDM file ID
        /// </summary>
        public async Task<LeoAICadDataClient.Utilities.FileInfo> CreateFileAsync(string directoryId, string vaultPath, string logicalFilePath, string actualFilePath, string externalId, Dictionary<string, string> childInfos = null)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"Attempting to create file: {logicalFilePath} (reading from: {actualFilePath}) in directory: {directoryId}");

                    // Read file and encode to Base64, using provided externalId as the identifier (FileID_Version)
                    LeoFileInfo.LeoFileInformation fInfo = LeoFileInfo.GetFileForUpload(actualFilePath, externalId);

                    string relativePath = GetRelativePath(vaultPath, logicalFilePath); // Use logical path for API
                    string memeType = LeoAIMemeType.GetMemeType(logicalFilePath); // Use logical path for extension

                    // Log API request parameters
                    Logger.Info($"[API CALL] CreateFile: path={NormalizeFilePathForApi(relativePath)}, identifier={fInfo.CheckSum}, mimeType={memeType}, externalId={externalId}, hasFileContent=true");
                    if (childInfos != null && childInfos.Count > 0)
                    {
                        Logger.Info($"[API CALL] CreateFile dependencies: {JsonConvert.SerializeObject(childInfos)}");
                    }

                    using (var content = new MultipartFormDataContent())
                    {
                        content.Add(new StringContent(memeType), "mimeType");
                        content.Add(new StringContent(fInfo.CheckSum), "checkSum");
                        content.Add(new StringContent(NormalizeFilePathForApi(relativePath)), "filePathInDirectory");
                        // content.Add(new StringContent(externalId), "externalId");

                        var fileBytes = Convert.FromBase64String(fInfo.Base64EncodedFile);
                        content.Add(new ByteArrayContent(fileBytes), "file", Path.GetFileName(logicalFilePath));

                        if (childInfos != null && childInfos.Count > 0)
                        {
                            var childDatas = childInfos.Select(kvp => new ChildData(kvp.Key, kvp.Value)).ToList();
                            content.Add(new StringContent(JsonConvert.SerializeObject(childDatas)), "dependencies");
                        }

                        var response = await SendWithAuthAsync(HttpMethod.Post, $"api/v1/synced-directories/{directoryId}/files", content).ConfigureAwait(false);
                        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            Logger.Info($"Successfully created file: {logicalFilePath}");
                            return JsonConvert.DeserializeObject<LeoAICadDataClient.Utilities.FileInfo>(responseString);
                        }
                        else
                        {
                            Logger.Error($"Failed to create file: {logicalFilePath}. Status: {response.StatusCode}, Response: {responseString}");

                            bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, responseString);

                            // Capture unexpected API errors to Sentry (excluding rate limits and conflicts which are handled separately)
                            // 429 = Rate limit (handled by retry logic)
                            // 409 = Conflict (expected when file already exists - handled by optimistic upload logic)
                            // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                            if ((int)response.StatusCode != 429 && (int)response.StatusCode != 409 && !isAuthExpired)
                            {
                                SentryApiErrorHandler.CaptureApiError("CreateFile", (int)response.StatusCode, responseString,
                                    new Dictionary<string, string> { { "file", logicalFilePath }, { "directoryId", directoryId } });
                            }

                            if ((int)response.StatusCode == 429)
                            {
                                throw new Exception($"Rate limit (429): {responseString}");
                            }
                            if (isAuthExpired)
                            {
                                Logger.Info($"CreateFile: auth token rejected by server (401 isAuthTokenExpired) for '{logicalFilePath}' — throwing for one-shot token-refresh retry");
                                throw new Exception($"Authorization token expired (401) in CreateFile: {responseString}");
                            }
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in CreateFile: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Capture exception to Sentry with detailed context
                    var errorContext = new Dictionary<string, string>
                    {
                        { "operation", "CreateFile" },
                        { "file", logicalFilePath },
                        { "directoryId", directoryId },
                        { "error_type", ex.GetType().Name }
                    };

                    // Add inner exception details if available
                    if (ex.InnerException != null)
                    {
                        errorContext.Add("inner_error", ex.InnerException.Message);
                        errorContext.Add("inner_error_type", ex.InnerException.GetType().Name);
                    }

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, errorContext);
                    }

                    throw;
                }
            }, $"CreateFile({Path.GetFileName(logicalFilePath)})").ConfigureAwait(false);
        }

        public static string GetRelativePath(string rootPath, string targetPath)
        {
            // Use Path-based calculation instead of URI-based to avoid issues with spaces and special characters
            // Normalize paths to ensure consistent comparison
            string normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedTarget = Path.GetFullPath(targetPath);

            // Check if target starts with root
            if (normalizedTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Remove root from target to get relative path
                string relativePath = normalizedTarget.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Logger.Info($"Calculated relative path: '{relativePath}' from root: '{rootPath}' and target: '{targetPath}'");
                return relativePath;
            }
            else
            {
                // If target doesn't start with root, return the target as-is (shouldn't happen in normal operation)
                Logger.Info($"Target path does not start with root path. Root: '{rootPath}', Target: '{targetPath}'");
                return targetPath;
            }
        }

        public static string NormalizeFilePathForApi(string filePath)
        {
            return filePath.Replace('\\', '/');
        }

        public async Task<string> CreateDirectoryAsync(string machineId, string uri)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    if (!LeoAIDataUtilities.IsValidMacAddressFormat(machineId))
                    {
                        Logger.Error($"Invalid MAC address format: {machineId}. Expected format: XX:XX:XX:XX:XX:XX or XX-XX-XX-XX-XX-XX");
                        return string.Empty;
                    }

                    Logger.Info($"Creating directory for machine: {machineId}, uri: {uri}");
                    var jsonPayload = JsonConvert.SerializeObject(new { machineId, uri });
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = await SendWithAuthAsync(HttpMethod.Post, "api/v1/synced-directories", content).ConfigureAwait(false);
                    var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var project = JsonConvert.DeserializeObject<ProjectData>(responseString);
                        Logger.Info($"Directory created successfully with ID: {project.Id}");
                        return project.Id;
                    }
                    else
                    {
                        Logger.Error($"Failed to create directory. Status: {response.StatusCode}, Response: {responseString}");

                        bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, responseString);

                        // Capture unexpected API errors to Sentry
                        // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                        if ((int)response.StatusCode != 429 && !isAuthExpired)
                        {
                            SentryApiErrorHandler.CaptureApiError("CreateDirectory", (int)response.StatusCode, responseString,
                                new Dictionary<string, string> { { "machineId", machineId }, { "uri", uri } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        if (isAuthExpired)
                        {
                            Logger.Info($"CreateDirectory: auth token rejected by server (401 isAuthTokenExpired) for machine '{machineId}' — throwing for one-shot token-refresh retry");
                            throw new Exception($"Authorization token expired (401) in CreateDirectory: {responseString}");
                        }
                        return string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"CreateDirectory failed: {ex.Message}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "CreateDirectory" },
                            { "machineId", machineId },
                            { "uri", uri }
                        });
                    }

                    throw;
                }
            }, $"CreateDirectory({machineId})").ConfigureAwait(false);
        }

        public async Task<List<LeoDirectoryInfo>> GetDirectoryInfoAsync(string machineId)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"GetDirectoryInfo: Starting to fetch directory info for machine {machineId}");
                    var response = await SendWithAuthAsync(HttpMethod.Get, "api/v1/synced-directories").ConfigureAwait(false);
                    var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonConvert.DeserializeObject<List<LeoDirectoryInfo>>(responseString);
                    }
                    else
                    {
                        Logger.Error($"GetDirectoryInfo failed. Status: {response.StatusCode}, Response: {responseString}");

                        bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, responseString);

                        // Capture unexpected API errors to Sentry
                        // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                        if ((int)response.StatusCode != 429 && !isAuthExpired)
                        {
                            SentryApiErrorHandler.CaptureApiError("GetDirectoryInfo", (int)response.StatusCode, responseString,
                                new Dictionary<string, string> { { "machineId", machineId } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        if (isAuthExpired)
                        {
                            Logger.Info($"GetDirectoryInfo: auth token rejected by server (401 isAuthTokenExpired) for machine '{machineId}' — throwing for one-shot token-refresh retry");
                            throw new Exception($"Authorization token expired (401) in GetDirectoryInfo: {responseString}");
                        }
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in GetDirectoryInfo: {ex.Message}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "GetDirectoryInfo" },
                            { "machineId", machineId }
                        });
                    }

                    throw;
                }
            }, $"GetDirectoryInfo({machineId})").ConfigureAwait(false);
        }

        public async Task<LeoAICadDataClient.Utilities.FileInfo> GetFileInfoByPathAsync(string directoryId, string relativePath)
        {
            try
            {
                Logger.Info($"GetFileInfoByPath: Attempting to find file '{relativePath}' in directory '{directoryId}'");

                // Fetch specific file using filepath_in_directory query parameter
                string normalizedPath = NormalizeFilePathForApi(relativePath);
                var syncMetadata = await GetSyncMetadataAsync(directoryId, normalizedPath).ConfigureAwait(false);

                if (syncMetadata == null || syncMetadata.Files == null || syncMetadata.Files.Count == 0)
                {
                    Logger.Info($"GetFileInfoByPath: File '{normalizedPath}' not found in directory '{directoryId}'.");
                    return null;
                }

                // Should return exactly one file
                var file = syncMetadata.Files.FirstOrDefault();
                Logger.Info($"GetFileInfoByPath: Successfully found file '{normalizedPath}'.");

                var fileInfo = new LeoAICadDataClient.Utilities.FileInfo
                {
                    ComponentId = file.ComponentId,
                    FilePathInDirectory = file.FilePathInDirectory,
                    CheckSum = file.CheckSum,
                    mimeType = file.MimeType,
                    ParentStatus = file.ParentStatus
                };

                Logger.Info($"[API RESPONSE] GetFileInfoByPath: componentId={fileInfo.ComponentId}, checksum={fileInfo.CheckSum}, parentStatus={fileInfo.ParentStatus}, path={fileInfo.FilePathInDirectory}");
                return fileInfo;
            }
            catch (Exception ex)
            {
                Logger.Error($"An exception occurred in GetFileInfoByPath: {ex.Message}");
                return null;
            }
        }

        // Default page size for pagination - adjust this value for testing
        private const int DEFAULT_SYNC_METADATA_PAGE_SIZE = 1000;

        public async Task<SyncMetadataResponse> GetSyncMetadataAsync(string directoryId)
        {
            return await GetSyncMetadataAsync(directoryId, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Get sync metadata with pagination support.
        /// </summary>
        /// <param name="directoryId">Directory ID</param>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="limit">Number of files per page</param>
        /// <returns>Page of sync metadata</returns>
        public async Task<SyncMetadataResponse> GetSyncMetadataPagedAsync(string directoryId, int page, int limit)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    string url = $"api/v1/synced-directories/{directoryId}/files/sync-metadata?page={page}&limit={limit}";
                    Logger.Info($"GetSyncMetadataPaged: Fetching page {page} (limit {limit}) for directory {directoryId}");

                    var response = await SendWithAuthAsync(HttpMethod.Get, url).ConfigureAwait(false);
                    var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonConvert.DeserializeObject<SyncMetadataResponse>(responseString);
                    }
                    else
                    {
                        Logger.Error($"GetSyncMetadataPaged failed. Status: {response.StatusCode}, Body: {responseString}");

                        bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, responseString);

                        // Capture unexpected API errors to Sentry
                        // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                        if ((int)response.StatusCode != 429 && !isAuthExpired)
                        {
                            SentryApiErrorHandler.CaptureApiError("GetSyncMetadataPaged", (int)response.StatusCode, responseString,
                                new Dictionary<string, string>
                                {
                                    { "directoryId", directoryId },
                                    { "page", page.ToString() },
                                    { "limit", limit.ToString() }
                                });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        if (isAuthExpired)
                        {
                            Logger.Info($"GetSyncMetadataPaged: auth token rejected by server (401 isAuthTokenExpired) for directory '{directoryId}' (page {page}) — throwing for one-shot token-refresh retry");
                            throw new Exception($"Authorization token expired (401) in GetSyncMetadataPaged: {responseString}");
                        }
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetSyncMetadataPaged: Exception occurred: {ex.Message}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "GetSyncMetadataPaged" },
                            { "directoryId", directoryId },
                            { "page", page.ToString() },
                            { "limit", limit.ToString() }
                        });
                    }

                    throw;
                }
            }, $"GetSyncMetadataPaged({directoryId}, page={page}, limit={limit})").ConfigureAwait(false);
        }

        /// <summary>
        /// Get all sync metadata by fetching all pages.
        /// Aggregates results from multiple API calls.
        /// </summary>
        /// <param name="directoryId">Directory ID</param>
        /// <param name="pageSize">Number of files per page (default 1000)</param>
        /// <returns>List of all files</returns>
        public async Task<List<SyncMetadataFile>> GetAllSyncMetadataAsync(string directoryId, int pageSize = DEFAULT_SYNC_METADATA_PAGE_SIZE)
        {
            var allFiles = new List<SyncMetadataFile>();
            int page = 0;
            int totalFetched = 0;

            Logger.Info($"GetAllSyncMetadata: Starting paginated fetch for directory {directoryId} (page size: {pageSize})");

            while (true)
            {
                var response = await GetSyncMetadataPagedAsync(directoryId, page, pageSize).ConfigureAwait(false);

                if (response?.Files == null || response.Files.Count == 0)
                {
                    Logger.Info($"GetAllSyncMetadata: Page {page} returned no files - finished");
                    break;
                }

                allFiles.AddRange(response.Files);
                totalFetched += response.Files.Count;
                Logger.Info($"GetAllSyncMetadata: Fetched page {page}, total files so far: {totalFetched}");

                // If we got fewer files than page size, this was the last page
                if (response.Files.Count < pageSize)
                {
                    Logger.Info($"GetAllSyncMetadata: Page {page} had {response.Files.Count} files (< {pageSize}) - last page");
                    break;
                }

                page++;
            }

            Logger.Info($"GetAllSyncMetadata: Completed - fetched {allFiles.Count} files across {page} pages");
            return allFiles;
        }

        public async Task<SyncMetadataResponse> GetSyncMetadataAsync(string directoryId, string filepathInDirectory)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    string url = $"api/v1/synced-directories/{directoryId}/files/sync-metadata";

                    if (!string.IsNullOrEmpty(filepathInDirectory))
                    {
                        url += $"?filepath_in_directory={Uri.EscapeDataString(filepathInDirectory)}";
                        Logger.Info($"GetSyncMetadata: Fetching sync metadata for specific file '{filepathInDirectory}' in directory {directoryId}");
                    }
                    else
                    {
                        Logger.Info($"GetSyncMetadata: Fetching all sync metadata for directory {directoryId}");
                    }

                    var response = await SendWithAuthAsync(HttpMethod.Get, url).ConfigureAwait(false);
                    var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        // Log the raw JSON response to help diagnose parsing issues
                        Logger.Info($"GetSyncMetadata: Raw JSON response: {responseString}");
                        return JsonConvert.DeserializeObject<SyncMetadataResponse>(responseString);
                    }
                    else
                    {
                        Logger.Error($"GetSyncMetadata failed. Status: {response.StatusCode}, Body: {responseString}");

                        bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, responseString);

                        // Capture unexpected API errors to Sentry
                        // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                        if ((int)response.StatusCode != 429 && !isAuthExpired)
                        {
                            SentryApiErrorHandler.CaptureApiError("GetSyncMetadata", (int)response.StatusCode, responseString,
                                new Dictionary<string, string> { { "directoryId", directoryId }, { "filepath", filepathInDirectory ?? "all" } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {responseString}");
                        }
                        if (isAuthExpired)
                        {
                            Logger.Info($"GetSyncMetadata: auth token rejected by server (401 isAuthTokenExpired) for directory '{directoryId}' (filepath '{filepathInDirectory ?? "all"}') — throwing for one-shot token-refresh retry");
                            throw new Exception($"Authorization token expired (401) in GetSyncMetadata: {responseString}");
                        }
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetSyncMetadata: Exception occurred: {ex.Message}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "GetSyncMetadata" },
                            { "directoryId", directoryId },
                            { "filepath", filepathInDirectory ?? "all" }
                        });
                    }

                    throw;
                }
            }, $"GetSyncMetadata({directoryId}, {filepathInDirectory ?? "all"})").ConfigureAwait(false);
        }

        public async Task<bool> DeleteFileAsync(string directoryId, string filePathInDirectory)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    string normalizedPath = NormalizeFilePathForApi(filePathInDirectory);
                    string encodedFilePath = Uri.EscapeDataString(normalizedPath);
                    string requestUri = $"api/v1/synced-directories/{directoryId}/files?filePathInDirectory={encodedFilePath}";

                    Logger.Info($"[API CALL] DeleteFile: path={normalizedPath}, directoryId={directoryId}");
                    Logger.Info($"Sending DELETE request to: {requestUri}");

                    var response = await SendWithAuthAsync(HttpMethod.Delete, requestUri).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Info($"Successfully deleted file: {normalizedPath}");
                        return true;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Logger.Error($"Failed to delete file: {normalizedPath}. Status: {response.StatusCode}, Response: {errorContent}");

                        bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, errorContent);

                        // Capture unexpected API errors to Sentry (excluding expected errors)
                        // 429 = Rate limit (handled by retry logic)
                        // 404 = Not found (expected when trying to delete old path that doesn't exist on server)
                        // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                        if ((int)response.StatusCode != 429 && (int)response.StatusCode != 404 && !isAuthExpired)
                        {
                            SentryApiErrorHandler.CaptureApiError("DeleteFile", (int)response.StatusCode, errorContent,
                                new Dictionary<string, string> { { "file", normalizedPath }, { "directoryId", directoryId } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {errorContent}");
                        }
                        if (isAuthExpired)
                        {
                            Logger.Info($"DeleteFile: auth token rejected by server (401 isAuthTokenExpired) for '{normalizedPath}' — throwing for one-shot token-refresh retry");
                            throw new Exception($"Authorization token expired (401) in DeleteFile: {errorContent}");
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in DeleteFile: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "DeleteFile" },
                            { "file", filePathInDirectory },
                            { "directoryId", directoryId }
                        });
                    }

                    throw;
                }
            }, $"DeleteFile({filePathInDirectory})").ConfigureAwait(false);
        }

        /// <summary>
        /// Updates file location (move/rename) - sends checksum to identify the file but NOT the file content
        /// Backend uses checksum to attach new path to existing file
        /// checksum: The checksum from the OLD file location (to identify which file to update)
        /// externalId: The unique PDM file ID
        /// </summary>
        public async Task<LeoAICadDataClient.Utilities.FileInfo> UpdateFileLocationAsync(string directoryId, string vaultPath, string filePath, string checksum, string externalId, Dictionary<string, string> childInfos = null)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"Attempting to update file location: {filePath} in directory: {directoryId}");

                    // For move/rename: use the provided checksum (from old file) to identify the file on server
                    // Do NOT calculate new checksum - we already compared checksums earlier
                    string relativePath = GetRelativePath(vaultPath, filePath);
                    string mimeType = LeoAIMemeType.GetMemeType(filePath);

                    // Log API request parameters
                    Logger.Info($"[API CALL] UpdateFileLocation: path={NormalizeFilePathForApi(relativePath)}, checksum={checksum}, mimeType={mimeType}, externalId={externalId}, hasFileContent=false");
                    if (childInfos != null && childInfos.Count > 0)
                    {
                        Logger.Info($"[API CALL] UpdateFileLocation dependencies: {JsonConvert.SerializeObject(childInfos)}");
                    }

                    using (var content = new MultipartFormDataContent())
                    {
                        content.Add(new StringContent(mimeType), "mimeType");
                        content.Add(new StringContent(checksum), "checkSum");
                        content.Add(new StringContent(NormalizeFilePathForApi(relativePath)), "filePathInDirectory");
                        // content.Add(new StringContent(externalId), "externalId");

                        // NOTE: We send checkSum so backend can identify which file to attach the new path to
                        // We do NOT send file content (no ByteArrayContent with Base64EncodedFile)

                        if (childInfos != null && childInfos.Count > 0)
                        {
                            var childDatas = childInfos.Select(kvp => new ChildData(kvp.Key, kvp.Value)).ToList();
                            content.Add(new StringContent(JsonConvert.SerializeObject(childDatas)), "dependencies");
                        }

                        var response = await SendWithAuthAsync(HttpMethod.Post, $"api/v1/synced-directories/{directoryId}/files", content).ConfigureAwait(false);
                        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            Logger.Info($"Successfully updated file location: {filePath}");
                            return JsonConvert.DeserializeObject<LeoAICadDataClient.Utilities.FileInfo>(responseString);
                        }
                        else
                        {
                            Logger.Error($"Failed to update file location: {filePath}. Status: {response.StatusCode}, Response: {responseString}");

                            bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, responseString);

                            // Capture unexpected API errors to Sentry (excluding expected errors)
                            // 429 = Rate limit (handled by retry logic)
                            // 409 = Conflict (expected when destination path already exists - handled by optimistic upload logic)
                            // 400 with "File is required" = Bad request (expected when server needs file content because checksum doesn't match)
                            // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                            bool shouldReport = true;
                            if ((int)response.StatusCode == 429)
                            {
                                shouldReport = false;
                            }
                            else if ((int)response.StatusCode == 409)
                            {
                                shouldReport = false;
                            }
                            else if ((int)response.StatusCode == 400 && responseString != null && responseString.Contains("File is required"))
                            {
                                shouldReport = false;
                            }
                            else if (isAuthExpired)
                            {
                                shouldReport = false;
                            }

                            if (shouldReport)
                            {
                                SentryApiErrorHandler.CaptureApiError("UpdateFileLocation", (int)response.StatusCode, responseString,
                                    new Dictionary<string, string> { { "file", filePath }, { "directoryId", directoryId }, { "checksum", checksum } });
                            }

                            if ((int)response.StatusCode == 429)
                            {
                                throw new Exception($"Rate limit (429): {responseString}");
                            }
                            if (isAuthExpired)
                            {
                                Logger.Info($"UpdateFileLocation: auth token rejected by server (401 isAuthTokenExpired) for '{filePath}' — throwing for one-shot token-refresh retry");
                                throw new Exception($"Authorization token expired (401) in UpdateFileLocation: {responseString}");
                            }
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in UpdateFileLocation: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "UpdateFileLocation" },
                            { "file", filePath },
                            { "directoryId", directoryId }
                        });
                    }

                    throw;
                }
            }, $"UpdateFileLocation({Path.GetFileName(filePath)})").ConfigureAwait(false);
        }

        public async Task<bool> DeleteDirectoryAsync(string directoryId)
        {
            await RefreshTokenIfRequiredAsync().ConfigureAwait(false);

            return await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    Logger.Info($"Attempting to delete directory: {directoryId}");
                    var response = await SendWithAuthAsync(HttpMethod.Delete, $"api/v1/synced-directories/{directoryId}").ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Info($"Successfully deleted directory: {directoryId}");
                        return true;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Logger.Error($"Failed to delete directory: {directoryId}. Status: {response.StatusCode}, Response: {errorContent}");

                        bool isAuthExpired = IsAuthTokenExpiredResponse((int)response.StatusCode, errorContent);

                        // Capture unexpected API errors to Sentry
                        // 401 isAuthTokenExpired = handled by the token-refresh retry in ExecuteWithRetryAsync
                        if ((int)response.StatusCode != 429 && !isAuthExpired)
                        {
                            SentryApiErrorHandler.CaptureApiError("DeleteDirectory", (int)response.StatusCode, errorContent,
                                new Dictionary<string, string> { { "directoryId", directoryId } });
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            throw new Exception($"Rate limit (429): {errorContent}");
                        }
                        if (isAuthExpired)
                        {
                            Logger.Info($"DeleteDirectory: auth token rejected by server (401 isAuthTokenExpired) for directory '{directoryId}' — throwing for one-shot token-refresh retry");
                            throw new Exception($"Authorization token expired (401) in DeleteDirectory: {errorContent}");
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred in DeleteDirectory: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");

                    // Transient 429s and 401-auth-expired are reported once by ExecuteWithRetryAsync on
                    // retry/refresh exhaustion — skip them here to avoid double-reporting.
                    if (!IsRateLimitException(ex) && !IsAuthTokenExpiredException(ex))
                    {
                        SentryApiErrorHandler.CaptureException(ex, new Dictionary<string, string>
                        {
                            { "operation", "DeleteDirectory" },
                            { "directoryId", directoryId }
                        });
                    }

                    throw;
                }
            }, $"DeleteDirectory({directoryId})").ConfigureAwait(false);
        }
    }

    public class SyncMetadataResponse
    {
        [JsonProperty("directoryId")]
        public string DirectoryId { get; set; }

        [JsonProperty("files")]
        public List<SyncMetadataFile> Files { get; set; }
    }

    public class SyncMetadataFile
    {
        [JsonProperty("componentId")]
        public string ComponentId { get; set; }

        [JsonProperty("fileStored")]
        public bool FileStored { get; set; }

        [JsonProperty("parentStatus")]
        public string ParentStatus { get; set; }

        [JsonProperty("checkSum")]
        public string CheckSum { get; set; }

        [JsonProperty("filePathInDirectory")]
        public string FilePathInDirectory { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("childrenStatuses")]
        public Newtonsoft.Json.Linq.JToken ChildrenStatuses { get; set; }
    }
}