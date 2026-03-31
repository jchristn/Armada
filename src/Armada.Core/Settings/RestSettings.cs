namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// REST API listener configuration.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>
        /// Listener hostname or IP address.
        /// Use "localhost" for local-only access, "*" or "+" for all interfaces.
        /// </summary>
        public string Hostname
        {
            get => _Hostname;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Hostname));
                _Hostname = value;
            }
        }

        /// <summary>
        /// Whether to enable SSL/TLS.
        /// </summary>
        public bool Ssl { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Hostname = "localhost";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public RestSettings()
        {
        }

        #endregion
    }
}
