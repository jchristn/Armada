namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>
    /// One metric (lines, branches, functions, statements) of an Istanbul json-summary report.
    /// </summary>
    public class IstanbulCoverageMetric
    {
        #region Public-Members

        /// <summary>
        /// Total items.
        /// </summary>
        [JsonPropertyName("total")]
        public int? Total { get; set; } = null;

        /// <summary>
        /// Covered items.
        /// </summary>
        [JsonPropertyName("covered")]
        public int? Covered { get; set; } = null;

        /// <summary>
        /// Percentage covered. Istanbul writes the string "Unknown" when the total is zero; that reads as null.
        /// </summary>
        [JsonPropertyName("pct")]
        [JsonConverter(typeof(LenientNullableDoubleConverter))]
        public double? Pct { get; set; } = null;

        #endregion
    }
}
