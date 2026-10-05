namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The "total" block of an Istanbul json-summary report.
    /// </summary>
    public class IstanbulCoverageTotals
    {
        #region Public-Members

        /// <summary>
        /// Line coverage.
        /// </summary>
        [JsonPropertyName("lines")]
        public IstanbulCoverageMetric? Lines { get; set; } = null;

        /// <summary>
        /// Branch coverage.
        /// </summary>
        [JsonPropertyName("branches")]
        public IstanbulCoverageMetric? Branches { get; set; } = null;

        /// <summary>
        /// Function coverage.
        /// </summary>
        [JsonPropertyName("functions")]
        public IstanbulCoverageMetric? Functions { get; set; } = null;

        /// <summary>
        /// Statement coverage.
        /// </summary>
        [JsonPropertyName("statements")]
        public IstanbulCoverageMetric? Statements { get; set; } = null;

        #endregion
    }
}
