namespace Armada.Runtimes
{
    using Armada.Core.Models;

    /// <summary>
    /// Result of <see cref="ApiContextPolicy.AfterResponse"/>: the failure that stops the run, if any, and a diagnostic
    /// note when older output was compacted.
    /// </summary>
    public sealed class ApiContextCheck
    {
        /// <summary>
        /// The context_limit_exceeded error that stops the run, or null to carry on.
        /// </summary>
        public RuntimeProviderError? Failure { get; set; } = null;

        /// <summary>
        /// Diagnostic note describing a compaction, or null.
        /// </summary>
        public string? Note { get; set; } = null;
    }
}
