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
    /// The one rule for when a voyage is finished, shared by the background voyage check and the completion path so the
    /// two can never disagree.
    /// </summary>
    public static class VoyageSettlement
    {
        /// <summary>
        /// Decide whether a voyage's missions have settled and, if so, its terminal status. A voyage is still running
        /// while any mission is Pending, Blocked, Assigned, InProgress, Testing, Review or PullRequestOpen, while a WorkProduced
        /// mission is still being completed (its handoff, rework or landing is being decided), or while a WorkProduced
        /// mission still has an unsettled merge-queue entry. A settled voyage is Failed when any mission Failed or
        /// LandingFailed, otherwise Complete.
        /// </summary>
        /// <param name="missions">The voyage's missions.</param>
        /// <param name="isCompletionInFlight">Whether a WorkProduced mission's completion is still running, or null.</param>
        /// <param name="activeMergeEntryMissionIds">Reads the IDs of missions with unsettled merge-queue entries; called only when needed.</param>
        /// <returns>The terminal status, or null while the voyage is still running (or has no missions).</returns>
        /// <exception cref="ArgumentNullException">Thrown when missions or activeMergeEntryMissionIds is null.</exception>
        public static async Task<VoyageStatusEnum?> DecideAsync(
            IReadOnlyList<Mission> missions,
            Func<string, bool>? isCompletionInFlight,
            Func<Task<HashSet<string>>> activeMergeEntryMissionIds)
        {
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            if (activeMergeEntryMissionIds == null) throw new ArgumentNullException(nameof(activeMergeEntryMissionIds));
            if (missions.Count == 0) return null;

            bool anyActive = missions.Any(m =>
                m.Status == MissionStatusEnum.Pending ||
                m.Status == MissionStatusEnum.Blocked ||
                m.Status == MissionStatusEnum.Assigned ||
                m.Status == MissionStatusEnum.InProgress ||
                m.Status == MissionStatusEnum.Testing ||
                m.Status == MissionStatusEnum.Review ||
                m.Status == MissionStatusEnum.PullRequestOpen ||
                (m.Status == MissionStatusEnum.WorkProduced && isCompletionInFlight != null && isCompletionInFlight(m.Id)));
            if (anyActive) return null;

            // WorkProduced counts as done here: an intermediate pipeline stage that already handed off.
            bool allDone = missions.All(m =>
                m.Status == MissionStatusEnum.Complete ||
                m.Status == MissionStatusEnum.Failed ||
                m.Status == MissionStatusEnum.Cancelled ||
                m.Status == MissionStatusEnum.LandingFailed ||
                m.Status == MissionStatusEnum.WorkProduced);
            if (!allDone) return null;

            // A WorkProduced mission still waiting in the merge queue has not landed yet (F14).
            if (missions.Any(m => m.Status == MissionStatusEnum.WorkProduced))
            {
                HashSet<string> queued = await activeMergeEntryMissionIds().ConfigureAwait(false);
                if (missions.Any(m => m.Status == MissionStatusEnum.WorkProduced && queued.Contains(m.Id))) return null;
            }

            bool anyFailed = missions.Any(m => m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.LandingFailed);
            return anyFailed ? VoyageStatusEnum.Failed : VoyageStatusEnum.Complete;
        }

        /// <summary>
        /// Record a voyage's terminal status and, for a completed voyage, settle its intermediate pipeline stages.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="voyage">The voyage.</param>
        /// <param name="status">Terminal status from <see cref="DecideAsync"/>.</param>
        /// <param name="missions">The voyage's missions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when database, voyage or missions is null.</exception>
        public static Task ApplyAsync(DatabaseDriver database, Voyage voyage, VoyageStatusEnum status, List<Mission> missions, CancellationToken token = default)
        {
            return ApplyCoreAsync(database, voyage, status, missions, null, token);
        }

        /// <summary>Record a voyage's terminal status and provide a warning callback for completion-event failures.</summary>
        /// <param name="database">Database driver.</param>
        /// <param name="voyage">The voyage.</param>
        /// <param name="status">Terminal status from <see cref="DecideAsync"/>.</param>
        /// <param name="missions">The voyage's missions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="warn">Logs a completion-event failure.</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static Task ApplyAsync(DatabaseDriver database, Voyage voyage, VoyageStatusEnum status, List<Mission> missions, CancellationToken token, Action<string> warn)
        {
            if (warn == null) throw new ArgumentNullException(nameof(warn));
            return ApplyCoreAsync(database, voyage, status, missions, warn, token);
        }

        private static async Task ApplyCoreAsync(DatabaseDriver database, Voyage voyage, VoyageStatusEnum status, List<Mission> missions, Action<string>? warn, CancellationToken token)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            if (missions == null) throw new ArgumentNullException(nameof(missions));

            VoyageStatusEnum previousStatus = voyage.Status;
            voyage.Status = status;
            voyage.CompletedUtc = DateTime.UtcNow;
            voyage.LastUpdateUtc = DateTime.UtcNow;
            await database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            if (status == VoyageStatusEnum.Complete && previousStatus != VoyageStatusEnum.Complete)
            {
                ArmadaEvent evt = new ArmadaEvent("voyage.completed", "Voyage " + voyage.Id + " completed")
                {
                    TenantId = voyage.TenantId,
                    UserId = voyage.UserId,
                    EntityType = "voyage",
                    EntityId = voyage.Id,
                    VoyageId = voyage.Id
                };

                try
                {
                    await database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    warn?.Invoke("[VoyageSettlement] could not record voyage.completed for voyage " + voyage.Id + ": " + ex.Message);
                }
            }

            if (status == VoyageStatusEnum.Complete) await PipelineStageSettler.SettleAsync(database, missions, token).ConfigureAwait(false);
        }
    }
}
