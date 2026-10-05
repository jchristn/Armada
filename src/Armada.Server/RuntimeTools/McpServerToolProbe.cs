namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Mcp;
    using ArmadaConstants = Armada.Core.Constants;

    /// <summary>
    /// Connects to one configured MCP server (streamable HTTP or stdio) and lists its tools with typed JSON-RPC
    /// handling: the reply to each request is selected by its <c>id</c> (notifications, server-to-client requests and
    /// unrelated frames are skipped), the result is deserialized into <see cref="RuntimeMcpListToolsResult"/>, and a
    /// JSON-RPC error becomes an <see cref="McpClientException"/> carrying <see cref="McpClientException.JsonRpcCode"/>.
    /// </summary>
    internal sealed class McpServerToolProbe
    {
        #region Private-Members

        private const string _ProtocolVersion = "2025-03-26";

        private readonly HttpClient _HttpClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="httpClient">HTTP client used for streamable HTTP servers.</param>
        public McpServerToolProbe(HttpClient httpClient)
        {
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the tools of a configured server over its transport.
        /// </summary>
        /// <param name="server">Server definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tools the server advertised, across all pages.</returns>
        public async Task<List<McpRemoteTool>> ListToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token = default)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            switch (server.TransportType)
            {
                case "stdio":
                    return await ListStdioToolsAsync(server, token).ConfigureAwait(false);
                case "streamable_http":
                case "http":
                    return await ListHttpToolsAsync(server, token).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException("Unsupported MCP transport: " + server.TransportType);
            }
        }

        /// <summary>
        /// List the tools of a streamable HTTP server: initialize, send the initialized notification, then page through
        /// <c>tools/list</c>.
        /// </summary>
        /// <param name="server">Server definition with a URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tools across all pages.</returns>
        public async Task<List<McpRemoteTool>> ListHttpToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token = default)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (String.IsNullOrWhiteSpace(server.Url))
            {
                throw new InvalidOperationException("HTTP MCP server is missing a URL.");
            }

            string? sessionId = null;
            const string initializeId = "1";

            using (HttpRequestMessage initializeRequest = BuildHttpRequest(server, BuildInitializePayload(), sessionId))
            using (HttpResponseMessage initializeResponse = await _HttpClient.SendAsync(initializeRequest, token).ConfigureAwait(false))
            {
                string initializeBody = await initializeResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (!initializeResponse.IsSuccessStatusCode)
                {
                    throw BuildHttpStatusException(initializeResponse, initializeBody, initializeId);
                }

                if (initializeResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
                {
                    sessionId = values.FirstOrDefault();
                }

                ReadHttpResponse<JsonRpcEmptyResult>(initializeBody, initializeId);
            }

            using (HttpRequestMessage initializedRequest = BuildHttpRequest(server, BuildInitializedPayload(), sessionId))
            using (HttpResponseMessage initializedResponse = await _HttpClient.SendAsync(initializedRequest, token).ConfigureAwait(false))
            {
                if (!initializedResponse.IsSuccessStatusCode && initializedResponse.StatusCode != HttpStatusCode.Accepted)
                {
                    string body = await initializedResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    throw new McpClientException(
                        FirstNonEmptyLine(body, initializedResponse.ReasonPhrase),
                        (int)initializedResponse.StatusCode,
                        null);
                }
            }

            List<McpRemoteTool> tools = new List<McpRemoteTool>();
            string? cursor = null;
            int requestId = 2;

            do
            {
                string id = requestId.ToString(CultureInfo.InvariantCulture);
                using HttpRequestMessage request = BuildHttpRequest(server, BuildListToolsPayload(requestId, cursor), sessionId);
                using HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw BuildHttpStatusException(response, body, id);
                }

                JsonRpcResponse<RuntimeMcpListToolsResult> page = ReadHttpResponse<RuntimeMcpListToolsResult>(body, id);
                cursor = AppendPage(tools, page);
                requestId++;
            }
            while (!String.IsNullOrWhiteSpace(cursor));

            return tools;
        }

        /// <summary>
        /// Select and validate the reply to one request from a streamable HTTP body (plain JSON or SSE).
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="body">Response body.</param>
        /// <param name="requestId">Request id in canonical string form.</param>
        /// <returns>The successful response.</returns>
        /// <exception cref="McpClientException">Thrown when the body has no reply for the request or the reply is a
        /// JSON-RPC error (with <see cref="McpClientException.JsonRpcCode"/> set).</exception>
        public static JsonRpcResponse<T> ReadHttpResponse<T>(string? body, string requestId) where T : class
        {
            JsonRpcResponse<T>? response = McpResponseReader.ReadResponse<T>(body, requestId);
            if (response == null)
            {
                throw new McpClientException("MCP response did not include a reply to request " + requestId + ".");
            }

            EnsureSuccess(response);
            return response;
        }

        /// <summary>
        /// Read messages from an MCP stdio server until the reply to the given request arrives, skipping notifications,
        /// server-to-client requests, replies to other ids, and lines that are not JSON.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="output">The server's standard output.</param>
        /// <param name="requestId">Request id in canonical string form.</param>
        /// <param name="framing">Message framing.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The successful response.</returns>
        /// <exception cref="McpClientException">Thrown when the reply is a JSON-RPC error (with
        /// <see cref="McpClientException.JsonRpcCode"/> set) or is not valid JSON-RPC.</exception>
        public static async Task<JsonRpcResponse<T>> ReadStdioResponseAsync<T>(
            Stream output,
            string requestId,
            McpStdioFramingEnum framing,
            CancellationToken token = default) where T : class
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            while (true)
            {
                string message = framing == McpStdioFramingEnum.JsonLine
                    ? await ReadJsonLineMessageAsync(output, token).ConfigureAwait(false)
                    : await ReadContentLengthMessageAsync(output, token).ConfigureAwait(false);

                // A stdio frame is one JSON object; anything else (log output on stdout) is skipped.
                if (!message.TrimStart().StartsWith("{", StringComparison.Ordinal)) continue;

                JsonRpcResponse<T>? response = McpResponseReader.ReadResponse<T>(message, requestId);
                if (response == null) continue;

                EnsureSuccess(response);
                return response;
            }
        }

        /// <summary>
        /// Throw a typed failure when a response carries a JSON-RPC error.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="response">Response.</param>
        /// <exception cref="McpClientException">Thrown with <see cref="McpClientException.JsonRpcCode"/> set.</exception>
        public static void EnsureSuccess<T>(JsonRpcResponse<T> response) where T : class
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.Error == null) return;

            string message = !String.IsNullOrWhiteSpace(response.Error.Message)
                ? response.Error.Message
                : "JSON-RPC error " + response.Error.Code.ToString(CultureInfo.InvariantCulture);
            throw new McpClientException(message, null, response.Error.Code);
        }

        #endregion

        #region Private-Methods

        private async Task<List<McpRemoteTool>> ListStdioToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token)
        {
            Exception jsonLineFailure;

            try
            {
                return await ListStdioToolsAsync(server, McpStdioFramingEnum.JsonLine, token).ConfigureAwait(false);
            }
            catch (McpClientException ex) when (ex.JsonRpcCode != null)
            {
                // The server spoke JSON-line framing and answered with a JSON-RPC error; retrying with a different
                // framing cannot change that answer.
                throw;
            }
            catch (Exception ex)
            {
                jsonLineFailure = ex;
            }

            try
            {
                return await ListStdioToolsAsync(server, McpStdioFramingEnum.ContentLength, token).ConfigureAwait(false);
            }
            catch (Exception framedFailure)
            {
                string message =
                    "JSON-line probe failed: " + FirstNonEmptyLine(jsonLineFailure.Message, null) + " " +
                    "Content-Length probe failed: " + FirstNonEmptyLine(framedFailure.Message, null);
                McpClientException? typed = framedFailure as McpClientException;
                throw new McpClientException(message.Trim(), typed?.HttpStatusCode, typed?.JsonRpcCode);
            }
        }

        private static async Task<List<McpRemoteTool>> ListStdioToolsAsync(
            RuntimeMcpServerDefinition server,
            McpStdioFramingEnum framing,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(server.Command))
            {
                throw new InvalidOperationException("STDIO MCP server is missing a command.");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = server.Command!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in server.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!String.IsNullOrWhiteSpace(server.WorkingDirectory) && Directory.Exists(server.WorkingDirectory))
            {
                startInfo.WorkingDirectory = server.WorkingDirectory;
            }

            if (server.Environment != null)
            {
                foreach (KeyValuePair<string, string> entry in server.Environment)
                {
                    if (!String.IsNullOrWhiteSpace(entry.Key))
                    {
                        startInfo.Environment[entry.Key] = entry.Value;
                    }
                }
            }

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start MCP stdio server process.");
            }

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(server.StartupTimeout + server.ToolTimeout);

            Stream input = process.StandardInput.BaseStream;
            Stream output = process.StandardOutput.BaseStream;
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await WriteStdioMessageAsync(input, BuildInitializePayload(), framing, timeoutCts.Token).ConfigureAwait(false);
                await ReadStdioResponseAsync<JsonRpcEmptyResult>(output, "1", framing, timeoutCts.Token).ConfigureAwait(false);
                await WriteStdioMessageAsync(input, BuildInitializedPayload(), framing, timeoutCts.Token).ConfigureAwait(false);

                List<McpRemoteTool> tools = new List<McpRemoteTool>();
                string? cursor = null;
                int requestId = 2;

                do
                {
                    await WriteStdioMessageAsync(input, BuildListToolsPayload(requestId, cursor), framing, timeoutCts.Token).ConfigureAwait(false);
                    JsonRpcResponse<RuntimeMcpListToolsResult> page = await ReadStdioResponseAsync<RuntimeMcpListToolsResult>(
                        output,
                        requestId.ToString(CultureInfo.InvariantCulture),
                        framing,
                        timeoutCts.Token).ConfigureAwait(false);
                    cursor = AppendPage(tools, page);
                    requestId++;
                }
                while (!String.IsNullOrWhiteSpace(cursor));

                return tools;
            }
            catch (Exception ex)
            {
                // Stop the server first so its stderr closes and can be read for the error detail.
                KillQuietly(process);
                string stderr = String.Empty;
                try
                {
                    stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch
                {
                }

                McpClientException? typed = ex as McpClientException;
                throw new McpClientException(
                    GetFramingDisplayName(framing) + ": " + FirstNonEmptyLine(ex.Message, stderr),
                    typed?.HttpStatusCode,
                    typed?.JsonRpcCode);
            }
            finally
            {
                KillQuietly(process);
            }
        }

        private static string? AppendPage(List<McpRemoteTool> tools, JsonRpcResponse<RuntimeMcpListToolsResult> page)
        {
            if (page.Result == null) return null;

            foreach (McpRemoteTool tool in page.Result.Tools)
            {
                if (tool == null || String.IsNullOrWhiteSpace(tool.Name)) continue;
                tools.Add(tool);
            }

            return page.Result.EffectiveNextCursor;
        }

        private static McpClientException BuildHttpStatusException(HttpResponseMessage response, string body, string requestId)
        {
            int status = (int)response.StatusCode;
            JsonRpcResponse<JsonRpcEmptyResult>? envelope = null;
            try
            {
                envelope = McpResponseReader.ReadResponse<JsonRpcEmptyResult>(body, requestId);
            }
            catch (McpClientException)
            {
            }

            if (envelope?.Error != null)
            {
                return new McpClientException(
                    String.IsNullOrWhiteSpace(envelope.Error.Message) ? FirstNonEmptyLine(response.ReasonPhrase, null) : envelope.Error.Message,
                    status,
                    envelope.Error.Code);
            }

            return new McpClientException(FirstNonEmptyLine(body, response.ReasonPhrase), status, null);
        }

        private static HttpRequestMessage BuildHttpRequest(RuntimeMcpServerDefinition server, string payload, string? sessionId)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, server.Url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            // MCP Streamable HTTP servers (including Armada's own Voltaic server) require the client to
            // accept BOTH application/json and text/event-stream; sending only application/json is
            // rejected with "requires Accept: application/json, text/event-stream".
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");

            if (!String.IsNullOrWhiteSpace(sessionId))
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            }

            foreach (KeyValuePair<string, string> header in server.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        private static string BuildInitializePayload()
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = _ProtocolVersion,
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "armada",
                        version = ArmadaConstants.ProductVersion
                    }
                }
            });
        }

        private static string BuildInitializedPayload()
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });
        }

        private static string BuildListToolsPayload(int requestId, string? cursor)
        {
            object parameters = cursor == null ? new { } : new { cursor };
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/list",
                @params = parameters
            });
        }

        private static async Task WriteStdioMessageAsync(Stream input, string json, McpStdioFramingEnum framing, CancellationToken token)
        {
            if (framing == McpStdioFramingEnum.JsonLine)
            {
                byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
                await input.WriteAsync(payload, token).ConfigureAwait(false);
                await input.FlushAsync(token).ConfigureAwait(false);
                return;
            }

            byte[] framedPayload = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + framedPayload.Length + "\r\n\r\n");
            await input.WriteAsync(header, token).ConfigureAwait(false);
            await input.WriteAsync(framedPayload, token).ConfigureAwait(false);
            await input.FlushAsync(token).ConfigureAwait(false);
        }

        private static async Task<string> ReadJsonLineMessageAsync(Stream output, CancellationToken token)
        {
            while (true)
            {
                List<byte> lineBytes = new List<byte>();
                byte[] buffer = new byte[1];

                while (true)
                {
                    int bytesRead = await output.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (bytesRead <= 0)
                    {
                        throw new McpClientException("MCP server closed the stream before replying.");
                    }

                    if (buffer[0] == '\n')
                    {
                        break;
                    }

                    if (buffer[0] != '\r')
                    {
                        lineBytes.Add(buffer[0]);
                    }
                }

                string message = Encoding.UTF8.GetString(lineBytes.ToArray()).Trim();
                if (!String.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }

        private static async Task<string> ReadContentLengthMessageAsync(Stream output, CancellationToken token)
        {
            List<byte> headerBytes = new List<byte>();
            byte[] buffer = new byte[1];

            while (true)
            {
                int bytesRead = await output.ReadAsync(buffer, token).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    throw new McpClientException("MCP server closed the stream before replying.");
                }

                headerBytes.Add(buffer[0]);

                int count = headerBytes.Count;
                if (count >= 4 &&
                    headerBytes[count - 4] == '\r' &&
                    headerBytes[count - 3] == '\n' &&
                    headerBytes[count - 2] == '\r' &&
                    headerBytes[count - 1] == '\n')
                {
                    break;
                }
            }

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            int contentLength = ParseContentLength(headerText);
            byte[] payload = new byte[contentLength];
            int offset = 0;

            while (offset < contentLength)
            {
                int bytesRead = await output.ReadAsync(payload.AsMemory(offset, contentLength - offset), token).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    throw new McpClientException("MCP server closed the stream during message payload.");
                }

                offset += bytesRead;
            }

            return Encoding.UTF8.GetString(payload);
        }

        private static int ParseContentLength(string headers)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring("Content-Length:".Length).Trim();
                    if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int contentLength) && contentLength >= 0)
                    {
                        return contentLength;
                    }
                }
            }

            throw new McpClientException("MCP response did not include a valid Content-Length header.");
        }

        private static void KillQuietly(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
            }
        }

        private static string GetFramingDisplayName(McpStdioFramingEnum framing)
        {
            return framing == McpStdioFramingEnum.JsonLine ? "json-line" : "content-length";
        }

        private static string FirstNonEmptyLine(string? primary, string? secondary)
        {
            foreach (string source in new[] { primary ?? String.Empty, secondary ?? String.Empty })
            {
                foreach (string line in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!String.IsNullOrWhiteSpace(trimmed))
                    {
                        return trimmed;
                    }
                }
            }

            return String.Empty;
        }

        #endregion
    }
}
