namespace Armada.Core.Services
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Loads an Ollama model before the first agent turn (RS-7.16). A cold model can take minutes to load, and a proxy in
    /// front of Ollama may give up on the first byte before then (Conductor answers 504 after 300 s); sending a load
    /// request with <c>keep_alive</c> first means the first real turn reaches a model that is already in memory. The load
    /// request is <c>POST /api/generate</c> with a model and <c>keep_alive</c> and no prompt, which loads the model and
    /// generates nothing. Only Ollama endpoints are warmed; failures are reported, never thrown, so a caller can go on
    /// to the first turn and let its normal classification decide. Must run where the endpoint is reachable: on the
    /// serving Harbor in split mode. Thread safe.
    /// </summary>
    public static class OllamaModelWarmup
    {
        #region Public-Members

        /// <summary>Default <c>keep_alive</c> sent with the load request.</summary>
        public const string DefaultKeepAlive = "30m";

        /// <summary>Default time to wait for the load to finish, in seconds.</summary>
        public const int DefaultTimeoutSeconds = 300;

        /// <summary>Longest allowed load wait, in seconds.</summary>
        public const int MaxTimeoutSeconds = 3600;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an endpoint is warmed before the first turn: an Ollama inference endpoint with an http or https base URL.
        /// </summary>
        /// <param name="endpoint">Model endpoint, or null.</param>
        /// <returns>True when <see cref="WarmAsync"/> would send a load request.</returns>
        public static bool Applies(ModelEndpoint? endpoint)
        {
            return endpoint != null
                && endpoint.Provider == ModelProviderEnum.Ollama
                && ModelProbeServedContext.OllamaRoot(endpoint.BaseUrl) != null;
        }

        /// <summary>
        /// Send a load request so the model is in memory before the first turn.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="model">Model to load, or null for the endpoint's model.</param>
        /// <param name="keepAlive">Ollama <c>keep_alive</c> duration, or null for <see cref="DefaultKeepAlive"/>.</param>
        /// <param name="timeoutSeconds">Load wait in seconds, 1 to <see cref="MaxTimeoutSeconds"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome; a skipped or failed warm-up is a result, not an exception.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when timeoutSeconds is outside 1 to <see cref="MaxTimeoutSeconds"/>.</exception>
        public static async Task<OllamaWarmupResult> WarmAsync(
            ModelEndpoint endpoint,
            string? model = null,
            string? keepAlive = null,
            int timeoutSeconds = DefaultTimeoutSeconds,
            CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (timeoutSeconds < 1 || timeoutSeconds > MaxTimeoutSeconds)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "timeoutSeconds must be between 1 and " + MaxTimeoutSeconds + ".");

            OllamaWarmupResult result = new OllamaWarmupResult();
            if (endpoint.Provider != ModelProviderEnum.Ollama)
            {
                result.Reason = "The " + endpoint.Provider + " provider is not warmed; only Ollama endpoints are.";
                return result;
            }

            string? root = ModelProbeServedContext.OllamaRoot(endpoint.BaseUrl);
            if (root == null)
            {
                result.Reason = "The endpoint's base URL is not an http or https URL.";
                return result;
            }

            string? name = String.IsNullOrWhiteSpace(model) ? endpoint.Model : model!.Trim();
            if (String.IsNullOrWhiteSpace(name))
            {
                result.Reason = "The endpoint names no model to load.";
                return result;
            }

            OllamaWarmupRequest body = new OllamaWarmupRequest
            {
                Model = name!.Trim(),
                KeepAlive = String.IsNullOrWhiteSpace(keepAlive) ? DefaultKeepAlive : keepAlive!.Trim()
            };

            result.Attempted = true;
            Stopwatch watch = Stopwatch.StartNew();
            HttpClient? configured = ModelEndpointAuth.CreateHttpClient(endpoint);
            try
            {
                using (HttpClient http = configured ?? new HttpClient())
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, root + "/api/generate"))
                {
                    http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    string secret = ModelEndpointAuth.ProviderSecret(endpoint);
                    if (!String.IsNullOrEmpty(secret)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                    request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        result.HttpStatusCode = (int)response.StatusCode;
                        result.Succeeded = response.IsSuccessStatusCode;
                        if (!result.Succeeded)
                        {
                            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                            result.Reason = "The load request returned HTTP " + result.HttpStatusCode + ": " + Shorten(text);
                        }
                    }
                }
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                result.Reason = "The load request did not finish within " + timeoutSeconds + " seconds.";
            }
            catch (HttpRequestException ex)
            {
                result.Reason = "The load request failed: " + ex.Message;
            }
            catch (IOException ex)
            {
                result.Reason = "The load request failed: " + ex.Message;
            }
            finally
            {
                watch.Stop();
                result.ElapsedMs = watch.ElapsedMilliseconds;
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "(no body)";
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length > 300 ? flat.Substring(0, 297).TrimEnd() + "..." : flat;
        }

        #endregion
    }
}
