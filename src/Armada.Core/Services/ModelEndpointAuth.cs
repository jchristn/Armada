namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Rules for model endpoint authentication: which types a provider accepts, what a valid configuration looks like,
    /// how an update merges extra headers whose values are write-only, and the HTTP client that applies the type to
    /// every request. Thread-safe.
    /// </summary>
    public static class ModelEndpointAuth
    {
        #region Private-Members

        // One connection pool for every endpoint client that needs its own authentication; the per-endpoint
        // ModelEndpointAuthHandler wraps it without owning it, so clients can be created per call without leaking sockets.
        private static readonly SocketsHttpHandler _SharedHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        private static readonly HashSet<string> _ReservedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a provider accepts an authentication type. Vertex AI and AWS Bedrock sign requests with their own
        /// credentials, so they accept only <see cref="ModelEndpointAuthTypeEnum.ProviderDefault"/>.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="authType">Authentication type.</param>
        /// <returns>True when supported.</returns>
        public static bool Supports(ModelProviderEnum provider, ModelEndpointAuthTypeEnum authType)
        {
            if (provider == ModelProviderEnum.OllamaCloud || provider == ModelProviderEnum.XAI)
                return authType == ModelEndpointAuthTypeEnum.ProviderDefault || authType == ModelEndpointAuthTypeEnum.Bearer;
            if (provider == ModelProviderEnum.VertexAI || provider == ModelProviderEnum.Bedrock)
                return authType == ModelEndpointAuthTypeEnum.ProviderDefault;
            return true;
        }

        /// <summary>
        /// Validate the authentication settings of an endpoint about to be stored (after <see cref="MergeHeaders"/> on
        /// update, so every header has its value).
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the settings are invalid.</exception>
        public static void Validate(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            if (!Supports(endpoint.Provider, endpoint.AuthType))
                throw new ArgumentException(endpoint.Provider + " does not support this authentication type.");

            if (endpoint.Provider == ModelProviderEnum.OllamaCloud || endpoint.Provider == ModelProviderEnum.XAI)
            {
                if (String.IsNullOrWhiteSpace(endpoint.ApiKey))
                    throw new ArgumentException(endpoint.Provider + " requires a bearer API key.");
                if (endpoint.Headers.Exists(h => h != null && String.Equals(h.Name, "Authorization", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Extra Authorization headers cannot override the provider bearer token.");
            }

            switch (endpoint.AuthType)
            {
                case ModelEndpointAuthTypeEnum.Basic:
                    if (String.IsNullOrWhiteSpace(endpoint.AuthUsername)) throw new ArgumentException("Basic authentication requires a username (authUsername).");
                    if (endpoint.AuthUsername!.Contains(':')) throw new ArgumentException("A Basic authentication username cannot contain a colon.");
                    break;
                case ModelEndpointAuthTypeEnum.Header:
                    if (String.IsNullOrWhiteSpace(endpoint.AuthHeaderName)) throw new ArgumentException("Header authentication requires a header name (authHeaderName), for example X-API-Key.");
                    ValidateHeaderName(endpoint.AuthHeaderName!);
                    break;
                case ModelEndpointAuthTypeEnum.QueryParameter:
                    if (String.IsNullOrWhiteSpace(endpoint.AuthQueryParameter)) throw new ArgumentException("Query parameter authentication requires a parameter name (authQueryParameter), for example key.");
                    if (endpoint.AuthQueryParameter!.IndexOfAny(new char[] { ' ', '&', '=', '?', '#' }) >= 0)
                        throw new ArgumentException("The query parameter name cannot contain spaces or the characters & = ? #.");
                    break;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelEndpointHeader header in endpoint.Headers)
            {
                if (header == null || String.IsNullOrWhiteSpace(header.Name)) throw new ArgumentException("Every extra header needs a name.");
                ValidateHeaderName(header.Name);
                if (!seen.Add(header.Name)) throw new ArgumentException("Extra header '" + header.Name + "' is listed more than once.");
                if (header.Value == null) throw new ArgumentException("Extra header '" + header.Name + "' needs a value.");
                if (ConflictsWithAuth(endpoint, header.Name))
                    throw new ArgumentException("Extra header '" + header.Name + "' conflicts with the " + endpoint.AuthType + " authentication header; remove it or change the authentication type.");
            }
        }

        /// <summary>
        /// Merge an update's extra headers into the stored endpoint: a supplied list replaces the stored one, and a
        /// header supplied without a value keeps the stored value of the header with the same name (case-insensitive).
        /// An omitted list keeps the stored headers.
        /// </summary>
        /// <param name="existing">Stored endpoint (receives the result).</param>
        /// <param name="incoming">Update request.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void MergeHeaders(ModelEndpoint existing, ModelEndpoint incoming)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            if (!incoming.HeadersSpecified) return;

            List<ModelEndpointHeader> merged = new List<ModelEndpointHeader>();
            foreach (ModelEndpointHeader header in incoming.Headers)
            {
                if (header == null) continue;
                string? value = header.Value;
                if (value == null)
                {
                    foreach (ModelEndpointHeader stored in existing.Headers)
                    {
                        if (String.Equals(stored.Name, header.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            value = stored.Value;
                            break;
                        }
                    }
                }

                merged.Add(new ModelEndpointHeader(header.Name, value));
            }

            existing.Headers = merged;
        }

        /// <summary>
        /// The secret to hand the provider client: the credential for <see cref="ModelEndpointAuthTypeEnum.ProviderDefault"/>,
        /// otherwise empty so the provider adds no header of its own (the auth handler applies the configured type).
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Secret, or empty.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public static string ProviderSecret(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            return endpoint.AuthType == ModelEndpointAuthTypeEnum.ProviderDefault ? endpoint.ApiKey ?? String.Empty : String.Empty;
        }

        /// <summary>
        /// An HTTP client that applies the endpoint's authentication type and extra headers, or null when the provider
        /// default applies and there are no extra headers (the provider client then makes its own).
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Client, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public static HttpClient? CreateHttpClient(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (endpoint.AuthType == ModelEndpointAuthTypeEnum.ProviderDefault && endpoint.Headers.Count == 0) return null;
            return new HttpClient(new ModelEndpointAuthHandler(endpoint, _SharedHandler), false);
        }

        /// <summary>
        /// An HTTP client for the endpoint that also reports allowlisted rate-limit response headers to
        /// <paramref name="usageSink"/>. With a sink the client is created for EVERY authentication type,
        /// including the provider-default path without extra headers that would otherwise let the provider
        /// client build its own transport (where no Armada handler could see the response). The provider
        /// client still applies its own default credential, exactly as it does on the configured-header path.
        /// Without a sink this is <see cref="CreateHttpClient(ModelEndpoint)"/>.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="usageSink">Receives each response's allowlisted rate-limit headers, or null for no capture.</param>
        /// <returns>Client, or null when there is no sink and no Armada handler is needed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public static HttpClient? CreateHttpClient(ModelEndpoint endpoint, Action<UsageHeaderObservation>? usageSink)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (usageSink == null) return CreateHttpClient(endpoint);
            HttpMessageHandler inner = endpoint.AuthType == ModelEndpointAuthTypeEnum.ProviderDefault && endpoint.Headers.Count == 0
                ? _SharedHandler
                : new ModelEndpointAuthHandler(endpoint, _SharedHandler);
            // The capture handler is outermost so it observes the final response the provider client receives.
            return new HttpClient(new UsageRateLimitHandler(usageSink, inner), false);
        }

        /// <summary>
        /// A stable description of the authentication settings, for grouping endpoints that share a credential.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Fingerprint (contains secrets; never log or return it).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public static string Fingerprint(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            List<string> parts = new List<string>
            {
                endpoint.AuthType.ToString(),
                endpoint.AuthUsername ?? String.Empty,
                endpoint.AuthHeaderName ?? String.Empty,
                endpoint.AuthQueryParameter ?? String.Empty,
                endpoint.ApiKey ?? String.Empty
            };
            foreach (ModelEndpointHeader header in endpoint.Headers)
                parts.Add(header.Name.ToLowerInvariant() + "=" + (header.Value ?? String.Empty));
            return String.Join("\0", parts);
        }

        #endregion

        #region Private-Methods

        private static void ValidateHeaderName(string name)
        {
            if (_ReservedHeaders.Contains(name)) throw new ArgumentException("Header '" + name + "' is set by the HTTP client and cannot be configured.");
            foreach (char c in name)
            {
                // RFC 9110 token characters.
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0;
                if (!ok) throw new ArgumentException("Header name '" + name + "' is not valid; use letters, digits, and - (no spaces or colons).");
            }
        }

        private static bool ConflictsWithAuth(ModelEndpoint endpoint, string headerName)
        {
            switch (endpoint.AuthType)
            {
                case ModelEndpointAuthTypeEnum.Bearer:
                case ModelEndpointAuthTypeEnum.Basic:
                    return String.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase);
                case ModelEndpointAuthTypeEnum.Header:
                    return String.Equals(headerName, endpoint.AuthHeaderName, StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        #endregion
    }
}
