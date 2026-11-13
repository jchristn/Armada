namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Runs after the advisor analyzed a record and before the record is written (the advisor itself is pure; a hook may
    /// read the database). Used for runbook linkage. A hook that throws is logged and ignored.
    /// </summary>
    public interface IBlackBoxAnalysisHook
    {
        /// <summary>
        /// Adjust a fresh analysis.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="record">Record.</param>
        /// <param name="analysis">The analysis the advisor produced (may be changed in place).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task OnAnalyzedAsync(BlackBoxEntry entry, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default);
    }
}
