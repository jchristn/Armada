namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory MCP streamable-HTTP endpoint for <c>McpToolClient</c> tests. Each request is deserialized into a
    /// <see cref="ToolClientStubRequest"/> and answered by <see cref="Respond"/>; notifications get 202. Helpers build a
    /// plain JSON reply or an SSE reply whose frames are written exactly as given.
    /// </summary>
    public sealed class ToolClientStubHandler : HttpMessageHandler
    {
        #region Public-Members

        /// <summary>
        /// Produces the reply for a request (never called for notifications).
        /// </summary>
        public Func<ToolClientStubRequest, HttpResponseMessage> Respond { get; set; } = request => Json(request.Id, "{}");

        /// <summary>
        /// Requests received, in order.
        /// </summary>
        public List<ToolClientStubRequest> Requests { get; } = new List<ToolClientStubRequest>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A plain JSON response with the given raw result.
        /// </summary>
        /// <param name="id">Request id.</param>
        /// <param name="resultJson">Raw result JSON.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Json(string? id, string resultJson)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + resultJson + "}", Encoding.UTF8, "application/json");
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "stub-session");
            return response;
        }

        /// <summary>
        /// An SSE response with the given body, written verbatim.
        /// </summary>
        /// <param name="body">SSE body.</param>
        /// <param name="status">HTTP status.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Sse(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            HttpResponseMessage response = new HttpResponseMessage(status);
            response.Content = new StringContent(body, Encoding.UTF8, "text/event-stream");
            return response;
        }

        /// <summary>
        /// A raw response with the given status and JSON body.
        /// </summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="body">Body.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Raw(HttpStatusCode status, string body)
        {
            HttpResponseMessage response = new HttpResponseMessage(status);
            response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return response;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "{}";
            ToolClientStubRequest rpc = JsonSerializer.Deserialize<ToolClientStubRequest>(body) ?? new ToolClientStubRequest();
            lock (Requests) Requests.Add(rpc);
            if (rpc.Id == null) return new HttpResponseMessage(HttpStatusCode.Accepted);
            return Respond(rpc);
        }

        #endregion
    }
}
