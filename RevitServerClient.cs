using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace BCCPlugIn
{
    public class RevitServerClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _userName;
        private readonly string _machineName;

        public RevitServerClient(string host, string version)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host cannot be empty", nameof(host));

            // Clean host name (remove http:// if user accidentally added it)
            string cleanHost = host.Replace("http://", "").Replace("https://", "").Trim('/');

            // Format: http://<host>/RevitServerAdminRESTService<version>/AdminRESTService.svc
            _baseUrl = $"http://{cleanHost}/RevitServerAdminRESTService{version}/AdminRESTService.svc";
            
            _userName = Environment.UserName;
            _machineName = Environment.MachineName;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Sends GET request to Revit Server with required headers and deserializes the JSON response.
        /// </summary>
        private async Task<T> GetAsync<T>(string relativeUrl)
        {
            string url = $"{_baseUrl}/{relativeUrl.TrimStart('/')}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                // Revit Server Admin REST API required headers
                request.Headers.Add("User-Name", _userName);
                request.Headers.Add("User-Machine-Name", _machineName);
                request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString());

                using (var response = await _httpClient.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    
                    string json = await response.Content.ReadAsStringAsync();
                    
                    using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(T));
                        return (T)serializer.ReadObject(ms);
                    }
                }
            }
        }

        /// <summary>
        /// Verifies connection to the server by fetching properties.
        /// </summary>
        public async Task<ServerProperties> CheckConnectionAsync()
        {
            return await GetAsync<ServerProperties>("serverProperties");
        }

        /// <summary>
        /// Fetches folders and models under the specified path.
        /// </summary>
        /// <param name="serverRelativePath">Pipe-delimited path, e.g., "FolderA|FolderB". Empty string or "|" for root.</param>
        public async Task<FolderContents> GetContentsAsync(string serverRelativePath)
        {
            string formattedPath;
            if (string.IsNullOrWhiteSpace(serverRelativePath) || serverRelativePath == "|")
            {
                // Root is represented as a space character (URL encoded as %20)
                formattedPath = "%20";
            }
            else
            {
                // Escape URL path, keeping the pipe characters or replacing them with %7C.
                // We split by '|' to escape folder names individually, then join with "%7C"
                string[] parts = serverRelativePath.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                string[] escapedParts = new string[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    escapedParts[i] = Uri.EscapeDataString(parts[i]);
                }
                formattedPath = string.Join("%7C", escapedParts);
            }

            return await GetAsync<FolderContents>($"{formattedPath}/contents");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
