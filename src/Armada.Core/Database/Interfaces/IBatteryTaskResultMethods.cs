namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for battery task results (battery_task_results): one row per graded task attempt.
    /// </summary>
    public interface IBatteryTaskResultMethods
    {
        /// <summary>
        /// Create a result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created result.</returns>
        Task<BatteryTaskResult> CreateAsync(BatteryTaskResult result, CancellationToken token = default);

        /// <summary>
        /// Every result of a run, in creation order.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The results (empty when none).</returns>
        Task<List<BatteryTaskResult>> ReadByRunAsync(string runId, CancellationToken token = default);

        /// <summary>
        /// One page of a run's results, filtered by outcome (query.Status) when set, ordered by creation (query.Order)
        /// with the id as the tie-break. Paged in the database; TotalRecords counts the same filter.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="query">Paging query (PageNumber, PageSize 1 to 1000, Order, Status as the outcome filter).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of results.</returns>
        /// <exception cref="ArgumentNullException">Thrown when runId or query is null.</exception>
        Task<EnumerationResult<BatteryTaskResult>> EnumerateByRunAsync(string runId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete every result of a run.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of rows deleted.</returns>
        Task<int> DeleteByRunAsync(string runId, CancellationToken token = default);
    }
}
