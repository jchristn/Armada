namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal class DatabaseFixture
    {
        private readonly DatabaseDriver _Driver;
        private readonly bool _NoCleanup;
        private readonly Stack<Func<CancellationToken, Task>> _Cleanup = new Stack<Func<CancellationToken, Task>>();

        public DatabaseFixture(DatabaseDriver driver, bool noCleanup)
        {
            _Driver = driver;
            _NoCleanup = noCleanup;
        }

        public async Task<TenantMetadata> CreateTenantAsync(string namePrefix, bool isProtected = false, CancellationToken token = default)
        {
            TenantMetadata tenant = new TenantMetadata(namePrefix + "-" + Token())
            {
                Active = true,
                IsProtected = isProtected
            };

            await _Driver.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Tenants.DeleteAsync(tenant.Id, ct).ConfigureAwait(false));
            return tenant;
        }

        public async Task<UserMaster> CreateUserAsync(string tenantId, string emailPrefix, bool isAdmin = false, bool isTenantAdmin = false, bool isProtected = false, CancellationToken token = default)
        {
            UserMaster user = new UserMaster(tenantId, emailPrefix + "-" + Token() + "@example.com", "password")
            {
                FirstName = "Test",
                LastName = "User",
                IsAdmin = isAdmin,
                IsTenantAdmin = isTenantAdmin,
                IsProtected = isProtected,
                Active = true
            };

            await _Driver.Users.CreateAsync(user, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Users.DeleteAsync(tenantId, user.Id, ct).ConfigureAwait(false));
            return user;
        }

        public async Task<Credential> CreateCredentialAsync(string tenantId, string userId, string namePrefix, bool active = true, bool isProtected = false, CancellationToken token = default)
        {
            Credential credential = new Credential(tenantId, userId)
            {
                Name = namePrefix + "-" + Token(),
                Active = active,
                IsProtected = isProtected
            };

            await _Driver.Credentials.CreateAsync(credential, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Credentials.DeleteAsync(tenantId, credential.Id, ct).ConfigureAwait(false));
            return credential;
        }

        public async Task<Fleet> CreateFleetAsync(string tenantId, string userId, string namePrefix, CancellationToken token = default)
        {
            Fleet fleet = new Fleet(namePrefix + "-" + Token())
            {
                TenantId = tenantId,
                UserId = userId,
                Description = "Fleet description for " + namePrefix,
                Active = true
            };

            await _Driver.Fleets.CreateAsync(fleet, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Fleets.DeleteAsync(fleet.Id, ct).ConfigureAwait(false));
            return fleet;
        }

        public async Task<Vessel> CreateVesselAsync(string tenantId, string userId, string fleetId, string namePrefix, CancellationToken token = default)
        {
            Vessel vessel = new Vessel(namePrefix + "-" + Token(), "https://github.com/example/" + namePrefix + ".git")
            {
                TenantId = tenantId,
                UserId = userId,
                FleetId = fleetId,
                DefaultBranch = "main",
                Active = true
            };

            await _Driver.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Vessels.DeleteAsync(vessel.Id, ct).ConfigureAwait(false));
            return vessel;
        }

        public async Task<Captain> CreateCaptainAsync(string tenantId, string userId, string namePrefix, CancellationToken token = default)
        {
            Captain captain = new Captain(namePrefix + "-" + Token(), AgentRuntimeEnum.Codex)
            {
                TenantId = tenantId,
                UserId = userId,
                State = CaptainStateEnum.Idle
            };

            await _Driver.Captains.CreateAsync(captain, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Captains.DeleteAsync(captain.Id, ct).ConfigureAwait(false));
            return captain;
        }

        public async Task<Voyage> CreateVoyageAsync(string tenantId, string userId, string titlePrefix, CancellationToken token = default)
        {
            Voyage voyage = new Voyage(titlePrefix + "-" + Token(), "Voyage description")
            {
                TenantId = tenantId,
                UserId = userId,
                Status = VoyageStatusEnum.Open
            };

            await _Driver.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Voyages.DeleteAsync(voyage.Id, ct).ConfigureAwait(false));
            return voyage;
        }

        public async Task<Mission> CreateMissionAsync(string tenantId, string userId, string voyageId, string vesselId, string captainId, string titlePrefix, CancellationToken token = default)
        {
            Mission mission = new Mission(titlePrefix + "-" + Token(), "Mission description")
            {
                TenantId = tenantId,
                UserId = userId,
                VoyageId = voyageId,
                VesselId = vesselId,
                CaptainId = captainId,
                Status = MissionStatusEnum.Pending,
                Priority = 10,
                BranchName = "feature/" + Token()
            };

            await _Driver.Missions.CreateAsync(mission, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Missions.DeleteAsync(mission.Id, ct).ConfigureAwait(false));
            return mission;
        }

        public async Task<Dock> CreateDockAsync(string tenantId, string userId, string vesselId, string captainId, CancellationToken token = default)
        {
            Dock dock = new Dock(vesselId)
            {
                TenantId = tenantId,
                UserId = userId,
                CaptainId = captainId,
                BranchName = "main",
                WorktreePath = "/tmp/" + Token(),
                Active = true
            };

            await _Driver.Docks.CreateAsync(dock, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Docks.DeleteAsync(dock.Id, ct).ConfigureAwait(false));
            return dock;
        }

        public async Task<Signal> CreateSignalAsync(string tenantId, string userId, string toCaptainId, CancellationToken token = default)
        {
            Signal signal = new Signal(SignalTypeEnum.Nudge, "{\"message\":\"hello\"}")
            {
                TenantId = tenantId,
                UserId = userId,
                ToCaptainId = toCaptainId,
                Read = false
            };

            await _Driver.Signals.CreateAsync(signal, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Signals.DeleteAsync(signal.Id, ct).ConfigureAwait(false));
            return signal;
        }

        public async Task<ArmadaEvent> CreateEventAsync(string tenantId, string userId, string missionId, string voyageId, string vesselId, string captainId, CancellationToken token = default)
        {
            ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created for integration test")
            {
                TenantId = tenantId,
                UserId = userId,
                MissionId = missionId,
                VoyageId = voyageId,
                VesselId = vesselId,
                CaptainId = captainId,
                EntityType = "mission",
                EntityId = missionId,
                Payload = "{\"kind\":\"test\"}"
            };

            await _Driver.Events.CreateAsync(evt, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.Events.DeleteAsync(evt.Id, ct).ConfigureAwait(false));
            return evt;
        }

        public async Task<MergeEntry> CreateMergeEntryAsync(string tenantId, string userId, string missionId, string vesselId, CancellationToken token = default)
        {
            MergeEntry entry = new MergeEntry("feature/" + Token())
            {
                TenantId = tenantId,
                UserId = userId,
                MissionId = missionId,
                VesselId = vesselId,
                Status = MergeStatusEnum.Queued,
                Priority = 5,
                TestCommand = "dotnet test"
            };

            await _Driver.MergeEntries.CreateAsync(entry, token).ConfigureAwait(false);
            RegisterCleanup(async ct => await _Driver.MergeEntries.DeleteAsync(entry.Id, ct).ConfigureAwait(false));
            return entry;
        }

        public async Task CleanupAsync(CancellationToken token = default)
        {
            if (_NoCleanup) return;

            while (_Cleanup.Count > 0)
            {
                Func<CancellationToken, Task> cleanup = _Cleanup.Pop();
                try
                {
                    await cleanup(token).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private void RegisterCleanup(Func<CancellationToken, Task> cleanup)
        {
            if (!_NoCleanup) _Cleanup.Push(cleanup);
        }

        private static string Token()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }
}
