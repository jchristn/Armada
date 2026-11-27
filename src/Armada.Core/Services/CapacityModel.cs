namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// The one capacity model (RS-4.8): the global mission limit, per-runtime caps and per-endpoint caps, all counted
    /// from one captain snapshot and one execution-slot count. Mission admission, the verification repair path and the
    /// Admiral's dispatch check read it, so they can no longer disagree about whether there is room. Endpoint
    /// availability (disabled, unhealthy, open circuit) is not capacity and stays with the caller.
    /// </summary>
    internal sealed class CapacityModel
    {
        private readonly List<Captain> _Captains;
        private readonly int _ExecutionSlots;
        private readonly int _GlobalLimit;
        private readonly int _CaptainCeiling;
        private readonly Dictionary<string, ModelEndpoint?> _Endpoints;
        private readonly Dictionary<AgentRuntimeEnum, int> _RuntimeLimits;
        private readonly Dictionary<AgentRuntimeEnum, int> _RuntimeBusy = new Dictionary<AgentRuntimeEnum, int>();
        private readonly Dictionary<string, int> _EndpointBusy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Build the model. Endpoint usage is counted per concurrency key (RS-8.9); for a loopback endpoint that key names
        /// the host, taken from <paramref name="hostsByCaptain"/> (the Harbors of each captain's leased docks), so the same
        /// localhost URL on two Harbors is two budgets. Without it every slot counts on the Admiral's host.
        /// </summary>
        internal CapacityModel(List<Captain> captains, int executionSlots, Dictionary<string, ModelEndpoint?> endpointsById, ArmadaSettings settings,
            Dictionary<string, List<string?>>? hostsByCaptain = null)
        {
            _Captains = captains ?? throw new ArgumentNullException(nameof(captains));
            _Endpoints = endpointsById ?? throw new ArgumentNullException(nameof(endpointsById));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _ExecutionSlots = executionSlots;
            _GlobalLimit = settings.MaxConcurrentMissions;
            _CaptainCeiling = settings.Concurrency.MaxCaptainConcurrency;
            _RuntimeLimits = ConcurrencyCapacityPolicy.RuntimeLimits(settings.Agents);
            foreach (Captain captain in captains)
            {
                int occupancy = ConcurrencyCapacityPolicy.Occupancy(captain);
                if (occupancy == 0) continue;
                _RuntimeBusy[captain.Runtime] = RuntimeBusy(captain.Runtime) + occupancy;
                ModelEndpoint? endpoint = EndpointOf(captain);
                if (endpoint == null) continue;
                foreach (string? host in ConcurrencyCapacityPolicy.SlotHosts(captain, hostsByCaptain))
                {
                    string key = ConcurrencyCapacityPolicy.EndpointKey(endpoint, host);
                    _EndpointBusy[key] = EndpointBusy(key) + 1;
                }
            }
        }

        /// <summary>Endpoint ids the model needs resolved for these captains.</summary>
        internal static List<string> EndpointIds(IEnumerable<Captain> captains)
            => captains.Where(c => !String.IsNullOrEmpty(c.ModelEndpointId)).Select(c => c.ModelEndpointId!).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>Whether the captain's endpoint, if any, is resolved in this model.</summary>
        internal bool KnowsEndpointOf(Captain captain) => String.IsNullOrEmpty(captain.ModelEndpointId) || _Endpoints.ContainsKey(captain.ModelEndpointId!);

        /// <summary>Resolve an endpoint for a captain created after the snapshot; it is idle, so no counts change.</summary>
        internal void AddEndpoint(string endpointId, ModelEndpoint? endpoint) => _Endpoints[endpointId] = endpoint;

        internal ModelEndpoint? EndpointOf(Captain captain)
            => !String.IsNullOrEmpty(captain.ModelEndpointId) && _Endpoints.TryGetValue(captain.ModelEndpointId!, out ModelEndpoint? endpoint) ? endpoint : null;

        /// <summary>The deployment-wide limit, or null when another mission may run.</summary>
        internal MissionAssignmentBlocker? GlobalBlocker()
        {
            if (_GlobalLimit == 0 || _ExecutionSlots < _GlobalLimit) return null;
            // Do not expose other tenants' mission IDs through a deployment-wide limit.
            return MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit,
                "Armada", _ExecutionSlots, _GlobalLimit, AssignmentBlockerActionEnum.EditSettings, "settings", null, "MaxConcurrentMissions", Constants.GlobalMissionConcurrencyCeiling);
        }

        /// <summary>The captain's runtime cap, or null when the runtime has room.</summary>
        internal MissionAssignmentBlocker? RuntimeBlocker(Captain captain)
        {
            if (!_RuntimeLimits.TryGetValue(captain.Runtime, out int limit)) return null;
            int busy = RuntimeBusy(captain.Runtime);
            if (busy < limit) return null;
            MissionAssignmentBlocker blocker = MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.RuntimeMissionLimit,
                captain.Runtime + " runtime", busy, limit, AssignmentBlockerActionEnum.EditSettings, "settings", null, "Agents." + captain.Runtime + ".MaxConcurrent");
            blocker.Advice[0].Action = null;
            blocker.Advice[0].Text += " Update this runtime cap in settings.json, or wait for running work to finish.";
            return blocker;
        }

        /// <summary>
        /// The captain's endpoint cap, or null when it has no endpoint, no cap, or room. <paramref name="harborId"/> is the
        /// Harbor the mission will run on (null for the Admiral's host or not yet placed); it matters only for a loopback
        /// endpoint.
        /// </summary>
        internal MissionAssignmentBlocker? EndpointBlocker(Captain captain, string? harborId = null)
        {
            ModelEndpoint? endpoint = EndpointOf(captain);
            int? limit = endpoint?.InferenceOptions?.MaxConcurrentMissions;
            if (endpoint == null || !limit.HasValue) return null;
            int busy = EndpointBusy(ConcurrencyCapacityPolicy.EndpointKey(endpoint, harborId));
            if (busy < limit.Value) return null;
            return MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.EndpointMissionLimit,
                "Endpoint " + endpoint.Name, busy, limit.Value, AssignmentBlockerActionEnum.EditEndpoint, "endpoint", endpoint.Id, "InferenceOptions.MaxConcurrentMissions", 64);
        }

        /// <summary>Whether this captain could take one more mission under every cap.</summary>
        internal bool CaptainHasRoom(Captain captain)
            => captain.HasMissionCapacity
            && captain.ActiveMissionCount < ConcurrencyClamp.CaptainLimit(captain.MaxConcurrency, _CaptainCeiling)
            && RuntimeBlocker(captain) == null && EndpointBlocker(captain) == null;

        /// <summary>Whether any mission could be admitted at all: the global limit has room and some captain does.</summary>
        internal bool AnyCaptainHasRoom() => GlobalBlocker() == null && _Captains.Any(CaptainHasRoom);

        private int RuntimeBusy(AgentRuntimeEnum runtime) => _RuntimeBusy.TryGetValue(runtime, out int busy) ? busy : 0;

        private int EndpointBusy(string key) => _EndpointBusy.TryGetValue(key, out int busy) ? busy : 0;
    }
}
