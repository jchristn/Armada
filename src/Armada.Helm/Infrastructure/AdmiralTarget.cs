namespace Armada.Helm.Infrastructure
{
    using System;
    using System.Net;
    using System.Net.Http.Headers;

    /// <summary>
    /// The Admiral a CLI command talks to, and the credential it sends. Never print <see cref="Token"/> or
    /// <see cref="ApiKey"/>; use <see cref="Describe"/> for messages.
    /// </summary>
    public class AdmiralTarget
    {
        #region Public-Members

        /// <summary>
        /// Admiral base URL without a trailing slash, for example <c>http://127.0.0.1:7890</c>.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Where the target came from.
        /// </summary>
        public AdmiralTargetSourceEnum Source { get; }

        /// <summary>
        /// The exact origin for messages: <c>--server</c>, <c>--profile</c>, the environment variable name, or null.
        /// </summary>
        public string? SourceDetail { get; set; } = null;

        /// <summary>
        /// Profile name when the target or its credential came from a saved profile, else null.
        /// </summary>
        public string? ProfileName { get; }

        /// <summary>
        /// True when the target is this machine's Admiral (the local default, or a loopback URL on the local
        /// admiralPort). Only a local target may auto-start the embedded server or run local-only commands.
        /// </summary>
        public bool IsLocal { get; }

        /// <summary>
        /// Bearer token or session token to send, or null.
        /// </summary>
        public string? Token { get; }

        /// <summary>
        /// The local Admiral API key to send (local targets without a token only), or null.
        /// </summary>
        public string? ApiKey { get; }

        /// <summary>
        /// Where the credential came from.
        /// </summary>
        public AdmiralCredentialSourceEnum CredentialSource { get; }

        /// <summary>
        /// True when a credential is sent.
        /// </summary>
        public bool HasCredential
        {
            get { return !String.IsNullOrEmpty(Token) || !String.IsNullOrEmpty(ApiKey); }
        }

        /// <summary>
        /// True when a credential would travel over plain HTTP to a host that is not loopback.
        /// </summary>
        public bool SendsCredentialInsecurely
        {
            get
            {
                if (!HasCredential) return false;
                if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? uri)) return false;
                return uri.Scheme == Uri.UriSchemeHttp && !IsLoopbackHost(uri.Host);
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="baseUrl">Base URL.</param>
        /// <param name="source">Source.</param>
        /// <param name="profileName">Profile name, or null.</param>
        /// <param name="isLocal">True for this machine's Admiral.</param>
        /// <param name="token">Token, or null.</param>
        /// <param name="apiKey">Local API key, or null.</param>
        /// <param name="credentialSource">Credential source.</param>
        public AdmiralTarget(string baseUrl, AdmiralTargetSourceEnum source, string? profileName, bool isLocal, string? token, string? apiKey, AdmiralCredentialSourceEnum credentialSource)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            BaseUrl = baseUrl.Trim().TrimEnd('/');
            Source = source;
            ProfileName = String.IsNullOrWhiteSpace(profileName) ? null : profileName;
            IsLocal = isLocal;
            Token = String.IsNullOrWhiteSpace(token) ? null : token.Trim();
            ApiKey = String.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            CredentialSource = credentialSource;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A one-line description for messages, for example <c>profile 'prod' (https://armada.example.com)</c>. Never
        /// includes a credential.
        /// </summary>
        /// <returns>Description.</returns>
        public string Describe()
        {
            switch (Source)
            {
                case AdmiralTargetSourceEnum.Flag:
                    if (String.Equals(SourceDetail, "--profile", StringComparison.Ordinal))
                        return IsLocal && ProfileName == null ? "the local Admiral (" + BaseUrl + ", from --profile)" : "profile '" + ProfileName + "' (" + BaseUrl + ", from --profile)";
                    return BaseUrl + " (from --server)";
                case AdmiralTargetSourceEnum.Environment:
                    return BaseUrl + " (from " + (SourceDetail ?? AdmiralTargetResolver.ServerUrlEnvironmentVariable) + ")";
                case AdmiralTargetSourceEnum.Profile:
                    return "profile '" + ProfileName + "' (" + BaseUrl + ", the active profile)";
                default:
                    return "the local Admiral (" + BaseUrl + ")";
            }
        }

        /// <summary>
        /// Describe the credential source without revealing the credential.
        /// </summary>
        /// <returns>Description.</returns>
        public string DescribeCredential()
        {
            switch (CredentialSource)
            {
                case AdmiralCredentialSourceEnum.LocalApiKey: return "local API key from settings.json";
                case AdmiralCredentialSourceEnum.Flag: return "token from --token";
                case AdmiralCredentialSourceEnum.Environment: return "token from " + AdmiralTargetResolver.TokenEnvironmentVariable;
                case AdmiralCredentialSourceEnum.ProfileStore: return "token stored for profile '" + ProfileName + "'";
                default: return "none";
            }
        }

        /// <summary>
        /// Add the credential headers to a request header collection. A token is sent as <c>Authorization: Bearer</c>,
        /// <c>X-Token</c>, and <c>X-Api-Key</c> so a bearer token, a dashboard or TUI session token, or an Admiral API
        /// key all authenticate (the Admiral accepts the first that validates). The local API key alone goes as
        /// <c>X-Api-Key</c>.
        /// </summary>
        /// <param name="headers">Headers.</param>
        public void ApplyCredentials(HttpRequestHeaders headers)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers));
            headers.Remove("Authorization");
            headers.Remove("X-Token");
            headers.Remove("X-Api-Key");
            if (!String.IsNullOrEmpty(Token))
            {
                headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                headers.TryAddWithoutValidation("X-Token", Token);
                headers.TryAddWithoutValidation("X-Api-Key", Token);
            }
            else if (!String.IsNullOrEmpty(ApiKey))
            {
                headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
            }
        }

        /// <summary>
        /// True when a URL host names this machine's loopback interface (localhost, 127.0.0.0/8, ::1).
        /// </summary>
        /// <param name="host">Host.</param>
        /// <returns>True for loopback.</returns>
        public static bool IsLoopbackHost(string? host)
        {
            if (String.IsNullOrWhiteSpace(host)) return false;
            string trimmed = host.Trim().Trim('[', ']');
            if (String.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(trimmed, out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        #endregion
    }
}
