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
    /// Comprehensive CRUD and enumeration tests for Mission database operations.
    /// </summary>
    public class MissionDatabaseTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Mission Database";

        #endregion

        #region Private-Members

        private static DateTime _BaseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all mission database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await Mission_Create();
            await Mission_Read();
            await Mission_Update();
            await Mission_Exists();
            await Mission_Enumerate();
            await Mission_EnumeratePaginated();
            await Mission_EnumerateByVoyage();
            await Mission_EnumerateByVessel();
            await Mission_EnumerateByCaptain();
            await Mission_EnumerateByStatus();
            await Mission_Delete();
            await Mission_ReadNotFound();
            await Mission_ExistsNotFound();
        }

        #endregion

        #region Private-Methods

        private async Task<(Fleet fleet, Vessel vessel, Voyage voyage)> CreatePrerequisitesAsync(SqliteDatabaseDriver db)
        {
            Fleet fleet = new Fleet("Test Fleet");
            fleet.CreatedUtc = _BaseTime;
            await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);

            Vessel vessel = new Vessel("Test Vessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            vessel.CreatedUtc = _BaseTime;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Voyage voyage = new Voyage("Test Voyage", "Test voyage description");
            voyage.CreatedUtc = _BaseTime;
            await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);

            return (fleet, vessel, voyage);
        }

        private async Task Mission_Create()
        {
            await RunTest("Mission_Create", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Create Test Mission", "A test mission description");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Priority = 5;
                    mission.BranchName = "feature/test";

                    Mission result = await db.Missions.CreateAsync(mission);

                    AssertNotNull(result);
                    AssertStartsWith("msn_", result.Id);
                    AssertEqual("Create Test Mission", result.Title);
                    AssertEqual("A test mission description", result.Description);
                    AssertEqual(vessel.Id, result.VesselId);
                    AssertEqual(voyage.Id, result.VoyageId);
                    AssertEqual(MissionStatusEnum.Pending, result.Status);
                    AssertEqual(5, result.Priority);
                    AssertEqual("feature/test", result.BranchName);
                }
            });
        }

        private async Task Mission_Read()
        {
            await RunTest("Mission_Read", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Read Test Mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Priority = 10;
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);

                    AssertNotNull(result);
                    AssertEqual(mission.Id, result!.Id);
                    AssertEqual("Read Test Mission", result.Title);
                    AssertEqual(vessel.Id, result.VesselId);
                    AssertEqual(voyage.Id, result.VoyageId);
                    AssertEqual(MissionStatusEnum.Pending, result.Status);
                    AssertEqual(10, result.Priority);
                }
            });
        }

        private async Task Mission_Update()
        {
            await RunTest("Mission_Update", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Captain captain = new Captain("update-captain");
                    await db.Captains.CreateAsync(captain);

                    Mission mission = new Mission("Original Title");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    mission.Title = "Updated Title";
                    mission.Description = "Updated description";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.Priority = 1;
                    mission.CaptainId = captain.Id;
                    mission.BranchName = "feature/updated";
                    mission.StartedUtc = DateTime.UtcNow;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);

                    AssertNotNull(result);
                    AssertEqual("Updated Title", result!.Title);
                    AssertEqual("Updated description", result.Description);
                    AssertEqual(MissionStatusEnum.InProgress, result.Status);
                    AssertEqual(1, result.Priority);
                    AssertEqual(captain.Id, result.CaptainId);
                    AssertEqual("feature/updated", result.BranchName);
                    AssertNotNull(result.StartedUtc);
                }
            });
        }

        private async Task Mission_Exists()
        {
            await RunTest("Mission_Exists", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Exists Test Mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    bool exists = await db.Missions.ExistsAsync(mission.Id);
                    AssertTrue(exists);
                }
            });
        }

        private async Task Mission_Enumerate()
        {
            await RunTest("Mission_Enumerate", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Mission m1 = new Mission("Enumerate Mission 1");
                    m1.VesselId = vessel.Id;
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Enumerate Mission 2");
                    m2.VesselId = vessel.Id;
                    m2.VoyageId = voyage.Id;
                    Mission m3 = new Mission("Enumerate Mission 3");
                    m3.VesselId = vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);

                    List<Mission> all = await db.Missions.EnumerateAsync();

                    AssertEqual(3, all.Count);
                }
            });
        }

        private async Task Mission_EnumeratePaginated()
        {
            await RunTest("Mission_EnumeratePaginated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    for (int i = 0; i < 10; i++)
                    {
                        Mission mission = new Mission("Paginated Mission " + i.ToString("D2"));
                        mission.VesselId = vessel.Id;
                        mission.VoyageId = voyage.Id;
                        mission.CreatedUtc = _BaseTime.AddMinutes(i);
                        await db.Missions.CreateAsync(mission);
                    }

                    EnumerationQuery queryPage1 = new EnumerationQuery();
                    queryPage1.PageNumber = 1;
                    queryPage1.PageSize = 3;
                    queryPage1.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page1 = await db.Missions.EnumerateAsync(queryPage1);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(10, (int)page1.TotalRecords);
                    AssertEqual(4, page1.TotalPages);
                    AssertEqual(1, page1.PageNumber);
                    AssertEqual(3, page1.PageSize);

                    EnumerationQuery queryPage2 = new EnumerationQuery();
                    queryPage2.PageNumber = 2;
                    queryPage2.PageSize = 3;
                    queryPage2.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page2 = await db.Missions.EnumerateAsync(queryPage2);

                    AssertEqual(3, page2.Objects.Count);
                    AssertEqual(10, (int)page2.TotalRecords);

                    EnumerationQuery queryPage4 = new EnumerationQuery();
                    queryPage4.PageNumber = 4;
                    queryPage4.PageSize = 3;
                    queryPage4.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page4 = await db.Missions.EnumerateAsync(queryPage4);

                    AssertEqual(1, page4.Objects.Count);

                    EnumerationQuery queryPage5 = new EnumerationQuery();
                    queryPage5.PageNumber = 5;
                    queryPage5.PageSize = 3;
                    queryPage5.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page5 = await db.Missions.EnumerateAsync(queryPage5);

                    AssertEqual(0, page5.Objects.Count);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Mission m in page1.Objects) allIds.Add(m.Id);
                    foreach (Mission m in page2.Objects) allIds.Add(m.Id);
                    AssertEqual(6, allIds.Count);
                }
            });
        }

        private async Task Mission_EnumerateByVoyage()
        {
            await RunTest("Mission_EnumerateByVoyage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Voyage voyage2 = new Voyage("Other Voyage");
                    await db.Voyages.CreateAsync(voyage2);

                    Mission m1 = new Mission("Voyage 1 Mission A");
                    m1.VesselId = vessel.Id;
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Voyage 1 Mission B");
                    m2.VesselId = vessel.Id;
                    m2.VoyageId = voyage.Id;
                    Mission m3 = new Mission("Voyage 2 Mission");
                    m3.VesselId = vessel.Id;
                    m3.VoyageId = voyage2.Id;
                    Mission m4 = new Mission("No Voyage Mission");
                    m4.VesselId = vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);
                    await db.Missions.CreateAsync(m4);

                    List<Mission> voyage1Missions = await db.Missions.EnumerateByVoyageAsync(voyage.Id);
                    AssertEqual(2, voyage1Missions.Count);

                    List<Mission> voyage2Missions = await db.Missions.EnumerateByVoyageAsync(voyage2.Id);
                    AssertEqual(1, voyage2Missions.Count);
                    AssertEqual("Voyage 2 Mission", voyage2Missions[0].Title);
                }
            });
        }

        private async Task Mission_EnumerateByVessel()
        {
            await RunTest("Mission_EnumerateByVessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Vessel vessel2 = new Vessel("Second Vessel", "https://github.com/test/repo2");
                    vessel2.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel2);

                    Mission m1 = new Mission("Vessel 1 Mission");
                    m1.VesselId = vessel.Id;
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Vessel 2 Mission A");
                    m2.VesselId = vessel2.Id;
                    Mission m3 = new Mission("Vessel 2 Mission B");
                    m3.VesselId = vessel2.Id;
                    Mission m4 = new Mission("No Vessel Mission");

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);
                    await db.Missions.CreateAsync(m4);

                    List<Mission> vessel1Missions = await db.Missions.EnumerateByVesselAsync(vessel.Id);
                    AssertEqual(1, vessel1Missions.Count);
                    AssertEqual("Vessel 1 Mission", vessel1Missions[0].Title);

                    List<Mission> vessel2Missions = await db.Missions.EnumerateByVesselAsync(vessel2.Id);
                    AssertEqual(2, vessel2Missions.Count);
                }
            });
        }

        private async Task Mission_EnumerateByCaptain()
        {
            await RunTest("Mission_EnumerateByCaptain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Captain captain1 = new Captain("captain-alpha");
                    await db.Captains.CreateAsync(captain1);
                    Captain captain2 = new Captain("captain-beta");
                    await db.Captains.CreateAsync(captain2);

                    Mission m1 = new Mission("Captain 1 Mission A");
                    m1.VesselId = vessel.Id;
                    m1.CaptainId = captain1.Id;
                    Mission m2 = new Mission("Captain 1 Mission B");
                    m2.VesselId = vessel.Id;
                    m2.CaptainId = captain1.Id;
                    Mission m3 = new Mission("Captain 2 Mission");
                    m3.VesselId = vessel.Id;
                    m3.CaptainId = captain2.Id;
                    Mission m4 = new Mission("Unassigned Mission");
                    m4.VesselId = vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);
                    await db.Missions.CreateAsync(m4);

                    List<Mission> captain1Missions = await db.Missions.EnumerateByCaptainAsync(captain1.Id);
                    AssertEqual(2, captain1Missions.Count);

                    List<Mission> captain2Missions = await db.Missions.EnumerateByCaptainAsync(captain2.Id);
                    AssertEqual(1, captain2Missions.Count);
                    AssertEqual("Captain 2 Mission", captain2Missions[0].Title);
                }
            });
        }

        private async Task Mission_EnumerateByStatus()
        {
            await RunTest("Mission_EnumerateByStatus", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Mission mPending = new Mission("Pending Mission");
                    mPending.VesselId = vessel.Id;
                    mPending.Status = MissionStatusEnum.Pending;

                    Mission mInProgress1 = new Mission("InProgress Mission 1");
                    mInProgress1.VesselId = vessel.Id;
                    mInProgress1.Status = MissionStatusEnum.InProgress;

                    Mission mInProgress2 = new Mission("InProgress Mission 2");
                    mInProgress2.VesselId = vessel.Id;
                    mInProgress2.Status = MissionStatusEnum.InProgress;

                    Mission mComplete = new Mission("Complete Mission");
                    mComplete.VesselId = vessel.Id;
                    mComplete.Status = MissionStatusEnum.Complete;

                    Mission mFailed = new Mission("Failed Mission");
                    mFailed.VesselId = vessel.Id;
                    mFailed.Status = MissionStatusEnum.Failed;

                    Mission mCancelled = new Mission("Cancelled Mission");
                    mCancelled.VesselId = vessel.Id;
                    mCancelled.Status = MissionStatusEnum.Cancelled;

                    await db.Missions.CreateAsync(mPending);
                    await db.Missions.CreateAsync(mInProgress1);
                    await db.Missions.CreateAsync(mInProgress2);
                    await db.Missions.CreateAsync(mComplete);
                    await db.Missions.CreateAsync(mFailed);
                    await db.Missions.CreateAsync(mCancelled);

                    List<Mission> pending = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending);
                    AssertEqual(1, pending.Count);
                    AssertEqual("Pending Mission", pending[0].Title);

                    List<Mission> inProgress = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                    AssertEqual(2, inProgress.Count);

                    List<Mission> complete = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Complete);
                    AssertEqual(1, complete.Count);
                    AssertEqual("Complete Mission", complete[0].Title);

                    List<Mission> failed = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed);
                    AssertEqual(1, failed.Count);

                    List<Mission> cancelled = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Cancelled);
                    AssertEqual(1, cancelled.Count);

                    List<Mission> assigned = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Assigned);
                    AssertEqual(0, assigned.Count);
                }
            });
        }

        private async Task Mission_Delete()
        {
            await RunTest("Mission_Delete", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (Fleet fleet, Vessel vessel, Voyage voyage) = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Delete Test Mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    bool existsBefore = await db.Missions.ExistsAsync(mission.Id);
                    AssertTrue(existsBefore);

                    await db.Missions.DeleteAsync(mission.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNull(result);

                    bool existsAfter = await db.Missions.ExistsAsync(mission.Id);
                    AssertFalse(existsAfter);
                }
            });
        }

        private async Task Mission_ReadNotFound()
        {
            await RunTest("Mission_ReadNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Mission? result = await db.Missions.ReadAsync("msn_nonexistent_id_12345");
                    AssertNull(result);
                }
            });
        }

        private async Task Mission_ExistsNotFound()
        {
            await RunTest("Mission_ExistsNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    bool exists = await db.Missions.ExistsAsync("msn_nonexistent_id_12345");
                    AssertFalse(exists);
                }
            });
        }

        #endregion
    }
}
