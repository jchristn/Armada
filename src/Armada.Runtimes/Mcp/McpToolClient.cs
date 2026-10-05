namespace Armada.Runtimes.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;

    /// <summary>
    /// A minimal client for the MCP streamable-HTTP transport. It opens a session against an MCP endpoint
    /// (<c>initialize</c> + server-assigned <c>Mcp-Session-Id</c> + <c>notifications/initialized</c>), lists
    /// the server's tools, and invokes them via <c>tools/call</c>. Responses are accepted as either plain
    /// <c>application/json</c> or a <c>text/event-stream</c> (SSE) body, from which the frame whose JSON-RPC id matches the request
    /// is selected (notifications on the same stream are skipped). Authentication is optional: when a session token is supplied it rides on every request
    /// as the <c>X-Token</c> header (the header Armada's authentication handler validates as a session
    /// token), and any additional caller-supplied headers are sent verbatim -- this is what lets a caller
    /// reach Armada's own MCP server scoped to a specific user, or an arbitrary external MCP server.
    /// </summary>
    public sealed class McpToolClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The endpoint URL this client targets.
        /// </summary>
        public string Endpoint => _Endpoint;

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly string _Endpoint;
        private readonly LoggingModule? _Logging;
        private readonly string _Header = "[McpToolClient] ";
        private string? _SessionId;
        private int _RpcId;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a client for a single MCP endpoint.
        /// </summary>
        /// <param name="endpoint">Absolute endpoint URL (for example http://127.0.0.1:8100/mcp).</param>
        /// <param name="sessionToken">Optional session token sent as the X-Token header on every request.</param>
        /// <param name="headers">Optional additional headers sent verbatim on every request.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="timeoutSeconds">Per-request timeout in seconds; clamped to 5..600.</param>
        public McpToolClient(
            string endpoint,
            string? sessionToken = null,
            IDictionary<string, string>? headers = null,
            LoggingModule? logging = null,
            int timeoutSeconds = 100)
            : this(endpoint, null, sessionToken, headers, logging, timeoutSeconds)
        {
        }

        /// <summary>
        /// Instantiate a client for a single MCP endpoint over a caller-supplied HTTP message handler (for example an
        /// in-memory test server). The handler is owned by the client and disposed with it.
        /// </summary>
        /// <param name="endpoint">Absolute endpoint URL (for example http://127.0.0.1:8100/mcp).</param>
        /// <param name="handler">HTTP message handler, or null for the default network handler.</param>
        /// <param name="sessionToken">Optional session token sent as the X-Token header on every request.</param>
        /// <param name="headers">Optional additional headers sent verbatim on every request.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="timeoutSeconds">Per-request timeout in seconds; clamped to 5..600.</param>
        public McpToolClient(
            string endpoint,
            HttpMessageHandler? handler,
            string? sessionToken = null,
            IDictionary<string, string>? headers = null,
            LoggingModule? logging = null,
            int timeoutSeconds = 100)
        {
            if (String.IsNullOrWhiteSpace(endpoint)) throw new ArgumentNullException(nameof(endpoint));

            _Endpoint = endpoint.Trim();
            _Logging = logging;
            _Http = handler != null ? new HttpClient(handler, true) : new HttpClient();
            _Http.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 600));

            if (!String.IsNullOrWhiteSpace(sessionToken))
                _Http.DefaultRequestHeaders.TryAddWithoutValidation("X-Token", sessionToken);

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (String.IsNullOrWhiteSpace(header.Key)) continue;
                    _Http.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value ?? String.Empty);
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the MCP session: send <c>initialize</c>, capture the server-assigned session id, then send the
        /// <c>notifications/initialized</c> acknowledgement. Safe to call once before listing or calling tools.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="McpClientException">Thrown on a transport or JSON-RPC protocol error.</exception>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            await SendAsync<JsonRpcEmptyResult>("initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "armada-api-runtime", version = "1.0" }
            }, token).ConfigureAwait(false);

            await SendNotificationAsync("notifications/initialized", token).ConfigureAwait(false);
        }

        /// <summary>
        /// List the tools advertised by the server.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The advertised tools; empty when the server advertises none.</returns>
        /// <exception cref="McpClientException">Thrown on a transport or JSON-RPC protocol error.</exception>
        public async Task<List<McpRemoteTool>> ListToolsAsync(CancellationToken token = default)
        {
            List<McpRemoteTool> tools = new List<McpRemoteTool>();
            string? cursor = null;

            // tools/list is paginated: follow nextCursor until the server stops issuing one (bounded so a
            // misbehaving server cannot loop forever).
            for (int page = 0; page < 100; page++)
            {
                object parameters = cursor == null ? (object)new { } : new { cursor = cursor };
                McpListToolsResult? result = await SendAsync<McpListToolsResult>("tools/list", parameters, token).ConfigureAwait(false);
                if (result == null) break;

                foreach (McpRemoteTool tool in result.Tools)
                {
                    if (tool != null && !String.IsNullOrWhiteSpace(tool.Name)) tools.Add(tool);
                }

                cursor = result.NextCursor;
                if (String.IsNullOrEmpty(cursor)) break;
            }

            return tools;
        }

        /// <summary>
        /// Invoke a tool by name with the given arguments (serialized JSON object; empty or null is treated
        /// as no arguments) and return the concatenated text content of the tool result. Callers that must know
        /// whether the call failed use <see cref="CallToolResultAsync"/> and read <see cref="McpToolCallResult.IsError"/>.
        /// </summary>
        /// <param name="name">Tool name as advertised by the server.</param>
        /// <param name="argumentsJson">Arguments as a serialized JSON object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool result text.</returns>
        /// <exception cref="McpClientException">Thrown on a transport or JSON-RPC protocol error.</exception>
        public async Task<string> CallToolAsync(string name, string? argumentsJson, CancellationToken token = default)
        {
            McpToolCallResult result = await CallToolResultAsync(name, argumentsJson, token).ConfigureAwait(false);
            return RenderResultText(result);
        }

        /// <summary>
        /// Invoke a tool and return the deserialized result, including the server's <c>isError</c> flag, so callers
        /// decide success from the protocol rather than from the result text.
        /// </summary>
        /// <param name="name">Tool name as advertised by the server.</param>
        /// <param name="argumentsJson">Arguments as a serialized JSON object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool call result.</returns>
        /// <exception cref="McpClientException">Thrown on a transport or JSON-RPC protocol error.</exception>
        public async Task<McpToolCallResult> CallToolResultAsync(string name, string? argumentsJson, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

            McpToolCallParams parameters = new McpToolCallParams();
            parameters.Name = name;
            parameters.Arguments = NormalizeArguments(argumentsJson);

            McpToolCallResult? result = await SendAsync<McpToolCallResult>("tools/call", parameters, token).ConfigureAwait(false);
            return result ?? new McpToolCallResult();
        }

        /// <summary>
        /// Render a tool result as text: text parts verbatim, any other part (image, audio, resource) as its JSON so
        /// nothing is silently dropped, joined with newlines.
        /// </summary>
        /// <param name="result">The tool call result.</param>
        /// <returns>The rendered text; empty when the result has no content.</returns>
        public static string RenderResultText(McpToolCallResult? result)
        {
            if (result == null) return String.Empty;

            StringBuilder builder = new StringBuilder();
            foreach (McpToolContentPart part in result.Content)
            {
                if (part == null) continue;
                if (builder.Length > 0) builder.Append('\n');
                if (String.Equals(part.Type, "text", StringComparison.Ordinal) && part.Text != null) builder.Append(part.Text);
                else builder.Append(JsonSerializer.Serialize(part));
            }

            if (builder.Length == 0 && !String.IsNullOrEmpty(result.StructuredContent)) builder.Append(result.StructuredContent);
            return builder.ToString();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try { _Http.Dispose(); } catch { }
        }

        #endregion

        #region Private-Methods

        private static string NormalizeArguments(string? argumentsJson)
        {
            if (String.IsNullOrWhiteSpace(argumentsJson)) return "{}";
            string trimmed = argumentsJson!.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return "{}";

            try
            {
                // Validate only; the arguments are forwarded verbatim.
                using (JsonDocument.Parse(trimmed)) { }
                return trimmed;
            }
            catch (JsonException)
            {
                return "{}";
            }
        }

        private string NextId()
        {
            return Interlocked.Increment(ref _RpcId).ToString(CultureInfo.InvariantCulture);
        }

        private async Task<T?> SendAsync<T>(string method, object parameters, CancellationToken token) where T : class
        {
            string id = NextId();
            HttpRequestMessage request = BuildRequest(new
            {
                jsonrpc = "2.0",
                id = Int64.Parse(id, CultureInfo.InvariantCulture),
                method = method,
                @params = parameters
            });

            using HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                JsonRpcResponse<T>? errorEnvelope = TryReadResponse<T>(body, id);
                int? rpcCode = errorEnvelope?.Error?.Code;
                throw new McpClientException(
                    "MCP request '" + method + "' to " + _Endpoint + " failed with " + status + ": " + Truncate(body, 500),
                    status,
                    rpcCode);
            }

            if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
            {
                foreach (string value in values)
                {
                    if (!String.IsNullOrWhiteSpace(value)) { _SessionId = value; break; }
                }
            }

            JsonRpcResponse<T>? envelope = McpResponseReader.ReadResponse<T>(body, id);
            if (envelope == null)
                throw new McpClientException("MCP request '" + method + "' to " + _Endpoint + " returned no JSON-RPC response for request " + id + ".", (int)response.StatusCode, null);

            if (envelope.Error != null)
                throw new McpClientException("MCP error from '" + method + "': " + envelope.Error.Message, (int)response.StatusCode, envelope.Error.Code);

            return envelope.Result;
        }

        private static JsonRpcResponse<T>? TryReadResponse<T>(string body, string id) where T : class
        {
            try
            {
                return McpResponseReader.ReadResponse<T>(body, id);
            }
            catch (McpClientException)
            {
                return null;
            }
        }

        private async Task SendNotificationAsync(string method, CancellationToken token)
        {
            HttpRequestMessage request = BuildRequest(new
            {
                jsonrpc = "2.0",
                method = method,
                @params = new { }
            });

            try
            {
                using HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false);
                _ = response;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !token.IsCancellationRequested))
            {
                // A failed initialized-notification is not fatal; the session id is already captured.
                _Logging?.Debug(_Header + "notification '" + method + "' to " + _Endpoint + " failed: " + ex.Message);
            }
        }

        private HttpRequestMessage BuildRequest(object payload)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _Endpoint);
            string json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (!String.IsNullOrEmpty(_SessionId)) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _SessionId);
            return request;
        }

        private static string Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value) || value!.Length <= max) return value ?? String.Empty;
            return value.Substring(0, max) + "... (truncated)";
        }

        #endregion
    }
}
