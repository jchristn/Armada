namespace Armada.Core.Services
{
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// How many stored process ids were restored after a restart, and how many stayed unverified.
    /// </summary>
    public sealed class ProcessLaunchIdentityRestoreResult
    {
        /// <summary>
        /// Rows whose process id and start time were recorded in memory.
        /// </summary>
        public int RestoredCount { get; set; }

        /// <summary>
        /// Rows that still have a process id and no start time.
        /// </summary>
        public int UnverifiedCount { get; set; }
    }

    /// <summary>
    /// Load captain and in-progress mission process start times back into the in-memory launch record
    /// after the admiral restarts. A row with a process id and no start time stays unverified.
    /// </summary>
    public static class ProcessLaunchIdentityRestore
    {
        /// <summary>
        /// Restore launch identities from captains and from missions that are still running.
        /// </summary>
        /// <param name="database">Initialized database.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many identities were restored and how many process ids stayed unverified.</returns>
        public static async Task<ProcessLaunchIdentityRestoreResult> RestoreAsync(DatabaseDriver database, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            ProcessLaunchIdentityRestoreResult result = new ProcessLaunchIdentityRestoreResult();
            List<Captain> captains = await database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            foreach (Captain captain in captains)
            {
                Count(result, captain.ProcessId, captain.ProcessStartedUtc);
            }

            List<Mission> missions = await database.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress, token).ConfigureAwait(false);
            foreach (Mission mission in missions)
            {
                Count(result, mission.ProcessId, mission.ProcessStartedUtc);
            }

            return result;
        }

        private static void Count(ProcessLaunchIdentityRestoreResult result, int? processId, DateTime? startedUtc)
        {
            if (!processId.HasValue || processId.Value <= 0) return;
            if (!startedUtc.HasValue)
            {
                result.UnverifiedCount++;
                return;
            }

            if (ProcessSupervisor.RestoreLaunch(processId.Value, startedUtc.Value)) result.RestoredCount++;
        }
    }
}
