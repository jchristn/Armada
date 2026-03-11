namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class DockDatabaseTests : TestSuite
    {
        public override string Name => "Dock Database";

        private async Task<(TestDatabase testDb, Vessel vessel)> SetupWithVesselAsync()
        {
            TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
            SqliteDatabaseDriver db = testDb.Driver;
            Fleet fleet = new Fleet("TestFleet");
            await db.Fleets.CreateAsync(fleet);
            Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel);
            return (testDb, vessel);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns dock", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = "/tmp/worktree";
                    dock.BranchName = "armada/test";
                    Dock result = await db.Docks.CreateAsync(dock);

                    AssertNotNull(result);
                    AssertEqual(vessel.Id, result.VesselId);
                }
            });

            await RunTest("ReadAsync returns created dock", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock dock = new Dock(vessel.Id);
                    await db.Docks.CreateAsync(dock);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);
                    AssertNotNull(result);
                    AssertEqual(dock.Id, result!.Id);
                    AssertTrue(result.Active);
                }
            });

            await RunTest("EnumerateByVesselAsync filters correctly", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock d1 = new Dock(vessel.Id);
                    Dock d2 = new Dock(vessel.Id);
                    await db.Docks.CreateAsync(d1);
                    await db.Docks.CreateAsync(d2);

                    List<Dock> docks = await db.Docks.EnumerateByVesselAsync(vessel.Id);
                    AssertEqual(2, docks.Count);
                }
            });

            await RunTest("FindAvailableAsync returns unassigned active dock", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock available = new Dock(vessel.Id);
                    await db.Docks.CreateAsync(available);

                    Captain captain = new Captain("test-captain");
                    await db.Captains.CreateAsync(captain);
                    Dock assigned = new Dock(vessel.Id);
                    assigned.CaptainId = captain.Id;
                    await db.Docks.CreateAsync(assigned);

                    Dock? found = await db.Docks.FindAvailableAsync(vessel.Id);
                    AssertNotNull(found);
                    AssertEqual(available.Id, found!.Id);
                    AssertNull(found.CaptainId);
                }
            });

            await RunTest("FindAvailableAsync returns null when none available", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock? found = await db.Docks.FindAvailableAsync(vessel.Id);
                    AssertNull(found);
                }
            });

            await RunTest("DeleteAsync removes dock", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock dock = new Dock(vessel.Id);
                    await db.Docks.CreateAsync(dock);

                    await db.Docks.DeleteAsync(dock.Id);
                    AssertNull(await db.Docks.ReadAsync(dock.Id));
                }
            });

            await RunTest("ExistsAsync works correctly", async () =>
            {
                (TestDatabase testDb, Vessel vessel) = await SetupWithVesselAsync();
                using (testDb)
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Dock dock = new Dock(vessel.Id);
                    await db.Docks.CreateAsync(dock);

                    AssertTrue(await db.Docks.ExistsAsync(dock.Id));
                    AssertFalse(await db.Docks.ExistsAsync("dck_nonexistent"));
                }
            });
        }
    }
}
