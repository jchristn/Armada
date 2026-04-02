#nullable enable

namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Runs integration tests against a live database driver.
    /// </summary>
    public class DatabaseTestRunner
    {
        private readonly DatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly bool _NoCleanup;
        private List<TestResult> _Results = new List<TestResult>();

        public DatabaseTestRunner(DatabaseDriver driver, DatabaseSettings settings, bool noCleanup = false)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _NoCleanup = noCleanup;
        }

        public async Task<List<TestResult>> RunAllAsync(CancellationToken token = default)
        {
            _Results = new List<TestResult>();

            Console.WriteLine("--- Schema Verification ---");
            await RunTest("Schema_Verify_Core_Columns_And_Indexes", "Schema", () => new SchemaVerificationTests(_Settings).VerifyAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Tenant/User/Credential ---");
            await RunTest("Tenant_Create_Read_Update_Enumerate", "Auth", () => TestTenantCrudAsync(token), token);
            await RunTest("Tenant_ReadByName_Exists", "Auth", () => TestTenantLookupAsync(token), token);
            await RunTest("User_Create_Read_Update_Enumerate", "Auth", () => TestUserCrudAsync(token), token);
            await RunTest("User_ReadByEmail_Exists", "Auth", () => TestUserLookupAsync(token), token);
            await RunTest("Credential_Create_Read_Update_Enumerate", "Auth", () => TestCredentialCrudAsync(token), token);
            await RunTest("Credential_ReadByBearerToken_EnumerateByUser", "Auth", () => TestCredentialLookupAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Operational Round-Trip ---");
            await RunTest("Fleet_Create_Read_Update_Enumerate", "Operational", () => TestFleetCrudAsync(token), token);
            await RunTest("Fleet_ReadByName_Exists_UnscopedEnumerate", "Operational", () => TestFleetLookupAsync(token), token);
            await RunTest("Vessel_Create_Read_Update_Enumerate", "Operational", () => TestVesselCrudAsync(token), token);
            await RunTest("Captain_Create_Read_Update", "Operational", () => TestCaptainCrudAsync(token), token);
            await RunTest("Voyage_Create_Read_Update", "Operational", () => TestVoyageCrudAsync(token), token);
            await RunTest("Mission_Create_Read_Update", "Operational", () => TestMissionCrudAsync(token), token);
            await RunTest("Dock_Create_Read_Update", "Operational", () => TestDockCrudAsync(token), token);
            await RunTest("Signal_Create_Read_Enumerate_MarkRead", "Operational", () => TestSignalCrudAsync(token), token);
            await RunTest("Signal_EnumerateRecent_Recipient_Unread", "Operational", () => TestSignalLookupAsync(token), token);
            await RunTest("Event_Create_Read_Enumerate", "Operational", () => TestEventCrudAsync(token), token);
            await RunTest("Event_FilteredEnumerations", "Operational", () => TestEventLookupAsync(token), token);
            await RunTest("MergeEntry_Create_Read_Update_Enumerate", "Operational", () => TestMergeEntryCrudAsync(token), token);
            await RunTest("MergeEntry_EnumerateByStatus_Exists", "Operational", () => TestMergeEntryLookupAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Cascade Verification ---");
            await RunTest("Tenant_Delete_Cascades_Auth_Data", "Cascade", () => TestTenantAuthCascadeDeleteAsync(token), token);
            await RunTest("Tenant_Delete_With_Operational_Subordinates_Is_FK_Fenced", "Cascade", () => TestTenantDeleteFencedByOperationalDataAsync(token), token);

            MultiTenantScopingTests scopingTests = new MultiTenantScopingTests(_Driver, _NoCleanup);
            List<TestResult> scopingResults = await scopingTests.RunAllAsync(token).ConfigureAwait(false);
            _Results.AddRange(scopingResults);

            return _Results;
        }

        private async Task RunTest(string name, string category, Func<Task> action, CancellationToken token)
        {
            TestResult result = new TestResult(name, category);
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                token.ThrowIfCancellationRequested();
                await action().ConfigureAwait(false);
                sw.Stop();
                result.MarkPassed(sw.Elapsed);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + sw.ElapsedMilliseconds + "ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.MarkFailed(sw.Elapsed, ex.Message, ex);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + sw.ElapsedMilliseconds + "ms) - " + ex.Message);
            }

            _Results.Add(result);
        }

        private async Task TestTenantCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenantA = await fixture.CreateTenantAsync("crud-tenant-a", token: token).ConfigureAwait(false);
                TenantMetadata tenantB = await fixture.CreateTenantAsync("crud-tenant-b", token: token).ConfigureAwait(false);

                TenantMetadata? read = await _Driver.Tenants.ReadAsync(tenantA.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Tenant read returned null");
                DatabaseAssert.HasPrefix(read.Id, "ten_", "Tenant.Id");
                DatabaseAssert.Equal(tenantA.Name, read.Name, "Tenant.Name");
                DatabaseAssert.Equal(tenantA.IsProtected, read.IsProtected, "Tenant.IsProtected");

                read.Name = tenantA.Name + " Updated";
                read.Active = false;
                TenantMetadata updated = await _Driver.Tenants.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal(read.Name, updated.Name, "Updated Tenant.Name");
                DatabaseAssert.Equal(false, updated.Active, "Updated Tenant.Active");

                EnumerationResult<TenantMetadata> page1 = await _Driver.Tenants.EnumerateAsync(new EnumerationQuery { PageNumber = 1, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.True(page1.TotalRecords >= 2, "Tenant enumeration should include at least the two created tenants");
                DatabaseAssert.EnumerationPage(page1, 1, 1, page1.TotalRecords, (int)Math.Ceiling((double)page1.TotalRecords), 1);

                EnumerationResult<TenantMetadata> page2 = await _Driver.Tenants.EnumerateAsync(new EnumerationQuery { PageNumber = 2, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.Equal(2, page2.PageNumber, "Tenant page 2 number");
                DatabaseAssert.Equal(1, page2.Objects.Count, "Tenant page 2 object count");
                DatabaseAssert.ContainsIds(new[] { page1.Objects[0], page2.Objects[0] }, x => x.Id, tenantA.Id, tenantB.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestTenantLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenant = await fixture.CreateTenantAsync("lookup-tenant", token: token).ConfigureAwait(false);
                DatabaseAssert.True(await _Driver.Tenants.ExistsAnyAsync(token).ConfigureAwait(false), "ExistsAnyAsync should return true after tenant creation");
                DatabaseAssert.True(await _Driver.Tenants.ExistsAsync(tenant.Id, token).ConfigureAwait(false), "Tenant ExistsAsync should return true");

                TenantMetadata? byName = await _Driver.Tenants.ReadByNameAsync(tenant.Name, token).ConfigureAwait(false);
                byName = DatabaseAssert.NotNull(byName, "Tenant ReadByNameAsync returned null");
                DatabaseAssert.Equal(tenant.Id, byName.Id, "Tenant.ReadByName.Id");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestUserCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenant = await fixture.CreateTenantAsync("user-tenant", token: token).ConfigureAwait(false);
                UserMaster userA = await fixture.CreateUserAsync(tenant.Id, "usera", isTenantAdmin: true, token: token).ConfigureAwait(false);
                UserMaster userB = await fixture.CreateUserAsync(tenant.Id, "userb", token: token).ConfigureAwait(false);

                UserMaster? read = await _Driver.Users.ReadByIdAsync(userA.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "User read returned null");
                DatabaseAssert.HasPrefix(read.Id, "usr_", "User.Id");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "User.TenantId");
                DatabaseAssert.Equal(userA.Email, read.Email, "User.Email");
                DatabaseAssert.Equal(true, read.IsTenantAdmin, "User.IsTenantAdmin");

                read.FirstName = "Updated";
                read.LastName = "Person";
                read.Active = false;
                UserMaster updated = await _Driver.Users.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal("Updated", updated.FirstName, "Updated User.FirstName");
                DatabaseAssert.Equal("Person", updated.LastName, "Updated User.LastName");
                DatabaseAssert.Equal(false, updated.Active, "Updated User.Active");

                EnumerationResult<UserMaster> page1 = await _Driver.Users.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 1, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.EnumerationPage(page1, 1, 1, 2, 2, 1);
                EnumerationResult<UserMaster> page2 = await _Driver.Users.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 2, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.EnumerationPage(page2, 2, 1, 2, 2, 1);
                DatabaseAssert.ContainsIds(new[] { page1.Objects[0], page2.Objects[0] }, x => x.Id, userA.Id, userB.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestUserLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenantA = await fixture.CreateTenantAsync("user-lookup-a", token: token).ConfigureAwait(false);
                TenantMetadata tenantB = await fixture.CreateTenantAsync("user-lookup-b", token: token).ConfigureAwait(false);
                string sharedEmail = "shared-user-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com";
                UserMaster userA = new UserMaster(tenantA.Id, sharedEmail, "password");
                userA.FirstName = "Shared";
                userA.LastName = "A";
                await _Driver.Users.CreateAsync(userA, token).ConfigureAwait(false);

                UserMaster userB = new UserMaster(tenantB.Id, sharedEmail, "password");
                userB.FirstName = "Shared";
                userB.LastName = "B";
                await _Driver.Users.CreateAsync(userB, token).ConfigureAwait(false);

                UserMaster? scoped = await _Driver.Users.ReadByEmailAsync(tenantA.Id, sharedEmail, token).ConfigureAwait(false);
                scoped = DatabaseAssert.NotNull(scoped, "User ReadByEmailAsync returned null");
                DatabaseAssert.Equal(userA.Id, scoped.Id, "User.ReadByEmail.Id");
                DatabaseAssert.True(await _Driver.Users.ExistsAsync(tenantA.Id, userA.Id, token).ConfigureAwait(false), "User ExistsAsync should return true");

                List<UserMaster> anyTenant = await _Driver.Users.ReadByEmailAnyTenantAsync(sharedEmail, token).ConfigureAwait(false);
                DatabaseAssert.Equal(2, anyTenant.Count, "ReadByEmailAnyTenant count");
                DatabaseAssert.ContainsIds(anyTenant, x => x.Id, userA.Id, userB.Id);

                if (!_NoCleanup)
                {
                    await _Driver.Users.DeleteAsync(tenantA.Id, userA.Id, token).ConfigureAwait(false);
                    await _Driver.Users.DeleteAsync(tenantB.Id, userB.Id, token).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestCredentialCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenant = await fixture.CreateTenantAsync("credential-tenant", token: token).ConfigureAwait(false);
                UserMaster user = await fixture.CreateUserAsync(tenant.Id, "credential-user", token: token).ConfigureAwait(false);
                Credential credentialA = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "credential-a", token: token).ConfigureAwait(false);
                Credential credentialB = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "credential-b", token: token).ConfigureAwait(false);

                Credential? read = await _Driver.Credentials.ReadByIdAsync(credentialA.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Credential read returned null");
                DatabaseAssert.HasPrefix(read.Id, "crd_", "Credential.Id");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "Credential.TenantId");
                DatabaseAssert.Equal(user.Id, read.UserId, "Credential.UserId");
                DatabaseAssert.Equal(credentialA.Name, read.Name, "Credential.Name");
                DatabaseAssert.True(!String.IsNullOrEmpty(read.BearerToken), "Credential.BearerToken missing");

                read.Name = "Renamed Credential";
                read.Active = false;
                Credential updated = await _Driver.Credentials.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal("Renamed Credential", updated.Name, "Updated Credential.Name");
                DatabaseAssert.Equal(false, updated.Active, "Updated Credential.Active");

                EnumerationResult<Credential> page1 = await _Driver.Credentials.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 1, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.EnumerationPage(page1, 1, 1, 2, 2, 1);
                EnumerationResult<Credential> page2 = await _Driver.Credentials.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 2, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.EnumerationPage(page2, 2, 1, 2, 2, 1);
                DatabaseAssert.ContainsIds(new[] { page1.Objects[0], page2.Objects[0] }, x => x.Id, credentialA.Id, credentialB.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestCredentialLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenant = await fixture.CreateTenantAsync("credential-lookup", token: token).ConfigureAwait(false);
                UserMaster user = await fixture.CreateUserAsync(tenant.Id, "credential-lookup-user", token: token).ConfigureAwait(false);
                Credential credentialA = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "credential-lookup-a", token: token).ConfigureAwait(false);
                Credential credentialB = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "credential-lookup-b", token: token).ConfigureAwait(false);

                Credential? byToken = await _Driver.Credentials.ReadByBearerTokenAsync(credentialA.BearerToken, token).ConfigureAwait(false);
                byToken = DatabaseAssert.NotNull(byToken, "Credential ReadByBearerTokenAsync returned null");
                DatabaseAssert.Equal(credentialA.Id, byToken.Id, "Credential.ReadByBearerToken.Id");

                List<Credential> byUser = await _Driver.Credentials.EnumerateByUserAsync(tenant.Id, user.Id, token).ConfigureAwait(false);
                DatabaseAssert.Equal(2, byUser.Count, "Credential EnumerateByUserAsync count");
                DatabaseAssert.ContainsIds(byUser, x => x.Id, credentialA.Id, credentialB.Id);

                EnumerationResult<Credential> paged = await _Driver.Credentials.EnumerateByUserAsync(tenant.Id, user.Id, new EnumerationQuery { PageNumber = 1, PageSize = 1 }, token).ConfigureAwait(false);
                DatabaseAssert.EnumerationPage(paged, 1, 1, 2, 2, 1);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestFleetCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (TenantMetadata tenant, UserMaster user, _, Fleet fleet, _, _, _, _, _, _, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Fleet? read = await _Driver.Fleets.ReadAsync(fleet.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Fleet read returned null");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "Fleet.TenantId");
                DatabaseAssert.Equal(user.Id, read.UserId, "Fleet.UserId");
                DatabaseAssert.Equal(fleet.Name, read.Name, "Fleet.Name");

                read.Description = "Updated fleet description";
                Fleet updated = await _Driver.Fleets.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal("Updated fleet description", updated.Description, "Fleet.Description");

                await AssertPagedTenantEnumerationAsync(
                    query => _Driver.Fleets.EnumerateAsync(tenant.Id, query, token),
                    fleet.Id,
                    async _ => await fixture.CreateFleetAsync(tenant.Id, user.Id, "page-two", token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestFleetLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (_, _, _, Fleet fleet, _, _, _, _, _, _, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Fleet? byName = await _Driver.Fleets.ReadByNameAsync(fleet.Name, token).ConfigureAwait(false);
                byName = DatabaseAssert.NotNull(byName, "Fleet ReadByNameAsync returned null");
                DatabaseAssert.Equal(fleet.Id, byName.Id, "Fleet.ReadByName.Id");
                DatabaseAssert.True(await _Driver.Fleets.ExistsAsync(fleet.Id, token).ConfigureAwait(false), "Fleet ExistsAsync should return true");

                List<Fleet> all = await _Driver.Fleets.EnumerateAsync(token).ConfigureAwait(false);
                DatabaseAssert.ContainsIds(all, x => x.Id, fleet.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestVesselCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (_, UserMaster user, _, Fleet fleet, Vessel vessel, _, _, _, _, _, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Vessel? read = await _Driver.Vessels.ReadAsync(vessel.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Vessel read returned null");
                DatabaseAssert.Equal(fleet.Id, read.FleetId, "Vessel.FleetId");
                DatabaseAssert.Equal(user.Id, read.UserId, "Vessel.UserId");
                DatabaseAssert.Equal(vessel.RepoUrl, read.RepoUrl, "Vessel.RepoUrl");

                read.DefaultBranch = "develop";
                Vessel updated = await _Driver.Vessels.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal("develop", updated.DefaultBranch, "Vessel.DefaultBranch");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestCaptainCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenant = await fixture.CreateTenantAsync("operational-tenant", token: token).ConfigureAwait(false);
                UserMaster user = await fixture.CreateUserAsync(tenant.Id, "operational-user", token: token).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync(tenant.Id, user.Id, "operational-captain", token, model: "gpt-5.4").ConfigureAwait(false);
                Captain? read = await _Driver.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Captain read returned null");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "Captain.TenantId");
                DatabaseAssert.Equal(user.Id, read.UserId, "Captain.UserId");
                DatabaseAssert.Equal(captain.Name, read.Name, "Captain.Name");
                DatabaseAssert.Equal("gpt-5.4", read.Model, "Captain.Model");

                read.RecoveryAttempts = 2;
                read.Model = "gpt-5.4-mini";
                await _Driver.Captains.UpdateAsync(read, token).ConfigureAwait(false);

                Captain? reread = await _Driver.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
                reread = DatabaseAssert.NotNull(reread, "Captain reread returned null");
                DatabaseAssert.Equal(2, reread.RecoveryAttempts, "Captain.RecoveryAttempts");
                DatabaseAssert.Equal("gpt-5.4-mini", reread.Model, "Captain.Model after update");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestVoyageCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (_, UserMaster user, _, _, _, _, Voyage voyage, _, _, _, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Voyage? read = await _Driver.Voyages.ReadAsync(voyage.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Voyage read returned null");
                DatabaseAssert.Equal(user.Id, read.UserId, "Voyage.UserId");
                DatabaseAssert.Equal(voyage.Title, read.Title, "Voyage.Title");

                read.Status = VoyageStatusEnum.Complete;
                Voyage updated = await _Driver.Voyages.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal(VoyageStatusEnum.Complete, updated.Status, "Voyage.Status");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestMissionCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                TenantMetadata tenant = await fixture.CreateTenantAsync("operational-tenant", token: token).ConfigureAwait(false);
                UserMaster user = await fixture.CreateUserAsync(tenant.Id, "operational-user", token: token).ConfigureAwait(false);
                Fleet fleet = await fixture.CreateFleetAsync(tenant.Id, user.Id, "operational-fleet", token).ConfigureAwait(false);
                Vessel vessel = await fixture.CreateVesselAsync(tenant.Id, user.Id, fleet.Id, "operational-vessel", token).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync(tenant.Id, user.Id, "operational-captain", token).ConfigureAwait(false);
                Voyage voyage = await fixture.CreateVoyageAsync(tenant.Id, user.Id, "operational-voyage", token).ConfigureAwait(false);
                Mission mission = await fixture.CreateMissionAsync(tenant.Id, user.Id, voyage.Id, vessel.Id, captain.Id, "operational-mission", token, totalRuntimeMs: 9000).ConfigureAwait(false);
                Mission? read = await _Driver.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Mission read returned null");
                DatabaseAssert.Equal(vessel.Id, read.VesselId, "Mission.VesselId");
                DatabaseAssert.Equal(captain.Id, read.CaptainId, "Mission.CaptainId");
                DatabaseAssert.Equal(voyage.Id, read.VoyageId, "Mission.VoyageId");
                DatabaseAssert.Equal(9000L, read.TotalRuntimeMs, "Mission.TotalRuntimeMs");

                read.Status = MissionStatusEnum.InProgress;
                read.Priority = 3;
                read.TotalRuntimeMs = 12000;
                await _Driver.Missions.UpdateAsync(read, token).ConfigureAwait(false);

                Mission? reread = await _Driver.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                reread = DatabaseAssert.NotNull(reread, "Mission reread returned null");
                DatabaseAssert.Equal(MissionStatusEnum.InProgress, reread.Status, "Mission.Status");
                DatabaseAssert.Equal(3, reread.Priority, "Mission.Priority");
                DatabaseAssert.Equal(12000L, reread.TotalRuntimeMs, "Mission.TotalRuntimeMs after update");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestDockCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (_, _, _, _, Vessel vessel, Captain captain, _, _, Dock dock, _, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Dock? read = await _Driver.Docks.ReadAsync(dock.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Dock read returned null");
                DatabaseAssert.Equal(vessel.Id, read.VesselId, "Dock.VesselId");
                DatabaseAssert.Equal(captain.Id, read.CaptainId, "Dock.CaptainId");

                read.Active = false;
                Dock updated = await _Driver.Docks.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal(false, updated.Active, "Dock.Active");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestSignalCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (TenantMetadata tenant, _, _, _, _, Captain captain, _, _, _, Signal signal, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Signal? read = await _Driver.Signals.ReadAsync(signal.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Signal read returned null");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "Signal.TenantId");
                DatabaseAssert.Equal(captain.Id, read.ToCaptainId, "Signal.ToCaptainId");
                DatabaseAssert.Equal(false, read.Read, "Signal.Read");

                EnumerationResult<Signal> page = await _Driver.Signals.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 1, PageSize = 10 }, token).ConfigureAwait(false);
                DatabaseAssert.True(page.TotalRecords >= 1, "Signal enumeration should include created signal");
                DatabaseAssert.ContainsIds(page.Objects, x => x.Id, signal.Id);

                await _Driver.Signals.MarkReadAsync(signal.Id, token).ConfigureAwait(false);
                Signal? reread = await _Driver.Signals.ReadAsync(signal.Id, token).ConfigureAwait(false);
                reread = DatabaseAssert.NotNull(reread, "Signal re-read returned null");
                DatabaseAssert.Equal(true, reread.Read, "Signal.Read after mark read");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestSignalLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (_, _, _, _, _, Captain captain, _, _, _, Signal signalA, _, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                Signal signalB = await fixture.CreateSignalAsync(signalA.TenantId!, signalA.UserId!, captain.Id, token).ConfigureAwait(false);

                List<Signal> recent = await _Driver.Signals.EnumerateRecentAsync(10, token).ConfigureAwait(false);
                DatabaseAssert.ContainsIds(recent, x => x.Id, signalA.Id, signalB.Id);

                List<Signal> unread = await _Driver.Signals.EnumerateByRecipientAsync(captain.Id, true, token).ConfigureAwait(false);
                DatabaseAssert.ContainsIds(unread, x => x.Id, signalA.Id, signalB.Id);

                await _Driver.Signals.MarkReadAsync(signalA.TenantId!, signalA.Id, token).ConfigureAwait(false);
                List<Signal> unreadAfter = await _Driver.Signals.EnumerateByRecipientAsync(captain.Id, true, token).ConfigureAwait(false);
                DatabaseAssert.True(!unreadAfter.Exists(x => x.Id == signalA.Id), "Tenant-scoped MarkReadAsync should remove signal from unread list");
                DatabaseAssert.True(unreadAfter.Exists(x => x.Id == signalB.Id), "Unread list should still contain untouched signal");
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestEventCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (TenantMetadata tenant, _, _, _, Vessel vessel, Captain captain, Voyage voyage, Mission mission, _, _, ArmadaEvent evt, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                ArmadaEvent? read = await _Driver.Events.ReadAsync(evt.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Event read returned null");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "Event.TenantId");
                DatabaseAssert.Equal(vessel.Id, read.VesselId, "Event.VesselId");
                DatabaseAssert.Equal(captain.Id, read.CaptainId, "Event.CaptainId");
                DatabaseAssert.Equal(mission.Id, read.MissionId, "Event.MissionId");
                DatabaseAssert.Equal(voyage.Id, read.VoyageId, "Event.VoyageId");

                EnumerationResult<ArmadaEvent> page = await _Driver.Events.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 1, PageSize = 10, MissionId = mission.Id }, token).ConfigureAwait(false);
                DatabaseAssert.True(page.TotalRecords >= 1, "Event enumeration should include created event");
                DatabaseAssert.ContainsIds(page.Objects, x => x.Id, evt.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestEventLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            ArmadaEvent? evtB = null;
            try
            {
                (_, _, _, _, Vessel vessel, Captain captain, Voyage voyage, Mission mission, _, _, ArmadaEvent evtA, _) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                evtB = new ArmadaEvent("mission.updated", "Mission updated for integration test")
                {
                    TenantId = evtA.TenantId,
                    UserId = evtA.UserId,
                    MissionId = mission.Id,
                    VoyageId = voyage.Id,
                    VesselId = vessel.Id,
                    CaptainId = captain.Id,
                    EntityType = "mission",
                    EntityId = mission.Id,
                    Payload = "{\"kind\":\"test-update\"}"
                };
                evtB = await _Driver.Events.CreateAsync(evtB, token).ConfigureAwait(false);

                List<ArmadaEvent> recent = await _Driver.Events.EnumerateRecentAsync(10, token).ConfigureAwait(false);
                DatabaseAssert.ContainsIds(recent, x => x.Id, evtA.Id, evtB.Id);

                List<ArmadaEvent> byType = await _Driver.Events.EnumerateByTypeAsync("mission.created", 10, token).ConfigureAwait(false);
                DatabaseAssert.True(byType.Exists(x => x.Id == evtA.Id), "EnumerateByTypeAsync should contain mission.created event");

                DatabaseAssert.ContainsIds(await _Driver.Events.EnumerateByEntityAsync("mission", mission.Id, 10, token).ConfigureAwait(false), x => x.Id, evtA.Id, evtB.Id);
                DatabaseAssert.ContainsIds(await _Driver.Events.EnumerateByCaptainAsync(captain.Id, 10, token).ConfigureAwait(false), x => x.Id, evtA.Id, evtB.Id);
                DatabaseAssert.ContainsIds(await _Driver.Events.EnumerateByMissionAsync(mission.Id, 10, token).ConfigureAwait(false), x => x.Id, evtA.Id, evtB.Id);
                DatabaseAssert.ContainsIds(await _Driver.Events.EnumerateByVesselAsync(vessel.Id, 10, token).ConfigureAwait(false), x => x.Id, evtA.Id, evtB.Id);
                DatabaseAssert.ContainsIds(await _Driver.Events.EnumerateByVoyageAsync(voyage.Id, 10, token).ConfigureAwait(false), x => x.Id, evtA.Id, evtB.Id);
            }
            finally
            {
                if (!_NoCleanup && evtB != null)
                {
                    try
                    {
                        await _Driver.Events.DeleteAsync(evtB.Id, token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestMergeEntryCrudAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (TenantMetadata tenant, UserMaster user, _, _, Vessel vessel, _, _, Mission mission, _, _, _, MergeEntry merge) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                MergeEntry? read = await _Driver.MergeEntries.ReadAsync(merge.Id, token).ConfigureAwait(false);
                read = DatabaseAssert.NotNull(read, "Merge entry read returned null");
                DatabaseAssert.Equal(tenant.Id, read.TenantId, "MergeEntry.TenantId");
                DatabaseAssert.Equal(user.Id, read.UserId, "MergeEntry.UserId");
                DatabaseAssert.Equal(vessel.Id, read.VesselId, "MergeEntry.VesselId");
                DatabaseAssert.Equal(mission.Id, read.MissionId, "MergeEntry.MissionId");

                read.Status = MergeStatusEnum.Landed;
                MergeEntry updated = await _Driver.MergeEntries.UpdateAsync(read, token).ConfigureAwait(false);
                DatabaseAssert.Equal(MergeStatusEnum.Landed, updated.Status, "MergeEntry.Status");

                EnumerationResult<MergeEntry> page = await _Driver.MergeEntries.EnumerateAsync(tenant.Id, new EnumerationQuery { PageNumber = 1, PageSize = 10, MissionId = mission.Id }, token).ConfigureAwait(false);
                DatabaseAssert.True(page.TotalRecords >= 1, "Merge entry enumeration should include created merge entry");
                DatabaseAssert.ContainsIds(page.Objects, x => x.Id, merge.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestMergeEntryLookupAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            try
            {
                (_, _, _, _, _, _, _, _, _, _, _, MergeEntry merge) = await SeedOperationalGraphAsync(fixture, token).ConfigureAwait(false);
                DatabaseAssert.True(await _Driver.MergeEntries.ExistsAsync(merge.Id, token).ConfigureAwait(false), "MergeEntry ExistsAsync should return true");

                List<MergeEntry> queued = await _Driver.MergeEntries.EnumerateByStatusAsync(MergeStatusEnum.Queued, token).ConfigureAwait(false);
                DatabaseAssert.ContainsIds(queued, x => x.Id, merge.Id);

                List<MergeEntry> all = await _Driver.MergeEntries.EnumerateAsync(token).ConfigureAwait(false);
                DatabaseAssert.ContainsIds(all, x => x.Id, merge.Id);
            }
            finally
            {
                await fixture.CleanupAsync(token).ConfigureAwait(false);
            }
        }

        private async Task TestTenantAuthCascadeDeleteAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            TenantMetadata tenant = await fixture.CreateTenantAsync("cascade-tenant", token: token).ConfigureAwait(false);
            UserMaster user = await fixture.CreateUserAsync(tenant.Id, "cascade-user", token: token).ConfigureAwait(false);
            Credential credential = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "cascade-credential", token: token).ConfigureAwait(false);
            await _Driver.Tenants.DeleteAsync(tenant.Id, token).ConfigureAwait(false);

            DatabaseAssert.True(await _Driver.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false) == null, "Tenant should be deleted");
            DatabaseAssert.True(await _Driver.Users.ReadByIdAsync(user.Id, token).ConfigureAwait(false) == null, "User should be deleted by tenant cascade");
            DatabaseAssert.True(await _Driver.Credentials.ReadByIdAsync(credential.Id, token).ConfigureAwait(false) == null, "Credential should be deleted by tenant cascade");
        }

        private async Task TestTenantDeleteFencedByOperationalDataAsync(CancellationToken token)
        {
            DatabaseFixture fixture = new DatabaseFixture(_Driver, _NoCleanup);
            TenantMetadata tenant = await fixture.CreateTenantAsync("cascade-tenant", token: token).ConfigureAwait(false);
            UserMaster user = await fixture.CreateUserAsync(tenant.Id, "cascade-user", token: token).ConfigureAwait(false);
            Credential credential = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "cascade-credential", token: token).ConfigureAwait(false);
            Fleet fleet = await fixture.CreateFleetAsync(tenant.Id, user.Id, "cascade-fleet", token).ConfigureAwait(false);
            Vessel vessel = await fixture.CreateVesselAsync(tenant.Id, user.Id, fleet.Id, "cascade-vessel", token).ConfigureAwait(false);
            Captain captain = await fixture.CreateCaptainAsync(tenant.Id, user.Id, "cascade-captain", token).ConfigureAwait(false);
            Voyage voyage = await fixture.CreateVoyageAsync(tenant.Id, user.Id, "cascade-voyage", token).ConfigureAwait(false);
            Mission mission = await fixture.CreateMissionAsync(tenant.Id, user.Id, voyage.Id, vessel.Id, captain.Id, "cascade-mission", token).ConfigureAwait(false);
            Dock dock = await fixture.CreateDockAsync(tenant.Id, user.Id, vessel.Id, captain.Id, token).ConfigureAwait(false);
            Signal signal = await fixture.CreateSignalAsync(tenant.Id, user.Id, captain.Id, token).ConfigureAwait(false);
            ArmadaEvent evt = await fixture.CreateEventAsync(tenant.Id, user.Id, mission.Id, voyage.Id, vessel.Id, captain.Id, token).ConfigureAwait(false);
            MergeEntry merge = await fixture.CreateMergeEntryAsync(tenant.Id, user.Id, mission.Id, vessel.Id, token).ConfigureAwait(false);

            bool deleteFailed = false;
            try
            {
                await _Driver.Tenants.DeleteAsync(tenant.Id, token).ConfigureAwait(false);
            }
            catch
            {
                deleteFailed = true;
            }

            DatabaseAssert.True(deleteFailed, "Tenant delete with operational subordinates should be fenced by foreign keys");
            DatabaseAssert.True(await _Driver.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false) != null, "Tenant should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Users.ReadByIdAsync(user.Id, token).ConfigureAwait(false) != null, "User should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Credentials.ReadByIdAsync(credential.Id, token).ConfigureAwait(false) != null, "Credential should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Fleets.ReadAsync(fleet.Id, token).ConfigureAwait(false) != null, "Fleet should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Vessels.ReadAsync(vessel.Id, token).ConfigureAwait(false) != null, "Vessel should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false) != null, "Captain should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Voyages.ReadAsync(voyage.Id, token).ConfigureAwait(false) != null, "Voyage should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false) != null, "Mission should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Docks.ReadAsync(dock.Id, token).ConfigureAwait(false) != null, "Dock should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Signals.ReadAsync(signal.Id, token).ConfigureAwait(false) != null, "Signal should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.Events.ReadAsync(evt.Id, token).ConfigureAwait(false) != null, "Event should still exist after fenced delete");
            DatabaseAssert.True(await _Driver.MergeEntries.ReadAsync(merge.Id, token).ConfigureAwait(false) != null, "Merge entry should still exist after fenced delete");
        }

        private async Task AssertPagedTenantEnumerationAsync<T>(Func<EnumerationQuery, Task<EnumerationResult<T>>> enumerate, string existingId, Func<string, Task<T>> createAdditional)
            where T : class
        {
            T second = await createAdditional("page-two").ConfigureAwait(false);
            string secondId = (string)second.GetType().GetProperty("Id")!.GetValue(second)!;

            EnumerationResult<T> page1 = await enumerate(new EnumerationQuery { PageNumber = 1, PageSize = 1 }).ConfigureAwait(false);
            EnumerationResult<T> page2 = await enumerate(new EnumerationQuery { PageNumber = 2, PageSize = 1 }).ConfigureAwait(false);

            DatabaseAssert.EnumerationPage(page1, 1, 1, 2, 2, 1);
            DatabaseAssert.EnumerationPage(page2, 2, 1, 2, 2, 1);
            DatabaseAssert.ContainsIds(new[] { page1.Objects[0], page2.Objects[0] }, x => (string)x!.GetType().GetProperty("Id")!.GetValue(x)!, existingId, secondId);
        }

        private async Task<(TenantMetadata tenant, UserMaster user, Credential credential, Fleet fleet, Vessel vessel, Captain captain, Voyage voyage, Mission mission, Dock dock, Signal signal, ArmadaEvent evt, MergeEntry merge)> SeedOperationalGraphAsync(DatabaseFixture fixture, CancellationToken token)
        {
            TenantMetadata tenant = await fixture.CreateTenantAsync("operational-tenant", token: token).ConfigureAwait(false);
            UserMaster user = await fixture.CreateUserAsync(tenant.Id, "operational-user", token: token).ConfigureAwait(false);
            Credential credential = await fixture.CreateCredentialAsync(tenant.Id, user.Id, "operational-credential", token: token).ConfigureAwait(false);
            Fleet fleet = await fixture.CreateFleetAsync(tenant.Id, user.Id, "operational-fleet", token).ConfigureAwait(false);
            Vessel vessel = await fixture.CreateVesselAsync(tenant.Id, user.Id, fleet.Id, "operational-vessel", token).ConfigureAwait(false);
            Captain captain = await fixture.CreateCaptainAsync(tenant.Id, user.Id, "operational-captain", token).ConfigureAwait(false);
            Voyage voyage = await fixture.CreateVoyageAsync(tenant.Id, user.Id, "operational-voyage", token).ConfigureAwait(false);
            Mission mission = await fixture.CreateMissionAsync(tenant.Id, user.Id, voyage.Id, vessel.Id, captain.Id, "operational-mission", token).ConfigureAwait(false);
            Dock dock = await fixture.CreateDockAsync(tenant.Id, user.Id, vessel.Id, captain.Id, token).ConfigureAwait(false);
            Signal signal = await fixture.CreateSignalAsync(tenant.Id, user.Id, captain.Id, token).ConfigureAwait(false);
            ArmadaEvent evt = await fixture.CreateEventAsync(tenant.Id, user.Id, mission.Id, voyage.Id, vessel.Id, captain.Id, token).ConfigureAwait(false);
            MergeEntry merge = await fixture.CreateMergeEntryAsync(tenant.Id, user.Id, mission.Id, vessel.Id, token).ConfigureAwait(false);

            return (tenant, user, credential, fleet, vessel, captain, voyage, mission, dock, signal, evt, merge);
        }
    }
}
