namespace Armada.Core.Models
{
    /// <summary>
    /// One scorecard metric. A rate is a count with its denominator ("4 of 11") plus a 95% Wilson score interval; a cost
    /// is a total with the number of results it covers and the value per result. A metric whose source Armada does not
    /// record today is returned with <see cref="Available"/> false and the reason, never estimated.
    /// </summary>
    public class ModelScorecardMetric
    {
        #region Public-Members

        /// <summary>
        /// Stable metric key, for example firstPassSuccess or stageCheck.skipped.NoCommands.
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// What is counted and what the denominator is.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Whether the metric could be computed from what Armada records.
        /// </summary>
        public bool Available { get; set; } = true;

        /// <summary>
        /// Why the metric is unavailable, or null when it is available.
        /// </summary>
        public string? UnavailableReason { get; set; } = null;

        /// <summary>
        /// Numerator (the "4" of "4 of 11"), or null when unavailable.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// Denominator (the "11" of "4 of 11"), or null when unavailable.
        /// </summary>
        public int? Of { get; set; } = null;

        /// <summary>
        /// Count divided by Of, rounded to four places, or null when Of is zero or the metric is not a rate.
        /// </summary>
        public double? Rate { get; set; } = null;

        /// <summary>
        /// Lower bound of the 95% Wilson score interval of the rate (0 when Of is zero), or null when not a rate.
        /// </summary>
        public double? Lower { get; set; } = null;

        /// <summary>
        /// Upper bound of the 95% Wilson score interval of the rate (1 when Of is zero), or null when not a rate.
        /// </summary>
        public double? Upper { get; set; } = null;

        /// <summary>
        /// Summed total for a cost metric (tokens or milliseconds over the results counted), or null.
        /// </summary>
        public long? Total { get; set; } = null;

        /// <summary>
        /// Total per counted result for a cost metric, rounded to one place, or null.
        /// </summary>
        public double? Value { get; set; } = null;

        /// <summary>
        /// Unit of Total and Value (tokens or ms), or null for a rate.
        /// </summary>
        public string? Unit { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelScorecardMetric()
        {
        }

        #endregion
    }
}
