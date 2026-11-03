namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>Observes persisted captain and vessel capacity without preempting or claiming work.</summary>
    internal sealed class ConcurrencyInvariantMonitor
    {
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, Observation> _Previous = new Dictionary<string, Observation>(StringComparer.Ordinal);
        private sealed record Observation(int Count, int Limit, string? Cause);

        internal ConcurrencyInvariantMonitor(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging)
        { _Database = database; _Settings = settings; _Logging = logging; }

        internal async Task CheckAsync(CancellationToken token = default)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (Captain captain in await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false))
                    await ObserveAsync("captain", captain.Id, captain.Name, captain.ActiveMissionCount,
                        ConcurrencyClamp.CaptainLimit(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency),
                        "captain", captain.TenantId, captain.UserId, seen, token).ConfigureAwait(false);
                foreach (Vessel vessel in await _Database.Vessels.EnumerateAsync(token).ConfigureAwait(false))
                {
                    List<MissionAdmissionSnapshot> missions = VoyageSlotPolicy.Runnable(
                        await _Database.Missions.EnumerateAdmissionSnapshotsAsync(vessel.Id, token).ConfigureAwait(false));
                    Dictionary<string, MissionAdmissionSnapshot> byId = missions.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    var groups = missions.GroupBy(m => VoyageSlotPolicy.Group(m, byId)).ToList();
                    await ObserveAsync("vessel_voyages", vessel.Id, vessel.Name, groups.Count(g => VoyageSlotPolicy.Admitted(g)),
                        vessel.MaxParallelVoyages, "vessel", vessel.TenantId, vessel.UserId, seen, token).ConfigureAwait(false);
                    // Report the busiest group once per vessel. Do not expose another user's voyage/mission IDs
                    // through events on a shared vessel, and keep event volume bounded by captains + vessels.
                    int busiest = groups.Select(g => g.Count(m => VoyageSlotPolicy.Running(m.Status))).DefaultIfEmpty(0).Max();
                    await ObserveAsync("voyage_missions", vessel.Id, vessel.Name, busiest,
                        vessel.MaxParallelMissionsPerVoyage, "vessel", vessel.TenantId, vessel.UserId, seen, token).ConfigureAwait(false);
                }
                foreach (string key in _Previous.Keys.Where(key => !seen.Contains(key)).ToList()) _Previous.Remove(key);
            }
            finally { _Gate.Release(); }
        }

        private async Task ObserveAsync(string gate, string id, string name, int count, int limit, string entityType,
            string? tenantId, string? userId, HashSet<string> seen, CancellationToken token)
        {
            string key = gate + ":" + id;
            seen.Add(key);
            _Previous.TryGetValue(key, out Observation? previous);
            if (limit == 0 || count <= limit)
            {
                _Previous[key] = new Observation(count, limit, null);
                return;
            }
            // Only claim a non-preemptive reduction when it was actually observed. An overrun first
            // seen after restart is unknown, not proof of an admission bug or a configuration edit.
            bool limitDropped = previous != null && (previous.Limit == 0 || limit < previous.Limit);
            string cause;
            if (previous == null) cause = "unknown";
            else if (count <= previous.Count && (previous.Cause == "limit_lowered" || previous.Cause == null && limitDropped)) cause = "limit_lowered";
            else if (limitDropped || previous.Cause == "unknown" && count <= previous.Count) cause = "unknown";
            else cause = "unexpected";
            Observation current = new Observation(count, limit, cause);
            if (current == previous) return;
            string usage = gate == "captain" ? "reserved mission slots" : gate == "vessel_voyages" ? "occupied voyage slots" : "running missions in the busiest voyage group";
            string detail = cause == "limit_lowered" ? "An observed limit reduction is draining; running work continues."
                : cause == "unknown" ? previous == null
                    ? "The overrun was first observed without a prior capacity baseline; its cause is unknown."
                    : "The order of capacity and limit changes between observations is uncertain; its cause is unknown."
                : "No observed limit reduction explains this overrun; inspect admission and slot ownership.";
            ArmadaEvent evt = new ArmadaEvent("concurrency.limit_exceeded", name + " has " + count + " " + usage + " / limit " + limit + ". " + detail)
            {
                EntityType = entityType, EntityId = id, TenantId = tenantId, UserId = userId,
                CaptainId = entityType == "captain" ? id : null, VesselId = entityType == "vessel" ? id : null,
                Payload = JsonSerializer.Serialize(new { Gate = gate, Count = count, Limit = limit, Cause = cause })
            };
            await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            ArmadaMetrics.ConcurrencyLimitViolations.Add(1, new KeyValuePair<string, object?>("gate", gate), new KeyValuePair<string, object?>("cause", cause));
            _Logging.Warn("[ConcurrencyInvariantMonitor] " + gate + " " + id + ": " + evt.Message);
            _Previous[key] = current;
        }
    }
}
