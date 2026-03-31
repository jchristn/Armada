namespace Armada.Test.Unit.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Edge case and stress tests for pagination, ordering, filtering, Unicode, nulls, concurrency, and large data sets.
    /// </summary>
    public class EdgeCaseTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Edge Cases";

        #region Private-Methods

        private static DateTime BaseTime => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Fleet CreateFleet(string name, DateTime createdUtc)
        {
            Fleet fleet = new Fleet(name);
            fleet.CreatedUtc = createdUtc;
            return fleet;
        }

        private static Vessel CreateVessel(string name, DateTime createdUtc)
        {
            Vessel vessel = new Vessel(name, "https://github.com/test/" + Guid.NewGuid().ToString("N").Substring(0, 8));
            vessel.CreatedUtc = createdUtc;
            return vessel;
        }

        private static Mission CreateMission(string title, DateTime createdUtc)
        {
            Mission mission = new Mission(title);
            mission.CreatedUtc = createdUtc;
            return mission;
        }

        #endregion

        /// <summary>
        /// Run all edge case tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("EdgeCase_PaginationPageZero", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create 3 fleets
                    for (int i = 0; i < 3; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet " + i, BaseTime.AddHours(i)));
                    }

                    // Page 0 should be clamped to page 1
                    EnumerationQuery queryZero = new EnumerationQuery();
                    queryZero.PageNumber = 0;
                    queryZero.PageSize = 10;
                    AssertEqual(1, queryZero.PageNumber, "PageNumber 0 should clamp to 1");

                    EnumerationResult<Fleet> resultZero = await db.Fleets.EnumerateAsync(queryZero);
                    AssertEqual(3, (int)resultZero.TotalRecords, "TotalRecords for page 0");
                    AssertEqual(3, resultZero.Objects.Count, "Objects count for page 0");

                    // Negative page should also clamp to 1
                    EnumerationQuery queryNeg = new EnumerationQuery();
                    queryNeg.PageNumber = -5;
                    queryNeg.PageSize = 10;
                    AssertEqual(1, queryNeg.PageNumber, "PageNumber -5 should clamp to 1");

                    EnumerationResult<Fleet> resultNeg = await db.Fleets.EnumerateAsync(queryNeg);
                    AssertEqual(3, (int)resultNeg.TotalRecords, "TotalRecords for negative page");
                    AssertEqual(3, resultNeg.Objects.Count, "Objects count for negative page");
                }
            });

            await RunTest("EdgeCase_PaginationBeyondEnd", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create 5 fleets
                    for (int i = 0; i < 5; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet " + i, BaseTime.AddHours(i)));
                    }

                    // Request page 100 with page size 10 — way beyond end
                    EnumerationQuery query = new EnumerationQuery();
                    query.PageNumber = 100;
                    query.PageSize = 10;

                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(0, result.Objects.Count, "Objects should be empty beyond last page");
                    AssertEqual(5, (int)result.TotalRecords, "TotalRecords should still be correct");
                    AssertEqual(1, result.TotalPages, "TotalPages should be 1 for 5 records with pageSize 10");
                }
            });

            await RunTest("EdgeCase_PaginationPageSizeOne", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    int count = 5;
                    for (int i = 0; i < count; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet " + i, BaseTime.AddHours(i)));
                    }

                    // Enumerate page by page with page size 1
                    for (int page = 1; page <= count; page++)
                    {
                        EnumerationQuery query = new EnumerationQuery();
                        query.PageNumber = page;
                        query.PageSize = 1;

                        EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);
                        AssertEqual(1, result.Objects.Count, "Page " + page + " should have exactly 1 record");
                        AssertEqual(count, (int)result.TotalRecords, "TotalRecords on page " + page);
                        AssertEqual(count, result.TotalPages, "TotalPages with pageSize 1");
                    }

                    // Page beyond count should be empty
                    EnumerationQuery queryBeyond = new EnumerationQuery();
                    queryBeyond.PageNumber = count + 1;
                    queryBeyond.PageSize = 1;

                    EnumerationResult<Fleet> resultBeyond = await db.Fleets.EnumerateAsync(queryBeyond);
                    AssertEqual(0, resultBeyond.Objects.Count, "Page beyond count should be empty");
                }
            });

            await RunTest("EdgeCase_OrderAscending", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create fleets with known timestamps in random order
                    await db.Fleets.CreateAsync(CreateFleet("Third", BaseTime.AddHours(3)));
                    await db.Fleets.CreateAsync(CreateFleet("First", BaseTime.AddHours(1)));
                    await db.Fleets.CreateAsync(CreateFleet("Second", BaseTime.AddHours(2)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    query.PageSize = 100;

                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(3, result.Objects.Count, "Should have 3 fleets");

                    // Verify ascending order by CreatedUtc
                    for (int i = 1; i < result.Objects.Count; i++)
                    {
                        AssertTrue(
                            result.Objects[i].CreatedUtc >= result.Objects[i - 1].CreatedUtc,
                            "Fleet at index " + i + " should have CreatedUtc >= previous");
                    }

                    AssertEqual("First", result.Objects[0].Name, "First in ascending order");
                    AssertEqual("Second", result.Objects[1].Name, "Second in ascending order");
                    AssertEqual("Third", result.Objects[2].Name, "Third in ascending order");
                }
            });

            await RunTest("EdgeCase_OrderDescending", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create fleets with known timestamps in random order
                    await db.Fleets.CreateAsync(CreateFleet("Second", BaseTime.AddHours(2)));
                    await db.Fleets.CreateAsync(CreateFleet("First", BaseTime.AddHours(1)));
                    await db.Fleets.CreateAsync(CreateFleet("Third", BaseTime.AddHours(3)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Order = EnumerationOrderEnum.CreatedDescending;
                    query.PageSize = 100;

                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(3, result.Objects.Count, "Should have 3 fleets");

                    // Verify descending order by CreatedUtc
                    for (int i = 1; i < result.Objects.Count; i++)
                    {
                        AssertTrue(
                            result.Objects[i].CreatedUtc <= result.Objects[i - 1].CreatedUtc,
                            "Fleet at index " + i + " should have CreatedUtc <= previous");
                    }

                    AssertEqual("Third", result.Objects[0].Name, "First in descending order");
                    AssertEqual("Second", result.Objects[1].Name, "Second in descending order");
                    AssertEqual("First", result.Objects[2].Name, "Third in descending order");
                }
            });

            await RunTest("EdgeCase_DateRangeFilter", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create fleets at hours 1, 2, 3, 4, 5
                    for (int i = 1; i <= 5; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet " + i, BaseTime.AddHours(i)));
                    }

                    // Filter: after hour 1, before hour 5 (exclusive) — should get hours 2, 3, 4
                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(5);
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    query.PageSize = 100;

                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(3, (int)result.TotalRecords, "Should match 3 records in date range");
                    AssertEqual(3, result.Objects.Count, "Should return 3 objects");
                    AssertEqual("Fleet 2", result.Objects[0].Name, "First in range");
                    AssertEqual("Fleet 4", result.Objects[2].Name, "Last in range");
                }
            });

            await RunTest("EdgeCase_EmptyResultSet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create some fleets
                    for (int i = 0; i < 3; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet " + i, BaseTime.AddHours(i)));
                    }

                    // Filter with a date range that matches nothing (far future)
                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddYears(100);
                    query.PageSize = 100;

                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(0, (int)result.TotalRecords, "TotalRecords should be 0");
                    AssertEqual(0, result.Objects.Count, "Objects should be empty");
                    AssertEqual(0, result.TotalPages, "TotalPages should be 0");
                }
            });

            await RunTest("EdgeCase_UnicodeText", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Emoji name
                    string emojiName = "\U0001F680 Rocket Fleet \U0001F31F";
                    Fleet emojiFleet = new Fleet(emojiName);
                    await db.Fleets.CreateAsync(emojiFleet);
                    Fleet? readEmoji = await db.Fleets.ReadAsync(emojiFleet.Id);
                    AssertNotNull(readEmoji, "Emoji fleet should be readable");
                    AssertEqual(emojiName, readEmoji!.Name, "Emoji fleet name round-trip");

                    // CJK characters
                    string cjkName = "\u8266\u968A\u7BA1\u7406\u30B7\u30B9\u30C6\u30E0";
                    Vessel cjkVessel = new Vessel(cjkName, "https://github.com/test/cjk");
                    cjkVessel.CreatedUtc = BaseTime;
                    await db.Vessels.CreateAsync(cjkVessel);
                    Vessel? readCjk = await db.Vessels.ReadAsync(cjkVessel.Id);
                    AssertNotNull(readCjk, "CJK vessel should be readable");
                    AssertEqual(cjkName, readCjk!.Name, "CJK vessel name round-trip");

                    // Diacritics
                    string diacriticName = "Mis\u00E0 \u00E9l\u00E8ve R\u00E9sum\u00E9";
                    Mission diacriticMission = new Mission(diacriticName);
                    diacriticMission.CreatedUtc = BaseTime;
                    await db.Missions.CreateAsync(diacriticMission);
                    Mission? readDiacritic = await db.Missions.ReadAsync(diacriticMission.Id);
                    AssertNotNull(readDiacritic, "Diacritic mission should be readable");
                    AssertEqual(diacriticName, readDiacritic!.Title, "Diacritic mission title round-trip");

                    // Mixed Unicode in description
                    string mixedDesc = "\U0001F4DD \u8AAC\u660E: caf\u00E9 \u2603 snowman";
                    Fleet mixedFleet = new Fleet("Mixed Unicode");
                    mixedFleet.Description = mixedDesc;
                    await db.Fleets.CreateAsync(mixedFleet);
                    Fleet? readMixed = await db.Fleets.ReadAsync(mixedFleet.Id);
                    AssertNotNull(readMixed, "Mixed unicode fleet should be readable");
                    AssertEqual(mixedDesc, readMixed!.Description, "Mixed unicode description round-trip");
                }
            });

            await RunTest("EdgeCase_NullOptionalFields", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Fleet with null description
                    Fleet fleet = new Fleet("Null Test Fleet");
                    fleet.Description = null;
                    await db.Fleets.CreateAsync(fleet);
                    Fleet? readFleet = await db.Fleets.ReadAsync(fleet.Id);
                    AssertNotNull(readFleet, "Fleet should be readable");
                    AssertNull(readFleet!.Description, "Fleet description should be null, not empty string");

                    // Vessel with null optional fields
                    Vessel vessel = new Vessel("Null Test Vessel", "https://github.com/test/null");
                    vessel.FleetId = null;
                    vessel.LocalPath = null;
                    vessel.WorkingDirectory = null;
                    vessel.ProjectContext = null;
                    vessel.StyleGuide = null;
                    await db.Vessels.CreateAsync(vessel);
                    Vessel? readVessel = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(readVessel, "Vessel should be readable");
                    AssertNull(readVessel!.FleetId, "Vessel FleetId should be null");
                    AssertNull(readVessel.LocalPath, "Vessel LocalPath should be null");
                    AssertNull(readVessel.WorkingDirectory, "Vessel WorkingDirectory should be null");
                    AssertNull(readVessel.ProjectContext, "Vessel ProjectContext should be null");
                    AssertNull(readVessel.StyleGuide, "Vessel StyleGuide should be null");

                    // Mission with null optional fields
                    Mission mission = new Mission("Null Test Mission");
                    mission.VoyageId = null;
                    mission.VesselId = null;
                    mission.CaptainId = null;
                    mission.Description = null;
                    mission.ParentMissionId = null;
                    mission.BranchName = null;
                    mission.DockId = null;
                    mission.PrUrl = null;
                    mission.CommitHash = null;
                    mission.DiffSnapshot = null;
                    await db.Missions.CreateAsync(mission);
                    Mission? readMission = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(readMission, "Mission should be readable");
                    AssertNull(readMission!.VoyageId, "Mission VoyageId should be null");
                    AssertNull(readMission.VesselId, "Mission VesselId should be null");
                    AssertNull(readMission.CaptainId, "Mission CaptainId should be null");
                    AssertNull(readMission.Description, "Mission Description should be null");
                    AssertNull(readMission.ParentMissionId, "Mission ParentMissionId should be null");
                    AssertNull(readMission.BranchName, "Mission BranchName should be null");
                    AssertNull(readMission.DockId, "Mission DockId should be null");
                    AssertNull(readMission.PrUrl, "Mission PrUrl should be null");
                    AssertNull(readMission.CommitHash, "Mission CommitHash should be null");
                    AssertNull(readMission.DiffSnapshot, "Mission DiffSnapshot should be null");

                    // Captain with null optional fields
                    Captain captain = new Captain("Null Test Captain");
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    captain.LastHeartbeatUtc = null;
                    await db.Captains.CreateAsync(captain);
                    Captain? readCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(readCaptain, "Captain should be readable");
                    AssertNull(readCaptain!.CurrentMissionId, "Captain CurrentMissionId should be null");
                    AssertNull(readCaptain.CurrentDockId, "Captain CurrentDockId should be null");
                    AssertNull(readCaptain.ProcessId, "Captain ProcessId should be null");
                    AssertNull(readCaptain.LastHeartbeatUtc, "Captain LastHeartbeatUtc should be null");
                }
            });

            await RunTest("EdgeCase_ConcurrentTryClaim", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create one Idle captain
                    Captain captain = new Captain("Contested Captain");
                    captain.State = CaptainStateEnum.Idle;
                    await db.Captains.CreateAsync(captain);

                    // Create a mission and a dock for claiming
                    Mission mission = new Mission("Contested Mission");
                    await db.Missions.CreateAsync(mission);

                    Vessel vessel = new Vessel("Claim Vessel", "https://github.com/test/claim");
                    await db.Vessels.CreateAsync(vessel);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = "/tmp/worktree/claim";
                    await db.Docks.CreateAsync(dock);

                    // Launch 10 parallel TryClaim calls
                    int parallelCount = 10;
                    Task<bool>[] claimTasks = new Task<bool>[parallelCount];
                    for (int i = 0; i < parallelCount; i++)
                    {
                        claimTasks[i] = db.Captains.TryClaimAsync(captain.Id, mission.Id, dock.Id);
                    }

                    bool[] results = await Task.WhenAll(claimTasks);

                    int successCount = results.Count(r => r);
                    int failCount = results.Count(r => !r);

                    AssertEqual(1, successCount, "Exactly 1 TryClaim should succeed");
                    AssertEqual(parallelCount - 1, failCount, "Remaining TryClaims should fail");

                    // Verify captain is now Working
                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(CaptainStateEnum.Working, updatedCaptain!.State, "Captain should be Working after claim");
                    AssertEqual(mission.Id, updatedCaptain.CurrentMissionId, "Captain should have the mission assigned");
                    AssertEqual(dock.Id, updatedCaptain.CurrentDockId, "Captain should have the dock assigned");
                }
            });

            await RunTest("EdgeCase_LargeEnumeration", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // Create 100 fleets
                    int totalRecords = 100;
                    for (int i = 0; i < totalRecords; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet " + i.ToString("D3"), BaseTime.AddMinutes(i)));
                    }

                    // Enumerate all with a single large page
                    EnumerationQuery querySingle = new EnumerationQuery();
                    querySingle.PageSize = 1000;

                    EnumerationResult<Fleet> resultSingle = await db.Fleets.EnumerateAsync(querySingle);
                    AssertEqual(totalRecords, (int)resultSingle.TotalRecords, "TotalRecords should be 100");
                    AssertEqual(totalRecords, resultSingle.Objects.Count, "All 100 should be in one page");

                    // Enumerate in pages of 10 and collect all
                    int pageSize = 10;
                    List<string> allIds = new List<string>();

                    for (int page = 1; page <= 10; page++)
                    {
                        EnumerationQuery queryPaged = new EnumerationQuery();
                        queryPaged.PageNumber = page;
                        queryPaged.PageSize = pageSize;
                        queryPaged.Order = EnumerationOrderEnum.CreatedAscending;

                        EnumerationResult<Fleet> resultPaged = await db.Fleets.EnumerateAsync(queryPaged);
                        AssertEqual(totalRecords, (int)resultPaged.TotalRecords, "TotalRecords on page " + page);
                        AssertEqual(10, resultPaged.TotalPages, "TotalPages for 100 records with pageSize 10");
                        AssertEqual(pageSize, resultPaged.Objects.Count, "Page " + page + " should have " + pageSize + " records");

                        foreach (Fleet fleet in resultPaged.Objects)
                        {
                            allIds.Add(fleet.Id);
                        }
                    }

                    // Verify all IDs are unique
                    HashSet<string> uniqueIds = new HashSet<string>(allIds);
                    AssertEqual(totalRecords, uniqueIds.Count, "All 100 IDs should be unique across pages");
                    AssertEqual(totalRecords, allIds.Count, "Should have collected exactly 100 IDs");
                }
            });
        }
    }
}
