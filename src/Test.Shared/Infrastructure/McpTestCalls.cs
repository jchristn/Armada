namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Calls an MCP tool over HTTP (initialize, then tools/call) and returns the raw JSON-RPC response body.
    /// </summary>
    public static class McpTestCalls
    {
        #region Public-Methods

        /// <summary>
        /// Call one tool.
        /// </summary>
        /// <param name="mcp">Client whose base address is the MCP server, with its credentials set.</param>
        /// <param name="tool">Tool name.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <returns>The JSON-RPC response body (a tool error is in the result text, not an HTTP failure).</returns>
        /// <exception cref="ArgumentNullException">Thrown when mcp or tool is null.</exception>
        public static async Task<string> CallToolAsync(HttpClient mcp, string tool, object arguments)
        {
            if (mcp == null) throw new ArgumentNullException(nameof(mcp));
            if (String.IsNullOrEmpty(tool)) throw new ArgumentNullException(nameof(tool));

            using HttpRequestMessage init = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            init.Content = JsonHelper.ToJsonContent(new
            {
                jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "armada-test", version = "1.0" } }
            });
            using HttpResponseMessage initResponse = await mcp.SendAsync(init).ConfigureAwait(false);
            string initialized = await initResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(initResponse.IsSuccessStatusCode, "MCP initialize HTTP " + (int)initResponse.StatusCode + ": " + initialized);
            using JsonDocument initialization = JsonDocument.Parse(initialized);
            AssertTrue(!initialization.RootElement.TryGetProperty("error", out JsonElement initError) || initError.ValueKind == JsonValueKind.Null, "MCP initialization failed: " + initialized);
            string sessionId = initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.First() : String.Empty;

            try
            {
                using HttpRequestMessage call = new HttpRequestMessage(HttpMethod.Post, "/rpc");
                // Serialized with nulls kept, so a test can send an explicit null argument.
                call.Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = arguments } }), Encoding.UTF8, "application/json");
                if (!String.IsNullOrEmpty(sessionId)) call.Headers.Add("Mcp-Session-Id", sessionId);
                using HttpResponseMessage response = await mcp.SendAsync(call).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertTrue(response.IsSuccessStatusCode, "MCP " + tool + " HTTP " + (int)response.StatusCode + ": " + body);
                return body;
            }
            finally
            {
                // Each invocation owns its session. Leaving it behind exhausts the transport's session budget
                // during a whole-process sweep even though each isolated test group passes.
                if (!String.IsNullOrEmpty(sessionId))
                {
                    try
                    {
                        using HttpRequestMessage close = new HttpRequestMessage(HttpMethod.Delete, "/rpc");
                        close.Headers.Add("Mcp-Session-Id", sessionId);
                        using HttpResponseMessage closed = await mcp.SendAsync(close).ConfigureAwait(false);
                    }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                }
            }
        }

        #endregion
    }
}
