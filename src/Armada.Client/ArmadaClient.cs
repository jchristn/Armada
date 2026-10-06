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
    /// Typed client for the Armada REST API. It exposes one async method per server-calling function in the web
    /// dashboard's <c>api/client.ts</c>, with the same name in PascalCase plus <c>Async</c>, grouped by area in partial
    /// class files. Requests are sent with the server's PascalCase JSON; non-success responses raise
    /// <see cref="ArmadaApiException"/>; a 401 also raises <see cref="Unauthorized"/> first.
    /// Thread-safe for concurrent requests. <see cref="SetToken"/> may be called at any time; requests already in
    /// flight keep the credential they started with.
    /// </summary>
    public partial class ArmadaClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Connection options. Never null.
        /// </summary>
        public ArmadaClientOptions Options { get; }

        /// <summary>
        /// Base URL of the Admiral (no trailing slash).
        /// </summary>
        public string BaseUrl
        {
            get { return Options.BaseUrl; }
        }

        /// <summary>
        /// Raised before an <see cref="ArmadaApiException"/> is thrown for a 401 response (the dashboard's
        /// <c>setOnUnauthorized</c> hook). Raised on the thread that completed the request.
        /// </summary>
        public event EventHandler<ArmadaApiException>? Unauthorized;

        /// <summary>
        /// Raised after every completed request with the method, path, status code, and duration. Used for
        /// diagnostics; handlers must be cheap and must not throw.
        /// </summary>
        public event EventHandler<ArmadaRequestCompletedEventArgs>? RequestCompleted;

        /// <summary>
        /// Time source. Request durations are measured on its monotonic clock, never its wall clock, so a wall-clock
        /// jump (the host sleeping and waking, an NTP step) cannot inflate or negate them. Defaults to
        /// <see cref="TimeProvider.System"/>; tests substitute a provider whose wall clock jumps.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Time;
            set => _Time = value ?? throw new ArgumentNullException(nameof(Time));
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private bool _Disposed = false;
        private TimeProvider _Time = TimeProvider.System;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a base URL with no credentials.
        /// </summary>
        /// <param name="baseUrl">Absolute http(s) base URL of the Admiral.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is not an absolute http(s) URL.</exception>
        public ArmadaClient(string baseUrl) : this(new ArmadaClientOptions(baseUrl), null)
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connection options.</param>
        /// <param name="handler">Optional HTTP handler (tests pass a stub); null uses the default handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public ArmadaClient(ArmadaClientOptions options, HttpMessageHandler? handler = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _Http = handler != null ? new HttpClient(handler, false) : new HttpClient();
            _Http.Timeout = Timeout.InfiniteTimeSpan;
            _OwnsHttp = true;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the session token sent as <c>X-Token</c> (the dashboard's <c>setAuthToken</c>). Null clears it.
        /// </summary>
        /// <param name="token">Session token or API key, or null.</param>
        public void SetToken(string? token)
        {
            Options.Token = String.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        /// <summary>
        /// Send an arbitrary request with the client's credentials and return the raw response (used by the API
        /// Explorer and for binary payloads). The caller owns and must dispose the response. Does not throw for
        /// non-success statuses.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="pathAndQuery">Path relative to the base URL, starting with <c>/</c>, or an absolute URL.</param>
        /// <param name="content">Optional request content.</param>
        /// <param name="extraHeaders">Optional extra headers.</param>
        /// <param name="timeoutMs">Optional timeout override in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="method"/> or <paramref name="pathAndQuery"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown on timeout or transport failure.</exception>
        public async Task<HttpResponseMessage> SendRawAsync(
            HttpMethod method,
            string pathAndQuery,
            HttpContent? content = null,
            IDictionary<string, string>? extraHeaders = null,
            int? timeoutMs = null,
            CancellationToken token = default)
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

        /// <summary>
        /// Dispose the HTTP client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Internal-Methods

        internal Task<T?> GetAsync<T>(string path, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Get, path, null, false, options, token);
        }

        internal Task<T?> PostAsync<T>(string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Post, path, body, true, options, token);
        }

        internal Task<T?> PutAsync<T>(string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Put, path, body, true, options, token);
        }

        internal Task<T?> DeleteAsync<T>(string path, ArmadaRequestOptions? options, CancellationToken token)
        {
            return SendJsonAsync<T>(HttpMethod.Delete, path, null, false, options, token);
        }

        internal async Task SendNoResultAsync(HttpMethod method, string path, object? body, ArmadaRequestOptions? options, CancellationToken token)
        {
            await SendJsonAsync<ArmadaRawJson>(method, path, body, method != HttpMethod.Get && method != HttpMethod.Delete, options, token).ConfigureAwait(false);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing && _OwnsHttp) _Http.Dispose();
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
                    RaiseCompleted(method, path, (int)response.StatusCode, _Time.GetElapsedTime(started));
                    return response;
                }
                catch (OperationCanceledException oce) when (!token.IsCancellationRequested)
                {
                    RaiseCompleted(method, path, 0, _Time.GetElapsedTime(started));
                    throw new ArmadaApiException("Request timed out", 0, ArmadaApiException.TimeoutCode, null, requestId, method, path, null, null, oce);
                }
                catch (HttpRequestException hre)
                {
                    RaiseCompleted(method, path, 0, _Time.GetElapsedTime(started));
                    throw new ArmadaApiException(
                        "Could not reach the Armada server at " + Options.BaseUrl + ": " + hre.Message,
                        0, "unreachable", null, requestId, method, path, null, null, hre);
                }
            }
        }

        private static ArmadaApiException BuildException(string method, string path, string requestId, HttpResponseMessage response, string text)
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

        private void RaiseUnauthorized(ArmadaApiException ex)
        {
            EventHandler<ArmadaApiException>? handler = Unauthorized;
            if (handler == null) return;
            try
            {
                handler(this, ex);
            }
            catch (Exception)
            {
                // A faulty observer must not mask the API error.
            }
        }

        private void RaiseCompleted(string method, string path, int status, TimeSpan duration)
        {
            EventHandler<ArmadaRequestCompletedEventArgs>? handler = RequestCompleted;
            if (handler == null) return;
            try
            {
                handler(this, new ArmadaRequestCompletedEventArgs(method, path, status, duration));
            }
            catch (Exception)
            {
                // Diagnostics observers must not break requests.
            }
        }

        private static string NewRequestId()
        {
            return "req_" + Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        private static string E(string? value)
        {
            return Armada.Client.Http.ArmadaQueryString.Escape(value);
        }

        #endregion
    }
}
