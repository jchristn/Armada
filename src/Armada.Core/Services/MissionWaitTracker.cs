namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Records how long Pending missions wait and on which gate. One interval stays open per mission; it closes when
    /// the gate changes, the mission is assigned, or it leaves Pending. Accuracy is one scheduler tick plus direct
    /// dispatch calls, because gates are observed only when admission is attempted.
    /// </summary>
    internal sealed class MissionWaitTracker
    {
        private const string _Header = "[MissionWaitTracker] ";
        private const int _Stripes = 32;
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, OpenMissionWait> _Open = new ConcurrentDictionary<string, OpenMissionWait>(StringComparer.Ordinal);
        private readonly SemaphoreSlim[] _Locks;

        internal MissionWaitTracker(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database;
            _Settings = settings;
            _Logging = logging;
            _Locks = Enumerable.Range(0, _Stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
            StartedUtc = DateTime.UtcNow;
            ConcurrencyGaugeState.PublishWaiting(WaitingByGate);
        }

        /// <summary>When this tracker started; intervals opened earlier belong to a previous Admiral process.</summary>
        internal DateTime StartedUtc { get; }

        /// <summary>Close intervals a previous Admiral process left open.</summary>
        internal Task<int> CloseInterruptedAsync(CancellationToken token)
            => _Database.MissionWaitIntervals.CloseAllOpenAsync(DateTime.UtcNow, StartedUtc, "admiral restart", token);

        /// <summary>Record that a Pending mission waits on a gate.</summary>
        internal async Task ObserveWaitAsync(Mission mission, MissionWaitGateEnum gate, string? detail, CancellationToken token)
        {
            SemaphoreSlim gateLock = LockFor(mission.Id);
            await gateLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DateTime now = DateTime.UtcNow;
                if (!_Open.TryGetValue(mission.Id, out OpenMissionWait? wait))
                {
                    // A mission never assigned has queued since it was created; a re-pended one since it was first seen.
                    wait = new OpenMissionWait { EpisodeStartUtc = mission.AssignedUtc.HasValue ? now : mission.CreatedUtc };
                    _Open[mission.Id] = wait;
                }
                if (MissionWaitGates.IsCapacity(gate))
                {
                    if (!wait.ReadyUtc.HasValue) wait.ReadyUtc = now;
                    wait.LastCapacityGate = gate;
                }
                if (wait.Gate != gate)
                {
                    if (wait.Gate.HasValue) await CloseAsync(mission.Id, wait, now, token).ConfigureAwait(false);
                    await _Database.MissionWaitIntervals.CreateAsync(new MissionWaitInterval
                    {
                        TenantId = mission.TenantId, MissionId = mission.Id, VesselId = mission.VesselId, VoyageId = mission.VoyageId,
                        Gate = gate, StartedUtc = now, Detail = Truncate(detail)
                    }, token).ConfigureAwait(false);
                    wait.Gate = gate;
                    wait.StartedUtc = now;
                }
                await ReportStarvationAsync(mission, wait, now, token).ConfigureAwait(false);
            }
            finally { gateLock.Release(); }
        }

        /// <summary>Resolve ready and queue times for a mission about to be assigned, and stamp them on it.</summary>
        internal async Task<AssignmentWaitTiming> PrepareAssignmentAsync(Mission mission, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            AssignmentWaitTiming timing = new AssignmentWaitTiming { AssignedUtc = now, ReadyUtc = now };
            if (_Open.TryGetValue(mission.Id, out OpenMissionWait? wait))
            {
                timing.ReadyUtc = wait.ReadyUtc ?? now;
                timing.LastCapacityGate = wait.LastCapacityGate;
                timing.QueueStartUtc = wait.EpisodeStartUtc;
            }
            else if (mission.CreatedUtc < StartedUtc || mission.AssignedUtc.HasValue)
            {
                // Waits observed by a previous Admiral process survive only in the interval table.
                DateTime episodeStart = mission.AssignedUtc ?? DateTime.MinValue;
                List<MissionWaitInterval> intervals = (await _Database.MissionWaitIntervals.EnumerateByMissionAsync(mission.Id, mission.TenantId, token).ConfigureAwait(false))
                    .Where(i => i.StartedUtc >= episodeStart).ToList();
                MissionWaitInterval? firstReady = intervals.FirstOrDefault(i => MissionWaitGates.IsCapacity(i.Gate));
                MissionWaitInterval? lastReady = intervals.LastOrDefault(i => MissionWaitGates.IsCapacity(i.Gate));
                if (firstReady != null) timing.ReadyUtc = firstReady.StartedUtc;
                timing.LastCapacityGate = lastReady?.Gate;
                timing.QueueStartUtc = mission.AssignedUtc.HasValue ? intervals.FirstOrDefault()?.StartedUtc : mission.CreatedUtc;
            }
            else timing.QueueStartUtc = mission.AssignedUtc.HasValue ? null : mission.CreatedUtc;
            mission.ReadyUtc = timing.ReadyUtc;
            mission.AssignedUtc = timing.AssignedUtc;
            return timing;
        }

        /// <summary>Close the mission's wait and record slot wait and queue time after its assignment is saved.</summary>
        internal async Task CompleteAssignmentAsync(Mission mission, AssignmentWaitTiming timing, CancellationToken token)
        {
            await ReleaseAsync(mission.Id, timing.AssignedUtc, token).ConfigureAwait(false);
            KeyValuePair<string, object?> persona = new KeyValuePair<string, object?>("persona", String.IsNullOrWhiteSpace(mission.Persona) ? "none" : mission.Persona);
            ArmadaMetrics.SlotWaitSeconds.Record(Math.Max(0, (timing.AssignedUtc - timing.ReadyUtc).TotalSeconds),
                new KeyValuePair<string, object?>("gate_last", timing.LastCapacityGate?.ToString() ?? "None"), persona);
            if (timing.QueueStartUtc.HasValue)
                ArmadaMetrics.QueueSeconds.Record(Math.Max(0, (timing.AssignedUtc - timing.QueueStartUtc.Value).TotalSeconds), persona);
        }

        /// <summary>Close a mission's open wait because it left Pending.</summary>
        internal Task ReleaseAsync(string missionId, CancellationToken token) => ReleaseAsync(missionId, DateTime.UtcNow, token);

        /// <summary>Close waits for tracked missions that are no longer Pending as of a snapshot.</summary>
        internal async Task ReconcileAsync(IEnumerable<string> pendingMissionIds, DateTime snapshotUtc, CancellationToken token)
        {
            HashSet<string> pending = new HashSet<string>(pendingMissionIds, StringComparer.Ordinal);
            foreach (KeyValuePair<string, OpenMissionWait> pair in _Open.ToArray())
            {
                // A wait opened after the snapshot may belong to a mission created after it.
                if (pending.Contains(pair.Key) || pair.Value.StartedUtc >= snapshotUtc) continue;
                await ReleaseAsync(pair.Key, token).ConfigureAwait(false);
            }
        }

        /// <summary>Pending missions by their current gate, read from memory only.</summary>
        internal Dictionary<MissionWaitGateEnum, int> WaitingByGate()
        {
            Dictionary<MissionWaitGateEnum, int> counts = new Dictionary<MissionWaitGateEnum, int>();
            foreach (OpenMissionWait wait in _Open.Values)
                if (wait.Gate.HasValue) counts[wait.Gate.Value] = (counts.TryGetValue(wait.Gate.Value, out int count) ? count : 0) + 1;
            return counts;
        }

        private async Task ReleaseAsync(string missionId, DateTime endedUtc, CancellationToken token)
        {
            SemaphoreSlim gateLock = LockFor(missionId);
            await gateLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_Open.TryRemove(missionId, out OpenMissionWait? wait) || !wait.Gate.HasValue) return;
                await CloseAsync(missionId, wait, endedUtc, token).ConfigureAwait(false);
            }
            finally { gateLock.Release(); }
        }

        private async Task CloseAsync(string missionId, OpenMissionWait wait, DateTime endedUtc, CancellationToken token)
        {
            await _Database.MissionWaitIntervals.CloseOpenAsync(missionId, endedUtc, token).ConfigureAwait(false);
            ArmadaMetrics.GateWaitSeconds.Record(Math.Max(0, (endedUtc - wait.StartedUtc).TotalSeconds),
                new KeyValuePair<string, object?>("gate", wait.Gate!.Value.ToString()));
            wait.Gate = null;
        }

        private async Task ReportStarvationAsync(Mission mission, OpenMissionWait wait, DateTime now, CancellationToken token)
        {
            if (wait.StarvationReported || !wait.Gate.HasValue) return;
            TimeSpan waited = now - wait.EpisodeStartUtc;
            if (waited < TimeSpan.FromMinutes(_Settings.Concurrency.StarvationWarningMinutes)) return;
            wait.StarvationReported = true;
            ArmadaMetrics.MissionsStarving.Add(1, new KeyValuePair<string, object?>("gate", wait.Gate.Value.ToString()));
            ArmadaEvent evt = new ArmadaEvent("mission.starving", "Mission " + mission.Id + " has waited " + (int)waited.TotalMinutes + " minutes; it now waits on " + wait.Gate.Value + ".")
            {
                EntityType = "mission", EntityId = mission.Id, MissionId = mission.Id, VesselId = mission.VesselId, VoyageId = mission.VoyageId,
                TenantId = mission.TenantId, UserId = mission.UserId,
                Payload = JsonSerializer.Serialize(new MissionStarvingPayload { MissionId = mission.Id, Gate = wait.Gate.Value, WaitedMinutes = (int)waited.TotalMinutes })
            };
            try { await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "could not record starvation of mission " + mission.Id + ": " + ex.Message); }
            _Logging.Warn(_Header + evt.Message);
        }

        private SemaphoreSlim LockFor(string missionId) => _Locks[(StringComparer.Ordinal.GetHashCode(missionId) & 0x7fffffff) % _Stripes];

        private static string? Truncate(string? detail) => detail == null || detail.Length <= 512 ? detail : detail.Substring(0, 512);
    }
}
