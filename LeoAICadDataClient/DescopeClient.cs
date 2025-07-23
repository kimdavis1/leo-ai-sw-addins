using LeoAICadDataClient.Utilities;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace LeoAICadDataClient
{
    public class DescopeClient
    {
        private readonly string _projectId;
        private readonly string _descopeApiUrl;

        public DescopeClient(string projectId, string descopeApiUrl)
        {
            _projectId = projectId;
            _descopeApiUrl = descopeApiUrl;
            // Enable TLS 1.2
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public async Task<string> ExchangeTokenAsync(string accessKey)
        {
            var handler = new HttpClientHandler { UseProxy = false };

            using (var httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36");
                try
                {
                    Logger.Info("Attempting to exchange token with Descope using HttpClient.");
                    var requestUrl = $"{_descopeApiUrl}/v1/auth/accesskey/exchange";
                    
                    var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                    
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{_projectId}:{accessKey}");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                    Logger.Info($"Sending request to Descope API...");
                    
                    using (var response = await httpClient.SendAsync(request))
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Logger.Info($"Received response with status code: {response.StatusCode}. Response: {responseContent}");

                        if (response.IsSuccessStatusCode)
                        {
                            dynamic result = JsonConvert.DeserializeObject(responseContent);
                            return result.sessionJwt;
                        }
                        else
                        {
                            Logger.Error("Failed to exchange token.");
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"An exception occurred during token exchange: {ex.Message}");
                    Logger.Error($"Exception Details: {ex}");
                    return null;
                }
            }
        }
    }
}