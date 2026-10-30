namespace Armada.Core.BlackBox
{
    using Armada.Core.Models;

    /// <summary>
    /// Interprets a black box record: why findings, configuration recommendations, Armada improvement hypotheses, ranked
    /// recovery paths and the triage verdict. Pure and deterministic (no I/O), so every rule has golden tests. Every claim
    /// carries a rule id. Implementations must be thread-safe. Armada's implementation is <see cref="BlackBoxAdvisor"/> over
    /// <see cref="BlackBoxRuleCatalog"/>.
    /// </summary>
    public interface IBlackBoxAdvisor
    {
        /// <summary>
        /// Version of the rules; stored on the entry so a rules change can re-analyze older entries.
        /// </summary>
        int RulesVersion { get; }

        /// <summary>
        /// Analyze a record.
        /// </summary>
        /// <param name="record">Record (not modified).</param>
        /// <returns>The analysis, or null when the advisor has nothing to say.</returns>
        BlackBoxAnalysis? Analyze(BlackBoxRecord record);
    }
}
