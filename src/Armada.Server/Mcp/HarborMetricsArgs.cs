namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of get_harbor_metrics.
    /// </summary>
    public class HarborMetricsArgs
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier (hbr_ prefix).
        /// </summary>
        public string HarborId { get; set; } = string.Empty;

        /// <summary>
        /// Window: 1h, 24h, or 7d; null for 24h.
        /// </summary>
        public string? Range { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborMetricsArgs()
        {
        }

        #endregion
    }
}
