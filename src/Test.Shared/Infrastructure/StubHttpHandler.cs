namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// HTTP handler that answers from canned responses keyed by "METHOD path" (query ignored unless a key includes it),
    /// records every request (method, path, query, and body together in one queue), and returns 404 for anything
    /// unmapped. Used to drive the TUI with a stubbed server. Tests select recorded requests by route
    /// (<see cref="RequestsFor"/>, <see cref="Last"/>) and read bodies as typed models (<see cref="StubRequest.BodyAs{T}"/>).
    /// </summary>
    public sealed class StubHttpHandler : HttpMessageHandler
    {
        #region Public-Members

        /// <summary>
        /// Every request seen, in arrival order (snapshot).
        /// </summary>
        public List<StubRequest> Log
        {
            get { return _Log.ToList(); }
        }

        /// <summary>
        /// Requests seen as "METHOD path?query" text (snapshot). For failure messages only; select requests with
        /// <see cref="RequestsFor"/> or <see cref="Log"/>.
        /// </summary>
        public List<string> Requests
        {
            get { return _Log.Select(r => r.Text).ToList(); }
        }


        #endregion

        #region Private-Members

        private readonly ConcurrentQueue<StubRequest> _Log = new ConcurrentQueue<StubRequest>();
        private int _Sequence = -1;
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
        /// Requests with exactly this method and path (query ignored), in arrival order.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path, for example "/api/v1/vessels".</param>
        /// <returns>Matching requests.</returns>
        public List<StubRequest> RequestsFor(string method, string path)
        {
            return _Log.Where(r => r.Is(method, path)).ToList();
        }

        /// <summary>
        /// Number of requests with exactly this method and path (query ignored).
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path.</param>
        /// <returns>Count.</returns>
        public int CountFor(string method, string path)
        {
            return _Log.Count(r => r.Is(method, path));
        }

        /// <summary>
        /// True when a request with this method and path was seen and, if given, satisfies a predicate.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path.</param>
        /// <param name="predicate">Optional predicate.</param>
        /// <returns>True when one matches.</returns>
        public bool Saw(string method, string path, Func<StubRequest, bool>? predicate = null)
        {
            return _Log.Any(r => r.Is(method, path) && (predicate == null || predicate(r)));
        }

        /// <summary>
        /// The last request with this method and path, or null.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path.</param>
        /// <returns>Request or null.</returns>
        public StubRequest? LastOrDefault(string method, string path)
        {
            return _Log.LastOrDefault(r => r.Is(method, path));
        }

        /// <summary>
        /// The last request with this method and path.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path.</param>
        /// <returns>Request.</returns>
        /// <exception cref="AssertionException">Thrown when no such request was seen.</exception>
        public StubRequest Last(string method, string path)
        {
            StubRequest? found = LastOrDefault(method, path);
            if (found == null) throw new AssertionException("No " + method.ToUpperInvariant() + " " + path + " request. Seen:\n" + String.Join("\n", Requests));
            return found;
        }

        /// <summary>
        /// The last request with this method and path, its body deserialized.
        /// </summary>
        /// <typeparam name="T">Body model.</typeparam>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path.</param>
        /// <returns>Body.</returns>
        /// <exception cref="AssertionException">Thrown when no such request was seen or the body does not parse.</exception>
        public T LastBody<T>(string method, string path)
        {
            return Last(method, path).BodyAs<T>();
        }

        /// <summary>
        /// Bodies of every request with this method and path that parse as <typeparamref name="T"/>, in arrival order.
        /// </summary>
        /// <typeparam name="T">Body model.</typeparam>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path.</param>
        /// <returns>Bodies.</returns>
        public List<T> BodiesFor<T>(string method, string path) where T : class
        {
            List<T> result = new List<T>();
            foreach (StubRequest r in RequestsFor(method, path))
            {
                T? body = r.TryBodyAs<T>();
                if (body != null) result.Add(body);
            }

            return result;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "";
            string path = request.RequestUri!.AbsolutePath;
            string key = request.Method.Method + " " + path;
            StubRequest recorded = new StubRequest();
            recorded.Sequence = Interlocked.Increment(ref _Sequence);
            recorded.Method = request.Method.Method.ToUpperInvariant();
            recorded.Path = path;
            recorded.Query = request.RequestUri.Query;
            recorded.Body = body;
            _Log.Enqueue(recorded);
            if (_Routes.TryGetValue(key + request.RequestUri.Query, out Func<string, HttpResponseMessage>? exact)) return exact(body);
            if (_Routes.TryGetValue(key, out Func<string, HttpResponseMessage>? responder)) return responder(body);
            return Response(HttpStatusCode.NotFound, "{\"Error\":\"NotFound\",\"Message\":\"No stub for " + key + "\"}");
        }

        #endregion
    }
}
