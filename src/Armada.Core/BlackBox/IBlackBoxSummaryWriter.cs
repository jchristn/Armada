namespace Armada.Core.BlackBox
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Writes the optional model-written Analysis.Summary of a black box entry (BlackBox.SummaryEndpointId). The pipeline
    /// labels the result model-written; rule findings stay authoritative. Any failure (null, empty, exception, timeout)
    /// falls back silently to the rules summary. Implementations must be thread-safe.
    /// </summary>
    public interface IBlackBoxSummaryWriter
    {
        /// <summary>
        /// Write a summary.
        /// </summary>
        /// <param name="endpointId">Model endpoint id from the settings.</param>
        /// <param name="record">Record (not modified).</param>
        /// <param name="analysis">The rules analysis (findings, triage, recovery paths).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary text, or null when none could be written.</returns>
        Task<string?> WriteSummaryAsync(string endpointId, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default);
    }
}
