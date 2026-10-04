namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// HTTP handler that answers from canned responses keyed by "METHOD path" (query ignored unless a key includes it),
    /// records every request, and returns 404 for anything unmapped. Used to drive the TUI with a stubbed server.
    /// </summary>
    public sealed class StubHttpHandler : HttpMessageHandler
    {
        #region Public-Members

        /// <summary>
        /// Requests seen, as "METHOD path?query".
        /// </summary>
        public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Bodies of requests seen, in order.
        /// </summary>
        public ConcurrentQueue<string> Bodies { get; } = new ConcurrentQueue<string>();

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, Func<string, HttpResponseMessage>> _Routes = new ConcurrentDictionary<string, Func<string, HttpResponseMessage>>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map a route to a JSON response.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path.</param>
        /// <param name="json">JSON body.</param>
        /// <param name="status">Status code.</param>
        /// <returns>This handler.</returns>
        public StubHttpHandler Json(string method, string path, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _Routes[method.ToUpperInvariant() + " " + path] = body => Response(status, json);
            return this;
        }

        /// <summary>
        /// Map a route to a function of the request body.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path.</param>
        /// <param name="responder">Responder.</param>
        /// <returns>This handler.</returns>
        public StubHttpHandler On(string method, string path, Func<string, HttpResponseMessage> responder)
        {
            _Routes[method.ToUpperInvariant() + " " + path] = responder;
            return this;
        }

        /// <summary>
        /// Build a JSON response.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="json">Body.</param>
        /// <returns>Response.</returns>
        public static HttpResponseMessage Response(HttpStatusCode status, string json)
        {
            HttpResponseMessage response = new HttpResponseMessage(status);
            response.Content = new StringContent(json ?? "", Encoding.UTF8, "application/json");
            return response;
        }

        /// <summary>
        /// Count requests whose text starts with a prefix.
        /// </summary>
        /// <param name="prefix">Prefix, for example "POST /api/v1/authenticate".</param>
        /// <returns>Count.</returns>
        public int Count(string prefix)
        {
            int n = 0;
            foreach (string r in Requests) if (r.StartsWith(prefix, StringComparison.Ordinal)) n++;
            return n;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "";
            string path = request.RequestUri!.AbsolutePath;
            string key = request.Method.Method + " " + path;
            Requests.Enqueue(key + request.RequestUri.Query);
            Bodies.Enqueue(body);
            if (_Routes.TryGetValue(key + request.RequestUri.Query, out Func<string, HttpResponseMessage>? exact)) return exact(body);
            if (_Routes.TryGetValue(key, out Func<string, HttpResponseMessage>? responder)) return responder(body);
            return Response(HttpStatusCode.NotFound, "{\"Error\":\"NotFound\",\"Message\":\"No stub for " + key + "\"}");
        }

        #endregion
    }
}
