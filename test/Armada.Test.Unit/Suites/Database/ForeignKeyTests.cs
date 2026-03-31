namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class ForeignKeyTests : TestSuite
    {
        public override string Name => "Foreign Key";

        protected override async Task RunTestsAsync()
        {
            await RunTest("DeleteFleet sets vessel FleetId null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("FK Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("FK Vessel", "https://github.com/test/repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    await db.Fleets.DeleteAsync(fleet.Id);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertNull(result!.FleetId);
                }
            });

            await RunTest("DeleteVoyage sets mission VoyageId null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("FK Voyage");
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("FK Mission");
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    await db.Voyages.DeleteAsync(voyage.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.VoyageId);
                }
            });

            await RunTest("DeleteCaptain sets mission CaptainId null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("fk-captain");
                    await db.Captains.CreateAsync(captain);

                    Mission mission = new Mission("FK Captain Mission");
                    mission.CaptainId = captain.Id;
                    await db.Missions.CreateAsync(mission);

                    await db.Captains.DeleteAsync(captain.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.CaptainId);
                }
            });

            await RunTest("DeleteVessel sets mission VesselId null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("FK Fleet2");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("FK Vessel2", "https://github.com/test/repo2");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Mission mission = new Mission("FK Vessel Mission");
                    mission.VesselId = vessel.Id;
                    await db.Missions.CreateAsync(mission);

                    await db.Vessels.DeleteAsync(vessel.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.VesselId);
                }
            });

            await RunTest("DeleteVessel cascade deletes docks", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Cascade Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Cascade Vessel", "https://github.com/test/cascade");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = "/path/to/worktree";
                    await db.Docks.CreateAsync(dock);

                    await db.Vessels.DeleteAsync(vessel.Id);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);
                    AssertNull(result);
                }
            });

            await RunTest("DeleteCaptain sets dock CaptainId null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Dock FK Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Dock FK Vessel", "https://github.com/test/dockfk");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Captain captain = new Captain("dock-fk-captain");
                    await db.Captains.CreateAsync(captain);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = "/path/to/worktree2";
                    dock.CaptainId = captain.Id;
                    await db.Docks.CreateAsync(dock);

                    await db.Captains.DeleteAsync(captain.Id);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);
                    AssertNotNull(result);
                    AssertNull(result!.CaptainId);
                }
            });

            await RunTest("DeleteCaptain sets signal CaptainIds null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain sender = new Captain("signal-sender");
                    Captain receiver = new Captain("signal-receiver");
                    await db.Captains.CreateAsync(sender);
                    await db.Captains.CreateAsync(receiver);

                    Signal signal = new Signal(SignalTypeEnum.Nudge, "test");
                    signal.FromCaptainId = sender.Id;
                    signal.ToCaptainId = receiver.Id;
                    await db.Signals.CreateAsync(signal);

                    await db.Captains.DeleteAsync(sender.Id);

                    Signal? result = await db.Signals.ReadAsync(signal.Id);
                    AssertNotNull(result);
                    AssertNull(result!.FromCaptainId);
                    AssertEqual(receiver.Id, result.ToCaptainId);
                }
            });
        }
    }
}
