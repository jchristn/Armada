namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;

    /// <summary>
    /// HTTP, credential, JSON, error, and paging plumbing shared by every sub-client of one <see cref="ArmadaClient"/>.
    /// Requests are sent with the server's PascalCase JSON; non-success responses raise <see cref="ArmadaApiException"/>,
    /// and a 401 raises <see cref="ArmadaClient.Unauthorized"/> first. Thread-safe for concurrent requests.
    /// </summary>
    internal sealed class ArmadaTransport : IDisposable
    {
        #region Internal-Members

        /// <summary>
        /// Connection options shared with the owning client.
        /// </summary>
        internal ArmadaClientOptions Options { get; }

        /// <summary>
        /// Time source; request durations use its monotonic clock.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Time;
            set => _Time = value ?? throw new ArgumentNullException(nameof(Time));
        }

        #endregion

        #region Private-Members

        private readonly ArmadaClient _Owner;
        private readonly HttpClient _Http;
        private bool _Disposed = false;
        private TimeProvider _Time = TimeProvider.System;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="owner">Client whose events are raised.</param>
        /// <param name="options">Connection options.</param>
        /// <param name="handler">Optional HTTP handler; null uses the default handler.</param>
        internal ArmadaTransport(ArmadaClient owner, ArmadaClientOptions options, HttpMessageHandler? handler)
        {
            _Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _Http = handler != null ? new HttpClient(handler, false) : new HttpClient();
            _Http.Timeout = Timeout.InfiniteTimeSpan;
        }

        #endregion

        #region Internal-Methods

        internal async Task<HttpResponseMessage> SendRawAsync(
            HttpMethod method,
            string pathAndQuery,
            HttpContent? content,
            IDictionary<string, string>? extraHeaders,
            int? timeoutMs,
            CancellationToken token)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (pathAndQuery == null) throw new ArgumentNullException(nameof(pathAndQuery));
            string requestId = NewRequestId();
            using (HttpRequestMessage request = BuildRequest(method, pathAndQuery, requestId))
            {
                request.Content = content;
                if (extraHeaders != null)
                {
                    foreach (KeyValuePair<string, string> kvp in extraHeaders)
                    {
                        request.Headers.Remove(kvp.Key);
                        request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                }

                ArmadaRequestOptions options = new ArmadaRequestOptions();
                options.TimeoutMs = timeoutMs;
                return await SendCoreAsync(request, method.Method, pathAndQuery, requestId, options, token).ConfigureAwait(false);
            }
        }

        internal Task<T?> GetAsync<T>(string path, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Get, path, null, false, options, token);
        }

        internal Task<T?> PostAsync<T>(string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Post, path, body, true, options, token);
        }

        /// <summary>
        /// Every row of a paged <c>POST .../enumerate</c> route, read 1000 at a time. Each request returns one page.
        /// </summary>
        internal async Task<List<T>?> EnumerateAllAsync<T>(string path, Dictionary<string, object?>? filters, CancellationToken token)
        {
            return await ArmadaPaging.ReadAllAsync<T>((pageNumber, pageToken) =>
            {
                Dictionary<string, object?> body = filters != null ? new Dictionary<string, object?>(filters) : new Dictionary<string, object?>();
                body["PageNumber"] = pageNumber;
                body["PageSize"] = 1000;
                return PostAsync<Armada.Core.Models.EnumerationResult<T>>(path, body, null, pageToken);
            }, ArmadaPaging.DefaultMaxPages, token).ConfigureAwait(false);
        }

        internal Task<T?> PutAsync<T>(string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Put, path, body, true, options, token);
        }

        internal Task<T?> PatchAsync<T>(string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Patch, path, body, true, options, token);
        }

        internal Task<T?> DeleteAsync<T>(string path, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Delete, path, null, false, options, token);
        }

        internal async Task SendNoResultAsync(HttpMethod method, string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            await SendJsonAsync<ArmadaRawJson>(method, path, body, method != HttpMethod.Get && method != HttpMethod.Delete, options, token).ConfigureAwait(false);
        }

        internal void RaiseUnauthorized(ArmadaApiException ex)
        {
            _Owner.OnUnauthorized(ex);
        }

        /// <summary>
        /// Dispose the HTTP client.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Http.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<T?> SendJsonAsync<T>(
            HttpMethod method,
            string path,
            object? body,
            bool sendBody,
            ArmadaRequestOptions? options,
            CancellationToken token)
        {
            string requestId = NewRequestId();
            using (HttpRequestMessage request = BuildRequest(method, path, requestId))
            {
                if (sendBody)
                {
                    string json = body == null ? "{}" : ArmadaJson.Serialize(body);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using (HttpResponseMessage response = await SendCoreAsync(request, method.Method, path, requestId, options, token).ConfigureAwait(false))
                {
                    string text = response.Content != null
                        ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                        : "";

                    int status = (int)response.StatusCode;
                    bool accepted = options != null && options.AcceptStatuses.Contains(status);
                    if (!response.IsSuccessStatusCode && !accepted)
                    {
                        ArmadaApiException ex = BuildException(method.Method, path, requestId, response, text);
                        if (ex.IsUnauthorized) RaiseUnauthorized(ex);
                        throw ex;
                    }

                    if (typeof(T) == typeof(ArmadaRawJson)) return (T)(object)new ArmadaRawJson(text);
                    if (typeof(T) == typeof(string)) return (T)(object)text;
                    if (response.StatusCode == HttpStatusCode.NoContent || String.IsNullOrWhiteSpace(text)) return default;

                    try
                    {
                        return ArmadaJson.Deserialize<T>(text);
                    }
                    catch (JsonException jex)
                    {
                        throw new ArmadaApiException(
                            "The server response could not be read as " + typeof(T).Name + ": " + jex.Message,
                            status, "invalid_response", null, requestId, method.Method, path, text, null, jex);
                    }
                }
            }
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string pathAndQuery, string requestId)
        {
            Uri uri = pathAndQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || pathAndQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(pathAndQuery)
                : new Uri(Options.BaseUrl + (pathAndQuery.StartsWith("/") ? pathAndQuery : "/" + pathAndQuery));

            HttpRequestMessage request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", Options.UserAgent);
            request.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
            if (!String.IsNullOrEmpty(Options.AcceptLanguage))
                request.Headers.TryAddWithoutValidation("Accept-Language", Options.AcceptLanguage);
            if (!String.IsNullOrEmpty(Options.BearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.BearerToken);
            if (!String.IsNullOrEmpty(Options.Token))
                request.Headers.TryAddWithoutValidation("X-Token", Options.Token);
            if (!String.IsNullOrEmpty(Options.ApiKey))
                request.Headers.TryAddWithoutValidation("X-Api-Key", Options.ApiKey);
            if (!String.IsNullOrEmpty(Options.ProxySessionToken))
                request.Headers.TryAddWithoutValidation("X-Armada-Proxy-Session", Options.ProxySessionToken);
            return request;
        }

        private async Task<HttpResponseMessage> SendCoreAsync(
            HttpRequestMessage request,
            string method,
            string path,
            string requestId,
            ArmadaRequestOptions? options,
            CancellationToken token)
        {
            int timeoutMs = options != null && options.TimeoutMs.HasValue ? options.TimeoutMs.Value : Options.TimeoutMs;
            long started = _Time.GetTimestamp();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(timeoutMs);
                try
                {
                    HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                    _Owner.OnRequestCompleted(method, path, (int)response.StatusCode, _Time.GetElapsedTime(started));
                    return response;
                }
                catch (OperationCanceledException oce) when (!token.IsCancellationRequested)
                {
                    _Owner.OnRequestCompleted(method, path, 0, _Time.GetElapsedTime(started));
                    throw new ArmadaApiException("Request timed out", 0, ArmadaApiException.TimeoutCode, null, requestId, method, path, null, null, oce);
                }
                catch (HttpRequestException hre)
                {
                    _Owner.OnRequestCompleted(method, path, 0, _Time.GetElapsedTime(started));
                    throw new ArmadaApiException(
                        "Could not reach the Armada server at " + Options.BaseUrl + ": " + hre.Message,
                        0, "unreachable", null, requestId, method, path, null, null, hre);
                }
            }
        }

        internal static ArmadaApiException BuildException(string method, string path, string requestId, HttpResponseMessage response, string text)
        {
            int status = (int)response.StatusCode;
            string message = status + ": " + (String.IsNullOrWhiteSpace(text) ? response.ReasonPhrase : text);
            string? code = null;
            string? errorName = null;
            string? serverRequestId = null;
            ApiErrorData? data = null;

            if (!String.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    ApiErrorBody? body = ArmadaJson.Deserialize<ApiErrorBody>(text);
                    if (body != null)
                    {
                        string? picked = !String.IsNullOrEmpty(body.Message) ? body.Message
                            : !String.IsNullOrEmpty(body.Description) ? body.Description
                            : body.Error;
                        if (!String.IsNullOrEmpty(picked)) message = picked!;
                        errorName = body.Error;
                        data = body.Data;
                        code = !String.IsNullOrEmpty(body.Data?.Code) ? body.Data!.Code : body.Code;
                        serverRequestId = body.RequestId ?? body.Data?.RequestId;
                    }
                }
                catch (JsonException)
                {
                    // Keep the raw text message.
                }
            }

            if (status == 401 && String.IsNullOrEmpty(code)) code = "unauthorized";

            if (response.Headers.TryGetValues("X-Request-Id", out IEnumerable<string>? values))
            {
                foreach (string value in values)
                {
                    if (!String.IsNullOrEmpty(value)) serverRequestId = value;
                }
            }

            return new ArmadaApiException(message, status, code, errorName, serverRequestId ?? requestId, method, path, text, data);
        }

        private static string NewRequestId()
        {
            return "req_" + Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        #endregion
    }
}
