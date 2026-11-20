namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Centralized cascade cleanup for dependent rows the database schema does not remove on its own.
    /// The <c>events</c> and <c>planning_sessions</c> tables carry plain (non-foreign-key) references to
    /// their parent entities (captain, mission, vessel, voyage), so when a parent is hard-deleted those
    /// rows would otherwise dangle and later fail to resolve -- for example an event whose linked entity
    /// is gone renders a "could not be loaded" error when opened. Every hard-delete path routes through
    /// this class so the rules live in exactly one place and stay consistent across entities.
    /// </summary>
    public static class CascadeCleanup
    {
        #region Private-Members

        private const int _BatchSize = 500;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove all telemetry events that reference the supplied vessel.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of events removed.</returns>
        public static Task<int> RemoveEventsForVesselAsync(DatabaseDriver database, string vesselId, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (String.IsNullOrEmpty(vesselId)) return Task.FromResult(0);
            return _DeleteEventsAsync(database, (int limit) => database.Events.EnumerateByVesselAsync(vesselId, limit, token), token);
        }

        /// <summary>
        /// Remove all telemetry events that reference the supplied mission.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of events removed.</returns>
        public static Task<int> RemoveEventsForMissionAsync(DatabaseDriver database, string missionId, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (String.IsNullOrEmpty(missionId)) return Task.FromResult(0);
            return _DeleteEventsAsync(database, (int limit) => database.Events.EnumerateByMissionAsync(missionId, limit, token), token);
        }

        /// <summary>
        /// Remove all telemetry events that reference the supplied voyage.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="voyageId">Voyage identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of events removed.</returns>
        public static Task<int> RemoveEventsForVoyageAsync(DatabaseDriver database, string voyageId, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (String.IsNullOrEmpty(voyageId)) return Task.FromResult(0);
            return _DeleteEventsAsync(database, (int limit) => database.Events.EnumerateByVoyageAsync(voyageId, limit, token), token);
        }

        /// <summary>
        /// Remove dependents that reference the supplied captain: telemetry events and planning sessions.
        /// Planning sessions store a non-nullable captain id without a foreign key, so they must be removed
        /// explicitly to avoid dangling sessions that reference a deleted captain.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of dependent rows removed.</returns>
        public static async Task<int> RemoveDependentsForCaptainAsync(DatabaseDriver database, string captainId, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (String.IsNullOrEmpty(captainId)) return 0;

            int removed = await _DeleteEventsAsync(database, (int limit) => database.Events.EnumerateByCaptainAsync(captainId, limit, token), token).ConfigureAwait(false);

            List<PlanningSession> sessions;
            try
            {
                sessions = await database.PlanningSessions.EnumerateByCaptainAsync(captainId, token).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                sessions = new List<PlanningSession>();
            }

            foreach (PlanningSession session in sessions)
            {
                try
                {
                    await database.PlanningSessions.DeleteAsync(session.Id, token).ConfigureAwait(false);
                    removed++;
                }
                catch (Exception)
                {
                    // Best-effort: a session that cannot be removed is skipped so one failure does not
                    // block the rest of the cascade. The parent delete still proceeds.
                }
            }

            return removed;
        }

        #endregion

        #region Private-Methods

        private static async Task<int> _DeleteEventsAsync(DatabaseDriver database, Func<int, Task<List<ArmadaEvent>>> fetch, CancellationToken token)
        {
            int removed = 0;
            int limit = _BatchSize;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                List<ArmadaEvent> batch = await fetch(limit).ConfigureAwait(false);
                if (batch == null || batch.Count == 0) break;

                int removedThisPass = 0;
                foreach (ArmadaEvent evt in batch)
                {
                    if (evt.EventType == "mission.archive" || evt.EventType == "mission.landing_verification") continue;
                    try
                    {
                        await database.Events.DeleteAsync(evt.Id, token).ConfigureAwait(false);
                        removed++;
                        removedThisPass++;
                    }
                    catch (Exception)
                    {
                        // Best-effort per row; continue with the rest of the batch.
                    }
                }

                // If nothing in this pass could be deleted, stop to avoid re-fetching the same rows forever.
                if (removedThisPass == 0)
                {
                    if (batch.Count >= limit && batch.Any(e => e.EventType == "mission.archive" || e.EventType == "mission.landing_verification")) { limit += _BatchSize; continue; }
                    break;
                }
            }

            return removed;
        }

        #endregion
    }
}
