namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The per-pass cache of a dispatch pass and the capacity checks made under the capacity lock.
    /// </summary>
    internal sealed class MissionDispatchPass
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly AsyncLocal<DispatchPassCache?> _Pass;
        private readonly ArmadaSettings _Settings;

        // Bumped before every claim write, under _CapacityLock. A pass that sees a newer value under the lock drops its
        // claim-sensitive reads, so a claim made outside the pass (voyage creation, verification repair) is counted.
        private long _ClaimGeneration;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionDispatchPass(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Pass = context.Pass;
            _Settings = context.Settings;
        }

        #endregion

        #region Internal-Methods

        /// <inheritdoc />
        internal IDisposable BeginDispatchPass()
        {
            DispatchPassCache? outer = _Pass.Value;
            _Pass.Value = new DispatchPassCache { Generation = Interlocked.Read(ref _ClaimGeneration) };
            return new DispatchPassScope(_Service, outer);
        }

        internal void PassStateChanged() => _Pass.Value?.AfterStateChange();

        internal void NoteClaimAttempt() => Interlocked.Increment(ref _ClaimGeneration);

        // Call with _CapacityLock held, so no claim can land between the comparison and the reads that follow.
        internal void PassRevalidateUnderLock()
        {
            DispatchPassCache? pass = _Pass.Value;
            if (pass == null) return;
            long current = Interlocked.Read(ref _ClaimGeneration);
            if (pass.Generation == current) return;
            pass.AfterStateChange();
            pass.Generation = current;
        }

        internal async Task<CapacityModel> PassCapacityAsync(CancellationToken token)
        {
            DispatchPassCache? pass = _Pass.Value;
            if (pass?.Capacity != null) return pass.Capacity;
            List<Captain> captains = await PassCaptainsAsync(token).ConfigureAwait(false);
            int slots = await PassExecutionSlotsAsync(token).ConfigureAwait(false);
            Dictionary<string, ModelEndpoint?> endpoints = new Dictionary<string, ModelEndpoint?>(StringComparer.Ordinal);
            foreach (string endpointId in CapacityModel.EndpointIds(captains))
                endpoints[endpointId] = await PassEndpointAsync(endpointId, token).ConfigureAwait(false);
            // RS-8.9: a loopback endpoint is a different server on every Harbor, so its usage is counted per dock host.
            Dictionary<string, List<string?>>? hosts = null;
            if (ConcurrencyCapacityPolicy.NeedsDockHosts(captains, c => !String.IsNullOrEmpty(c.ModelEndpointId) && endpoints.TryGetValue(c.ModelEndpointId!, out ModelEndpoint? e) ? e : null))
                hosts = ConcurrencyCapacityPolicy.HostsByCaptain(await _Database.Docks.EnumerateAsync(token).ConfigureAwait(false));
            CapacityModel model = new CapacityModel(captains, slots, endpoints, _Settings, hosts);
            if (pass != null) pass.Capacity = model;
            return model;
        }

        // RS-4.9: the shared caps the claim write does not enforce itself (global, runtime, endpoint), re-read for the
        // chosen captain. Call with _CapacityLock held, after PassRevalidateUnderLock.
        internal async Task<MissionAssignmentBlocker?> CapacityBlockerUnderLockAsync(Captain captain, CancellationToken token, string? harborId = null)
        {
            CapacityModel model = await PassCapacityAsync(token).ConfigureAwait(false);
            MissionAssignmentBlocker? global = model.GlobalBlocker();
            if (global != null) return global;
            if (!model.KnowsEndpointOf(captain))
                model.AddEndpoint(captain.ModelEndpointId!, await PassEndpointAsync(captain.ModelEndpointId!, token).ConfigureAwait(false));
            return model.RuntimeBlocker(captain) ?? model.EndpointBlocker(captain, harborId);
        }

        /// <inheritdoc />
        internal async Task<DispatchCapacity> ReadDispatchCapacityAsync(CancellationToken token = default)
        {
            CapacityModel model = await PassCapacityAsync(token).ConfigureAwait(false);
            return new DispatchCapacity { GlobalLimitReached = model.GlobalBlocker() != null, AnyCaptainHasRoom = model.AnyCaptainHasRoom() };
        }

        // RS-8.5: every captain read for routing carries its model profile's tier, so tier decisions see it.
        internal async Task<List<Captain>> PassCaptainsAsync(CancellationToken token)
        {
            DispatchPassCache? pass = _Pass.Value;
            if (pass == null) return await WithProfileTiersAsync(await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
            if (pass.Captains == null) { pass.Captains = await WithProfileTiersAsync(await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false), token).ConfigureAwait(false); pass.Reads++; }
            return new List<Captain>(pass.Captains);
        }

        internal async Task<Captain?> PassCaptainAsync(string captainId, CancellationToken token)
        {
            if (_Pass.Value == null)
            {
                Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
                if (captain != null) await WithProfileTiersAsync(new List<Captain> { captain }, token).ConfigureAwait(false);
                return captain;
            }
            return (await PassCaptainsAsync(token).ConfigureAwait(false)).FirstOrDefault(c => c.Id == captainId);
        }

        internal Task<List<Mission>> PassRunningOnVesselAsync(string vesselId, CancellationToken token)
            => Cached(_Pass.Value?.RunningByVessel, vesselId, () => _Database.Missions.EnumerateRunningOnVesselAsync(vesselId, token));

        internal Task<List<MissionAdmissionSnapshot>> PassAdmissionSnapshotsAsync(string vesselId, CancellationToken token)
            => Cached(_Pass.Value?.AdmissionByVessel, vesselId, () => _Database.Missions.EnumerateAdmissionSnapshotsAsync(vesselId, token));

        internal Task<Voyage?> PassVoyageAsync(string voyageId, CancellationToken token)
            => Cached(_Pass.Value?.Voyages, voyageId, () => _Database.Voyages.ReadAsync(voyageId, token));

        internal Task<Vessel?> PassVesselAsync(string vesselId, CancellationToken token)
            => Cached(_Pass.Value?.Vessels, vesselId, () => _Database.Vessels.ReadAsync(vesselId, token));

        internal Task<ModelEndpoint?> PassEndpointAsync(string endpointId, CancellationToken token)
            => Cached(_Pass.Value?.Endpoints, endpointId, () => _Database.ModelEndpoints.ReadAsync(endpointId, token));

        internal Task<ModelProfile?> PassProfileAsync(string profileId, CancellationToken token)
            => Cached(_Pass.Value?.Profiles, profileId, () => _Database.ModelProfiles.ReadAsync(profileId, token));

        internal Task<Persona?> PassPersonaAsync(string? tenantId, string personaName, string? userId, CancellationToken token)
            => Cached(_Pass.Value?.Personas, (tenantId ?? "") + "\n" + (userId ?? "") + "\n" + personaName,
                () => _Context.MissionCaptainRouter.ReadPersonaByNameAsync(tenantId, personaName, token, userId));

        #endregion

        #region Private-Methods

        private async Task<List<Captain>> WithProfileTiersAsync(List<Captain> captains, CancellationToken token)
        {
            await CaptainProfileTiers.ApplyAsync(captains, id => PassProfileAsync(id, token)).ConfigureAwait(false);
            return captains;
        }

        private async Task<int> PassExecutionSlotsAsync(CancellationToken token)
        {
            DispatchPassCache? pass = _Pass.Value;
            if (pass == null) return await _Database.Missions.CountExecutionSlotsAsync(token).ConfigureAwait(false);
            if (!pass.ExecutionSlots.HasValue) { pass.ExecutionSlots = await _Database.Missions.CountExecutionSlotsAsync(token).ConfigureAwait(false); pass.Reads++; }
            return pass.ExecutionSlots.Value;
        }

        private async Task<T> Cached<T>(Dictionary<string, T>? cache, string key, Func<Task<T>> read)
        {
            if (cache == null) return await read().ConfigureAwait(false);
            if (cache.TryGetValue(key, out T? value)) return value;
            value = await read().ConfigureAwait(false);
            cache[key] = value;
            _Pass.Value!.Reads++;
            return value;
        }

        #endregion
    }
}
