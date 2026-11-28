namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Reads the context window a model server runs a model with, for endpoints that configure none
    /// (RELIABILITY_SIMPLICITY_PLAN.md RS-7.8). Ollama reports the served window of a loaded model in GET /api/ps and the
    /// Modelfile's num_ctx in POST /api/show; vLLM and other OpenAI-compatible servers that report it give max_model_len in
    /// GET /v1/models. The model's trained maximum (Ollama model_info) is never used: Ollama does not serve it unless asked,
    /// so compacting against it would let history overflow the real window. A configured value always wins, and discovery
    /// runs on the host that serves the run (the Admiral, or the Harbor for a Harbor launch).
    /// </summary>
    public static class ModelContextDiscovery
    {
        #region Public-Members

        /// <summary>
        /// Smallest window accepted (512, the InferenceOptions.ContextLength minimum).
        /// </summary>
        public const int MinContextLength = 512;

        /// <summary>
        /// Largest window accepted (2097152, the InferenceOptions.ContextLength maximum).
        /// </summary>
        public const int MaxContextLength = 2097152;

        /// <summary>
        /// Source label for Ollama's GET /api/ps.
        /// </summary>
        public const string OllamaPsSource = "ollama /api/ps";

        /// <summary>
        /// Source label for the num_ctx parameter in Ollama's POST /api/show.
        /// </summary>
        public const string OllamaShowSource = "ollama /api/show num_ctx";

        /// <summary>
        /// Source label for max_model_len in an OpenAI-compatible GET /v1/models.
        /// </summary>
        public const string ModelListSource = "/v1/models max_model_len";

        #endregion

        #region Private-Members

        private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(5);

        private static readonly Regex _NumCtx = new Regex(@"^\s*num_ctx\s+(\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether discovery can apply to an endpoint: an Inference endpoint on Ollama, Ollama Cloud, or an
        /// OpenAI-compatible server.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>True when the provider can report a window.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static bool Applies(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (endpoint.Kind != ModelEndpointKindEnum.Inference) return false;
            return endpoint.Provider == ModelProviderEnum.Ollama
                || endpoint.Provider == ModelProviderEnum.OllamaCloud
                || endpoint.Provider == ModelProviderEnum.OpenAICompatible;
        }

        /// <summary>
        /// The window a run uses: the configured value, else the discovered one, else null.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Window in tokens, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static int? WindowOf(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            return endpoint.InferenceOptions?.ContextLength ?? endpoint.DiscoveredContextLength;
        }

        /// <summary>
        /// Where the window a run uses comes from, or null when there is none.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>The source, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static ContextLengthSourceEnum? SourceOf(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (endpoint.InferenceOptions?.ContextLength.HasValue == true) return ContextLengthSourceEnum.Configured;
            if (endpoint.DiscoveredContextLength.HasValue) return ContextLengthSourceEnum.Discovered;
            return null;
        }

        /// <summary>
        /// Give a run snapshot of an endpoint the discovered window when it configures none, so compaction and the
        /// context-limit stop apply. On Ollama this also sends the window as num_ctx; the value came from the server, so it
        /// matches what the server already runs and does not reload the model.
        /// </summary>
        /// <param name="snapshot">Run snapshot of the endpoint (never the stored endpoint).</param>
        /// <returns>True when the discovered window was applied.</returns>
        /// <exception cref="ArgumentNullException">Thrown when snapshot is null.</exception>
        public static bool ApplyWindow(ModelEndpoint snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.InferenceOptions.ContextLength.HasValue || !IsAcceptable(snapshot.DiscoveredContextLength)) return false;
            snapshot.InferenceOptions.ContextLength = snapshot.DiscoveredContextLength;
            return true;
        }

        /// <summary>
        /// Record a discovery result on an endpoint. A result with no value leaves an earlier discovered value in place.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="result">Result.</param>
        /// <param name="utc">When it was discovered.</param>
        /// <returns>True when the stored window or source changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or result is null.</exception>
        public static bool Apply(ModelEndpoint endpoint, ModelContextDiscoveryResult result, DateTime utc)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!result.Found) return false;
            bool changed = endpoint.DiscoveredContextLength != result.ContextLength
                || !String.Equals(endpoint.DiscoveredContextSource, result.Source, StringComparison.Ordinal);
            endpoint.DiscoveredContextLength = result.ContextLength;
            endpoint.DiscoveredContextSource = result.Source;
            endpoint.ContextDiscoveredUtc = utc;
            return changed;
        }

        /// <summary>
        /// Read the window the endpoint's server runs a model with. Never throws for server or network failures: the
        /// result then carries the reason.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="model">Model to look up, or null for the endpoint's model.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static async Task<ModelContextDiscoveryResult> DiscoverAsync(ModelEndpoint endpoint, string? model = null, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            ModelContextDiscoveryResult result = new ModelContextDiscoveryResult();
            if (!Applies(endpoint))
            {
                result.Reason = "The " + endpoint.Provider + " provider does not report the context it serves.";
                return result;
            }

            string? wanted = String.IsNullOrWhiteSpace(model) ? endpoint.Model : model;
            try
            {
                if (endpoint.Provider == ModelProviderEnum.OpenAICompatible)
                {
                    string? modelsUrl = ModelListUrl(endpoint.BaseUrl);
                    if (modelsUrl == null) return Reason(result, "The endpoint's base URL is not an http or https URL.");
                    string? body = await SendAsync(endpoint, HttpMethod.Get, modelsUrl, null, token).ConfigureAwait(false);
                    return Found(result, FindMaxModelLen(body, wanted), ModelListSource)
                        ?? Reason(result, "The server's /v1/models does not report max_model_len for " + (wanted ?? "the model") + ".");
                }

                string? root = ModelProbeServedContext.OllamaRootOf(endpoint.BaseUrl);
                if (root == null) return Reason(result, "The endpoint's base URL is not an http or https URL.");
                string? ps = await SendAsync(endpoint, HttpMethod.Get, root + "/api/ps", null, token).ConfigureAwait(false);
                ModelContextDiscoveryResult? loaded = Found(result, ModelProbeServedContext.FindContext(ps, wanted), OllamaPsSource);
                if (loaded != null) return loaded;
                if (String.IsNullOrWhiteSpace(wanted)) return Reason(result, "The model is not loaded and the endpoint names no model to look up.");

                string request = JsonSerializer.Serialize(new OllamaShowRequest { Model = wanted! });
                string? show = await SendAsync(endpoint, HttpMethod.Post, root + "/api/show", request, token).ConfigureAwait(false);
                return Found(result, ParseShowNumCtx(show), OllamaShowSource)
                    ?? Reason(result, "Ollama does not have " + wanted + " loaded and its Modelfile sets no num_ctx; the server default applies.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return Reason(result, "The model server did not answer within " + (int)_Timeout.TotalSeconds + " s.");
            }
            catch (HttpRequestException ex)
            {
                return Reason(result, "The model server could not be reached: " + ex.Message);
            }
        }

        /// <summary>
        /// The num_ctx parameter in an Ollama /api/show body, or null.
        /// </summary>
        /// <param name="body">Response body.</param>
        /// <returns>The window, or null when absent or out of range.</returns>
        public static int? ParseShowNumCtx(string? body)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            OllamaShowResponse? show;
            try
            {
                show = JsonSerializer.Deserialize<OllamaShowResponse>(body);
            }
            catch (JsonException)
            {
                return null;
            }

            if (show == null || String.IsNullOrWhiteSpace(show.Parameters)) return null;
            Match match = _NumCtx.Match(show.Parameters);
            if (!match.Success) return null;
            if (!Int32.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) return null;
            return IsAcceptable(value) ? value : null;
        }

        /// <summary>
        /// A model's max_model_len in an OpenAI-compatible /v1/models body, or null.
        /// </summary>
        /// <param name="body">Response body.</param>
        /// <param name="model">Model, or null for the only listed model.</param>
        /// <returns>The window, or null when absent or out of range.</returns>
        public static int? FindMaxModelLen(string? body, string? model)
        {
            if (String.IsNullOrWhiteSpace(body)) return null;
            OpenAiModelListResponse? list;
            try
            {
                list = JsonSerializer.Deserialize<OpenAiModelListResponse>(body);
            }
            catch (JsonException)
            {
                return null;
            }

            if (list?.Data == null || list.Data.Count == 0) return null;
            int? value = null;
            if (String.IsNullOrWhiteSpace(model))
            {
                if (list.Data.Count == 1) value = list.Data[0]?.MaxModelLen;
            }
            else
            {
                foreach (OpenAiModelListEntry entry in list.Data)
                {
                    if (entry != null && String.Equals(entry.Id?.Trim(), model.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        value = entry.MaxModelLen;
                        break;
                    }
                }
            }

            return IsAcceptable(value) ? value : null;
        }

        #endregion

        #region Private-Methods

        private static bool IsAcceptable(int? value)
        {
            return value.HasValue && value.Value >= MinContextLength && value.Value <= MaxContextLength;
        }

        private static ModelContextDiscoveryResult? Found(ModelContextDiscoveryResult result, int? value, string source)
        {
            if (!IsAcceptable(value)) return null;
            result.ContextLength = value;
            result.Source = source;
            result.Reason = null;
            return result;
        }

        private static ModelContextDiscoveryResult Reason(ModelContextDiscoveryResult result, string reason)
        {
            result.ContextLength = null;
            result.Source = null;
            result.Reason = reason;
            return result;
        }

        private static string? ModelListUrl(string? baseUrl)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) return null;
            string root = baseUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(root, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
            return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root + "/models" : root + "/v1/models";
        }

        private static async Task<string?> SendAsync(ModelEndpoint endpoint, HttpMethod method, string url, string? json, CancellationToken token)
        {
            HttpClient? configured = ModelEndpointAuth.CreateHttpClient(endpoint);
            using (HttpClient http = configured ?? new HttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                http.Timeout = _Timeout;
                string secret = ModelEndpointAuth.ProviderSecret(endpoint);
                if (!String.IsNullOrEmpty(secret)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
