namespace Test.Shared.Infrastructure
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>One-session MCP transport fixture that detects leaked sessions between helper calls.</summary>
    public sealed class OwnedMcpSessionHandler : HttpMessageHandler
    {
        /// <summary>Reject initialization before any tool is allowed to run.</summary>
        public bool RejectInitialization { get; set; }
        /// <summary>Currently outstanding owned sessions.</summary>
        public int ActiveSessions { get; private set; }
        /// <summary>Successfully closed owned sessions.</summary>
        public int ClosedSessions { get; private set; }
        /// <summary>Executed tool calls.</summary>
        public int ToolCalls { get; private set; }
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Delete)
            {
                if (request.Headers.TryGetValues("Mcp-Session-Id", out System.Collections.Generic.IEnumerable<string>? sessions)
                    && String.Join("", sessions) == "owned-test-session" && ActiveSessions == 1)
                { ActiveSessions = 0; ClosedSessions++; return Reply(HttpStatusCode.NoContent, ""); }
                return Reply(HttpStatusCode.NotFound, "{}");
            }
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false));
            if (document.RootElement.GetProperty("method").GetString() == "initialize")
            {
                if (RejectInitialization || ActiveSessions != 0)
                    return Reply(HttpStatusCode.BadRequest, "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32000,\"message\":\"Session budget reached\"}}");
                ActiveSessions++;
                HttpResponseMessage initialized = Reply(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}}}");
                initialized.Headers.Add("Mcp-Session-Id", "owned-test-session"); return initialized;
            }
            if (ActiveSessions != 1 || !request.Headers.Contains("Mcp-Session-Id")) return Reply(HttpStatusCode.BadRequest, "{}");
            ToolCalls++;
            return Reply(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}");
        }
        private static HttpResponseMessage Reply(HttpStatusCode status, string body)
            => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
