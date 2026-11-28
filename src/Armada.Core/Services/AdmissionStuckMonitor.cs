namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Flags Pending work whose gates were all open on three consecutive heartbeat dispatch passes yet never got a claim.
    /// It only reads the typed results dispatch recorded (RS-4.6) and never evaluates admission itself.
    /// </summary>
    internal sealed class AdmissionStuckMonitor
    {
        internal const int RequiredObservations = 3;
        internal const string EventType = "mission.admission_stuck";
        private readonly DatabaseDriver _Database;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, Observation> _Consecutive = new Dictionary<string, Observation>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _Reported = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        internal AdmissionStuckMonitor(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>Observe the results of one completed heartbeat dispatch pass.</summary>
        /// <param name="attempted">Results of the missions the pass attempted and left Pending.</param>
        /// <param name="passCompleted">False when the pass did not run (no capacity, memory pressure, an error).</param>
        /// <param name="token">Cancellation token.</param>
        internal async Task ObserveAsync(IReadOnlyList<MissionAdmissionResult> attempted, bool passCompleted, CancellationToken token = default)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!passCompleted) { _Consecutive.Clear(); return; }
                HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);
                foreach (MissionAdmissionResult result in attempted)
                {
                    string id = result.Mission.Id;
                    DateTime version = result.Mission.LastUpdateUtc;
                    if (result.Blocker.Reason != MissionAssignmentBlockerReasonEnum.AwaitingDispatch) { _Reported.Remove(id); continue; }
                    open.Add(id);
                    int count = _Consecutive.TryGetValue(id, out Observation? previous) && previous.Version == version
                        ? Math.Min(previous.Count + 1, RequiredObservations) : 1;
                    _Consecutive[id] = new Observation(version, count);
                    if (count < RequiredObservations || _Reported.TryGetValue(id, out DateTime reported) && reported == version) continue;
                    // Only a flagged mission costs a read: confirm it is still the same Pending row before reporting it.
                    Mission? current = await _Database.Missions.ReadAsync(id, token).ConfigureAwait(false);
                    if (current == null || current.Status != MissionStatusEnum.Pending || current.LastUpdateUtc != version)
                    { _Consecutive.Remove(id); continue; }
                    await _Database.Events.CreateAsync(new ArmadaEvent(EventType,
                        "Mission remained Pending through three dispatch passes with every admission gate open. Inspect scheduler claims and assignment errors.")
                    {
                        TenantId = current.TenantId, UserId = current.UserId, EntityType = "mission", EntityId = current.Id,
                        MissionId = current.Id, VesselId = current.VesselId, VoyageId = current.VoyageId,
                        Payload = JsonSerializer.Serialize(new StuckPayload
                        {
                            ReadyObservations = count, MissionVersionUtc = version, BlockedSinceUtc = result.FirstObservedUtc, LastResult = result.Blocker.Summary
                        })
                    }, token).ConfigureAwait(false);
                    ArmadaMetrics.AdmissionStuck.Add(1);
                    _Reported[id] = version;
                }
                foreach (string id in _Consecutive.Keys.Where(id => !open.Contains(id)).ToList()) _Consecutive.Remove(id);
                foreach (string id in _Reported.Keys.Where(id => !open.Contains(id)).ToList()) _Reported.Remove(id);
            }
            finally { _Gate.Release(); }
        }

        private sealed class Observation
        {
            internal Observation(DateTime version, int count) { Version = version; Count = count; }
            internal DateTime Version { get; }
            internal int Count { get; }
        }

        private sealed class StuckPayload
        {
            public int ReadyObservations { get; set; }
            public DateTime MissionVersionUtc { get; set; }
            public DateTime BlockedSinceUtc { get; set; }
            public string LastResult { get; set; } = "";
        }
    }
}
