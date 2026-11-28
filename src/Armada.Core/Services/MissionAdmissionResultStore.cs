namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using Armada.Core.Models;

    /// <summary>
    /// The latest admission result of each Pending mission (RS-4.6) and the wait each in-progress admission attempt
    /// stopped at. The only owner of that state.
    /// </summary>
    internal sealed class MissionAdmissionResultStore
    {
        private readonly ConcurrentDictionary<string, MissionAdmissionResult> _Results = new ConcurrentDictionary<string, MissionAdmissionResult>(StringComparer.Ordinal);
        // The gate each failed admission attempt stopped at, recorded by the public wrapper once the locks are released.
        private readonly ConcurrentDictionary<string, MissionWaitObservation> _AttemptWaits = new ConcurrentDictionary<string, MissionWaitObservation>(StringComparer.Ordinal);

        /// <summary>Note the outcome of the current attempt, replacing any earlier note for the mission.</summary>
        /// <param name="observation">The attempt's outcome.</param>
        internal void NoteAttempt(MissionWaitObservation observation) => _AttemptWaits[observation.Mission.Id] = observation;

        /// <summary>Take the outcome noted for the current attempt.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="observation">The noted outcome.</param>
        /// <returns>True when one was noted.</returns>
        internal bool TryTakeAttempt(string missionId, [NotNullWhen(true)] out MissionWaitObservation? observation)
        {
            return _AttemptWaits.TryRemove(missionId, out observation);
        }

        /// <summary>Record an attempt's outcome as the mission's admission result.</summary>
        /// <param name="observation">The attempt's outcome.</param>
        internal void Record(MissionWaitObservation observation)
        {
            string id = observation.Mission.Id;
            if (observation.LeftPending || observation.Blocker == null) { _Results.TryRemove(id, out _); return; }
            DateTime now = DateTime.UtcNow;
            MissionAssignmentBlocker blocker = observation.Blocker;
            _Results.AddOrUpdate(id,
                _ => new MissionAdmissionResult { Mission = observation.Mission, Blocker = blocker, FirstObservedUtc = now, LastObservedUtc = now },
                (_, previous) => new MissionAdmissionResult
                {
                    Mission = observation.Mission,
                    Blocker = blocker,
                    FirstObservedUtc = previous.Blocker.Reason == blocker.Reason ? previous.FirstObservedUtc : now,
                    LastObservedUtc = now
                });
        }

        /// <summary>Drop a mission's admission result.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void Remove(string missionId) => _Results.TryRemove(missionId, out _);

        /// <summary>A mission's admission result, or null.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>The result or null.</returns>
        internal MissionAdmissionResult? Get(string missionId)
            => !String.IsNullOrEmpty(missionId) && _Results.TryGetValue(missionId, out MissionAdmissionResult? result) ? result : null;

        /// <summary>Drop results of missions that left Pending outside admission before the snapshot.</summary>
        /// <param name="pending">Missions still Pending.</param>
        /// <param name="snapshotUtc">When the pending set was read.</param>
        internal void DropNotPending(HashSet<string> pending, DateTime snapshotUtc)
        {
            foreach (KeyValuePair<string, MissionAdmissionResult> entry in _Results)
                if (!pending.Contains(entry.Key) && entry.Value.LastObservedUtc < snapshotUtc)
                    _Results.TryRemove(entry);
        }
    }
}
