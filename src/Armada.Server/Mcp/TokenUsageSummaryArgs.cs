namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments for the <c>token_usage_summary</c> MCP tool.
    /// </summary>
    public class TokenUsageSummaryArgs
    {
        #region Public-Members

        /// <summary>
        /// Only include usage newer than this many hours (default 24). Ignored when FromUtc is supplied.
        /// </summary>
        public int? SinceHours { get; set; } = null;

        /// <summary>
        /// Explicit UTC window start (ISO-8601). Overrides SinceHours when supplied.
        /// </summary>
        public string? FromUtc { get; set; } = null;

        /// <summary>
        /// Explicit UTC window end (ISO-8601, default now).
        /// </summary>
        public string? ToUtc { get; set; } = null;

        /// <summary>
        /// Time-bucket width in minutes; fractional allowed, e.g. 0.5 for 30-second buckets (default 60).
        /// </summary>
        public double? BucketMinutes { get; set; } = null;

        /// <summary>
        /// Optional model filter.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Optional runtime filter.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Optional source filter (mission, chat, planning).
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Optional vessel filter.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional captain filter.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Optional Harbor filter.
        /// </summary>
        public string? HarborId { get; set; } = null;

        #endregion
    }
}
