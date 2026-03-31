namespace Armada.Test.Unit.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Tests that tenant-scoped overloads (ReadAsync, DeleteAsync, EnumerateAsync)
    /// work correctly on existing entities, enforcing tenant isolation.
    /// Covers Fleet (exhaustive), Vessel, Mission, and Signal (representative).
    /// </summary>
    public class EntityTenantScopedTests : TestSuite
    {
        public override string Name => "Entity Tenant-Scoped Operations";

        protected override async Task RunTestsAsync()
        {
            // ── Fleet ──────────────────────────────────────────────────────

            await RunTest("Fleet ReadAsync(tenantId, id) returns fleet for correct tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Fleet-Read-OK");
                    fleet.TenantId = t1;
                    await db.Fleets.CreateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadAsync(t1, fleet.Id);
                    AssertNotNull(result);
                    AssertEqual(fleet.Id, result!.Id);
                    AssertEqual(t1, result.TenantId);
                }
            });

            await RunTest("Fleet ReadAsync(tenantId, id) returns null for wrong tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Fleet-Read-WrongTenant");
                    fleet.TenantId = t1;
                    await db.Fleets.CreateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadAsync(t2, fleet.Id);
                    AssertNull(result);
                }
            });

            await RunTest("Fleet DeleteAsync(tenantId, id) removes fleet for correct tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Fleet-Delete-OK");
                    fleet.TenantId = t1;
                    await db.Fleets.CreateAsync(fleet);

                    await db.Fleets.DeleteAsync(t1, fleet.Id);

                    // Fleet should be gone even via global read
                    AssertNull(await db.Fleets.ReadAsync(fleet.Id));
                }
            });

            await RunTest("Fleet DeleteAsync(tenantId, id) does nothing for wrong tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Fleet-Delete-WrongTenant");
                    fleet.TenantId = t1;
                    await db.Fleets.CreateAsync(fleet);

                    // Attempt delete from wrong tenant
                    await db.Fleets.DeleteAsync(t2, fleet.Id);

                    // Fleet should still exist
                    AssertNotNull(await db.Fleets.ReadAsync(t1, fleet.Id));
                }
            });

            await RunTest("Fleet EnumerateAsync(tenantId) returns only matching tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet f1 = new Fleet("Fleet-T1-A") { TenantId = t1 };
                    Fleet f2 = new Fleet("Fleet-T1-B") { TenantId = t1 };
                    Fleet f3 = new Fleet("Fleet-T2-A") { TenantId = t2 };
                    await db.Fleets.CreateAsync(f1);
                    await db.Fleets.CreateAsync(f2);
                    await db.Fleets.CreateAsync(f3);

                    List<Fleet> t1Fleets = await db.Fleets.EnumerateAsync(t1);
                    AssertEqual(2, t1Fleets.Count);
                    foreach (Fleet f in t1Fleets)
                    {
                        AssertEqual(t1, f.TenantId, "All enumerated fleets should belong to t1");
                    }

                    List<Fleet> t2Fleets = await db.Fleets.EnumerateAsync(t2);
                    AssertEqual(1, t2Fleets.Count);
                    AssertEqual(t2, t2Fleets[0].TenantId);
                }
            });

            await RunTest("Fleet EnumerateAsync(tenantId, query) paginates within tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    // Create 3 fleets in t1 and 1 in t2
                    for (int i = 0; i < 3; i++)
                    {
                        Fleet f = new Fleet("Fleet-Page-" + i) { TenantId = t1 };
                        await db.Fleets.CreateAsync(f);
                    }
                    Fleet noise = new Fleet("Fleet-Noise") { TenantId = t2 };
                    await db.Fleets.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 1 };
                    EnumerationResult<Fleet> page1 = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(3, (int)page1.TotalRecords);
                    foreach (Fleet f in page1.Objects)
                    {
                        AssertEqual(t1, f.TenantId, "Paginated results should belong to t1");
                    }
                }
            });

            // ── Vessel ─────────────────────────────────────────────────────

            await RunTest("Vessel ReadAsync(tenantId, id) returns null for wrong tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Vessel-Fleet") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Vessel-Read-Wrong", "https://github.com/test/repo");
                    vessel.TenantId = t1;
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    AssertNotNull(await db.Vessels.ReadAsync(t1, vessel.Id));
                    AssertNull(await db.Vessels.ReadAsync(t2, vessel.Id));
                }
            });

            await RunTest("Vessel EnumerateAsync(tenantId) returns correct count", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleetA = new Fleet("Vessel-FleetA") { TenantId = t1 };
                    Fleet fleetB = new Fleet("Vessel-FleetB") { TenantId = t2 };
                    await db.Fleets.CreateAsync(fleetA);
                    await db.Fleets.CreateAsync(fleetB);

                    Vessel v1 = new Vessel("V1-T1", "https://github.com/t1/r1") { TenantId = t1, FleetId = fleetA.Id };
                    Vessel v2 = new Vessel("V2-T1", "https://github.com/t1/r2") { TenantId = t1, FleetId = fleetA.Id };
                    Vessel v3 = new Vessel("V3-T2", "https://github.com/t2/r1") { TenantId = t2, FleetId = fleetB.Id };
                    await db.Vessels.CreateAsync(v1);
                    await db.Vessels.CreateAsync(v2);
                    await db.Vessels.CreateAsync(v3);

                    List<Vessel> t1Vessels = await db.Vessels.EnumerateAsync(t1);
                    AssertEqual(2, t1Vessels.Count);
                    foreach (Vessel v in t1Vessels)
                    {
                        AssertEqual(t1, v.TenantId, "All enumerated vessels should belong to t1");
                    }

                    List<Vessel> t2Vessels = await db.Vessels.EnumerateAsync(t2);
                    AssertEqual(1, t2Vessels.Count);
                }
            });

            // ── Mission ────────────────────────────────────────────────────

            await RunTest("Mission ReadAsync(tenantId, id) returns null for wrong tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    // Seed prerequisite entities
                    Fleet fleet = new Fleet("Mission-Fleet") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Mission-Vessel", "https://github.com/test/mission") { TenantId = t1, FleetId = fleet.Id };
                    await db.Vessels.CreateAsync(vessel);

                    Voyage voyage = new Voyage("Mission-Voyage") { TenantId = t1 };
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("Mission-Read-Wrong", "desc");
                    mission.TenantId = t1;
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    AssertNotNull(await db.Missions.ReadAsync(t1, mission.Id));
                    AssertNull(await db.Missions.ReadAsync(t2, mission.Id));
                }
            });

            await RunTest("Mission EnumerateAsync(tenantId) returns correct count", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    // Seed prerequisites for t1
                    Fleet fleetA = new Fleet("Mission-FleetA") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleetA);
                    Vessel vesselA = new Vessel("Mission-VesselA", "https://github.com/t1/m") { TenantId = t1, FleetId = fleetA.Id };
                    await db.Vessels.CreateAsync(vesselA);
                    Voyage voyageA = new Voyage("Mission-VoyageA") { TenantId = t1 };
                    await db.Voyages.CreateAsync(voyageA);

                    // Seed prerequisites for t2
                    Fleet fleetB = new Fleet("Mission-FleetB") { TenantId = t2 };
                    await db.Fleets.CreateAsync(fleetB);
                    Vessel vesselB = new Vessel("Mission-VesselB", "https://github.com/t2/m") { TenantId = t2, FleetId = fleetB.Id };
                    await db.Vessels.CreateAsync(vesselB);
                    Voyage voyageB = new Voyage("Mission-VoyageB") { TenantId = t2 };
                    await db.Voyages.CreateAsync(voyageB);

                    Mission m1 = new Mission("M1-T1") { TenantId = t1, VesselId = vesselA.Id, VoyageId = voyageA.Id };
                    Mission m2 = new Mission("M2-T1") { TenantId = t1, VesselId = vesselA.Id, VoyageId = voyageA.Id };
                    Mission m3 = new Mission("M3-T2") { TenantId = t2, VesselId = vesselB.Id, VoyageId = voyageB.Id };
                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);

                    List<Mission> t1Missions = await db.Missions.EnumerateAsync(t1);
                    AssertEqual(2, t1Missions.Count);
                    foreach (Mission m in t1Missions)
                    {
                        AssertEqual(t1, m.TenantId, "All enumerated missions should belong to t1");
                    }

                    List<Mission> t2Missions = await db.Missions.EnumerateAsync(t2);
                    AssertEqual(1, t2Missions.Count);
                }
            });

            // ── Signal ─────────────────────────────────────────────────────

            await RunTest("Signal ReadAsync(tenantId, id) returns null for wrong tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Signal signal = new Signal(SignalTypeEnum.Assignment, "{\"data\":1}");
                    signal.TenantId = t1;
                    await db.Signals.CreateAsync(signal);

                    AssertNotNull(await db.Signals.ReadAsync(t1, signal.Id));
                    AssertNull(await db.Signals.ReadAsync(t2, signal.Id));
                }
            });

            await RunTest("Signal EnumerateAsync(tenantId) returns correct count", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Signal s1 = new Signal(SignalTypeEnum.Nudge) { TenantId = t1 };
                    Signal s2 = new Signal(SignalTypeEnum.Heartbeat) { TenantId = t1 };
                    Signal s3 = new Signal(SignalTypeEnum.Error) { TenantId = t2 };
                    await db.Signals.CreateAsync(s1);
                    await db.Signals.CreateAsync(s2);
                    await db.Signals.CreateAsync(s3);

                    List<Signal> t1Signals = await db.Signals.EnumerateAsync(t1);
                    AssertEqual(2, t1Signals.Count);
                    foreach (Signal s in t1Signals)
                    {
                        AssertEqual(t1, s.TenantId, "All enumerated signals should belong to t1");
                    }

                    List<Signal> t2Signals = await db.Signals.EnumerateAsync(t2);
                    AssertEqual(1, t2Signals.Count);
                }
            });
        }

        private async Task<(string tenantA, string tenantB)> CreateTwoTenantsAsync(SqliteDatabaseDriver db)
        {
            TenantMetadata tA = new TenantMetadata("TenantA " + Guid.NewGuid().ToString("N").Substring(0, 6));
            TenantMetadata tB = new TenantMetadata("TenantB " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(tA);
            await db.Tenants.CreateAsync(tB);
            return (tA.Id, tB.Id);
        }
    }
}
