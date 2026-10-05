namespace Armada.Core.Models
{
    /// <summary>
    /// Wire values of <see cref="HistoricalTimelineEntry.SourceType"/> that callers branch on.
    /// </summary>
    public static class HistoricalTimelineSourceTypes
    {
        #region Public-Members

        /// <summary>
        /// A request-history row (the only timeline source that maps to a deletable record).
        /// </summary>
        public const string Request = "Request";

        #endregion
    }
}
