namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for recovery state that must survive an Admiral restart.
    /// </summary>
    public interface IRecoveryStateMethods
    {
        /// <summary>
        /// Insert or replace an entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertAsync(RecoveryStateEntry entry, CancellationToken token = default);

        /// <summary>
        /// Read an entry, or null when there is none.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null.</returns>
        Task<RecoveryStateEntry?> ReadAsync(string kind, string key, CancellationToken token = default);

        /// <summary>
        /// Every unexpired entry of a kind.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries.</returns>
        Task<List<RecoveryStateEntry>> EnumerateAsync(string kind, CancellationToken token = default);

        /// <summary>
        /// Delete an entry. No-op when there is none.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(string kind, string key, CancellationToken token = default);

        /// <summary>
        /// Delete every expired entry.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries removed.</returns>
        Task<int> PurgeExpiredAsync(CancellationToken token = default);
    }
}
