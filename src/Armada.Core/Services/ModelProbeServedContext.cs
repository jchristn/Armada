namespace Armada.Core.Services
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Reads the context an endpoint actually serves for a model. Only Ollama reports it (GET /api/ps, for a loaded
    /// model); for every other provider and for vendor CLI runtimes the served-context probe records could-not-run.
    /// </summary>
    public static class ModelProbeServedContext
    {
        #region Public-Methods

        /// <summary>
        /// Why the served-context probe cannot apply to a captain, or null when it can.
        /// </summary>
        /// <param name="captain">Captain probed.</param>
        /// <param name="endpoint">Its model endpoint, or null.</param>
        /// <param name="harborId">Harbor the probes ran on, or null for the Admiral's host.</param>
        /// <returns>The reason, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        public static string? CouldNotRunReason(Captain captain, ModelEndpoint? endpoint, string? harborId)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (endpoint == null)
                return "The " + captain.Runtime + " runtime does not report the context it serves (a vendor CLI with no model endpoint).";
            if (endpoint.Provider != ModelProviderEnum.Ollama)
                return "The " + endpoint.Provider + " provider does not report the context it serves; only Ollama does.";
            string? root = OllamaRoot(endpoint.BaseUrl);
            if (root == null) return "The endpoint's base URL is not an http or https URL.";
            if (!String.IsNullOrEmpty(harborId) && Uri.TryCreate(root, UriKind.Absolute, out Uri? uri) && uri.IsLoopback)
                return "The endpoint is a loopback address on Harbor " + harborId + ", which the Admiral cannot reach.";
            return null;
        }

        /// <summary>
        /// Read the served context of a loaded model from Ollama's GET /api/ps.
        /// </summary>
        /// <param name="endpoint">Ollama endpoint.</param>
        /// <param name="model">Model.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The context length, or null when the model is not loaded or the server does not report it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static async Task<int?> ReadOllamaAsync(ModelEndpoint endpoint, string? model, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            string? root = OllamaRoot(endpoint.BaseUrl);
            if (root == null) return null;

            HttpClient? configured = ModelEndpointAuth.CreateHttpClient(endpoint);
            using (HttpClient http = configured ?? new HttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, root + "/api/ps"))
            {
                http.Timeout = TimeSpan.FromSeconds(10);
                string secret = ModelEndpointAuth.ProviderSecret(endpoint);
                if (!String.IsNullOrEmpty(secret)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return FindContext(body, model ?? endpoint.Model);
                }
            }
        }

        /// <summary>
        /// Find a model's context length in an /api/ps body.
        /// </summary>
        /// <param name="body">Response body.</param>
        /// <param name="model">Model, or null for the only loaded model.</param>
        /// <returns>The context length, or null.</returns>
        public static int? FindContext(string? body, string? model)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            OllamaPsResponse? ps;
            try
            {
                ps = JsonSerializer.Deserialize<OllamaPsResponse>(body);
            }
            catch (JsonException)
            {
                return null;
            }

            if (ps?.Models == null || ps.Models.Count == 0) return null;
            if (String.IsNullOrWhiteSpace(model)) return ps.Models.Count == 1 ? ps.Models[0].ContextLength : null;
            string wanted = Normalize(model);
            foreach (OllamaPsModel loaded in ps.Models)
            {
                if (loaded == null) continue;
                if (String.Equals(Normalize(loaded.Name), wanted, StringComparison.Ordinal)
                    || String.Equals(Normalize(loaded.Model), wanted, StringComparison.Ordinal))
                    return loaded.ContextLength;
            }

            return null;
        }

        #endregion

        #region Private-Methods

        internal static string? OllamaRoot(string? baseUrl)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) return null;
            string root = baseUrl.Trim().TrimEnd('/');
            if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) root = root.Substring(0, root.Length - 3);
            else if (root.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) root = root.Substring(0, root.Length - 4);
            return Uri.TryCreate(root, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? root : null;
        }

        private static string Normalize(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return String.Empty;
            string trimmed = name.Trim().ToLowerInvariant();
            return trimmed.Contains(':') ? trimmed : trimmed + ":latest";
        }

        #endregion
    }
}
