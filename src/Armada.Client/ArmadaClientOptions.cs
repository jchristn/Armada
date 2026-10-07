namespace Armada.Client
{
    using System;

    /// <summary>
    /// Connection settings for an <see cref="ArmadaClient"/>: the Admiral base URL, request timeout, and the
    /// credential to present. Exactly one credential is normally set; when several are set they are all sent and the
    /// server uses the first it recognizes (Authorization, then X-Token, then X-Api-Key).
    /// Not thread-safe for concurrent mutation; set values before issuing requests or use
    /// <see cref="ArmadaClient.SetToken"/> to swap the session token at runtime.
    /// </summary>
    public class ArmadaClientOptions
    {
        #region Public-Members

        /// <summary>
        /// Base URL of the Admiral REST API, for example <c>http://127.0.0.1:7890</c>. A trailing slash is removed.
        /// Must be an absolute http or https URL.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when the value is not an absolute http(s) URL.</exception>
        public string BaseUrl
        {
            get { return _BaseUrl; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(BaseUrl));
                string trimmed = value.Trim().TrimEnd('/');
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed)
                    || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                {
                    throw new ArgumentException("Base URL must be an absolute http or https URL: " + value, nameof(BaseUrl));
                }

                _BaseUrl = trimmed;
            }
        }

        /// <summary>
        /// Session token sent as the <c>X-Token</c> header (what the dashboard sends). The server also accepts an API
        /// key in this header. Null sends no token.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Bearer token sent as <c>Authorization: Bearer ...</c>. Null sends no Authorization header.
        /// </summary>
        public string? BearerToken { get; set; } = null;

        /// <summary>
        /// API key sent as the <c>X-Api-Key</c> header. Null sends no API key header.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Armada.Proxy session token (the <c>token</c> returned by <c>POST /proxy-api/v1/auth/login</c>), sent as the
        /// <c>X-Armada-Proxy-Session</c> header when <see cref="BaseUrl"/> points at an Armada.Proxy. The proxy consumes
        /// it and never relays it; the Admiral credential (<see cref="Token"/>, <see cref="BearerToken"/>, or
        /// <see cref="ApiKey"/>) is relayed to the selected deployment. Null sends no proxy header.
        /// </summary>
        public string? ProxySessionToken { get; set; } = null;

        /// <summary>
        /// Default request timeout in milliseconds. Default 30000 (the dashboard default). Minimum 1000, maximum
        /// 3600000 (one hour); values outside the range are clamped. Individual calls that the dashboard runs with a
        /// longer timeout (planning, chat, checks, imports) override this per call.
        /// </summary>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set { _TimeoutMs = Math.Clamp(value, 1000, 3600000); }
        }

        /// <summary>
        /// Value of the <c>User-Agent</c> header. Default <c>Armada.Client</c>.
        /// </summary>
        public string UserAgent { get; set; } = "Armada.Client";

        /// <summary>
        /// Value sent as the <c>Accept-Language</c> header, or null to omit it. The TUI sets this to the active locale.
        /// </summary>
        public string? AcceptLanguage { get; set; } = null;

        #endregion

        #region Private-Members

        private string _BaseUrl = "http://127.0.0.1:7890";
        private int _TimeoutMs = 30000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the default base URL (http://127.0.0.1:7890).
        /// </summary>
        public ArmadaClientOptions()
        {
        }

        /// <summary>
        /// Instantiate for a base URL.
        /// </summary>
        /// <param name="baseUrl">Absolute http(s) base URL of the Admiral.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is not an absolute http(s) URL.</exception>
        public ArmadaClientOptions(string baseUrl)
        {
            BaseUrl = baseUrl;
        }

        #endregion
    }
}
