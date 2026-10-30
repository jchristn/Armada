namespace Armada.Core.BlackBox.Enrichment
{
    using System;

    /// <summary>
    /// The payload of a mission.landing_verification event (written by LandingVerification), read for
    /// evidence/verification-{n}.txt.
    /// </summary>
    public class BlackBoxVerificationReceipt
    {
        /// <summary>
        /// Passed, Failed or TimedOut.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Commit verified.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Command run (redacted).
        /// </summary>
        public string? Command { get; set; } = null;

        /// <summary>
        /// Exit code, or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Start (UTC), or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Completion (UTC), or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Output (redacted, at most 16000 chars).
        /// </summary>
        public string? Output { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxVerificationReceipt()
        {
        }
    }
}
