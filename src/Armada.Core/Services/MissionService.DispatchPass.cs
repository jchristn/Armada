namespace Armada.Core.Services
{
    using Armada.Core.Models;

    // RS-4.7: inside a dispatch pass every admission read goes through one DispatchPassCache, so N pending missions
    // cost one read of each shared table instead of N. Outside a pass (the API, the TUI, tests) these read directly.
    public partial class MissionService
    {
        private readonly AsyncLocal<DispatchPassCache?> _Pass = new AsyncLocal<DispatchPassCache?>();

        // Bumped before every claim write, under _CapacityLock. A pass that sees a newer value under the lock drops its
        // claim-sensitive reads, so a claim made outside the pass (voyage creation, verification repair) is counted.
        private long _ClaimGeneration;

        /// <inheritdoc />
        public IDisposable BeginDispatchPass()
        {
            DispatchPassCache? outer = _Pass.Value;
            _Pass.Value = new DispatchPassCache { Generation = Interlocked.Read(ref _ClaimGeneration) };
            return new PassScope(this, outer);
        }

        /// <summary>Database reads the current dispatch pass has made, or -1 outside a pass.</summary>
        internal int DispatchPassReads => _Pass.Value?.Reads ?? -1;

        private void PassStateChanged() => _Pass.Value?.AfterStateChange();

        private void NoteClaimAttempt() => Interlocked.Increment(ref _ClaimGeneration);

        // Call with _CapacityLock held, so no claim can land between the comparison and the reads that follow.
        private void PassRevalidateUnderLock()
        {
            DispatchPassCache? pass = _Pass.Value;
            if (pass == null) return;
            long current = Interlocked.Read(ref _ClaimGeneration);
            if (pass.Generation == current) return;
            pass.AfterStateChange();
            pass.Generation = current;
        }

        private async Task<CapacityModel> PassCapacityAsync(CancellationToken token)
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
        private async Task<MissionAssignmentBlocker?> CapacityBlockerUnderLockAsync(Captain captain, CancellationToken token, string? harborId = null)
        {
            CapacityModel model = await PassCapacityAsync(token).ConfigureAwait(false);
            MissionAssignmentBlocker? global = model.GlobalBlocker();
            if (global != null) return global;
            if (!model.KnowsEndpointOf(captain))
                model.AddEndpoint(captain.ModelEndpointId!, await PassEndpointAsync(captain.ModelEndpointId!, token).ConfigureAwait(false));
            return model.RuntimeBlocker(captain) ?? model.EndpointBlocker(captain, harborId);
        }

        /// <inheritdoc />
        public async Task<DispatchCapacity> ReadDispatchCapacityAsync(CancellationToken token = default)
        {
            CapacityModel model = await PassCapacityAsync(token).ConfigureAwait(false);
            return new DispatchCapacity { GlobalLimitReached = model.GlobalBlocker() != null, AnyCaptainHasRoom = model.AnyCaptainHasRoom() };
        }

        // RS-8.5: every captain read for routing carries its model profile's tier, so tier decisions see it.
        private async Task<List<Captain>> PassCaptainsAsync(CancellationToken token)
        {
            DispatchPassCache? pass = _Pass.Value;
            if (pass == null) return await WithProfileTiersAsync(await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
            if (pass.Captains == null) { pass.Captains = await WithProfileTiersAsync(await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false), token).ConfigureAwait(false); pass.Reads++; }
            return new List<Captain>(pass.Captains);
        }

        private async Task<Captain?> PassCaptainAsync(string captainId, CancellationToken token)
        {
            if (_Pass.Value == null)
            {
                Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
                if (captain != null) await WithProfileTiersAsync(new List<Captain> { captain }, token).ConfigureAwait(false);
                return captain;
            }
            return (await PassCaptainsAsync(token).ConfigureAwait(false)).FirstOrDefault(c => c.Id == captainId);
        }

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

        private Task<List<Mission>> PassRunningOnVesselAsync(string vesselId, CancellationToken token)
            => Cached(_Pass.Value?.RunningByVessel, vesselId, () => _Database.Missions.EnumerateRunningOnVesselAsync(vesselId, token));

        private Task<List<MissionAdmissionSnapshot>> PassAdmissionSnapshotsAsync(string vesselId, CancellationToken token)
            => Cached(_Pass.Value?.AdmissionByVessel, vesselId, () => _Database.Missions.EnumerateAdmissionSnapshotsAsync(vesselId, token));

        private Task<Voyage?> PassVoyageAsync(string voyageId, CancellationToken token)
            => Cached(_Pass.Value?.Voyages, voyageId, () => _Database.Voyages.ReadAsync(voyageId, token));

        private Task<Vessel?> PassVesselAsync(string vesselId, CancellationToken token)
            => Cached(_Pass.Value?.Vessels, vesselId, () => _Database.Vessels.ReadAsync(vesselId, token));

        private Task<ModelEndpoint?> PassEndpointAsync(string endpointId, CancellationToken token)
            => Cached(_Pass.Value?.Endpoints, endpointId, () => _Database.ModelEndpoints.ReadAsync(endpointId, token));

        private Task<ModelProfile?> PassProfileAsync(string profileId, CancellationToken token)
            => Cached(_Pass.Value?.Profiles, profileId, () => _Database.ModelProfiles.ReadAsync(profileId, token));

        private Task<Persona?> PassPersonaAsync(string? tenantId, string personaName, string? userId, CancellationToken token)
            => Cached(_Pass.Value?.Personas, (tenantId ?? "") + "\n" + (userId ?? "") + "\n" + personaName,
                () => ReadPersonaByNameAsync(tenantId, personaName, token, userId));

        private async Task<T> Cached<T>(Dictionary<string, T>? cache, string key, Func<Task<T>> read)
        {
            if (cache == null) return await read().ConfigureAwait(false);
            if (cache.TryGetValue(key, out T? value)) return value;
            value = await read().ConfigureAwait(false);
            cache[key] = value;
            _Pass.Value!.Reads++;
            return value;
        }

        private sealed class PassScope : IDisposable
        {
            private readonly MissionService _Service;
            private readonly DispatchPassCache? _Outer;
            private bool _Disposed;
            internal PassScope(MissionService service, DispatchPassCache? outer) { _Service = service; _Outer = outer; }
            public void Dispose()
            {
                if (_Disposed) return;
                _Disposed = true;
                _Service._Pass.Value = _Outer;
            }
        }
    }
}
