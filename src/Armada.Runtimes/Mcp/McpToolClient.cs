namespace Armada.Runtimes.Mcp
{
    using System;
    using System.Collections.Generic;
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
    /// <c>application/json</c> or a <c>text/event-stream</c> (SSE) body, from which the first JSON-RPC frame
    /// is extracted. Authentication is optional: when a session token is supplied it rides on every request
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
        {
            if (String.IsNullOrWhiteSpace(endpoint)) throw new ArgumentNullException(nameof(endpoint));

            _Endpoint = endpoint.Trim();
            _Logging = logging;
            _Http = new HttpClient();
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
        public async Task InitializeAsync(CancellationToken token = default)
        {
            JsonElement result = await SendAsync("initialize", NextId(), new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "armada-api-runtime", version = "1.0" }
            }, token).ConfigureAwait(false);
            _ = result;

            await SendNotificationAsync("notifications/initialized", token).ConfigureAwait(false);
        }

        /// <summary>
        /// List the tools advertised by the server.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The advertised tools; empty when the server advertises none.</returns>
        public async Task<List<McpRemoteTool>> ListToolsAsync(CancellationToken token = default)
        {
            List<McpRemoteTool> tools = new List<McpRemoteTool>();
            JsonElement result = await SendAsync("tools/list", NextId(), new { }, token).ConfigureAwait(false);

            if (result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("tools", out JsonElement toolsElement)
                && toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement toolElement in toolsElement.EnumerateArray())
                {
                    if (toolElement.ValueKind != JsonValueKind.Object) continue;

                    McpRemoteTool tool = new McpRemoteTool();
                    if (toolElement.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String)
                        tool.Name = nameElement.GetString() ?? String.Empty;
                    if (toolElement.TryGetProperty("description", out JsonElement descElement) && descElement.ValueKind == JsonValueKind.String)
                        tool.Description = descElement.GetString() ?? String.Empty;
                    if (toolElement.TryGetProperty("inputSchema", out JsonElement schemaElement) && schemaElement.ValueKind == JsonValueKind.Object)
                        tool.InputSchemaJson = schemaElement.GetRawText();

                    if (!String.IsNullOrWhiteSpace(tool.Name)) tools.Add(tool);
                }
            }

            return tools;
        }

        /// <summary>
        /// Invoke a tool by name with the given arguments (serialized JSON object; empty or null is treated
        /// as no arguments) and return the concatenated text content of the tool result.
        /// </summary>
        /// <param name="name">Tool name as advertised by the server.</param>
        /// <param name="argumentsJson">Arguments as a serialized JSON object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool result text.</returns>
        public async Task<string> CallToolAsync(string name, string? argumentsJson, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

            JsonElement arguments;
            try
            {
                string trimmed = String.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson!.Trim();
                using JsonDocument doc = JsonDocument.Parse(trimmed);
                arguments = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                using JsonDocument doc = JsonDocument.Parse("{}");
                arguments = doc.RootElement.Clone();
            }

            JsonElement result = await SendAsync("tools/call", NextId(), new
            {
                name = name,
                arguments = arguments
            }, token).ConfigureAwait(false);

            return ExtractResultText(result);
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

        private int NextId()
        {
            return Interlocked.Increment(ref _RpcId);
        }

        private async Task<JsonElement> SendAsync(string method, int id, object parameters, CancellationToken token)
        {
            HttpRequestMessage request = BuildRequest(new
            {
                jsonrpc = "2.0",
                id = id,
                method = method,
                @params = parameters
            });

            using HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new McpClientException("MCP request '" + method + "' to " + _Endpoint + " failed with " + (int)response.StatusCode + ": " + Truncate(body, 500));

            if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
            {
                foreach (string value in values)
                {
                    if (!String.IsNullOrWhiteSpace(value)) { _SessionId = value; break; }
                }
            }

            JsonElement envelope = ParseEnvelope(body);
            if (envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("error", out JsonElement error))
            {
                string message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() ?? "unknown error" : error.GetRawText();
                throw new McpClientException("MCP error from '" + method + "': " + message);
            }

            if (envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("result", out JsonElement resultElement))
                return resultElement.Clone();

            return default;
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
            catch (Exception ex)
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

        /// <summary>
        /// Parse a JSON-RPC envelope from either a plain JSON body or an SSE (event-stream) body, taking the
        /// first non-empty <c>data:</c> frame in the latter.
        /// </summary>
        private static JsonElement ParseEnvelope(string body)
        {
            if (String.IsNullOrWhiteSpace(body)) return default;

            string trimmed = body.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.Clone();
            }

            foreach (string rawLine in body.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    string data = line.Substring(5).Trim();
                    if (!String.IsNullOrEmpty(data) && data.StartsWith("{", StringComparison.Ordinal))
                    {
                        using JsonDocument doc = JsonDocument.Parse(data);
                        return doc.RootElement.Clone();
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// Concatenate the text of an MCP tool result's content array. Non-text content parts are rendered as
        /// their raw JSON so nothing is silently dropped. When the result carries no content array, the whole
        /// result is returned as JSON.
        /// </summary>
        private static string ExtractResultText(JsonElement result)
        {
            if (result.ValueKind != JsonValueKind.Object) return result.ValueKind == JsonValueKind.Undefined ? String.Empty : result.GetRawText();

            if (result.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
            {
                StringBuilder builder = new StringBuilder();
                foreach (JsonElement part in content.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) continue;
                    if (part.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && type.GetString() == "text"
                        && part.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                    {
                        if (builder.Length > 0) builder.Append('\n');
                        builder.Append(text.GetString());
                    }
                    else
                    {
                        if (builder.Length > 0) builder.Append('\n');
                        builder.Append(part.GetRawText());
                    }
                }

                return builder.ToString();
            }

            return result.GetRawText();
        }

        private static string Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value) || value!.Length <= max) return value ?? String.Empty;
            return value.Substring(0, max) + "... (truncated)";
        }

        #endregion
    }
}
