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
    /// Comprehensive tenant-scoped paginated enumeration tests for Fleet, Vessel, and Mission.
    /// Each test seeds its own data with a fresh database.
    /// </summary>
    public class TenantScopedPaginationTests : TestSuite
    {
        public override string Name => "Tenant-Scoped Pagination";

        private static DateTime BaseTime => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        protected override async Task RunTestsAsync()
        {
            // ══════════════════════════════════════════════════════════════
            // Fleet tenant-scoped paginated enumeration
            // ══════════════════════════════════════════════════════════════

            await RunTest("Fleet tenant pagination page 1 returns correct counts", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 5; i++)
                    {
                        Fleet f = new Fleet("Fleet-T1-" + i) { TenantId = t1, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Fleet f = new Fleet("Fleet-T2-" + i) { TenantId = t2, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 1 };
                    EnumerationResult<Fleet> page1 = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(5, (int)page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);
                }
            });

            await RunTest("Fleet tenant pagination page 2 returns 2 objects", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 5; i++)
                    {
                        Fleet f = new Fleet("Fleet-T1-" + i) { TenantId = t1, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Fleet f = new Fleet("Fleet-T2-" + i) { TenantId = t2, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 2 };
                    EnumerationResult<Fleet> page2 = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(2, page2.Objects.Count);
                    AssertEqual(5, (int)page2.TotalRecords);
                    AssertEqual(3, page2.TotalPages);
                }
            });

            await RunTest("Fleet tenant pagination page 3 returns 1 object", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 5; i++)
                    {
                        Fleet f = new Fleet("Fleet-T1-" + i) { TenantId = t1, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Fleet f = new Fleet("Fleet-T2-" + i) { TenantId = t2, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 3 };
                    EnumerationResult<Fleet> page3 = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(1, page3.Objects.Count);
                    AssertEqual(5, (int)page3.TotalRecords);
                    AssertEqual(3, page3.TotalPages);
                }
            });

            await RunTest("Fleet tenant pagination beyond range returns empty with correct totals", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 5; i++)
                    {
                        Fleet f = new Fleet("Fleet-T1-" + i) { TenantId = t1, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Fleet f = new Fleet("Fleet-T2-" + i) { TenantId = t2, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 10 };
                    EnumerationResult<Fleet> beyondRange = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(0, beyondRange.Objects.Count);
                    AssertEqual(5, (int)beyondRange.TotalRecords);
                }
            });

            await RunTest("Fleet tenant pagination t2 returns correct total records", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 5; i++)
                    {
                        Fleet f = new Fleet("Fleet-T1-" + i) { TenantId = t1, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Fleet f = new Fleet("Fleet-T2-" + i) { TenantId = t2, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 10, PageNumber = 1 };
                    EnumerationResult<Fleet> t2Result = await db.Fleets.EnumerateAsync(t2, query);

                    AssertEqual(2, t2Result.Objects.Count);
                    AssertEqual(2, (int)t2Result.TotalRecords);
                }
            });

            await RunTest("Fleet tenant pagination CreatedAfter filter through tenant path", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    for (int i = 0; i < 5; i++)
                    {
                        Fleet f = new Fleet("Fleet-T1-" + i) { TenantId = t1, CreatedUtc = BaseTime.AddMinutes(i) };
                        await db.Fleets.CreateAsync(f);
                    }
                    // Also seed t2 noise
                    Fleet noise = new Fleet("Fleet-T2-Noise") { TenantId = t2, CreatedUtc = BaseTime.AddMinutes(10) };
                    await db.Fleets.CreateAsync(noise);

                    // Filter to fleets created after minute 2 (should get minutes 3 and 4 = 2 fleets)
                    EnumerationQuery query = new EnumerationQuery
                    {
                        PageSize = 10,
                        PageNumber = 1,
                        CreatedAfter = BaseTime.AddMinutes(2)
                    };
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(2, result.Objects.Count);
                    AssertEqual(2, (int)result.TotalRecords);
                    foreach (Fleet f in result.Objects)
                    {
                        AssertEqual(t1, f.TenantId, "CreatedAfter-filtered fleets should belong to t1");
                    }
                }
            });

            await RunTest("Fleet tenant pagination order ascending vs descending", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet oldest = new Fleet("Fleet-Oldest") { TenantId = t1, CreatedUtc = BaseTime };
                    Fleet middle = new Fleet("Fleet-Middle") { TenantId = t1, CreatedUtc = BaseTime.AddHours(1) };
                    Fleet newest = new Fleet("Fleet-Newest") { TenantId = t1, CreatedUtc = BaseTime.AddHours(2) };
                    await db.Fleets.CreateAsync(oldest);
                    await db.Fleets.CreateAsync(middle);
                    await db.Fleets.CreateAsync(newest);

                    // Noise in t2
                    Fleet t2Fleet = new Fleet("Fleet-T2") { TenantId = t2, CreatedUtc = BaseTime.AddHours(3) };
                    await db.Fleets.CreateAsync(t2Fleet);

                    // Ascending
                    EnumerationQuery ascQuery = new EnumerationQuery
                    {
                        PageSize = 10,
                        PageNumber = 1,
                        Order = EnumerationOrderEnum.CreatedAscending
                    };
                    EnumerationResult<Fleet> ascResult = await db.Fleets.EnumerateAsync(t1, ascQuery);

                    AssertEqual(3, ascResult.Objects.Count);
                    AssertEqual("Fleet-Oldest", ascResult.Objects[0].Name);
                    AssertEqual("Fleet-Middle", ascResult.Objects[1].Name);
                    AssertEqual("Fleet-Newest", ascResult.Objects[2].Name);

                    // Descending
                    EnumerationQuery descQuery = new EnumerationQuery
                    {
                        PageSize = 10,
                        PageNumber = 1,
                        Order = EnumerationOrderEnum.CreatedDescending
                    };
                    EnumerationResult<Fleet> descResult = await db.Fleets.EnumerateAsync(t1, descQuery);

                    AssertEqual(3, descResult.Objects.Count);
                    AssertEqual("Fleet-Newest", descResult.Objects[0].Name);
                    AssertEqual("Fleet-Middle", descResult.Objects[1].Name);
                    AssertEqual("Fleet-Oldest", descResult.Objects[2].Name);
                }
            });

            await RunTest("Fleet tenant pagination full property validation", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("PropVal-Fleet");
                    fleet.TenantId = t1;
                    fleet.Description = "Test fleet description";
                    fleet.Active = true;
                    await db.Fleets.CreateAsync(fleet);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 10, PageNumber = 1 };
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    Fleet readBack = result.Objects[0];

                    AssertEqual(fleet.Id, readBack.Id);
                    AssertEqual("PropVal-Fleet", readBack.Name);
                    AssertEqual("Test fleet description", readBack.Description);
                    AssertEqual(true, readBack.Active);
                    AssertEqual(t1, readBack.TenantId);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                    AssertNotEqual(default(DateTime), readBack.LastUpdateUtc, "LastUpdateUtc should not be default");
                }
            });

            // ══════════════════════════════════════════════════════════════
            // Vessel tenant-scoped paginated enumeration
            // ══════════════════════════════════════════════════════════════

            await RunTest("Vessel tenant pagination page 1 returns correct counts", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleetT1 = new Fleet("Vessel-FleetT1") { TenantId = t1 };
                    Fleet fleetT2 = new Fleet("Vessel-FleetT2") { TenantId = t2 };
                    await db.Fleets.CreateAsync(fleetT1);
                    await db.Fleets.CreateAsync(fleetT2);

                    for (int i = 0; i < 4; i++)
                    {
                        Vessel v = new Vessel("Vessel-T1-" + i, "https://github.com/t1/repo-" + i)
                        {
                            TenantId = t1,
                            FleetId = fleetT1.Id,
                            CreatedUtc = BaseTime.AddMinutes(i)
                        };
                        await db.Vessels.CreateAsync(v);
                    }
                    Vessel vT2 = new Vessel("Vessel-T2-0", "https://github.com/t2/repo-0")
                    {
                        TenantId = t2,
                        FleetId = fleetT2.Id,
                        CreatedUtc = BaseTime.AddMinutes(10)
                    };
                    await db.Vessels.CreateAsync(vT2);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 1 };
                    EnumerationResult<Vessel> page1 = await db.Vessels.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(4, (int)page1.TotalRecords);
                    AssertEqual(2, page1.TotalPages);
                }
            });

            await RunTest("Vessel tenant pagination page 2 returns 2 objects", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleetT1 = new Fleet("Vessel-FleetT1") { TenantId = t1 };
                    Fleet fleetT2 = new Fleet("Vessel-FleetT2") { TenantId = t2 };
                    await db.Fleets.CreateAsync(fleetT1);
                    await db.Fleets.CreateAsync(fleetT2);

                    for (int i = 0; i < 4; i++)
                    {
                        Vessel v = new Vessel("Vessel-T1-" + i, "https://github.com/t1/repo-" + i)
                        {
                            TenantId = t1,
                            FleetId = fleetT1.Id,
                            CreatedUtc = BaseTime.AddMinutes(i)
                        };
                        await db.Vessels.CreateAsync(v);
                    }
                    Vessel vT2 = new Vessel("Vessel-T2-0", "https://github.com/t2/repo-0")
                    {
                        TenantId = t2,
                        FleetId = fleetT2.Id
                    };
                    await db.Vessels.CreateAsync(vT2);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 2 };
                    EnumerationResult<Vessel> page2 = await db.Vessels.EnumerateAsync(t1, query);

                    AssertEqual(2, page2.Objects.Count);
                    AssertEqual(4, (int)page2.TotalRecords);
                    AssertEqual(2, page2.TotalPages);
                }
            });

            await RunTest("Vessel tenant pagination beyond range returns empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleetT1 = new Fleet("Vessel-FleetT1") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleetT1);

                    for (int i = 0; i < 4; i++)
                    {
                        Vessel v = new Vessel("Vessel-T1-" + i, "https://github.com/t1/repo-" + i)
                        {
                            TenantId = t1,
                            FleetId = fleetT1.Id,
                            CreatedUtc = BaseTime.AddMinutes(i)
                        };
                        await db.Vessels.CreateAsync(v);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 10 };
                    EnumerationResult<Vessel> beyondRange = await db.Vessels.EnumerateAsync(t1, query);

                    AssertEqual(0, beyondRange.Objects.Count);
                    AssertEqual(4, (int)beyondRange.TotalRecords);
                }
            });

            await RunTest("Vessel tenant pagination full property validation", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Vessel-PropVal-Fleet") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("PropVal-Vessel", "https://github.com/propval/repo");
                    vessel.TenantId = t1;
                    vessel.FleetId = fleet.Id;
                    vessel.DefaultBranch = "main";
                    vessel.Active = true;
                    await db.Vessels.CreateAsync(vessel);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 10, PageNumber = 1 };
                    EnumerationResult<Vessel> result = await db.Vessels.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    Vessel readBack = result.Objects[0];

                    AssertEqual(vessel.Id, readBack.Id);
                    AssertEqual("PropVal-Vessel", readBack.Name);
                    AssertEqual(fleet.Id, readBack.FleetId);
                    AssertEqual(t1, readBack.TenantId);
                    AssertEqual("main", readBack.DefaultBranch);
                    AssertEqual(true, readBack.Active);
                    AssertEqual("https://github.com/propval/repo", readBack.RepoUrl);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                    AssertNotEqual(default(DateTime), readBack.LastUpdateUtc, "LastUpdateUtc should not be default");
                }
            });

            // ══════════════════════════════════════════════════════════════
            // Mission tenant-scoped paginated enumeration
            // ══════════════════════════════════════════════════════════════

            await RunTest("Mission tenant pagination page 1 returns correct counts", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    // Seed prerequisites for t1
                    Fleet fleetT1 = new Fleet("Mission-FleetT1") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleetT1);
                    Vessel vesselT1 = new Vessel("Mission-VesselT1", "https://github.com/t1/mission")
                    {
                        TenantId = t1,
                        FleetId = fleetT1.Id
                    };
                    await db.Vessels.CreateAsync(vesselT1);
                    Voyage voyageT1 = new Voyage("Mission-VoyageT1") { TenantId = t1 };
                    await db.Voyages.CreateAsync(voyageT1);

                    // Seed prerequisites for t2
                    Fleet fleetT2 = new Fleet("Mission-FleetT2") { TenantId = t2 };
                    await db.Fleets.CreateAsync(fleetT2);
                    Vessel vesselT2 = new Vessel("Mission-VesselT2", "https://github.com/t2/mission")
                    {
                        TenantId = t2,
                        FleetId = fleetT2.Id
                    };
                    await db.Vessels.CreateAsync(vesselT2);
                    Voyage voyageT2 = new Voyage("Mission-VoyageT2") { TenantId = t2 };
                    await db.Voyages.CreateAsync(voyageT2);

                    for (int i = 0; i < 3; i++)
                    {
                        Mission m = new Mission("Mission-T1-" + i)
                        {
                            TenantId = t1,
                            VesselId = vesselT1.Id,
                            VoyageId = voyageT1.Id,
                            CreatedUtc = BaseTime.AddMinutes(i)
                        };
                        await db.Missions.CreateAsync(m);
                    }
                    Mission mT2 = new Mission("Mission-T2-0")
                    {
                        TenantId = t2,
                        VesselId = vesselT2.Id,
                        VoyageId = voyageT2.Id,
                        CreatedUtc = BaseTime.AddMinutes(10)
                    };
                    await db.Missions.CreateAsync(mT2);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 1 };
                    EnumerationResult<Mission> page1 = await db.Missions.EnumerateAsync(t1, query);

                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(3, (int)page1.TotalRecords);
                    AssertEqual(2, page1.TotalPages);
                }
            });

            await RunTest("Mission tenant pagination page 2 returns 1 object", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleetT1 = new Fleet("Mission-FleetT1") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleetT1);
                    Vessel vesselT1 = new Vessel("Mission-VesselT1", "https://github.com/t1/mission")
                    {
                        TenantId = t1,
                        FleetId = fleetT1.Id
                    };
                    await db.Vessels.CreateAsync(vesselT1);
                    Voyage voyageT1 = new Voyage("Mission-VoyageT1") { TenantId = t1 };
                    await db.Voyages.CreateAsync(voyageT1);

                    for (int i = 0; i < 3; i++)
                    {
                        Mission m = new Mission("Mission-T1-" + i)
                        {
                            TenantId = t1,
                            VesselId = vesselT1.Id,
                            VoyageId = voyageT1.Id,
                            CreatedUtc = BaseTime.AddMinutes(i)
                        };
                        await db.Missions.CreateAsync(m);
                    }

                    EnumerationQuery query = new EnumerationQuery { PageSize = 2, PageNumber = 2 };
                    EnumerationResult<Mission> page2 = await db.Missions.EnumerateAsync(t1, query);

                    AssertEqual(1, page2.Objects.Count);
                    AssertEqual(3, (int)page2.TotalRecords);
                    AssertEqual(2, page2.TotalPages);
                }
            });

            await RunTest("Mission tenant pagination full property validation", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string t2) = await CreateTwoTenantsAsync(db);

                    Fleet fleet = new Fleet("Mission-PropVal-Fleet") { TenantId = t1 };
                    await db.Fleets.CreateAsync(fleet);
                    Vessel vessel = new Vessel("Mission-PropVal-Vessel", "https://github.com/propval/mission")
                    {
                        TenantId = t1,
                        FleetId = fleet.Id
                    };
                    await db.Vessels.CreateAsync(vessel);
                    Voyage voyage = new Voyage("Mission-PropVal-Voyage") { TenantId = t1 };
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("PropVal-Mission", "Detailed mission description");
                    mission.TenantId = t1;
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.Pending;
                    mission.Priority = 42;
                    await db.Missions.CreateAsync(mission);

                    EnumerationQuery query = new EnumerationQuery { PageSize = 10, PageNumber = 1 };
                    EnumerationResult<Mission> result = await db.Missions.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count);
                    Mission readBack = result.Objects[0];

                    AssertEqual(mission.Id, readBack.Id);
                    AssertEqual("PropVal-Mission", readBack.Title);
                    AssertEqual("Detailed mission description", readBack.Description);
                    AssertEqual(vessel.Id, readBack.VesselId);
                    AssertEqual(voyage.Id, readBack.VoyageId);
                    AssertEqual(t1, readBack.TenantId);
                    AssertEqual(MissionStatusEnum.Pending, readBack.Status);
                    AssertEqual(42, readBack.Priority);
                    AssertNotEqual(default(DateTime), readBack.CreatedUtc, "CreatedUtc should not be default");
                    AssertNotEqual(default(DateTime), readBack.LastUpdateUtc, "LastUpdateUtc should not be default");
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
