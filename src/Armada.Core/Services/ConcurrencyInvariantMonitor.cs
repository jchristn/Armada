namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>Observes persisted captain and vessel capacity without preempting or claiming work.</summary>
    internal sealed class ConcurrencyInvariantMonitor
    {
        /// <summary>Called after a <c>concurrency.limit_exceeded</c> event is recorded.</summary>
        internal Action<ArmadaEvent>? EventRecorded { get; set; }

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
                List<Captain> captains = await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);
                ConcurrencyGaugeSnapshot gauges = new ConcurrencyGaugeSnapshot();
                foreach (Captain captain in captains)
                {
                    string runtimeName = captain.Runtime.ToString();
                    gauges.ActiveByRuntime[runtimeName] = (gauges.ActiveByRuntime.TryGetValue(runtimeName, out int active) ? active : 0) + captain.ActiveMissionCount;
                    gauges.SlotsInUseByRuntime[runtimeName] = (gauges.SlotsInUseByRuntime.TryGetValue(runtimeName, out int used) ? used : 0) + ConcurrencyCapacityPolicy.Occupancy(captain);
                    int slots = captain.Active ? ConcurrencyClamp.CaptainLimit(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency) : 0;
                    gauges.SlotsTotalByRuntime[runtimeName] = (gauges.SlotsTotalByRuntime.TryGetValue(runtimeName, out int total) ? total : 0) + slots;
                }
                foreach (Captain captain in captains)
                    await ObserveAsync("captain", captain.Id, captain.Name, captain.ActiveMissionCount,
                        ConcurrencyClamp.CaptainLimit(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency),
                        "captain", captain.TenantId, captain.UserId, seen, token).ConfigureAwait(false);
                await ObserveAsync("global", "global", "Armada", await _Database.Missions.CountExecutionSlotsAsync(token).ConfigureAwait(false),
                    _Settings.MaxConcurrentMissions, "settings", null, null, seen, token).ConfigureAwait(false);
                Dictionary<AgentRuntimeEnum, int> runtimeLimits = ConcurrencyCapacityPolicy.RuntimeLimits(_Settings.Agents);
                foreach (AgentRuntimeEnum runtime in Enum.GetValues<AgentRuntimeEnum>().Concat(runtimeLimits.Keys).Distinct())
                    await ObserveAsync("runtime", runtime.ToString(), runtime + " runtime",
                        captains.Where(c => c.Runtime == runtime).Sum(ConcurrencyCapacityPolicy.Occupancy),
                        runtimeLimits.TryGetValue(runtime, out int limit) ? limit : 0,
                        "settings", null, null, seen, token).ConfigureAwait(false);
                List<ModelEndpoint> endpoints = await _Database.ModelEndpoints.EnumerateAsync(token).ConfigureAwait(false);
                Dictionary<string, ModelEndpoint> byEndpoint = endpoints.ToDictionary(e => e.Id, StringComparer.Ordinal);
                Dictionary<string, int> endpointUsage = captains.Where(c => c.ModelEndpointId != null && byEndpoint.ContainsKey(c.ModelEndpointId))
                    .GroupBy(c => ConcurrencyCapacityPolicy.EndpointKey(byEndpoint[c.ModelEndpointId!]), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(ConcurrencyCapacityPolicy.Occupancy), StringComparer.OrdinalIgnoreCase);
                foreach (IGrouping<string, ModelEndpoint> group in endpoints.GroupBy(ConcurrencyCapacityPolicy.EndpointKey, StringComparer.OrdinalIgnoreCase))
                {
                    bool mixedLimits = group.Select(e => e.InferenceOptions?.MaxConcurrentMissions).Distinct().Count() > 1;
                    int usage = endpointUsage.TryGetValue(group.Key, out int count) ? count : 0;
                    foreach (ModelEndpoint endpoint in group)
                        await ObserveAsync("endpoint", endpoint.Id, "Endpoint " + endpoint.Name, usage,
                            endpoint.InferenceOptions?.MaxConcurrentMissions ?? 0, "model_endpoint", null, null, seen, token,
                            mixedLimits ? "Endpoint aliases share usage but have different limits; the observation cannot establish an admission violation." : null).ConfigureAwait(false);
                }
                foreach (Vessel vessel in await _Database.Vessels.EnumerateAsync(token).ConfigureAwait(false))
                {
                    List<MissionAdmissionSnapshot> missions = VoyageSlotPolicy.Runnable(
                        await _Database.Missions.EnumerateAdmissionSnapshotsAsync(vessel.Id, token).ConfigureAwait(false));
                    Dictionary<string, MissionAdmissionSnapshot> byId = missions.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    List<IGrouping<string, MissionAdmissionSnapshot>> groups = missions.GroupBy(m => VoyageSlotPolicy.Group(m, byId)).ToList();
                    int heldVoyageSlots = groups.Count(g => VoyageSlotPolicy.Admitted(g));
                    if (vessel.MaxParallelVoyages > 0) { gauges.VoyageSlotsInUse += heldVoyageSlots; gauges.VoyageSlotsTotal += vessel.MaxParallelVoyages; }
                    else gauges.UnlimitedVessels++;
                    await ObserveAsync("vessel_voyages", vessel.Id, vessel.Name, heldVoyageSlots,
                        vessel.MaxParallelVoyages, "vessel", vessel.TenantId, vessel.UserId, seen, token).ConfigureAwait(false);
                    // Report the busiest group once per vessel. Do not expose another user's voyage/mission IDs
                    // through events on a shared vessel, and keep event volume bounded by captains + vessels.
                    int busiest = groups.Select(g => g.Count(m => VoyageSlotPolicy.Running(m.Status))).DefaultIfEmpty(0).Max();
                    await ObserveAsync("voyage_missions", vessel.Id, vessel.Name, busiest,
                        vessel.MaxParallelMissionsPerVoyage, "vessel", vessel.TenantId, vessel.UserId, seen, token).ConfigureAwait(false);
                }
                foreach (string key in _Previous.Keys.Where(key => !seen.Contains(key)).ToList()) _Previous.Remove(key);
                ConcurrencyGaugeState.Publish(gauges);
            }
            finally { _Gate.Release(); }
        }

        private async Task ObserveAsync(string gate, string id, string name, int count, int limit, string entityType,
            string? tenantId, string? userId, HashSet<string> seen, CancellationToken token, string? ambiguity = null)
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
            if (ambiguity != null || previous == null) cause = "unknown";
            else if (count <= previous.Count && (previous.Cause == "limit_lowered" || previous.Cause == null && limitDropped)) cause = "limit_lowered";
            else if (limitDropped || previous.Cause == "unknown" && count <= previous.Count) cause = "unknown";
            else cause = "unexpected";
            Observation current = new Observation(count, limit, cause);
            if (current == previous) return;
            string usage = gate == "captain" ? "reserved mission slots" : gate == "vessel_voyages" ? "occupied voyage slots"
                : gate == "voyage_missions" ? "running missions in the busiest voyage group" : "occupied execution slots";
            string detail = cause == "limit_lowered" ? "An observed limit reduction is draining; running work continues."
                : cause == "unknown" ? previous == null
                    ? "The overrun was first observed without a prior capacity baseline; its cause is unknown."
                    : "The order of capacity and limit changes between observations is uncertain; its cause is unknown."
                : "No observed limit reduction explains this overrun; inspect admission and slot ownership.";
            if (ambiguity != null) detail = ambiguity;
            ArmadaEvent evt = new ArmadaEvent("concurrency.limit_exceeded", name + " has " + count + " " + usage + " / limit " + limit + ". " + detail)
            {
                EntityType = entityType, EntityId = entityType == "settings" ? null : id, TenantId = tenantId, UserId = userId,
                CaptainId = entityType == "captain" ? id : null, VesselId = entityType == "vessel" ? id : null,
                Payload = JsonSerializer.Serialize(new { Gate = gate, Count = count, Limit = limit, Cause = cause })
            };
            await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            if (EventRecorded != null)
            {
                try { EventRecorded(evt); }
                catch (Exception ex) { _Logging.Debug("[ConcurrencyInvariantMonitor] event notification failed: " + ex.Message); }
            }
            ArmadaMetrics.ConcurrencyLimitViolations.Add(1, new KeyValuePair<string, object?>("gate", gate), new KeyValuePair<string, object?>("cause", cause));
            _Logging.Warn("[ConcurrencyInvariantMonitor] " + gate + " " + id + ": " + evt.Message);
            _Previous[key] = current;
        }
    }
}
