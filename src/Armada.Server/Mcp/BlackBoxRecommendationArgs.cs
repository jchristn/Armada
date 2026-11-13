namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of apply_blackbox_recommendation.
    /// </summary>
    public class BlackBoxRecommendationArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Configuration recommendation id from the entry's analysis.
        /// </summary>
        public string? RecommendationId { get; set; } = null;

        /// <summary>
        /// Dry run (default true).
        /// </summary>
        public bool? DryRun { get; set; } = null;

        /// <summary>
        /// Token from the dry run, required for the real run.
        /// </summary>
        public string? ConfirmToken { get; set; } = null;
    }
}
