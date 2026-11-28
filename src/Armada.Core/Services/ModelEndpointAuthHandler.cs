namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Applies a model endpoint's authentication type and extra headers to every outgoing request, so any provider
    /// client can reach an endpoint behind a bearer token, basic authentication, a custom header, a query parameter, or
    /// an access proxy that needs its own headers. Copies the settings at construction, so later edits to the
    /// endpoint do not affect a client already built. Thread-safe.
    /// </summary>
    public class ModelEndpointAuthHandler : DelegatingHandler
    {
        #region Public-Members

        /// <summary>
        /// Key passed to a PolyPrompt client that requires one at construction when this handler authenticates the
        /// request instead. Any request header carrying it is removed before the request is sent.
        /// </summary>
        public const string ExternalAuthPlaceholder = "armada-external-auth";

        #endregion

        #region Private-Members

        private readonly ModelEndpointAuthTypeEnum _AuthType;
        private readonly string _Secret;
        private readonly string? _Username;
        private readonly string? _HeaderName;
        private readonly string? _QueryParameter;
        private readonly List<KeyValuePair<string, string>> _Headers = new List<KeyValuePair<string, string>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for an endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint whose authentication to apply.</param>
        /// <param name="innerHandler">Handler that sends the request.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelEndpointAuthHandler(ModelEndpoint endpoint, HttpMessageHandler innerHandler)
            : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            _AuthType = endpoint.AuthType;
            _Secret = endpoint.ApiKey ?? String.Empty;
            _Username = endpoint.AuthUsername;
            _HeaderName = endpoint.AuthHeaderName;
            _QueryParameter = endpoint.AuthQueryParameter;
            foreach (ModelEndpointHeader header in endpoint.Headers)
            {
                if (String.IsNullOrWhiteSpace(header.Name) || header.Value == null) continue;
                _Headers.Add(new KeyValuePair<string, string>(header.Name, header.Value));
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Apply(request);
            return base.SendAsync(request, cancellationToken);
        }

        #endregion

        #region Private-Methods

        private void Apply(HttpRequestMessage request)
        {
            List<string> placeholders = new List<string>();
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                foreach (string value in header.Value)
                    if (value.Contains(ExternalAuthPlaceholder, StringComparison.Ordinal)) placeholders.Add(header.Key);
            foreach (string name in placeholders) request.Headers.Remove(name);

            foreach (KeyValuePair<string, string> header in _Headers)
            {
                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (_Secret.Length == 0) return;
            switch (_AuthType)
            {
                case ModelEndpointAuthTypeEnum.Bearer:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _Secret);
                    break;
                case ModelEndpointAuthTypeEnum.Basic:
                    string pair = (_Username ?? String.Empty) + ":" + _Secret;
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
                    break;
                case ModelEndpointAuthTypeEnum.Header:
                    if (String.IsNullOrWhiteSpace(_HeaderName)) break;
                    request.Headers.Remove(_HeaderName!);
                    request.Headers.TryAddWithoutValidation(_HeaderName!, _Secret);
                    break;
                case ModelEndpointAuthTypeEnum.QueryParameter:
                    if (String.IsNullOrWhiteSpace(_QueryParameter) || request.RequestUri == null) break;
                    request.RequestUri = AppendQuery(request.RequestUri, _QueryParameter!, _Secret);
                    break;
            }
        }

        private static Uri AppendQuery(Uri uri, string name, string value)
        {
            UriBuilder builder = new UriBuilder(uri);
            string pair = Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
            string existing = builder.Query.TrimStart('?');
            builder.Query = existing.Length == 0 ? pair : existing + "&" + pair;
            return builder.Uri;
        }

        #endregion
    }
}
