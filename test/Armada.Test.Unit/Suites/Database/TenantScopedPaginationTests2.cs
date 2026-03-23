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
    /// Comprehensive tenant-scoped paginated enumeration tests for Signal, Event, Voyage, and Dock entities.
    /// Each test uses a fresh database. Follows the TestSuite pattern from EnumerationTests.
    /// </summary>
    public class TenantScopedPaginationTests2 : TestSuite
    {
        public override string Name => "Tenant-Scoped Pagination 2";

        #region Private-Methods

        private static DateTime BaseTime => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private async Task<(string t1, string t2)> CreateTwoTenantsAsync(SqliteDatabaseDriver db)
        {
            TenantMetadata tA = new TenantMetadata("TenantA " + Guid.NewGuid().ToString("N").Substring(0, 6));
            TenantMetadata tB = new TenantMetadata("TenantB " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(tA);
            await db.Tenants.CreateAsync(tB);
            return (tA.Id, tB.Id);
        }

        #endregion

        protected override async Task RunTestsAsync()
        {
            // ── Signal Tenant-Scoped Paginated Enumeration ──────────────────

            await RunTest("Signal tenant-scoped enumerate page 1 returns correct count and totals", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 4; i++)
                    {
                        Signal s = new Signal(SignalTypeEnum.Nudge, "{\"index\":" + i + "}");
                        s.TenantId = t1;
                        s.ToCaptainId = "capt_target_" + i;
                        s.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Signals.CreateAsync(s);
                    }

                    Signal noise = new Signal(SignalTypeEnum.Heartbeat, "{\"noise\":true}");
                    noise.TenantId = t2;
                    noise.ToCaptainId = "capt_noise";
                    noise.CreatedUtc = BaseTime.AddMinutes(10);
                    await db.Signals.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Signal> page1 = await db.Signals.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(4, (int)page1.TotalRecords);
                    AssertEqual(2, page1.TotalPages);
                }
            });

            await RunTest("Signal tenant-scoped enumerate page 2 returns remaining items", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 4; i++)
                    {
                        Signal s = new Signal(SignalTypeEnum.Nudge, "{\"index\":" + i + "}");
                        s.TenantId = t1;
                        s.ToCaptainId = "capt_target_" + i;
                        s.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Signals.CreateAsync(s);
                    }

                    Signal noise = new Signal(SignalTypeEnum.Heartbeat);
                    noise.TenantId = t2;
                    await db.Signals.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 2;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Signal> page2 = await db.Signals.EnumerateAsync(t1, query);

                    AssertEqual(2, page2.Objects.Count);
                }
            });

            await RunTest("Signal tenant-scoped enumerate beyond range returns empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 4; i++)
                    {
                        Signal s = new Signal(SignalTypeEnum.Nudge, "{\"index\":" + i + "}");
                        s.TenantId = t1;
                        s.ToCaptainId = "capt_target_" + i;
                        s.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Signals.CreateAsync(s);
                    }

                    Signal noise = new Signal(SignalTypeEnum.Heartbeat);
                    noise.TenantId = t2;
                    await db.Signals.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 999;
                    EnumerationResult<Signal> beyond = await db.Signals.EnumerateAsync(t1, query);

                    AssertEqual(0, beyond.Objects.Count);
                    AssertEqual(4, (int)beyond.TotalRecords);
                    AssertEqual(2, beyond.TotalPages);
                }
            });

            await RunTest("Signal tenant-scoped enumerate validates full properties on read-back", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Signal original = new Signal(SignalTypeEnum.Assignment, "{\"task\":\"validate\"}");
                    original.TenantId = t1;
                    original.ToCaptainId = "capt_validate";
                    original.CreatedUtc = BaseTime;
                    await db.Signals.CreateAsync(original);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<Signal> result = await db.Signals.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    Signal readBack = result.Objects[0];
                    AssertEqual(original.Id, readBack.Id);
                    AssertEqual(t1, readBack.TenantId);
                    AssertEqual(SignalTypeEnum.Assignment, readBack.Type);
                    AssertEqual("{\"task\":\"validate\"}", readBack.Payload);
                    AssertEqual("capt_validate", readBack.ToCaptainId);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                }
            });

            // ── Event Tenant-Scoped Paginated Enumeration ───────────────────

            await RunTest("Event tenant-scoped enumerate page 1 returns correct count and totals", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 4; i++)
                    {
                        ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created " + i);
                        evt.TenantId = t1;
                        evt.EntityType = "mission";
                        evt.EntityId = "msn_entity_" + i;
                        evt.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Events.CreateAsync(evt);
                    }

                    ArmadaEvent noise = new ArmadaEvent("captain.stalled", "Captain stalled");
                    noise.TenantId = t2;
                    noise.EntityType = "captain";
                    noise.EntityId = "capt_noise";
                    noise.CreatedUtc = BaseTime.AddMinutes(10);
                    await db.Events.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<ArmadaEvent> page1 = await db.Events.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(4, (int)page1.TotalRecords);
                    AssertEqual(2, page1.TotalPages);
                }
            });

            await RunTest("Event tenant-scoped enumerate page 2 returns remaining items", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 4; i++)
                    {
                        ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created " + i);
                        evt.TenantId = t1;
                        evt.EntityType = "mission";
                        evt.EntityId = "msn_entity_" + i;
                        evt.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Events.CreateAsync(evt);
                    }

                    ArmadaEvent noise = new ArmadaEvent("captain.stalled", "Captain stalled");
                    noise.TenantId = t2;
                    await db.Events.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 2;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<ArmadaEvent> page2 = await db.Events.EnumerateAsync(t1, query);

                    AssertEqual(2, page2.Objects.Count);
                }
            });

            await RunTest("Event tenant-scoped enumerate beyond range returns empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 4; i++)
                    {
                        ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created " + i);
                        evt.TenantId = t1;
                        evt.EntityType = "mission";
                        evt.EntityId = "msn_entity_" + i;
                        evt.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Events.CreateAsync(evt);
                    }

                    ArmadaEvent noise = new ArmadaEvent("captain.stalled", "Captain stalled");
                    noise.TenantId = t2;
                    await db.Events.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 999;
                    EnumerationResult<ArmadaEvent> beyond = await db.Events.EnumerateAsync(t1, query);

                    AssertEqual(0, beyond.Objects.Count);
                    AssertEqual(4, (int)beyond.TotalRecords);
                    AssertEqual(2, beyond.TotalPages);
                }
            });

            await RunTest("Event tenant-scoped enumerate validates full properties on read-back", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    ArmadaEvent original = new ArmadaEvent("voyage.completed", "Voyage completed successfully");
                    original.TenantId = t1;
                    original.EntityType = "voyage";
                    original.EntityId = "vyg_validate_01";
                    original.CreatedUtc = BaseTime;
                    await db.Events.CreateAsync(original);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    ArmadaEvent readBack = result.Objects[0];
                    AssertEqual(original.Id, readBack.Id);
                    AssertEqual(t1, readBack.TenantId);
                    AssertEqual("voyage.completed", readBack.EventType);
                    AssertEqual("voyage", readBack.EntityType);
                    AssertEqual("Voyage completed successfully", readBack.Message);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                }
            });

            // ── Voyage Tenant-Scoped Paginated Enumeration ──────────────────

            await RunTest("Voyage tenant-scoped enumerate page 1 returns correct count and totals", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 3; i++)
                    {
                        Voyage v = new Voyage("Voyage-T1-" + i, "Description " + i);
                        v.TenantId = t1;
                        v.Status = VoyageStatusEnum.Open;
                        v.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Voyages.CreateAsync(v);
                    }

                    Voyage noise = new Voyage("Voyage-T2-Noise", "Noise description");
                    noise.TenantId = t2;
                    noise.CreatedUtc = BaseTime.AddMinutes(10);
                    await db.Voyages.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Voyage> page1 = await db.Voyages.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(3, (int)page1.TotalRecords);
                    AssertEqual(2, page1.TotalPages);
                }
            });

            await RunTest("Voyage tenant-scoped enumerate page 2 returns remaining items", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 3; i++)
                    {
                        Voyage v = new Voyage("Voyage-T1-" + i, "Description " + i);
                        v.TenantId = t1;
                        v.Status = VoyageStatusEnum.Open;
                        v.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Voyages.CreateAsync(v);
                    }

                    Voyage noise = new Voyage("Voyage-T2-Noise");
                    noise.TenantId = t2;
                    await db.Voyages.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 2;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Voyage> page2 = await db.Voyages.EnumerateAsync(t1, query);

                    AssertEqual(1, page2.Objects.Count);
                }
            });

            await RunTest("Voyage tenant-scoped enumerate validates full properties on read-back", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Voyage original = new Voyage("Voyage-Validate", "Validate description");
                    original.TenantId = t1;
                    original.Status = VoyageStatusEnum.Open;
                    original.CreatedUtc = BaseTime;
                    await db.Voyages.CreateAsync(original);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<Voyage> result = await db.Voyages.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    Voyage readBack = result.Objects[0];
                    AssertEqual(original.Id, readBack.Id);
                    AssertEqual(t1, readBack.TenantId);
                    AssertEqual("Voyage-Validate", readBack.Title);
                    AssertEqual("Validate description", readBack.Description);
                    AssertEqual(VoyageStatusEnum.Open, readBack.Status);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                }
            });

            // ── Dock Tenant-Scoped Paginated Enumeration ────────────────────

            await RunTest("Dock tenant-scoped enumerate page 1 returns correct count and totals", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    // Create fleet and vessel prerequisites for t1
                    Fleet fleet1 = new Fleet("Dock-Fleet-T1") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet1);
                    Vessel vessel1 = new Vessel("Dock-Vessel-T1", "https://github.com/test/dock-t1") { TenantId = t1, FleetId = fleet1.Id };
                    await db.Vessels.CreateAsync(vessel1);

                    // Create fleet and vessel prerequisites for t2
                    Fleet fleet2 = new Fleet("Dock-Fleet-T2") { TenantId = t2 };
                    await db.Fleets.CreateAsync(fleet2);
                    Vessel vessel2 = new Vessel("Dock-Vessel-T2", "https://github.com/test/dock-t2") { TenantId = t2, FleetId = fleet2.Id };
                    await db.Vessels.CreateAsync(vessel2);

                    for (int i = 0; i < 3; i++)
                    {
                        Dock d = new Dock(vessel1.Id);
                        d.TenantId = t1;
                        d.WorktreePath = "/tmp/worktree/t1_" + i;
                        d.Active = true;
                        d.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Docks.CreateAsync(d);
                    }

                    Dock noise = new Dock(vessel2.Id);
                    noise.TenantId = t2;
                    noise.WorktreePath = "/tmp/worktree/t2_noise";
                    noise.Active = true;
                    noise.CreatedUtc = BaseTime.AddMinutes(10);
                    await db.Docks.CreateAsync(noise);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Dock> page1 = await db.Docks.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(3, (int)page1.TotalRecords);
                    AssertEqual(2, page1.TotalPages);
                }
            });

            await RunTest("Dock tenant-scoped enumerate page 2 returns remaining items", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet1 = new Fleet("Dock-Fleet-T1") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet1);
                    Vessel vessel1 = new Vessel("Dock-Vessel-T1", "https://github.com/test/dock-t1") { TenantId = t1, FleetId = fleet1.Id };
                    await db.Vessels.CreateAsync(vessel1);

                    for (int i = 0; i < 3; i++)
                    {
                        Dock d = new Dock(vessel1.Id);
                        d.TenantId = t1;
                        d.WorktreePath = "/tmp/worktree/t1_" + i;
                        d.Active = true;
                        d.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Docks.CreateAsync(d);
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 2;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Dock> page2 = await db.Docks.EnumerateAsync(t1, query);

                    AssertEqual(1, page2.Objects.Count);
                }
            });

            await RunTest("Dock tenant-scoped enumerate beyond range returns empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet1 = new Fleet("Dock-Fleet-T1") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet1);
                    Vessel vessel1 = new Vessel("Dock-Vessel-T1", "https://github.com/test/dock-t1") { TenantId = t1, FleetId = fleet1.Id };
                    await db.Vessels.CreateAsync(vessel1);

                    for (int i = 0; i < 3; i++)
                    {
                        Dock d = new Dock(vessel1.Id);
                        d.TenantId = t1;
                        d.WorktreePath = "/tmp/worktree/t1_" + i;
                        d.Active = true;
                        d.CreatedUtc = BaseTime.AddMinutes(i);
                        await db.Docks.CreateAsync(d);
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 999;
                    EnumerationResult<Dock> beyond = await db.Docks.EnumerateAsync(t1, query);

                    AssertEqual(0, beyond.Objects.Count);
                    AssertEqual(3, (int)beyond.TotalRecords);
                    AssertEqual(2, beyond.TotalPages);
                }
            });

            await RunTest("Dock tenant-scoped enumerate validates full properties on read-back", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet1 = new Fleet("Dock-Fleet-Validate") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet1);
                    Vessel vessel1 = new Vessel("Dock-Vessel-Validate", "https://github.com/test/dock-validate") { TenantId = t1, FleetId = fleet1.Id };
                    await db.Vessels.CreateAsync(vessel1);

                    Dock original = new Dock(vessel1.Id);
                    original.TenantId = t1;
                    original.WorktreePath = "/tmp/worktree/validate";
                    original.Active = true;
                    original.CreatedUtc = BaseTime;
                    await db.Docks.CreateAsync(original);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<Dock> result = await db.Docks.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    Dock readBack = result.Objects[0];
                    AssertEqual(original.Id, readBack.Id);
                    AssertEqual(t1, readBack.TenantId);
                    AssertEqual(vessel1.Id, readBack.VesselId);
                    AssertEqual("/tmp/worktree/validate", readBack.WorktreePath);
                    AssertEqual(true, readBack.Active);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                }
            });
        }
    }
}
