namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// The part of GET /api/v1/status/health the upgrade suite reads.
    /// </summary>
    public sealed class UpgradeHealthResponse
    {
        /// <summary>
        /// Product version the Admiral reports.
        /// </summary>
        public string? Version { get; set; } = null;
    }
}
