namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class MissionDatabaseTests : TestSuite
    {
        public override string Name => "Mission Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns mission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Mission mission = new Mission("Test Mission", "Test description");
                    Mission result = await db.Missions.CreateAsync(mission);

                    AssertNotNull(result);
                    AssertEqual("Test Mission", result.Title);
                }
            });

            await RunTest("ReadAsync returns created mission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Mission mission = new Mission("Read Test");
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual(mission.Id, result!.Id);
                    AssertEqual("Read Test", result.Title);
                    AssertEqual(MissionStatusEnum.Pending, result.Status);
                }
            });

            await RunTest("UpdateAsync modifies mission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Mission mission = new Mission("Original");
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("update-captain");
                    await db.Captains.CreateAsync(captain);

                    mission.Title = "Updated";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.Priority = 10;
                    mission.CaptainId = captain.Id;
                    mission.StartedUtc = DateTime.UtcNow;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual("Updated", result!.Title);
                    AssertEqual(MissionStatusEnum.InProgress, result.Status);
                    AssertEqual(10, result.Priority);
                    AssertEqual(captain.Id, result.CaptainId);
                    AssertNotNull(result.StartedUtc);
                }
            });

            await RunTest("DeleteAsync removes mission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Mission mission = new Mission("ToDelete");
                    await db.Missions.CreateAsync(mission);

                    await db.Missions.DeleteAsync(mission.Id);
                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNull(result);
                }
            });

            await RunTest("EnumerateByStatusAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Mission m1 = new Mission("Pending 1");
                    Mission m2 = new Mission("In Progress");
                    m2.Status = MissionStatusEnum.InProgress;
                    Mission m3 = new Mission("Pending 2");

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);

                    List<Mission> pending = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending);
                    AssertEqual(2, pending.Count);

                    List<Mission> inProgress = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                    AssertEqual(1, inProgress.Count);
                    AssertEqual("In Progress", inProgress[0].Title);
                }
            });

            await RunTest("EnumerateByVoyageAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("Test Voyage");
                    await db.Voyages.CreateAsync(voyage);

                    Mission m1 = new Mission("V Mission");
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Standalone");

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);

                    List<Mission> voyageMissions = await db.Missions.EnumerateByVoyageAsync(voyage.Id);
                    AssertEqual(1, voyageMissions.Count);
                    AssertEqual("V Mission", voyageMissions[0].Title);
                }
            });

            await RunTest("EnumerateByVesselAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("TestFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Mission m1 = new Mission("Vessel Mission");
                    m1.VesselId = vessel.Id;
                    Mission m2 = new Mission("Other Mission");

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);

                    List<Mission> vesselMissions = await db.Missions.EnumerateByVesselAsync(vessel.Id);
                    AssertEqual(1, vesselMissions.Count);
                }
            });

            await RunTest("EnumerateByCaptainAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("test-captain");
                    await db.Captains.CreateAsync(captain);

                    Mission m1 = new Mission("Captain Mission");
                    m1.CaptainId = captain.Id;
                    Mission m2 = new Mission("Unassigned");

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);

                    List<Mission> captainMissions = await db.Missions.EnumerateByCaptainAsync(captain.Id);
                    AssertEqual(1, captainMissions.Count);
                }
            });

            await RunTest("ExistsAsync works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Mission mission = new Mission("Exists Test");
                    await db.Missions.CreateAsync(mission);

                    AssertTrue(await db.Missions.ExistsAsync(mission.Id));
                    AssertFalse(await db.Missions.ExistsAsync("msn_nonexistent"));
                }
            });
        }
    }
}
