namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The tenant-scoped and user-scoped paginated enumerations apply the same <see cref="EnumerationQuery"/> filters as
    /// the unscoped one (status, fleet, vessel, captain, voyage, mission, signal type, recipient, unread). Before the fix
    /// several providers dropped every filter except the created-date range on the scoped overloads, so a tenant admin or
    /// regular user filtering missions by voyage (REST and MCP) got the whole tenant back. Each case seeds two records in
    /// the same tenant and user that differ only in the filtered column, and checks the global, tenant and user overloads
    /// return the same single match.
    /// </summary>
    public sealed class ScopedEnumerationFilterSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.ScopedEnumerationFilters";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("mission_filters_apply_in_every_scope", "Missions: status, vessel, captain and voyage filters apply to the tenant and user overloads", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TestOwnerIdentity owner = await CreateOwnerAsync(db).ConfigureAwait(false);
                    Vessel v1 = await CreateVesselAsync(db, owner, null, "a").ConfigureAwait(false);
                    Vessel v2 = await CreateVesselAsync(db, owner, null, "b").ConfigureAwait(false);
                    Captain c1 = await CreateCaptainAsync(db, owner, "a", CaptainStateEnum.Working).ConfigureAwait(false);
                    Captain c2 = await CreateCaptainAsync(db, owner, "b", CaptainStateEnum.Idle).ConfigureAwait(false);
                    Voyage y1 = await CreateVoyageAsync(db, owner, "a", VoyageStatusEnum.Open).ConfigureAwait(false);
                    Voyage y2 = await CreateVoyageAsync(db, owner, "b", VoyageStatusEnum.Complete).ConfigureAwait(false);

                    Mission m1 = new Mission("filter-a", "a");
                    Stamp(m1, owner);
                    m1.VesselId = v1.Id;
                    m1.CaptainId = c1.Id;
                    m1.VoyageId = y1.Id;
                    m1.Status = MissionStatusEnum.Failed;
                    m1 = await db.Missions.CreateAsync(m1).ConfigureAwait(false);
                    Mission m2 = new Mission("filter-b", "b");
                    Stamp(m2, owner);
                    m2.VesselId = v2.Id;
                    m2.CaptainId = c2.Id;
                    m2.VoyageId = y2.Id;
                    m2.Status = MissionStatusEnum.Complete;
                    await db.Missions.CreateAsync(m2).ConfigureAwait(false);

                    List<EnumerationQuery> queries = new List<EnumerationQuery>
                    {
                        new EnumerationQuery { Status = MissionStatusEnum.Failed.ToString() },
                        new EnumerationQuery { VesselId = v1.Id },
                        new EnumerationQuery { CaptainId = c1.Id },
                        new EnumerationQuery { VoyageId = y1.Id }
                    };
                    foreach (EnumerationQuery query in queries)
                    {
                        await ExpectSingleAsync("missions", m1.Id, owner, query,
                            q => db.Missions.EnumerateAsync(q), (t, q) => db.Missions.EnumerateAsync(t, q), (t, u, q) => db.Missions.EnumerateAsync(t, u, q), m => m.Id).ConfigureAwait(false);
                        await ExpectSingleAsync("mission summaries", m1.Id, owner, query,
                            q => db.Missions.EnumerateSummariesAsync(q), (t, q) => db.Missions.EnumerateSummariesAsync(t, q), (t, u, q) => db.Missions.EnumerateSummariesAsync(t, u, q), m => m.Id).ConfigureAwait(false);
                    }
                }
            }));

            cases.Add(CaseAsync("voyage_captain_vessel_filters_apply_in_every_scope", "Voyages (status), captains (status) and vessels (fleet) filter in every scope", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TestOwnerIdentity owner = await CreateOwnerAsync(db).ConfigureAwait(false);
                    Voyage y1 = await CreateVoyageAsync(db, owner, "a", VoyageStatusEnum.Open).ConfigureAwait(false);
                    await CreateVoyageAsync(db, owner, "b", VoyageStatusEnum.Complete).ConfigureAwait(false);
                    await ExpectSingleAsync("voyages", y1.Id, owner, new EnumerationQuery { Status = VoyageStatusEnum.Open.ToString() },
                        q => db.Voyages.EnumerateAsync(q), (t, q) => db.Voyages.EnumerateAsync(t, q), (t, u, q) => db.Voyages.EnumerateAsync(t, u, q), v => v.Id).ConfigureAwait(false);

                    Captain c1 = await CreateCaptainAsync(db, owner, "a", CaptainStateEnum.Working).ConfigureAwait(false);
                    await CreateCaptainAsync(db, owner, "b", CaptainStateEnum.Idle).ConfigureAwait(false);
                    await ExpectSingleAsync("captains", c1.Id, owner, new EnumerationQuery { Status = CaptainStateEnum.Working.ToString() },
                        q => db.Captains.EnumerateAsync(q), (t, q) => db.Captains.EnumerateAsync(t, q), (t, u, q) => db.Captains.EnumerateAsync(t, u, q), c => c.Id).ConfigureAwait(false);

                    Fleet f1 = await CreateFleetAsync(db, owner, "a").ConfigureAwait(false);
                    Fleet f2 = await CreateFleetAsync(db, owner, "b").ConfigureAwait(false);
                    Vessel v1 = await CreateVesselAsync(db, owner, f1.Id, "a").ConfigureAwait(false);
                    await CreateVesselAsync(db, owner, f2.Id, "b").ConfigureAwait(false);
                    await ExpectSingleAsync("vessels", v1.Id, owner, new EnumerationQuery { FleetId = f1.Id },
                        q => db.Vessels.EnumerateAsync(q), (t, q) => db.Vessels.EnumerateAsync(t, q), (t, u, q) => db.Vessels.EnumerateAsync(t, u, q), v => v.Id).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("dock_signal_merge_filters_apply_in_every_scope", "Docks (vessel, captain), signals (recipient, type, unread) and merge entries (status, vessel, mission) filter in every scope", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TestOwnerIdentity owner = await CreateOwnerAsync(db).ConfigureAwait(false);
                    Vessel v1 = await CreateVesselAsync(db, owner, null, "a").ConfigureAwait(false);
                    Vessel v2 = await CreateVesselAsync(db, owner, null, "b").ConfigureAwait(false);
                    Captain c1 = await CreateCaptainAsync(db, owner, "a", CaptainStateEnum.Working).ConfigureAwait(false);
                    Captain c2 = await CreateCaptainAsync(db, owner, "b", CaptainStateEnum.Idle).ConfigureAwait(false);

                    Dock d1 = new Dock(v1.Id);
                    Stamp(d1, owner);
                    d1.CaptainId = c1.Id;
                    d1.BranchName = "filter-a";
                    d1 = await db.Docks.CreateAsync(d1).ConfigureAwait(false);
                    Dock d2 = new Dock(v2.Id);
                    Stamp(d2, owner);
                    d2.CaptainId = c2.Id;
                    d2.BranchName = "filter-b";
                    await db.Docks.CreateAsync(d2).ConfigureAwait(false);
                    foreach (EnumerationQuery query in new[] { new EnumerationQuery { VesselId = v1.Id }, new EnumerationQuery { CaptainId = c1.Id } })
                    {
                        await ExpectSingleAsync("docks", d1.Id, owner, query,
                            q => db.Docks.EnumerateAsync(q), (t, q) => db.Docks.EnumerateAsync(t, q), (t, u, q) => db.Docks.EnumerateAsync(t, u, q), d => d.Id).ConfigureAwait(false);
                    }

                    Signal s1 = new Signal(SignalTypeEnum.Nudge, "a");
                    Stamp(s1, owner);
                    s1.ToCaptainId = c1.Id;
                    s1 = await db.Signals.CreateAsync(s1).ConfigureAwait(false);
                    Signal s2 = new Signal(SignalTypeEnum.Mail, "b");
                    Stamp(s2, owner);
                    s2.ToCaptainId = c2.Id;
                    s2.Read = true;
                    await db.Signals.CreateAsync(s2).ConfigureAwait(false);
                    foreach (EnumerationQuery query in new[]
                    {
                        new EnumerationQuery { ToCaptainId = c1.Id },
                        new EnumerationQuery { SignalType = SignalTypeEnum.Nudge.ToString() },
                        new EnumerationQuery { UnreadOnly = true }
                    })
                    {
                        await ExpectSingleAsync("signals", s1.Id, owner, query,
                            q => db.Signals.EnumerateAsync(q), (t, q) => db.Signals.EnumerateAsync(t, q), (t, u, q) => db.Signals.EnumerateAsync(t, u, q), s => s.Id).ConfigureAwait(false);
                    }

                    Mission m1 = new Mission("merge-a", "a");
                    Stamp(m1, owner);
                    m1.VesselId = v1.Id;
                    m1 = await db.Missions.CreateAsync(m1).ConfigureAwait(false);
                    MergeEntry e1 = new MergeEntry("filter-a");
                    Stamp(e1, owner);
                    e1.VesselId = v1.Id;
                    e1.MissionId = m1.Id;
                    e1.Status = MergeStatusEnum.Failed;
                    e1 = await db.MergeEntries.CreateAsync(e1).ConfigureAwait(false);
                    MergeEntry e2 = new MergeEntry("filter-b");
                    Stamp(e2, owner);
                    e2.VesselId = v2.Id;
                    e2.Status = MergeStatusEnum.Queued;
                    await db.MergeEntries.CreateAsync(e2).ConfigureAwait(false);
                    foreach (EnumerationQuery query in new[]
                    {
                        new EnumerationQuery { Status = MergeStatusEnum.Failed.ToString() },
                        new EnumerationQuery { VesselId = v1.Id },
                        new EnumerationQuery { MissionId = m1.Id }
                    })
                    {
                        await ExpectSingleAsync("merge entries", e1.Id, owner, query,
                            q => db.MergeEntries.EnumerateAsync(q), (t, q) => db.MergeEntries.EnumerateAsync(t, q), (t, u, q) => db.MergeEntries.EnumerateAsync(t, u, q), e => e.Id).ConfigureAwait(false);
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Scoped Enumeration Filters",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task ExpectSingleAsync<T>(
            string label,
            string expectedId,
            TestOwnerIdentity owner,
            EnumerationQuery query,
            Func<EnumerationQuery, Task<EnumerationResult<T>>> all,
            Func<string, EnumerationQuery, Task<EnumerationResult<T>>> byTenant,
            Func<string, string, EnumerationQuery, Task<EnumerationResult<T>>> byTenantUser,
            Func<T, string> idOf)
        {
            string filter = Describe(query);
            EnumerationResult<T> global = await all(query).ConfigureAwait(false);
            AssertEqual(1, global.Objects.Count, label + " (global) with " + filter);
            AssertEqual(expectedId, idOf(global.Objects[0]), label + " (global) match with " + filter);

            EnumerationResult<T> tenant = await byTenant(owner.TenantId, query).ConfigureAwait(false);
            AssertEqual(1, tenant.Objects.Count, label + " (tenant) with " + filter + ": got [" + String.Join(", ", tenant.Objects.Select(idOf)) + "]");
            AssertEqual(expectedId, idOf(tenant.Objects[0]), label + " (tenant) match with " + filter);
            AssertEqual(1L, tenant.TotalRecords, label + " (tenant) total with " + filter);

            EnumerationResult<T> user = await byTenantUser(owner.TenantId, owner.UserId, query).ConfigureAwait(false);
            AssertEqual(1, user.Objects.Count, label + " (user) with " + filter + ": got [" + String.Join(", ", user.Objects.Select(idOf)) + "]");
            AssertEqual(expectedId, idOf(user.Objects[0]), label + " (user) match with " + filter);
            AssertEqual(1L, user.TotalRecords, label + " (user) total with " + filter);
        }

        private static string Describe(EnumerationQuery query)
        {
            List<string> parts = new List<string>();
            if (query.Status != null) parts.Add("status=" + query.Status);
            if (query.FleetId != null) parts.Add("fleetId");
            if (query.VesselId != null) parts.Add("vesselId");
            if (query.CaptainId != null) parts.Add("captainId");
            if (query.VoyageId != null) parts.Add("voyageId");
            if (query.MissionId != null) parts.Add("missionId");
            if (query.SignalType != null) parts.Add("signalType=" + query.SignalType);
            if (query.ToCaptainId != null) parts.Add("toCaptainId");
            if (query.UnreadOnly == true) parts.Add("unreadOnly");
            return String.Join(",", parts);
        }

        private static async Task<TestOwnerIdentity> CreateOwnerAsync(DatabaseDriver db)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            TenantMetadata tenant = new TenantMetadata("filters-" + suffix);
            tenant = await db.Tenants.CreateAsync(tenant).ConfigureAwait(false);
            UserMaster user = new UserMaster(tenant.Id, "filters-" + suffix + "@example.com", "password");
            user = await db.Users.CreateAsync(user).ConfigureAwait(false);
            TestOwnerIdentity owner = new TestOwnerIdentity();
            owner.TenantId = tenant.Id;
            owner.UserId = user.Id;
            return owner;
        }

        private static void Stamp(Mission m, TestOwnerIdentity owner) { m.TenantId = owner.TenantId; m.UserId = owner.UserId; }

        private static void Stamp(Dock d, TestOwnerIdentity owner) { d.TenantId = owner.TenantId; d.UserId = owner.UserId; }

        private static void Stamp(Signal s, TestOwnerIdentity owner) { s.TenantId = owner.TenantId; s.UserId = owner.UserId; }

        private static void Stamp(MergeEntry e, TestOwnerIdentity owner) { e.TenantId = owner.TenantId; e.UserId = owner.UserId; }

        private static async Task<Fleet> CreateFleetAsync(DatabaseDriver db, TestOwnerIdentity owner, string tag)
        {
            Fleet fleet = new Fleet("filter-fleet-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            fleet.TenantId = owner.TenantId;
            fleet.UserId = owner.UserId;
            return await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
        }

        private static async Task<Vessel> CreateVesselAsync(DatabaseDriver db, TestOwnerIdentity owner, string? fleetId, string tag)
        {
            string name = "filter-vessel-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Vessel vessel = new Vessel(name, "https://example.invalid/" + name + ".git");
            vessel.TenantId = owner.TenantId;
            vessel.UserId = owner.UserId;
            vessel.FleetId = fleetId;
            return await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static async Task<Captain> CreateCaptainAsync(DatabaseDriver db, TestOwnerIdentity owner, string tag, CaptainStateEnum state)
        {
            Captain captain = new Captain("filter-captain-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            captain.TenantId = owner.TenantId;
            captain.UserId = owner.UserId;
            captain.State = state;
            return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private static async Task<Voyage> CreateVoyageAsync(DatabaseDriver db, TestOwnerIdentity owner, string tag, VoyageStatusEnum status)
        {
            Voyage voyage = new Voyage("filter-voyage-" + tag, tag);
            voyage.TenantId = owner.TenantId;
            voyage.UserId = owner.UserId;
            voyage.Status = status;
            return await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
