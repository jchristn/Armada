namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Settings for <see cref="ArmadaSocket"/>.
    /// </summary>
    public class ArmadaSocketOptions
    {
        #region Public-Members

        /// <summary>
        /// Base URL of the Admiral REST API (http or https); the socket connects to <c>/ws</c> on the same host.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string BaseUrl
        {
            get { return _BaseUrl; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(BaseUrl));
                _BaseUrl = value.Trim().TrimEnd('/');
            }
        }

        /// <summary>
        /// Returns the current session token (or API key) for each connection attempt, so a refreshed token is used on
        /// reconnect. Null or a null result connects without a token.
        /// </summary>
        public Func<string?>? TokenProvider { get; set; } = null;

        /// <summary>
        /// Creates the transport for each connection attempt. Null uses <see cref="ClientWebSocketTransport"/>.
        /// </summary>
        public Func<IArmadaSocketTransport>? TransportFactory { get; set; } = null;

        /// <summary>
        /// Source of jitter in [0, 1). Null uses a shared random generator. Tests pass a fixed value.
        /// </summary>
        public Func<double>? Random { get; set; } = null;

        /// <summary>
        /// Delay function used between reconnect attempts. Null uses <see cref="System.Threading.Tasks.Task.Delay(int, System.Threading.CancellationToken)"/>.
        /// Tests substitute an immediate delay.
        /// </summary>
        public Func<int, System.Threading.CancellationToken, System.Threading.Tasks.Task>? Delay { get; set; } = null;

        /// <summary>
        /// Connect timeout in milliseconds. Default 10000. Clamped to 1000..120000.
        /// </summary>
        public int ConnectTimeoutMs
        {
            get { return _ConnectTimeoutMs; }
            set { _ConnectTimeoutMs = Math.Clamp(value, 1000, 120000); }
        }

        /// <summary>
        /// Send <c>{ Route: subscribe, AllTenants: true }</c> instead of a tenant-scoped subscribe (global admins).
        /// Default false.
        /// </summary>
        public bool AllTenants { get; set; } = false;

        #endregion

        #region Private-Members

        private string _BaseUrl = "http://127.0.0.1:7890";
        private int _ConnectTimeoutMs = 10000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ArmadaSocketOptions()
        {
        }

        /// <summary>
        /// Instantiate for a base URL and token provider.
        /// </summary>
        /// <param name="baseUrl">Admiral base URL.</param>
        /// <param name="tokenProvider">Token provider, or null.</param>
        public ArmadaSocketOptions(string baseUrl, Func<string?>? tokenProvider)
        {
            BaseUrl = baseUrl;
            TokenProvider = tokenProvider;
        }

        #endregion
    }
}
