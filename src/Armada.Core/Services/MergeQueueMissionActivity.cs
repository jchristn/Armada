namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Answers whether a mission still has merge-queue work in flight (an entry that is Queued, Testing, or Passed and
    /// not yet Landed, Failed, or Cancelled). Voyage completion uses it so a voyage is not reported Complete while one of
    /// its WorkProduced missions is still waiting in the merge queue.
    /// </summary>
    public static class MergeQueueMissionActivity
    {
        #region Public-Members

        /// <summary>
        /// Merge entry statuses that mean the entry has not settled yet.
        /// </summary>
        public static readonly IReadOnlyList<MergeStatusEnum> ActiveStatuses = new List<MergeStatusEnum>
        {
            MergeStatusEnum.Queued,
            MergeStatusEnum.Testing,
            MergeStatusEnum.Passed
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Mission IDs that have at least one unsettled merge-queue entry (all tenants; used by background checks).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The mission IDs.</returns>
        public static async Task<HashSet<string>> GetMissionIdsWithActiveEntriesAsync(DatabaseDriver database, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            HashSet<string> missionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (MergeStatusEnum status in ActiveStatuses)
            {
                List<MergeEntry> entries = await database.MergeEntries.EnumerateByStatusAsync(status, token).ConfigureAwait(false);
                foreach (MergeEntry entry in entries.Where(e => !String.IsNullOrEmpty(e.MissionId)))
                {
                    missionIds.Add(entry.MissionId!);
                }
            }

            return missionIds;
        }

        /// <summary>
        /// True when any WorkProduced mission in the list still has an unsettled merge-queue entry. Reads the merge queue
        /// only when there is a WorkProduced mission to check.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="missions">The voyage's missions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the voyage is still waiting on the merge queue.</returns>
        public static async Task<bool> AnyAwaitingMergeQueueAsync(DatabaseDriver database, IReadOnlyList<Mission> missions, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            if (!missions.Any(m => m.Status == MissionStatusEnum.WorkProduced)) return false;
            HashSet<string> active = await GetMissionIdsWithActiveEntriesAsync(database, token).ConfigureAwait(false);
            return missions.Any(m => m.Status == MissionStatusEnum.WorkProduced && active.Contains(m.Id));
        }

        #endregion
    }
}
