namespace Armada.Core.Harbor
{
    /// <summary>
    /// The Harbor capability for context discovery (RELIABILITY_SIMPLICITY_PLAN.md RS-7.8).
    /// </summary>
    public static class HarborContextDiscovery
    {
        /// <summary>
        /// Capability a Harbor advertises when, for an API captain whose endpoint configures no context window, it reads
        /// the window from the model server on its own host before the first turn, uses it for compaction, and reports it
        /// in <see cref="HarborExited.DiscoveredContextLength"/>. An older Harbor runs without a window, as in 1.4.0.
        /// </summary>
        public const string Capability = "api-context-discovery-v1";
    }
}
