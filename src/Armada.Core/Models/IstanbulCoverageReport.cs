namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Istanbul json-summary report (coverage-summary.json); only the aggregate block is read.
    /// </summary>
    public class IstanbulCoverageReport
    {
        #region Public-Members

        /// <summary>
        /// Aggregate totals.
        /// </summary>
        [JsonPropertyName("total")]
        public IstanbulCoverageTotals? Total { get; set; } = null;

        #endregion
    }
}
