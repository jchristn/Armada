namespace Armada.Core.Connectivity
{
    using System;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Settings for a <see cref="UrlProbe"/>.
    /// </summary>
    public class UrlProbeOptions
    {
        #region Public-Members

        /// <summary>
        /// Most time the whole probe may take, in milliseconds (100 to 120000; default 10000). A stage still running
        /// when it runs out fails with <see cref="UrlProbeFailureEnum.Timeout"/>.
        /// </summary>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set
            {
                if (value < 100 || value > 120000) throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "TimeoutMs must be between 100 and 120000");
                _TimeoutMs = value;
            }
        }

        /// <summary>
        /// User-Agent header sent with the HTTP request.
        /// </summary>
        public string UserAgent
        {
            get { return _UserAgent; }
            set { _UserAgent = String.IsNullOrWhiteSpace(value) ? "Armada-UrlProbe" : value; }
        }

        /// <summary>
        /// Resolves a host name to addresses. Null uses the system resolver. Tests replace it.
        /// </summary>
        public Func<string, CancellationToken, Task<IPAddress[]>>? Resolver { get; set; } = null;

        #endregion

        #region Private-Members

        private int _TimeoutMs = 10000;
        private string _UserAgent = "Armada-UrlProbe";

        #endregion
    }
}
