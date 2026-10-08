namespace Armada.Client.Metrics
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// One answer from <see cref="HarborMetricsFeed"/>: the metrics, or why there are none, in plain language.
    /// </summary>
    public class HarborMetricsLoadResult
    {
        #region Public-Members

        /// <summary>
        /// Outcome.
        /// </summary>
        public HarborMetricsLoadStateEnum State { get; set; } = HarborMetricsLoadStateEnum.Failed;

        /// <summary>
        /// The metrics when <see cref="State"/> is Loaded, else null.
        /// </summary>
        public HarborMetrics? Metrics { get; set; } = null;

        /// <summary>
        /// Range asked for (1h, 24h, or 7d).
        /// </summary>
        public string Range { get; set; } = "24h";

        /// <summary>
        /// Harbor ID asked about.
        /// </summary>
        public string HarborId { get; set; } = String.Empty;

        /// <summary>
        /// HTTP status of the failing response, or 0 when there was none (offline, or no request was made).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// What happened, in plain language, for the user; empty when loaded.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// The server's or transport's own error text, for logs and details; empty when none.
        /// </summary>
        public string Detail { get; set; } = String.Empty;

        /// <summary>
        /// When the answer was received (UTC).
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True when the metrics arrived.
        /// </summary>
        public bool IsLoaded
        {
            get { return State == HarborMetricsLoadStateEnum.Loaded && Metrics != null; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborMetricsLoadResult()
        {
        }

        #endregion
    }
}
