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

    public class EnumerationTests : TestSuite
    {
        public override string Name => "Enumeration";

        #region Private-Methods

        private static DateTime BaseTime => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Fleet CreateFleet(string name, DateTime createdUtc)
        {
            Fleet fleet = new Fleet(name);
            fleet.CreatedUtc = createdUtc;
            return fleet;
        }

        private static Vessel CreateVessel(string name, string? fleetId, DateTime createdUtc)
        {
            Vessel vessel = new Vessel(name, "https://github.com/test/" + name.ToLowerInvariant().Replace(" ", "-"));
            vessel.FleetId = fleetId;
            vessel.CreatedUtc = createdUtc;
            return vessel;
        }

        private static Captain CreateCaptain(string name, CaptainStateEnum state, DateTime createdUtc)
        {
            Captain captain = new Captain(name);
            captain.State = state;
            captain.CreatedUtc = createdUtc;
            return captain;
        }

        private static Mission CreateMission(string title, MissionStatusEnum status, string? vesselId, string? captainId, string? voyageId, DateTime createdUtc)
        {
            Mission mission = new Mission(title);
            mission.Status = status;
            mission.VesselId = vesselId;
            mission.CaptainId = captainId;
            mission.VoyageId = voyageId;
            mission.CreatedUtc = createdUtc;
            return mission;
        }

        private static Voyage CreateVoyage(string title, VoyageStatusEnum status, DateTime createdUtc)
        {
            Voyage voyage = new Voyage(title);
            voyage.Status = status;
            voyage.CreatedUtc = createdUtc;
            return voyage;
        }

        private static Signal CreateSignal(SignalTypeEnum type, string? toCaptainId, bool read, DateTime createdUtc)
        {
            Signal signal = new Signal(type, "{\"test\":true}");
            signal.ToCaptainId = toCaptainId;
            signal.Read = read;
            signal.CreatedUtc = createdUtc;
            return signal;
        }

        private static ArmadaEvent CreateEvent(string eventType, string? captainId, string? missionId, string? vesselId, string? voyageId, DateTime createdUtc)
        {
            ArmadaEvent evt = new ArmadaEvent(eventType, "Test event: " + eventType);
            evt.CaptainId = captainId;
            evt.MissionId = missionId;
            evt.VesselId = vesselId;
            evt.VoyageId = voyageId;
            evt.CreatedUtc = createdUtc;
            return evt;
        }

        private static Dock CreateDock(string vesselId, string? captainId, DateTime createdUtc)
        {
            Dock dock = new Dock(vesselId);
            dock.CaptainId = captainId;
            dock.WorktreePath = "/tmp/worktree/" + Guid.NewGuid().ToString("N").Substring(0, 8);
            dock.CreatedUtc = createdUtc;
            return dock;
        }

        #endregion

        protected override async Task RunTestsAsync()
        {
            // Fleet Enumeration Tests

            await RunTest("Fleet enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 15; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet-" + i.ToString("D2"), BaseTime.AddMinutes(i)));
                    }

                    EnumerationQuery queryPage1 = new EnumerationQuery();
                    queryPage1.PageNumber = 1;
                    queryPage1.PageSize = 5;
                    queryPage1.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> page1 = await db.Fleets.EnumerateAsync(queryPage1);

                    AssertEqual(5, page1.Objects.Count);
                    AssertEqual(15, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);
                    AssertEqual(1, page1.PageNumber);
                    AssertEqual(5, page1.PageSize);

                    EnumerationQuery queryPage2 = new EnumerationQuery();
                    queryPage2.PageNumber = 2;
                    queryPage2.PageSize = 5;
                    queryPage2.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> page2 = await db.Fleets.EnumerateAsync(queryPage2);

                    AssertEqual(5, page2.Objects.Count);
                    AssertEqual(15, page2.TotalRecords);
                    AssertEqual(3, page2.TotalPages);

                    EnumerationQuery queryPage3 = new EnumerationQuery();
                    queryPage3.PageNumber = 3;
                    queryPage3.PageSize = 5;
                    queryPage3.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> page3 = await db.Fleets.EnumerateAsync(queryPage3);

                    AssertEqual(5, page3.Objects.Count);
                    AssertEqual(15, page3.TotalRecords);

                    EnumerationQuery queryPage4 = new EnumerationQuery();
                    queryPage4.PageNumber = 4;
                    queryPage4.PageSize = 5;
                    queryPage4.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> page4 = await db.Fleets.EnumerateAsync(queryPage4);

                    AssertEqual(0, page4.Objects.Count);
                    AssertEqual(15, page4.TotalRecords);
                    AssertEqual(3, page4.TotalPages);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Fleet f in page1.Objects) allIds.Add(f.Id);
                    foreach (Fleet f in page2.Objects) allIds.Add(f.Id);
                    foreach (Fleet f in page3.Objects) allIds.Add(f.Id);
                    AssertEqual(15, allIds.Count);
                }
            });

            await RunTest("Fleet enumerate order created descending returns newest first", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Oldest", BaseTime));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Middle", BaseTime.AddHours(1)));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Newest", BaseTime.AddHours(2)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(3, result.Objects.Count);
                    AssertEqual("Fleet-Newest", result.Objects[0].Name);
                    AssertEqual("Fleet-Middle", result.Objects[1].Name);
                    AssertEqual("Fleet-Oldest", result.Objects[2].Name);
                }
            });

            await RunTest("Fleet enumerate order created ascending returns oldest first", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Oldest", BaseTime));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Middle", BaseTime.AddHours(1)));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Newest", BaseTime.AddHours(2)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(3, result.Objects.Count);
                    AssertEqual("Fleet-Oldest", result.Objects[0].Name);
                    AssertEqual("Fleet-Middle", result.Objects[1].Name);
                    AssertEqual("Fleet-Newest", result.Objects[2].Name);
                }
            });

            await RunTest("Fleet enumerate created after filter returns only newer", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Old", BaseTime));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Mid", BaseTime.AddHours(2)));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-New", BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(2, result.TotalRecords);
                    AssertEqual(2, result.Objects.Count);
                    AssertEqual("Fleet-Mid", result.Objects[0].Name);
                    AssertEqual("Fleet-New", result.Objects[1].Name);
                }
            });

            await RunTest("Fleet enumerate created before filter returns only older", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Old", BaseTime));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Mid", BaseTime.AddHours(2)));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-New", BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedBefore = BaseTime.AddHours(3);
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(2, result.TotalRecords);
                    AssertEqual(2, result.Objects.Count);
                    AssertEqual("Fleet-Old", result.Objects[0].Name);
                    AssertEqual("Fleet-Mid", result.Objects[1].Name);
                }
            });

            await RunTest("Fleet enumerate date range filter returns between", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Old", BaseTime));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-Mid", BaseTime.AddHours(2)));
                    await db.Fleets.CreateAsync(CreateFleet("Fleet-New", BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual(1, result.Objects.Count);
                    AssertEqual("Fleet-Mid", result.Objects[0].Name);
                }
            });

            await RunTest("Fleet enumerate empty database returns empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    EnumerationQuery query = new EnumerationQuery();
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(0, result.TotalRecords);
                    AssertEqual(0, result.TotalPages);
                    AssertEqual(0, result.Objects.Count);
                }
            });

            await RunTest("Fleet enumerate page beyond range returns empty with correct total", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Fleets.CreateAsync(CreateFleet("Fleet-" + i, BaseTime.AddMinutes(i)));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageNumber = 999;
                    query.PageSize = 5;
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(0, result.Objects.Count);
                    AssertEqual(5, result.TotalRecords);
                    AssertEqual(1, result.TotalPages);
                    AssertEqual(999, result.PageNumber);
                }
            });

            // Vessel Enumeration Tests

            await RunTest("Vessel enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 15; i++)
                    {
                        await db.Vessels.CreateAsync(CreateVessel("Vessel-" + i.ToString("D2"), null, BaseTime.AddMinutes(i)));
                    }

                    EnumerationQuery queryPage1 = new EnumerationQuery();
                    queryPage1.PageNumber = 1;
                    queryPage1.PageSize = 5;
                    queryPage1.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Vessel> page1 = await db.Vessels.EnumerateAsync(queryPage1);

                    AssertEqual(5, page1.Objects.Count);
                    AssertEqual(15, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    EnumerationQuery queryPage2 = new EnumerationQuery();
                    queryPage2.PageNumber = 2;
                    queryPage2.PageSize = 5;
                    queryPage2.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Vessel> page2 = await db.Vessels.EnumerateAsync(queryPage2);

                    AssertEqual(5, page2.Objects.Count);

                    EnumerationQuery queryPage3 = new EnumerationQuery();
                    queryPage3.PageNumber = 3;
                    queryPage3.PageSize = 5;
                    queryPage3.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Vessel> page3 = await db.Vessels.EnumerateAsync(queryPage3);

                    AssertEqual(5, page3.Objects.Count);

                    EnumerationQuery queryPage4 = new EnumerationQuery();
                    queryPage4.PageNumber = 4;
                    queryPage4.PageSize = 5;
                    EnumerationResult<Vessel> page4 = await db.Vessels.EnumerateAsync(queryPage4);

                    AssertEqual(0, page4.Objects.Count);
                    AssertEqual(15, page4.TotalRecords);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Vessel v in page1.Objects) allIds.Add(v.Id);
                    foreach (Vessel v in page2.Objects) allIds.Add(v.Id);
                    foreach (Vessel v in page3.Objects) allIds.Add(v.Id);
                    AssertEqual(15, allIds.Count);
                }
            });

            await RunTest("Vessel enumerate FleetId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleetA = await db.Fleets.CreateAsync(CreateFleet("Fleet-A", BaseTime));
                    Fleet fleetB = await db.Fleets.CreateAsync(CreateFleet("Fleet-B", BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 5; i++)
                        await db.Vessels.CreateAsync(CreateVessel("VesselA-" + i, fleetA.Id, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Vessels.CreateAsync(CreateVessel("VesselB-" + i, fleetB.Id, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Vessels.CreateAsync(CreateVessel("VesselNoFleet-" + i, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery queryA = new EnumerationQuery();
                    queryA.FleetId = fleetA.Id;
                    EnumerationResult<Vessel> resultA = await db.Vessels.EnumerateAsync(queryA);

                    AssertEqual(5, resultA.TotalRecords);
                    AssertEqual(5, resultA.Objects.Count);
                    foreach (Vessel v in resultA.Objects)
                        AssertEqual(fleetA.Id, v.FleetId);

                    EnumerationQuery queryB = new EnumerationQuery();
                    queryB.FleetId = fleetB.Id;
                    EnumerationResult<Vessel> resultB = await db.Vessels.EnumerateAsync(queryB);

                    AssertEqual(3, resultB.TotalRecords);
                    AssertEqual(3, resultB.Objects.Count);
                    foreach (Vessel v in resultB.Objects)
                        AssertEqual(fleetB.Id, v.FleetId);
                }
            });

            await RunTest("Vessel enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Vessels.CreateAsync(CreateVessel("Vessel-Oldest", null, BaseTime));
                    await db.Vessels.CreateAsync(CreateVessel("Vessel-Middle", null, BaseTime.AddHours(1)));
                    await db.Vessels.CreateAsync(CreateVessel("Vessel-Newest", null, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Vessel> descResult = await db.Vessels.EnumerateAsync(queryDesc);

                    AssertEqual("Vessel-Newest", descResult.Objects[0].Name);
                    AssertEqual("Vessel-Oldest", descResult.Objects[2].Name);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Vessel> ascResult = await db.Vessels.EnumerateAsync(queryAsc);

                    AssertEqual("Vessel-Oldest", ascResult.Objects[0].Name);
                    AssertEqual("Vessel-Newest", ascResult.Objects[2].Name);
                }
            });

            await RunTest("Vessel enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Vessels.CreateAsync(CreateVessel("Vessel-Old", null, BaseTime));
                    await db.Vessels.CreateAsync(CreateVessel("Vessel-Mid", null, BaseTime.AddHours(2)));
                    await db.Vessels.CreateAsync(CreateVessel("Vessel-New", null, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Vessel> result = await db.Vessels.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual(1, result.Objects.Count);
                    AssertEqual("Vessel-Mid", result.Objects[0].Name);
                }
            });

            await RunTest("Vessel enumerate FleetId filter with pagination works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = await db.Fleets.CreateAsync(CreateFleet("BigFleet", BaseTime));

                    for (int i = 0; i < 8; i++)
                        await db.Vessels.CreateAsync(CreateVessel("FleetVessel-" + i.ToString("D2"), fleet.Id, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 4; i++)
                        await db.Vessels.CreateAsync(CreateVessel("OtherVessel-" + i, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.FleetId = fleet.Id;
                    query.PageSize = 3;
                    query.PageNumber = 1;
                    EnumerationResult<Vessel> page1 = await db.Vessels.EnumerateAsync(query);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(8, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 3;
                    EnumerationResult<Vessel> page3 = await db.Vessels.EnumerateAsync(query);

                    AssertEqual(2, page3.Objects.Count);
                    AssertEqual(8, page3.TotalRecords);
                }
            });

            // Mission Enumeration Tests

            await RunTest("Mission enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 20; i++)
                        await db.Missions.CreateAsync(CreateMission("Mission-" + i.ToString("D2"), MissionStatusEnum.Pending, null, null, null, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 7;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page1 = await db.Missions.EnumerateAsync(query);

                    AssertEqual(7, page1.Objects.Count);
                    AssertEqual(20, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 2;
                    EnumerationResult<Mission> page2 = await db.Missions.EnumerateAsync(query);
                    AssertEqual(7, page2.Objects.Count);

                    query.PageNumber = 3;
                    EnumerationResult<Mission> page3 = await db.Missions.EnumerateAsync(query);
                    AssertEqual(6, page3.Objects.Count);

                    query.PageNumber = 4;
                    EnumerationResult<Mission> page4 = await db.Missions.EnumerateAsync(query);
                    AssertEqual(0, page4.Objects.Count);
                    AssertEqual(20, page4.TotalRecords);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Mission m in page1.Objects) allIds.Add(m.Id);
                    foreach (Mission m in page2.Objects) allIds.Add(m.Id);
                    foreach (Mission m in page3.Objects) allIds.Add(m.Id);
                    AssertEqual(20, allIds.Count);
                }
            });

            await RunTest("Mission enumerate status filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 4; i++)
                        await db.Missions.CreateAsync(CreateMission("Pending-" + i, MissionStatusEnum.Pending, null, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Missions.CreateAsync(CreateMission("InProgress-" + i, MissionStatusEnum.InProgress, null, null, null, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Missions.CreateAsync(CreateMission("Complete-" + i, MissionStatusEnum.Complete, null, null, null, BaseTime.AddMinutes(20 + i)));
                    await db.Missions.CreateAsync(CreateMission("Failed-0", MissionStatusEnum.Failed, null, null, null, BaseTime.AddMinutes(30)));

                    EnumerationQuery queryPending = new EnumerationQuery();
                    queryPending.Status = "Pending";
                    EnumerationResult<Mission> pendingResult = await db.Missions.EnumerateAsync(queryPending);
                    AssertEqual(4, pendingResult.TotalRecords);
                    foreach (Mission m in pendingResult.Objects)
                        AssertEqual(MissionStatusEnum.Pending, m.Status);

                    EnumerationQuery queryInProgress = new EnumerationQuery();
                    queryInProgress.Status = "InProgress";
                    EnumerationResult<Mission> inProgressResult = await db.Missions.EnumerateAsync(queryInProgress);
                    AssertEqual(3, inProgressResult.TotalRecords);

                    EnumerationQuery queryComplete = new EnumerationQuery();
                    queryComplete.Status = "Complete";
                    EnumerationResult<Mission> completeResult = await db.Missions.EnumerateAsync(queryComplete);
                    AssertEqual(2, completeResult.TotalRecords);

                    EnumerationQuery queryFailed = new EnumerationQuery();
                    queryFailed.Status = "Failed";
                    EnumerationResult<Mission> failedResult = await db.Missions.EnumerateAsync(queryFailed);
                    AssertEqual(1, failedResult.TotalRecords);
                }
            });

            await RunTest("Mission enumerate VesselId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vesselA = await db.Vessels.CreateAsync(CreateVessel("VesselA", null, BaseTime));
                    Vessel vesselB = await db.Vessels.CreateAsync(CreateVessel("VesselB", null, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 5; i++)
                        await db.Missions.CreateAsync(CreateMission("MissionA-" + i, MissionStatusEnum.Pending, vesselA.Id, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Missions.CreateAsync(CreateMission("MissionB-" + i, MissionStatusEnum.Pending, vesselB.Id, null, null, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VesselId = vesselA.Id;
                    EnumerationResult<Mission> result = await db.Missions.EnumerateAsync(query);

                    AssertEqual(5, result.TotalRecords);
                    foreach (Mission m in result.Objects)
                        AssertEqual(vesselA.Id, m.VesselId);
                }
            });

            await RunTest("Mission enumerate CaptainId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Captain captainA = await db.Captains.CreateAsync(CreateCaptain("CaptainA", CaptainStateEnum.Working, BaseTime));
                    Captain captainB = await db.Captains.CreateAsync(CreateCaptain("CaptainB", CaptainStateEnum.Idle, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 4; i++)
                        await db.Missions.CreateAsync(CreateMission("MissionCptA-" + i, MissionStatusEnum.InProgress, null, captainA.Id, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Missions.CreateAsync(CreateMission("MissionCptB-" + i, MissionStatusEnum.InProgress, null, captainB.Id, null, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CaptainId = captainA.Id;
                    EnumerationResult<Mission> result = await db.Missions.EnumerateAsync(query);

                    AssertEqual(4, result.TotalRecords);
                    foreach (Mission m in result.Objects)
                        AssertEqual(captainA.Id, m.CaptainId);
                }
            });

            await RunTest("Mission enumerate VoyageId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Voyage voyageA = await db.Voyages.CreateAsync(CreateVoyage("VoyageA", VoyageStatusEnum.Open, BaseTime));
                    Voyage voyageB = await db.Voyages.CreateAsync(CreateVoyage("VoyageB", VoyageStatusEnum.Open, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 6; i++)
                        await db.Missions.CreateAsync(CreateMission("MissionVA-" + i, MissionStatusEnum.Pending, null, null, voyageA.Id, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Missions.CreateAsync(CreateMission("MissionVB-" + i, MissionStatusEnum.Pending, null, null, voyageB.Id, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VoyageId = voyageA.Id;
                    EnumerationResult<Mission> result = await db.Missions.EnumerateAsync(query);

                    AssertEqual(6, result.TotalRecords);
                    foreach (Mission m in result.Objects)
                        AssertEqual(voyageA.Id, m.VoyageId);
                }
            });

            await RunTest("Mission enumerate combined filters returns intersection", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("CombinedVessel", null, BaseTime));

                    for (int i = 0; i < 3; i++)
                        await db.Missions.CreateAsync(CreateMission("PendingVessel-" + i, MissionStatusEnum.Pending, vessel.Id, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Missions.CreateAsync(CreateMission("CompleteVessel-" + i, MissionStatusEnum.Complete, vessel.Id, null, null, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 4; i++)
                        await db.Missions.CreateAsync(CreateMission("PendingNoVessel-" + i, MissionStatusEnum.Pending, null, null, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VesselId = vessel.Id;
                    query.Status = "Pending";
                    EnumerationResult<Mission> result = await db.Missions.EnumerateAsync(query);

                    AssertEqual(3, result.TotalRecords);
                    foreach (Mission m in result.Objects)
                    {
                        AssertEqual(vessel.Id, m.VesselId);
                        AssertEqual(MissionStatusEnum.Pending, m.Status);
                    }
                }
            });

            await RunTest("Mission enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Missions.CreateAsync(CreateMission("Mission-Oldest", MissionStatusEnum.Pending, null, null, null, BaseTime));
                    await db.Missions.CreateAsync(CreateMission("Mission-Middle", MissionStatusEnum.Pending, null, null, null, BaseTime.AddHours(1)));
                    await db.Missions.CreateAsync(CreateMission("Mission-Newest", MissionStatusEnum.Pending, null, null, null, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Mission> descResult = await db.Missions.EnumerateAsync(queryDesc);

                    AssertEqual("Mission-Newest", descResult.Objects[0].Title);
                    AssertEqual("Mission-Oldest", descResult.Objects[2].Title);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> ascResult = await db.Missions.EnumerateAsync(queryAsc);

                    AssertEqual("Mission-Oldest", ascResult.Objects[0].Title);
                    AssertEqual("Mission-Newest", ascResult.Objects[2].Title);
                }
            });

            await RunTest("Mission enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Missions.CreateAsync(CreateMission("Mission-Old", MissionStatusEnum.Pending, null, null, null, BaseTime));
                    await db.Missions.CreateAsync(CreateMission("Mission-Mid", MissionStatusEnum.Pending, null, null, null, BaseTime.AddHours(2)));
                    await db.Missions.CreateAsync(CreateMission("Mission-New", MissionStatusEnum.Pending, null, null, null, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Mission> result = await db.Missions.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual("Mission-Mid", result.Objects[0].Title);
                }
            });

            await RunTest("Mission enumerate status filter with pagination works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 10; i++)
                        await db.Missions.CreateAsync(CreateMission("Pending-" + i.ToString("D2"), MissionStatusEnum.Pending, null, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 5; i++)
                        await db.Missions.CreateAsync(CreateMission("Complete-" + i, MissionStatusEnum.Complete, null, null, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Status = "Pending";
                    query.PageSize = 4;
                    query.PageNumber = 1;
                    EnumerationResult<Mission> page1 = await db.Missions.EnumerateAsync(query);

                    AssertEqual(4, page1.Objects.Count);
                    AssertEqual(10, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 3;
                    EnumerationResult<Mission> page3 = await db.Missions.EnumerateAsync(query);
                    AssertEqual(2, page3.Objects.Count);
                    AssertEqual(10, page3.TotalRecords);
                }
            });

            // Voyage Enumeration Tests

            await RunTest("Voyage enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 12; i++)
                        await db.Voyages.CreateAsync(CreateVoyage("Voyage-" + i.ToString("D2"), VoyageStatusEnum.Open, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 4;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Voyage> page1 = await db.Voyages.EnumerateAsync(query);

                    AssertEqual(4, page1.Objects.Count);
                    AssertEqual(12, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 2;
                    EnumerationResult<Voyage> page2 = await db.Voyages.EnumerateAsync(query);
                    AssertEqual(4, page2.Objects.Count);

                    query.PageNumber = 3;
                    EnumerationResult<Voyage> page3 = await db.Voyages.EnumerateAsync(query);
                    AssertEqual(4, page3.Objects.Count);

                    query.PageNumber = 4;
                    EnumerationResult<Voyage> page4 = await db.Voyages.EnumerateAsync(query);
                    AssertEqual(0, page4.Objects.Count);
                    AssertEqual(12, page4.TotalRecords);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Voyage v in page1.Objects) allIds.Add(v.Id);
                    foreach (Voyage v in page2.Objects) allIds.Add(v.Id);
                    foreach (Voyage v in page3.Objects) allIds.Add(v.Id);
                    AssertEqual(12, allIds.Count);
                }
            });

            await RunTest("Voyage enumerate status filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 3; i++)
                        await db.Voyages.CreateAsync(CreateVoyage("Open-" + i, VoyageStatusEnum.Open, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 4; i++)
                        await db.Voyages.CreateAsync(CreateVoyage("InProgress-" + i, VoyageStatusEnum.InProgress, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Voyages.CreateAsync(CreateVoyage("Complete-" + i, VoyageStatusEnum.Complete, BaseTime.AddMinutes(20 + i)));
                    await db.Voyages.CreateAsync(CreateVoyage("Cancelled-0", VoyageStatusEnum.Cancelled, BaseTime.AddMinutes(30)));

                    EnumerationQuery queryOpen = new EnumerationQuery();
                    queryOpen.Status = "Open";
                    EnumerationResult<Voyage> openResult = await db.Voyages.EnumerateAsync(queryOpen);
                    AssertEqual(3, openResult.TotalRecords);
                    foreach (Voyage v in openResult.Objects)
                        AssertEqual(VoyageStatusEnum.Open, v.Status);

                    EnumerationQuery queryInProgress = new EnumerationQuery();
                    queryInProgress.Status = "InProgress";
                    EnumerationResult<Voyage> inProgressResult = await db.Voyages.EnumerateAsync(queryInProgress);
                    AssertEqual(4, inProgressResult.TotalRecords);

                    EnumerationQuery queryComplete = new EnumerationQuery();
                    queryComplete.Status = "Complete";
                    EnumerationResult<Voyage> completeResult = await db.Voyages.EnumerateAsync(queryComplete);
                    AssertEqual(2, completeResult.TotalRecords);

                    EnumerationQuery queryCancelled = new EnumerationQuery();
                    queryCancelled.Status = "Cancelled";
                    EnumerationResult<Voyage> cancelledResult = await db.Voyages.EnumerateAsync(queryCancelled);
                    AssertEqual(1, cancelledResult.TotalRecords);
                }
            });

            await RunTest("Voyage enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Voyages.CreateAsync(CreateVoyage("Voyage-Oldest", VoyageStatusEnum.Open, BaseTime));
                    await db.Voyages.CreateAsync(CreateVoyage("Voyage-Middle", VoyageStatusEnum.Open, BaseTime.AddHours(1)));
                    await db.Voyages.CreateAsync(CreateVoyage("Voyage-Newest", VoyageStatusEnum.Open, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Voyage> descResult = await db.Voyages.EnumerateAsync(queryDesc);

                    AssertEqual("Voyage-Newest", descResult.Objects[0].Title);
                    AssertEqual("Voyage-Oldest", descResult.Objects[2].Title);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Voyage> ascResult = await db.Voyages.EnumerateAsync(queryAsc);

                    AssertEqual("Voyage-Oldest", ascResult.Objects[0].Title);
                    AssertEqual("Voyage-Newest", ascResult.Objects[2].Title);
                }
            });

            await RunTest("Voyage enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Voyages.CreateAsync(CreateVoyage("Voyage-Old", VoyageStatusEnum.Open, BaseTime));
                    await db.Voyages.CreateAsync(CreateVoyage("Voyage-Mid", VoyageStatusEnum.Open, BaseTime.AddHours(2)));
                    await db.Voyages.CreateAsync(CreateVoyage("Voyage-New", VoyageStatusEnum.Open, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Voyage> result = await db.Voyages.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual("Voyage-Mid", result.Objects[0].Title);
                }
            });

            await RunTest("Voyage enumerate status filter with pagination works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 9; i++)
                        await db.Voyages.CreateAsync(CreateVoyage("Open-" + i.ToString("D2"), VoyageStatusEnum.Open, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Voyages.CreateAsync(CreateVoyage("Complete-" + i, VoyageStatusEnum.Complete, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Status = "Open";
                    query.PageSize = 4;
                    query.PageNumber = 1;
                    EnumerationResult<Voyage> page1 = await db.Voyages.EnumerateAsync(query);

                    AssertEqual(4, page1.Objects.Count);
                    AssertEqual(9, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 3;
                    EnumerationResult<Voyage> page3 = await db.Voyages.EnumerateAsync(query);
                    AssertEqual(1, page3.Objects.Count);
                    AssertEqual(9, page3.TotalRecords);
                }
            });

            // Captain Enumeration Tests

            await RunTest("Captain enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 10; i++)
                        await db.Captains.CreateAsync(CreateCaptain("Captain-" + i.ToString("D2"), CaptainStateEnum.Idle, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 3;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Captain> page1 = await db.Captains.EnumerateAsync(query);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(10, page1.TotalRecords);
                    AssertEqual(4, page1.TotalPages);

                    query.PageNumber = 4;
                    EnumerationResult<Captain> page4 = await db.Captains.EnumerateAsync(query);
                    AssertEqual(1, page4.Objects.Count);
                    AssertEqual(10, page4.TotalRecords);

                    query.PageNumber = 5;
                    EnumerationResult<Captain> page5 = await db.Captains.EnumerateAsync(query);
                    AssertEqual(0, page5.Objects.Count);
                    AssertEqual(10, page5.TotalRecords);
                }
            });

            await RunTest("Captain enumerate state filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 3; i++)
                        await db.Captains.CreateAsync(CreateCaptain("Idle-" + i, CaptainStateEnum.Idle, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 4; i++)
                        await db.Captains.CreateAsync(CreateCaptain("Working-" + i, CaptainStateEnum.Working, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Captains.CreateAsync(CreateCaptain("Stalled-" + i, CaptainStateEnum.Stalled, BaseTime.AddMinutes(20 + i)));
                    await db.Captains.CreateAsync(CreateCaptain("Stopping-0", CaptainStateEnum.Stopping, BaseTime.AddMinutes(30)));

                    EnumerationQuery queryIdle = new EnumerationQuery();
                    queryIdle.Status = "Idle";
                    EnumerationResult<Captain> idleResult = await db.Captains.EnumerateAsync(queryIdle);
                    AssertEqual(3, idleResult.TotalRecords);
                    foreach (Captain c in idleResult.Objects)
                        AssertEqual(CaptainStateEnum.Idle, c.State);

                    EnumerationQuery queryWorking = new EnumerationQuery();
                    queryWorking.Status = "Working";
                    EnumerationResult<Captain> workingResult = await db.Captains.EnumerateAsync(queryWorking);
                    AssertEqual(4, workingResult.TotalRecords);

                    EnumerationQuery queryStalled = new EnumerationQuery();
                    queryStalled.Status = "Stalled";
                    EnumerationResult<Captain> stalledResult = await db.Captains.EnumerateAsync(queryStalled);
                    AssertEqual(2, stalledResult.TotalRecords);

                    EnumerationQuery queryStopping = new EnumerationQuery();
                    queryStopping.Status = "Stopping";
                    EnumerationResult<Captain> stoppingResult = await db.Captains.EnumerateAsync(queryStopping);
                    AssertEqual(1, stoppingResult.TotalRecords);
                }
            });

            await RunTest("Captain enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Captains.CreateAsync(CreateCaptain("Captain-Oldest", CaptainStateEnum.Idle, BaseTime));
                    await db.Captains.CreateAsync(CreateCaptain("Captain-Middle", CaptainStateEnum.Idle, BaseTime.AddHours(1)));
                    await db.Captains.CreateAsync(CreateCaptain("Captain-Newest", CaptainStateEnum.Idle, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Captain> descResult = await db.Captains.EnumerateAsync(queryDesc);

                    AssertEqual("Captain-Newest", descResult.Objects[0].Name);
                    AssertEqual("Captain-Oldest", descResult.Objects[2].Name);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Captain> ascResult = await db.Captains.EnumerateAsync(queryAsc);

                    AssertEqual("Captain-Oldest", ascResult.Objects[0].Name);
                    AssertEqual("Captain-Newest", ascResult.Objects[2].Name);
                }
            });

            await RunTest("Captain enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Captains.CreateAsync(CreateCaptain("Captain-Old", CaptainStateEnum.Idle, BaseTime));
                    await db.Captains.CreateAsync(CreateCaptain("Captain-Mid", CaptainStateEnum.Idle, BaseTime.AddHours(2)));
                    await db.Captains.CreateAsync(CreateCaptain("Captain-New", CaptainStateEnum.Idle, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Captain> result = await db.Captains.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual("Captain-Mid", result.Objects[0].Name);
                }
            });

            await RunTest("Captain enumerate state filter with pagination works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 7; i++)
                        await db.Captains.CreateAsync(CreateCaptain("Idle-" + i.ToString("D2"), CaptainStateEnum.Idle, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Captains.CreateAsync(CreateCaptain("Working-" + i, CaptainStateEnum.Working, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.Status = "Idle";
                    query.PageSize = 3;
                    query.PageNumber = 1;
                    EnumerationResult<Captain> page1 = await db.Captains.EnumerateAsync(query);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(7, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 3;
                    EnumerationResult<Captain> page3 = await db.Captains.EnumerateAsync(query);
                    AssertEqual(1, page3.Objects.Count);
                }
            });

            // Signal Enumeration Tests

            await RunTest("Signal enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 15; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, false, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 5;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Signal> page1 = await db.Signals.EnumerateAsync(query);

                    AssertEqual(5, page1.Objects.Count);
                    AssertEqual(15, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 2;
                    EnumerationResult<Signal> page2 = await db.Signals.EnumerateAsync(query);
                    AssertEqual(5, page2.Objects.Count);

                    query.PageNumber = 3;
                    EnumerationResult<Signal> page3 = await db.Signals.EnumerateAsync(query);
                    AssertEqual(5, page3.Objects.Count);

                    query.PageNumber = 4;
                    EnumerationResult<Signal> page4 = await db.Signals.EnumerateAsync(query);
                    AssertEqual(0, page4.Objects.Count);
                    AssertEqual(15, page4.TotalRecords);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Signal s in page1.Objects) allIds.Add(s.Id);
                    foreach (Signal s in page2.Objects) allIds.Add(s.Id);
                    foreach (Signal s in page3.Objects) allIds.Add(s.Id);
                    AssertEqual(15, allIds.Count);
                }
            });

            await RunTest("Signal enumerate ToCaptainId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Captain captainA = await db.Captains.CreateAsync(CreateCaptain("CaptainA", CaptainStateEnum.Idle, BaseTime));
                    Captain captainB = await db.Captains.CreateAsync(CreateCaptain("CaptainB", CaptainStateEnum.Idle, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 5; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Assignment, captainA.Id, false, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Assignment, captainB.Id, false, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Assignment, null, false, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.ToCaptainId = captainA.Id;
                    EnumerationResult<Signal> result = await db.Signals.EnumerateAsync(query);

                    AssertEqual(5, result.TotalRecords);
                    foreach (Signal s in result.Objects)
                        AssertEqual(captainA.Id, s.ToCaptainId);
                }
            });

            await RunTest("Signal enumerate SignalType filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 4; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Assignment, null, false, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Progress, null, false, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Error, null, false, BaseTime.AddMinutes(20 + i)));
                    await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Heartbeat, null, false, BaseTime.AddMinutes(30)));

                    EnumerationQuery queryAssignment = new EnumerationQuery();
                    queryAssignment.SignalType = "Assignment";
                    EnumerationResult<Signal> assignmentResult = await db.Signals.EnumerateAsync(queryAssignment);
                    AssertEqual(4, assignmentResult.TotalRecords);
                    foreach (Signal s in assignmentResult.Objects)
                        AssertEqual(SignalTypeEnum.Assignment, s.Type);

                    EnumerationQuery queryProgress = new EnumerationQuery();
                    queryProgress.SignalType = "Progress";
                    EnumerationResult<Signal> progressResult = await db.Signals.EnumerateAsync(queryProgress);
                    AssertEqual(3, progressResult.TotalRecords);

                    EnumerationQuery queryError = new EnumerationQuery();
                    queryError.SignalType = "Error";
                    EnumerationResult<Signal> errorResult = await db.Signals.EnumerateAsync(queryError);
                    AssertEqual(2, errorResult.TotalRecords);

                    EnumerationQuery queryHeartbeat = new EnumerationQuery();
                    queryHeartbeat.SignalType = "Heartbeat";
                    EnumerationResult<Signal> heartbeatResult = await db.Signals.EnumerateAsync(queryHeartbeat);
                    AssertEqual(1, heartbeatResult.TotalRecords);
                }
            });

            await RunTest("Signal enumerate unread only filter returns only unread", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 5; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, false, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, true, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery queryUnread = new EnumerationQuery();
                    queryUnread.UnreadOnly = true;
                    EnumerationResult<Signal> unreadResult = await db.Signals.EnumerateAsync(queryUnread);
                    AssertEqual(5, unreadResult.TotalRecords);
                    foreach (Signal s in unreadResult.Objects)
                        AssertFalse(s.Read);

                    EnumerationQuery queryAll = new EnumerationQuery();
                    EnumerationResult<Signal> allResult = await db.Signals.EnumerateAsync(queryAll);
                    AssertEqual(8, allResult.TotalRecords);

                    EnumerationQuery queryNotUnread = new EnumerationQuery();
                    queryNotUnread.UnreadOnly = false;
                    EnumerationResult<Signal> notUnreadResult = await db.Signals.EnumerateAsync(queryNotUnread);
                    AssertEqual(8, notUnreadResult.TotalRecords);
                }
            });

            await RunTest("Signal enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Signal oldest = await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, false, BaseTime));
                    Signal middle = await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, false, BaseTime.AddHours(1)));
                    Signal newest = await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, false, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Signal> descResult = await db.Signals.EnumerateAsync(queryDesc);

                    AssertEqual(newest.Id, descResult.Objects[0].Id);
                    AssertEqual(oldest.Id, descResult.Objects[2].Id);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Signal> ascResult = await db.Signals.EnumerateAsync(queryAsc);

                    AssertEqual(oldest.Id, ascResult.Objects[0].Id);
                    AssertEqual(newest.Id, ascResult.Objects[2].Id);
                }
            });

            await RunTest("Signal enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Signal old = await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, null, false, BaseTime));
                    Signal mid = await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Mail, null, false, BaseTime.AddHours(2)));
                    Signal recent = await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Error, null, false, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Signal> result = await db.Signals.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual(mid.Id, result.Objects[0].Id);
                }
            });

            await RunTest("Signal enumerate combined filters work correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Captain captain = await db.Captains.CreateAsync(CreateCaptain("FilterCaptain", CaptainStateEnum.Idle, BaseTime));

                    for (int i = 0; i < 3; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Assignment, captain.Id, false, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Assignment, captain.Id, true, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 4; i++)
                        await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Progress, captain.Id, false, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.ToCaptainId = captain.Id;
                    query.SignalType = "Assignment";
                    query.UnreadOnly = true;
                    EnumerationResult<Signal> result = await db.Signals.EnumerateAsync(query);

                    AssertEqual(3, result.TotalRecords);
                    foreach (Signal s in result.Objects)
                    {
                        AssertEqual(captain.Id, s.ToCaptainId);
                        AssertEqual(SignalTypeEnum.Assignment, s.Type);
                        AssertFalse(s.Read);
                    }
                }
            });

            // Event Enumeration Tests

            await RunTest("Event enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 20; i++)
                        await db.Events.CreateAsync(CreateEvent("test.event", null, null, null, null, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 7;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<ArmadaEvent> page1 = await db.Events.EnumerateAsync(query);

                    AssertEqual(7, page1.Objects.Count);
                    AssertEqual(20, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 2;
                    EnumerationResult<ArmadaEvent> page2 = await db.Events.EnumerateAsync(query);
                    AssertEqual(7, page2.Objects.Count);

                    query.PageNumber = 3;
                    EnumerationResult<ArmadaEvent> page3 = await db.Events.EnumerateAsync(query);
                    AssertEqual(6, page3.Objects.Count);

                    query.PageNumber = 4;
                    EnumerationResult<ArmadaEvent> page4 = await db.Events.EnumerateAsync(query);
                    AssertEqual(0, page4.Objects.Count);
                    AssertEqual(20, page4.TotalRecords);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (ArmadaEvent e in page1.Objects) allIds.Add(e.Id);
                    foreach (ArmadaEvent e in page2.Objects) allIds.Add(e.Id);
                    foreach (ArmadaEvent e in page3.Objects) allIds.Add(e.Id);
                    AssertEqual(20, allIds.Count);
                }
            });

            await RunTest("Event enumerate EventType filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 4; i++)
                        await db.Events.CreateAsync(CreateEvent("mission.created", null, null, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Events.CreateAsync(CreateEvent("captain.stalled", null, null, null, null, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 2; i++)
                        await db.Events.CreateAsync(CreateEvent("voyage.completed", null, null, null, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery queryMission = new EnumerationQuery();
                    queryMission.EventType = "mission.created";
                    EnumerationResult<ArmadaEvent> missionResult = await db.Events.EnumerateAsync(queryMission);
                    AssertEqual(4, missionResult.TotalRecords);
                    foreach (ArmadaEvent e in missionResult.Objects)
                        AssertEqual("mission.created", e.EventType);

                    EnumerationQuery queryCaptain = new EnumerationQuery();
                    queryCaptain.EventType = "captain.stalled";
                    EnumerationResult<ArmadaEvent> captainResult = await db.Events.EnumerateAsync(queryCaptain);
                    AssertEqual(3, captainResult.TotalRecords);

                    EnumerationQuery queryVoyage = new EnumerationQuery();
                    queryVoyage.EventType = "voyage.completed";
                    EnumerationResult<ArmadaEvent> voyageResult = await db.Events.EnumerateAsync(queryVoyage);
                    AssertEqual(2, voyageResult.TotalRecords);
                }
            });

            await RunTest("Event enumerate CaptainId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Captain captainA = await db.Captains.CreateAsync(CreateCaptain("EvtCaptainA", CaptainStateEnum.Idle, BaseTime));
                    Captain captainB = await db.Captains.CreateAsync(CreateCaptain("EvtCaptainB", CaptainStateEnum.Idle, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 5; i++)
                        await db.Events.CreateAsync(CreateEvent("captain.heartbeat", captainA.Id, null, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Events.CreateAsync(CreateEvent("captain.heartbeat", captainB.Id, null, null, null, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 3; i++)
                        await db.Events.CreateAsync(CreateEvent("system.event", null, null, null, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CaptainId = captainA.Id;
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(query);

                    AssertEqual(5, result.TotalRecords);
                    foreach (ArmadaEvent e in result.Objects)
                        AssertEqual(captainA.Id, e.CaptainId);
                }
            });

            await RunTest("Event enumerate MissionId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Mission missionA = await db.Missions.CreateAsync(CreateMission("EvtMissionA", MissionStatusEnum.Pending, null, null, null, BaseTime));
                    Mission missionB = await db.Missions.CreateAsync(CreateMission("EvtMissionB", MissionStatusEnum.Pending, null, null, null, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 4; i++)
                        await db.Events.CreateAsync(CreateEvent("mission.progress", null, missionA.Id, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Events.CreateAsync(CreateEvent("mission.progress", null, missionB.Id, null, null, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.MissionId = missionA.Id;
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(query);

                    AssertEqual(4, result.TotalRecords);
                    foreach (ArmadaEvent e in result.Objects)
                        AssertEqual(missionA.Id, e.MissionId);
                }
            });

            await RunTest("Event enumerate VesselId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vesselA = await db.Vessels.CreateAsync(CreateVessel("EvtVesselA", null, BaseTime));
                    Vessel vesselB = await db.Vessels.CreateAsync(CreateVessel("EvtVesselB", null, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 6; i++)
                        await db.Events.CreateAsync(CreateEvent("vessel.updated", null, null, vesselA.Id, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Events.CreateAsync(CreateEvent("vessel.updated", null, null, vesselB.Id, null, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VesselId = vesselA.Id;
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(query);

                    AssertEqual(6, result.TotalRecords);
                    foreach (ArmadaEvent e in result.Objects)
                        AssertEqual(vesselA.Id, e.VesselId);
                }
            });

            await RunTest("Event enumerate VoyageId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Voyage voyageA = await db.Voyages.CreateAsync(CreateVoyage("EvtVoyageA", VoyageStatusEnum.Open, BaseTime));
                    Voyage voyageB = await db.Voyages.CreateAsync(CreateVoyage("EvtVoyageB", VoyageStatusEnum.Open, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 3; i++)
                        await db.Events.CreateAsync(CreateEvent("voyage.started", null, null, null, voyageA.Id, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 5; i++)
                        await db.Events.CreateAsync(CreateEvent("voyage.started", null, null, null, voyageB.Id, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VoyageId = voyageA.Id;
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(query);

                    AssertEqual(3, result.TotalRecords);
                    foreach (ArmadaEvent e in result.Objects)
                        AssertEqual(voyageA.Id, e.VoyageId);
                }
            });

            await RunTest("Event enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    ArmadaEvent oldest = await db.Events.CreateAsync(CreateEvent("test.oldest", null, null, null, null, BaseTime));
                    ArmadaEvent middle = await db.Events.CreateAsync(CreateEvent("test.middle", null, null, null, null, BaseTime.AddHours(1)));
                    ArmadaEvent newest = await db.Events.CreateAsync(CreateEvent("test.newest", null, null, null, null, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<ArmadaEvent> descResult = await db.Events.EnumerateAsync(queryDesc);

                    AssertEqual(newest.Id, descResult.Objects[0].Id);
                    AssertEqual(oldest.Id, descResult.Objects[2].Id);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<ArmadaEvent> ascResult = await db.Events.EnumerateAsync(queryAsc);

                    AssertEqual(oldest.Id, ascResult.Objects[0].Id);
                    AssertEqual(newest.Id, ascResult.Objects[2].Id);
                }
            });

            await RunTest("Event enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    ArmadaEvent old = await db.Events.CreateAsync(CreateEvent("old.event", null, null, null, null, BaseTime));
                    ArmadaEvent mid = await db.Events.CreateAsync(CreateEvent("mid.event", null, null, null, null, BaseTime.AddHours(2)));
                    ArmadaEvent recent = await db.Events.CreateAsync(CreateEvent("recent.event", null, null, null, null, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual(mid.Id, result.Objects[0].Id);
                }
            });

            await RunTest("Event enumerate combined filters returns intersection", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Captain captain = await db.Captains.CreateAsync(CreateCaptain("CombinedEvtCaptain", CaptainStateEnum.Idle, BaseTime));
                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("CombinedEvtVessel", null, BaseTime));

                    for (int i = 0; i < 3; i++)
                        await db.Events.CreateAsync(CreateEvent("mission.assigned", captain.Id, null, vessel.Id, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Events.CreateAsync(CreateEvent("captain.heartbeat", captain.Id, null, null, null, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 4; i++)
                        await db.Events.CreateAsync(CreateEvent("vessel.updated", null, null, vessel.Id, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CaptainId = captain.Id;
                    query.VesselId = vessel.Id;
                    EnumerationResult<ArmadaEvent> result = await db.Events.EnumerateAsync(query);

                    AssertEqual(3, result.TotalRecords);
                    foreach (ArmadaEvent e in result.Objects)
                    {
                        AssertEqual(captain.Id, e.CaptainId);
                        AssertEqual(vessel.Id, e.VesselId);
                    }
                }
            });

            await RunTest("Event enumerate EventType with pagination works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 11; i++)
                        await db.Events.CreateAsync(CreateEvent("mission.progress", null, null, null, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 5; i++)
                        await db.Events.CreateAsync(CreateEvent("other.event", null, null, null, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.EventType = "mission.progress";
                    query.PageSize = 4;
                    query.PageNumber = 1;
                    EnumerationResult<ArmadaEvent> page1 = await db.Events.EnumerateAsync(query);

                    AssertEqual(4, page1.Objects.Count);
                    AssertEqual(11, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 3;
                    EnumerationResult<ArmadaEvent> page3 = await db.Events.EnumerateAsync(query);
                    AssertEqual(3, page3.Objects.Count);
                    AssertEqual(11, page3.TotalRecords);
                }
            });

            // Dock Enumeration Tests

            await RunTest("Dock enumerate pagination returns correct pages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("DockVessel", null, BaseTime));

                    for (int i = 0; i < 10; i++)
                        await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 3;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Dock> page1 = await db.Docks.EnumerateAsync(query);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(10, page1.TotalRecords);
                    AssertEqual(4, page1.TotalPages);

                    query.PageNumber = 4;
                    EnumerationResult<Dock> page4 = await db.Docks.EnumerateAsync(query);
                    AssertEqual(1, page4.Objects.Count);

                    query.PageNumber = 5;
                    EnumerationResult<Dock> page5 = await db.Docks.EnumerateAsync(query);
                    AssertEqual(0, page5.Objects.Count);
                    AssertEqual(10, page5.TotalRecords);

                    HashSet<string> allIds = new HashSet<string>();
                    query.PageNumber = 1;
                    EnumerationResult<Dock> p1 = await db.Docks.EnumerateAsync(query);
                    foreach (Dock d in p1.Objects) allIds.Add(d.Id);
                    query.PageNumber = 2;
                    EnumerationResult<Dock> p2 = await db.Docks.EnumerateAsync(query);
                    foreach (Dock d in p2.Objects) allIds.Add(d.Id);
                    query.PageNumber = 3;
                    EnumerationResult<Dock> p3 = await db.Docks.EnumerateAsync(query);
                    foreach (Dock d in p3.Objects) allIds.Add(d.Id);
                    query.PageNumber = 4;
                    EnumerationResult<Dock> p4r = await db.Docks.EnumerateAsync(query);
                    foreach (Dock d in p4r.Objects) allIds.Add(d.Id);
                    AssertEqual(10, allIds.Count);
                }
            });

            await RunTest("Dock enumerate VesselId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vesselA = await db.Vessels.CreateAsync(CreateVessel("DockVesselA", null, BaseTime));
                    Vessel vesselB = await db.Vessels.CreateAsync(CreateVessel("DockVesselB", null, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 5; i++)
                        await db.Docks.CreateAsync(CreateDock(vesselA.Id, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Docks.CreateAsync(CreateDock(vesselB.Id, null, BaseTime.AddMinutes(10 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VesselId = vesselA.Id;
                    EnumerationResult<Dock> result = await db.Docks.EnumerateAsync(query);

                    AssertEqual(5, result.TotalRecords);
                    foreach (Dock d in result.Objects)
                        AssertEqual(vesselA.Id, d.VesselId);

                    query.VesselId = vesselB.Id;
                    EnumerationResult<Dock> resultB = await db.Docks.EnumerateAsync(query);
                    AssertEqual(3, resultB.TotalRecords);
                }
            });

            await RunTest("Dock enumerate CaptainId filter returns only matching", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("DockCptVessel", null, BaseTime));
                    Captain captainA = await db.Captains.CreateAsync(CreateCaptain("DockCaptainA", CaptainStateEnum.Working, BaseTime));
                    Captain captainB = await db.Captains.CreateAsync(CreateCaptain("DockCaptainB", CaptainStateEnum.Working, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 4; i++)
                        await db.Docks.CreateAsync(CreateDock(vessel.Id, captainA.Id, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 2; i++)
                        await db.Docks.CreateAsync(CreateDock(vessel.Id, captainB.Id, BaseTime.AddMinutes(10 + i)));
                    for (int i = 0; i < 3; i++)
                        await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CaptainId = captainA.Id;
                    EnumerationResult<Dock> result = await db.Docks.EnumerateAsync(query);

                    AssertEqual(4, result.TotalRecords);
                    foreach (Dock d in result.Objects)
                        AssertEqual(captainA.Id, d.CaptainId);
                }
            });

            await RunTest("Dock enumerate ordering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("DockOrderVessel", null, BaseTime));

                    Dock oldest = await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime));
                    Dock middle = await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime.AddHours(1)));
                    Dock newest = await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime.AddHours(2)));

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<Dock> descResult = await db.Docks.EnumerateAsync(queryDesc);

                    AssertEqual(newest.Id, descResult.Objects[0].Id);
                    AssertEqual(oldest.Id, descResult.Objects[2].Id);

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Dock> ascResult = await db.Docks.EnumerateAsync(queryAsc);

                    AssertEqual(oldest.Id, ascResult.Objects[0].Id);
                    AssertEqual(newest.Id, ascResult.Objects[2].Id);
                }
            });

            await RunTest("Dock enumerate date filtering works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("DockDateVessel", null, BaseTime));

                    Dock old = await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime));
                    Dock mid = await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime.AddHours(2)));
                    Dock recent = await db.Docks.CreateAsync(CreateDock(vessel.Id, null, BaseTime.AddHours(4)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.CreatedAfter = BaseTime.AddHours(1);
                    query.CreatedBefore = BaseTime.AddHours(3);
                    EnumerationResult<Dock> result = await db.Docks.EnumerateAsync(query);

                    AssertEqual(1, result.TotalRecords);
                    AssertEqual(mid.Id, result.Objects[0].Id);
                }
            });

            await RunTest("Dock enumerate VesselId with pagination works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vesselA = await db.Vessels.CreateAsync(CreateVessel("PagDockVesselA", null, BaseTime));
                    Vessel vesselB = await db.Vessels.CreateAsync(CreateVessel("PagDockVesselB", null, BaseTime.AddMinutes(1)));

                    for (int i = 0; i < 7; i++)
                        await db.Docks.CreateAsync(CreateDock(vesselA.Id, null, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 4; i++)
                        await db.Docks.CreateAsync(CreateDock(vesselB.Id, null, BaseTime.AddMinutes(20 + i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VesselId = vesselA.Id;
                    query.PageSize = 3;
                    query.PageNumber = 1;
                    EnumerationResult<Dock> page1 = await db.Docks.EnumerateAsync(query);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(7, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    query.PageNumber = 3;
                    EnumerationResult<Dock> page3 = await db.Docks.EnumerateAsync(query);
                    AssertEqual(1, page3.Objects.Count);
                    AssertEqual(7, page3.TotalRecords);
                }
            });

            await RunTest("Dock enumerate combined vessel and captain filter returns intersection", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel vesselA = await db.Vessels.CreateAsync(CreateVessel("CombDockVesselA", null, BaseTime));
                    Vessel vesselB = await db.Vessels.CreateAsync(CreateVessel("CombDockVesselB", null, BaseTime.AddMinutes(1)));
                    Captain captain = await db.Captains.CreateAsync(CreateCaptain("CombDockCaptain", CaptainStateEnum.Working, BaseTime));

                    for (int i = 0; i < 2; i++)
                        await db.Docks.CreateAsync(CreateDock(vesselA.Id, captain.Id, BaseTime.AddMinutes(i)));
                    for (int i = 0; i < 3; i++)
                        await db.Docks.CreateAsync(CreateDock(vesselA.Id, null, BaseTime.AddMinutes(10 + i)));
                    await db.Docks.CreateAsync(CreateDock(vesselB.Id, captain.Id, BaseTime.AddMinutes(20)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.VesselId = vesselA.Id;
                    query.CaptainId = captain.Id;
                    EnumerationResult<Dock> result = await db.Docks.EnumerateAsync(query);

                    AssertEqual(2, result.TotalRecords);
                    foreach (Dock d in result.Objects)
                    {
                        AssertEqual(vesselA.Id, d.VesselId);
                        AssertEqual(captain.Id, d.CaptainId);
                    }
                }
            });

            // Cross-Cutting Tests

            await RunTest("All entities enumerate empty database returns zero results", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    EnumerationQuery query = new EnumerationQuery();

                    EnumerationResult<Fleet> fleets = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(0, fleets.TotalRecords);
                    AssertEqual(0, fleets.TotalPages);
                    AssertEqual(0, fleets.Objects.Count);

                    EnumerationResult<Vessel> vessels = await db.Vessels.EnumerateAsync(query);
                    AssertEqual(0, vessels.TotalRecords);
                    AssertEqual(0, vessels.Objects.Count);

                    EnumerationResult<Captain> captains = await db.Captains.EnumerateAsync(query);
                    AssertEqual(0, captains.TotalRecords);
                    AssertEqual(0, captains.Objects.Count);

                    EnumerationResult<Mission> missions = await db.Missions.EnumerateAsync(query);
                    AssertEqual(0, missions.TotalRecords);
                    AssertEqual(0, missions.Objects.Count);

                    EnumerationResult<Voyage> voyages = await db.Voyages.EnumerateAsync(query);
                    AssertEqual(0, voyages.TotalRecords);
                    AssertEqual(0, voyages.Objects.Count);

                    EnumerationResult<Dock> docks = await db.Docks.EnumerateAsync(query);
                    AssertEqual(0, docks.TotalRecords);
                    AssertEqual(0, docks.Objects.Count);

                    EnumerationResult<Signal> signals = await db.Signals.EnumerateAsync(query);
                    AssertEqual(0, signals.TotalRecords);
                    AssertEqual(0, signals.Objects.Count);

                    EnumerationResult<ArmadaEvent> events = await db.Events.EnumerateAsync(query);
                    AssertEqual(0, events.TotalRecords);
                    AssertEqual(0, events.Objects.Count);
                }
            });

            await RunTest("All entities enumerate non-matching filter returns zero with empty objects", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = await db.Fleets.CreateAsync(CreateFleet("TestFleet", BaseTime));
                    Vessel vessel = await db.Vessels.CreateAsync(CreateVessel("TestVessel", fleet.Id, BaseTime));
                    Captain captain = await db.Captains.CreateAsync(CreateCaptain("TestCaptain", CaptainStateEnum.Idle, BaseTime));
                    Voyage voyage = await db.Voyages.CreateAsync(CreateVoyage("TestVoyage", VoyageStatusEnum.Open, BaseTime));
                    Mission mission = await db.Missions.CreateAsync(CreateMission("TestMission", MissionStatusEnum.Pending, vessel.Id, captain.Id, voyage.Id, BaseTime));
                    await db.Docks.CreateAsync(CreateDock(vessel.Id, captain.Id, BaseTime));
                    await db.Signals.CreateAsync(CreateSignal(SignalTypeEnum.Nudge, captain.Id, false, BaseTime));
                    await db.Events.CreateAsync(CreateEvent("test.event", captain.Id, mission.Id, vessel.Id, voyage.Id, BaseTime));

                    EnumerationQuery vesselQuery = new EnumerationQuery();
                    vesselQuery.FleetId = "flt_nonexistent";
                    EnumerationResult<Vessel> vesselResult = await db.Vessels.EnumerateAsync(vesselQuery);
                    AssertEqual(0, vesselResult.TotalRecords);
                    AssertEqual(0, vesselResult.Objects.Count);

                    EnumerationQuery missionQuery = new EnumerationQuery();
                    missionQuery.Status = "Cancelled";
                    EnumerationResult<Mission> missionResult = await db.Missions.EnumerateAsync(missionQuery);
                    AssertEqual(0, missionResult.TotalRecords);
                    AssertEqual(0, missionResult.Objects.Count);

                    EnumerationQuery captainQuery = new EnumerationQuery();
                    captainQuery.Status = "Stalled";
                    EnumerationResult<Captain> captainResult = await db.Captains.EnumerateAsync(captainQuery);
                    AssertEqual(0, captainResult.TotalRecords);
                    AssertEqual(0, captainResult.Objects.Count);

                    EnumerationQuery signalQuery = new EnumerationQuery();
                    signalQuery.ToCaptainId = "cpt_nonexistent";
                    EnumerationResult<Signal> signalResult = await db.Signals.EnumerateAsync(signalQuery);
                    AssertEqual(0, signalResult.TotalRecords);
                    AssertEqual(0, signalResult.Objects.Count);

                    EnumerationQuery eventQuery = new EnumerationQuery();
                    eventQuery.EventType = "nonexistent.type";
                    EnumerationResult<ArmadaEvent> eventResult = await db.Events.EnumerateAsync(eventQuery);
                    AssertEqual(0, eventResult.TotalRecords);
                    AssertEqual(0, eventResult.Objects.Count);

                    EnumerationQuery dockQuery = new EnumerationQuery();
                    dockQuery.VesselId = "vsl_nonexistent";
                    EnumerationResult<Dock> dockResult = await db.Docks.EnumerateAsync(dockQuery);
                    AssertEqual(0, dockResult.TotalRecords);
                    AssertEqual(0, dockResult.Objects.Count);
                }
            });

            await RunTest("All entities enumerate page size one paginates correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 3; i++)
                        await db.Fleets.CreateAsync(CreateFleet("PageSizeOneFleet-" + i, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;

                    query.PageNumber = 1;
                    EnumerationResult<Fleet> page1 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(1, page1.Objects.Count);
                    AssertEqual(3, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);
                    AssertEqual("PageSizeOneFleet-0", page1.Objects[0].Name);

                    query.PageNumber = 2;
                    EnumerationResult<Fleet> page2 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(1, page2.Objects.Count);
                    AssertEqual("PageSizeOneFleet-1", page2.Objects[0].Name);

                    query.PageNumber = 3;
                    EnumerationResult<Fleet> page3 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(1, page3.Objects.Count);
                    AssertEqual("PageSizeOneFleet-2", page3.Objects[0].Name);

                    query.PageNumber = 4;
                    EnumerationResult<Fleet> page4 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(0, page4.Objects.Count);
                }
            });

            await RunTest("All entities enumerate large page size returns single page", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 5; i++)
                        await db.Fleets.CreateAsync(CreateFleet("LargePageFleet-" + i, BaseTime.AddMinutes(i)));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 1000;
                    query.PageNumber = 1;
                    EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(query);

                    AssertEqual(5, result.Objects.Count);
                    AssertEqual(5, result.TotalRecords);
                    AssertEqual(1, result.TotalPages);
                }
            });

            await RunTest("All entities enumerate pagination consistent with ordering", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 10; i++)
                        await db.Fleets.CreateAsync(CreateFleet("OrderedFleet-" + i.ToString("D2"), BaseTime.AddMinutes(i)));

                    EnumerationQuery queryAsc = new EnumerationQuery();
                    queryAsc.Order = EnumerationOrderEnum.CreatedAscending;
                    queryAsc.PageSize = 3;

                    List<string> ascIds = new List<string>();
                    for (int page = 1; page <= 4; page++)
                    {
                        queryAsc.PageNumber = page;
                        EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(queryAsc);
                        foreach (Fleet f in result.Objects)
                            ascIds.Add(f.Id);
                    }
                    AssertEqual(10, ascIds.Count);

                    EnumerationQuery queryDesc = new EnumerationQuery();
                    queryDesc.Order = EnumerationOrderEnum.CreatedDescending;
                    queryDesc.PageSize = 3;

                    List<string> descIds = new List<string>();
                    for (int page = 1; page <= 4; page++)
                    {
                        queryDesc.PageNumber = page;
                        EnumerationResult<Fleet> result = await db.Fleets.EnumerateAsync(queryDesc);
                        foreach (Fleet f in result.Objects)
                            descIds.Add(f.Id);
                    }
                    AssertEqual(10, descIds.Count);

                    List<string> reversedDesc = new List<string>(descIds);
                    reversedDesc.Reverse();
                    AssertEqual(ascIds.Count, reversedDesc.Count);
                    for (int i = 0; i < ascIds.Count; i++)
                        AssertEqual(ascIds[i], reversedDesc[i]);
                }
            });
        }
    }
}
