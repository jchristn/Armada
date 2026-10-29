namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of run_blackbox_recovery.
    /// </summary>
    public class BlackBoxRecoveryArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Recovery path id from the entry's analysis.
        /// </summary>
        public string? PathId { get; set; } = null;

        /// <summary>
        /// Dry run (default true).
        /// </summary>
        public bool? DryRun { get; set; } = null;

        /// <summary>
        /// Token from the dry run, required for the real run.
        /// </summary>
        public string? ConfirmToken { get; set; } = null;

        /// <summary>
        /// Must be true to run a path marked dataLossRisk.
        /// </summary>
        public bool? AcknowledgeDataLoss { get; set; } = null;
    }
}
