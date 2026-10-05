namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory MCP streamable HTTP server for probe tests. Requests are deserialized and answered by JSON-RPC method:
    /// <c>initialize</c> replies over SSE with a progress notification sent before the response, the initialized
    /// notification is accepted with 202, and <c>tools/list</c> serves <see cref="Pages"/> by cursor (the first page as
    /// plain JSON, later pages over SSE), or the configured JSON-RPC error.
    /// </summary>
    public sealed class McpStubServerHandler : HttpMessageHandler
    {
        #region Public-Members

        /// <summary>
        /// Session id returned from <c>initialize</c>.
        /// </summary>
        public string SessionId { get; set; } = "stub-session-1";

        /// <summary>
        /// Tool names per page; page N (N &gt; 0) is requested with cursor "page-N".
        /// </summary>
        public List<List<string>> Pages { get; set; } = new List<List<string>>();

        /// <summary>
        /// When set, <c>tools/list</c> answers with a JSON-RPC error carrying this code.
        /// </summary>
        public int? ToolsListErrorCode { get; set; } = null;

        /// <summary>
        /// HTTP status used with <see cref="ToolsListErrorCode"/>.
        /// </summary>
        public HttpStatusCode ToolsListErrorStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>
        /// Requests received, in order.
        /// </summary>
        public ConcurrentQueue<McpStubRequest> Requests { get; } = new ConcurrentQueue<McpStubRequest>();

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "";
            McpStubRequest rpc = JsonSerializer.Deserialize<McpStubRequest>(body) ?? new McpStubRequest();
            if (request.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values)) rpc.SessionId = values.FirstOrDefault();
            Requests.Enqueue(rpc);

            switch (rpc.Method)
            {
                case "initialize":
                    HttpResponseMessage init = Sse(
                        "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{\"level\":\"info\",\"data\":\"starting\"}}",
                        "{\"jsonrpc\":\"2.0\",\"id\":" + rpc.Id + ",\"result\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"serverInfo\":{\"name\":\"stub\",\"version\":\"1\"}}}");
                    init.Headers.Add("Mcp-Session-Id", SessionId);
                    return init;
                case "notifications/initialized":
                    return new HttpResponseMessage(HttpStatusCode.Accepted);
                case "tools/list":
                    return ToolsList(rpc);
                default:
                    return Plain(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":" + rpc.Id + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}");
            }
        }

        #endregion

        #region Private-Methods

        private HttpResponseMessage ToolsList(McpStubRequest rpc)
        {
            if (ToolsListErrorCode.HasValue)
            {
                return Plain(ToolsListErrorStatus, "{\"jsonrpc\":\"2.0\",\"id\":" + rpc.Id + ",\"error\":{\"code\":" + ToolsListErrorCode.Value + ",\"message\":\"stub tools/list failure\"}}");
            }

            string? cursor = rpc.Params?.Cursor;
            int page = cursor == null ? 0 : Int32.Parse(cursor.Substring("page-".Length));
            List<object> tools = new List<object>();
            foreach (string name in Pages[page])
            {
                tools.Add(new { name = name, description = "Stub tool " + name, inputSchema = new { type = "object" } });
            }

            object result = page + 1 < Pages.Count
                ? new { tools = tools, nextCursor = "page-" + (page + 1) }
                : (object)new { tools = tools };
            string response = "{\"jsonrpc\":\"2.0\",\"id\":" + rpc.Id + ",\"result\":" + JsonSerializer.Serialize(result) + "}";
            return page == 0
                ? Plain(HttpStatusCode.OK, response)
                : Sse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":1}}", response);
        }

        private static HttpResponseMessage Plain(HttpStatusCode status, string json)
        {
            HttpResponseMessage response = new HttpResponseMessage(status);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        }

        private static HttpResponseMessage Sse(params string[] frames)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string frame in frames)
            {
                builder.Append("event: message\n");
                builder.Append("data: ").Append(frame).Append("\n\n");
            }

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(builder.ToString(), Encoding.UTF8, "text/event-stream");
            return response;
        }

        #endregion
    }
}
