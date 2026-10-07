namespace Test.Shared.Infrastructure
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// HttpMessageHandler that records the last request and answers with a scripted response, so the Expo HTTP transport
    /// is exercised without any network.
    /// </summary>
    public sealed class StubExpoHandler : HttpMessageHandler
    {
        #region Public-Members

        /// <summary>Response for the next request.</summary>
        public StubExpoResponse Next { get; set; } = new StubExpoResponse(200, "{}");

        /// <summary>When true, the next request throws HttpRequestException.</summary>
        public bool ThrowNext { get; set; } = false;

        /// <summary>URL of the last request.</summary>
        public string LastUrl { get; private set; } = String.Empty;

        /// <summary>Body of the last request.</summary>
        public string LastBody { get; private set; } = String.Empty;

        /// <summary>Authorization header of the last request, or null.</summary>
        public string? LastAuthorization { get; private set; } = null;

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Record and answer.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The scripted response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString() ?? String.Empty;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content == null ? String.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new HttpRequestException("connection refused");
            }

            HttpResponseMessage response = new HttpResponseMessage((HttpStatusCode)Next.StatusCode);
            response.Content = new StringContent(Next.Body, Encoding.UTF8, "application/json");
            return response;
        }

        #endregion
    }
}
