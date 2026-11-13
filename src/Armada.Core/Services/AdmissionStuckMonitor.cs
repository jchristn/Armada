namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Bounded, observational detection of Pending work that stays ready across dispatch attempts.</summary>
    internal sealed class AdmissionStuckMonitor
    {
        internal const int BatchSize = 64;
        internal const int RequiredObservations = 3;
        internal sealed record Window(long Sequence, List<Mission> Selected, HashSet<string> Pending,
            Dictionary<string, DateTime> Ready);
        private readonly DatabaseDriver _Database;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private long _Sequence;
        private long _Completed;
        private readonly Dictionary<string, (DateTime Version, int Count)> _Consecutive = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _Reported = new(StringComparer.Ordinal);
        internal AdmissionStuckMonitor(DatabaseDriver database) { _Database = database; }

        internal async Task<Window> CaptureAsync(Func<bool> memoryAllows,
            Func<Mission, CancellationToken, Task<bool>> ready, CancellationToken token, List<Mission>? pendingSnapshot = null)
        {
            long sequence = Interlocked.Increment(ref _Sequence);
            List<Mission> pending = (pendingSnapshot ?? await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false))
                .OrderBy(m => m.Priority).ThenBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            int offset = pending.Count == 0 ? 0 : (int)((sequence - 1) / RequiredObservations * BatchSize % pending.Count);
            List<Mission> selected = pending.Skip(offset).Take(BatchSize).ToList();
            Window window = new(sequence, selected, pending.Select(m => m.Id).ToHashSet(StringComparer.Ordinal), new(StringComparer.Ordinal));
            if (memoryAllows())
                foreach (Mission mission in selected)
                    if (await ready(mission, token).ConfigureAwait(false)) window.Ready[mission.Id] = mission.LastUpdateUtc;
            return window;
        }

        internal async Task CompleteAsync(Window? window, bool dispatchCompleted, Func<bool> memoryAllows,
            Func<Mission, CancellationToken, Task<bool>> ready, CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (window == null || !dispatchCompleted) { _Consecutive.Clear(); return; }
                if (window.Sequence <= _Completed) return;
                if (window.Sequence != _Completed + 1) _Consecutive.Clear();
                _Completed = window.Sequence;
                foreach (string id in _Reported.Keys.Where(id => !window.Pending.Contains(id)).ToList()) _Reported.Remove(id);
                HashSet<string> observed = new(StringComparer.Ordinal);
                bool memory = memoryAllows();
                foreach (Mission sampled in window.Selected)
                {
                    Mission? current = memory && window.Ready.ContainsKey(sampled.Id)
                        ? await _Database.Missions.ReadAsync(sampled.Id, token).ConfigureAwait(false) : null;
                    bool eligible = current != null && current.Status == MissionStatusEnum.Pending
                        && current.LastUpdateUtc == window.Ready[sampled.Id] && await ready(current, token).ConfigureAwait(false);
                    if (!eligible) { _Consecutive.Remove(sampled.Id); _Reported.Remove(sampled.Id); continue; }
                    observed.Add(current!.Id);
                    int count = _Consecutive.TryGetValue(current.Id, out var previous) && previous.Version == current.LastUpdateUtc
                        ? Math.Min(previous.Count + 1, RequiredObservations) : 1;
                    _Consecutive[current.Id] = (current.LastUpdateUtc, count);
                    if (count < RequiredObservations || _Reported.TryGetValue(current.Id, out DateTime reported) && reported == current.LastUpdateUtc) continue;
                    await _Database.Events.CreateAsync(new ArmadaEvent("mission.admission_stuck",
                        "Mission remained Pending before and after three dispatch opportunities with capacity and preflight checks open. Inspect scheduler claims and assignment errors.")
                    {
                        TenantId = current.TenantId, UserId = current.UserId, EntityType = "mission", EntityId = current.Id,
                        MissionId = current.Id, VesselId = current.VesselId, VoyageId = current.VoyageId,
                        Payload = JsonSerializer.Serialize(new { ReadyObservations = count, MissionVersionUtc = current.LastUpdateUtc })
                    }, token).ConfigureAwait(false);
                    ArmadaMetrics.AdmissionStuck.Add(1);
                    _Reported[current.Id] = current.LastUpdateUtc;
                }
                foreach (string id in _Consecutive.Keys.Where(id => !observed.Contains(id)).ToList()) _Consecutive.Remove(id);
            }
            finally { _Gate.Release(); }
        }
    }
}
