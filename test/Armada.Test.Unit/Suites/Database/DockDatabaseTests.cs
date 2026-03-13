namespace Armada.Test.Unit.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// CRUD and query tests for the Dock database methods.
    /// </summary>
    public class DockDatabaseTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Dock Database";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all dock database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Dock_Create", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    Dock dock = new Dock(setup.Vessel.Id);
                    dock.WorktreePath = "/tmp/worktree";
                    dock.BranchName = "armada/test";

                    Dock result = await db.Docks.CreateAsync(dock);

                    AssertNotNull(result);
                    AssertEqual(dock.Id, result.Id);
                    AssertEqual(setup.Vessel.Id, result.VesselId);
                    AssertEqual("/tmp/worktree", result.WorktreePath);
                    AssertEqual("armada/test", result.BranchName);
                    AssertTrue(result.Active);
                }
            });

            await RunTest("Dock_Read", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    Dock dock = new Dock(setup.Vessel.Id);
                    dock.WorktreePath = "/tmp/worktree";
                    dock.BranchName = "armada/test";
                    await db.Docks.CreateAsync(dock);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);

                    AssertNotNull(result);
                    AssertEqual(dock.Id, result!.Id);
                    AssertEqual(setup.Vessel.Id, result.VesselId);
                    AssertEqual("/tmp/worktree", result.WorktreePath);
                    AssertEqual("armada/test", result.BranchName);
                    AssertTrue(result.Active);
                }
            });

            await RunTest("Dock_Update", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    Dock dock = new Dock(setup.Vessel.Id);
                    dock.WorktreePath = "/tmp/worktree";
                    dock.BranchName = "armada/test";
                    await db.Docks.CreateAsync(dock);

                    dock.WorktreePath = "/tmp/worktree-updated";
                    dock.BranchName = "armada/updated";
                    dock.Active = false;
                    Dock updated = await db.Docks.UpdateAsync(dock);

                    AssertNotNull(updated);
                    AssertEqual("/tmp/worktree-updated", updated.WorktreePath);
                    AssertEqual("armada/updated", updated.BranchName);
                    AssertFalse(updated.Active);

                    Dock? readBack = await db.Docks.ReadAsync(dock.Id);
                    AssertNotNull(readBack);
                    AssertEqual("/tmp/worktree-updated", readBack!.WorktreePath);
                    AssertEqual("armada/updated", readBack.BranchName);
                    AssertFalse(readBack.Active);
                }
            });

            await RunTest("Dock_Exists", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    Dock dock = new Dock(setup.Vessel.Id);
                    await db.Docks.CreateAsync(dock);

                    bool exists = await db.Docks.ExistsAsync(dock.Id);
                    AssertTrue(exists);
                }
            });

            await RunTest("Dock_Enumerate", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    Dock d1 = new Dock(setup.Vessel.Id);
                    Dock d2 = new Dock(setup.Vessel.Id);
                    Dock d3 = new Dock(setup.Vessel.Id);
                    await db.Docks.CreateAsync(d1);
                    await db.Docks.CreateAsync(d2);
                    await db.Docks.CreateAsync(d3);

                    List<Dock> docks = await db.Docks.EnumerateAsync();

                    AssertNotNull(docks);
                    AssertEqual(3, docks.Count);
                }
            });

            await RunTest("Dock_EnumeratePaginated", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    for (int i = 0; i < 5; i++)
                    {
                        Dock dock = new Dock(setup.Vessel.Id);
                        await db.Docks.CreateAsync(dock);
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;

                    EnumerationResult<Dock> result = await db.Docks.EnumerateAsync(query);

                    AssertNotNull(result);
                    AssertTrue(result.Success);
                    AssertEqual(2, result.Objects.Count);
                    AssertEqual(5, (int)result.TotalRecords);
                    AssertEqual(3, result.TotalPages);
                    AssertEqual(1, result.PageNumber);
                    AssertEqual(2, result.PageSize);

                    EnumerationQuery queryPage2 = new EnumerationQuery();
                    queryPage2.PageSize = 2;
                    queryPage2.PageNumber = 2;

                    EnumerationResult<Dock> page2 = await db.Docks.EnumerateAsync(queryPage2);

                    AssertEqual(2, page2.Objects.Count);
                    AssertEqual(2, page2.PageNumber);
                }
            });

            await RunTest("Dock_EnumerateByVessel", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;

                    Vessel otherVessel = new Vessel("OtherVessel", "https://github.com/test/other");
                    otherVessel.FleetId = setup.Vessel.FleetId;
                    await db.Vessels.CreateAsync(otherVessel);

                    Dock d1 = new Dock(setup.Vessel.Id);
                    Dock d2 = new Dock(setup.Vessel.Id);
                    Dock d3 = new Dock(otherVessel.Id);
                    await db.Docks.CreateAsync(d1);
                    await db.Docks.CreateAsync(d2);
                    await db.Docks.CreateAsync(d3);

                    List<Dock> docks = await db.Docks.EnumerateByVesselAsync(setup.Vessel.Id);

                    AssertEqual(2, docks.Count);
                    foreach (Dock d in docks)
                    {
                        AssertEqual(setup.Vessel.Id, d.VesselId);
                    }
                }
            });

            await RunTest("Dock_FindAvailable_Found", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;

                    Dock available = new Dock(setup.Vessel.Id);
                    available.Active = true;
                    await db.Docks.CreateAsync(available);

                    Captain captain = new Captain("test-captain");
                    await db.Captains.CreateAsync(captain);
                    Dock assigned = new Dock(setup.Vessel.Id);
                    assigned.CaptainId = captain.Id;
                    await db.Docks.CreateAsync(assigned);

                    Dock? found = await db.Docks.FindAvailableAsync(setup.Vessel.Id);

                    AssertNotNull(found);
                    AssertEqual(available.Id, found!.Id);
                    AssertNull(found.CaptainId);
                    AssertTrue(found.Active);
                }
            });

            await RunTest("Dock_FindAvailable_None", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;

                    Captain captain = new Captain("test-captain");
                    await db.Captains.CreateAsync(captain);

                    Dock assigned1 = new Dock(setup.Vessel.Id);
                    assigned1.CaptainId = captain.Id;
                    await db.Docks.CreateAsync(assigned1);

                    Dock assigned2 = new Dock(setup.Vessel.Id);
                    assigned2.CaptainId = captain.Id;
                    await db.Docks.CreateAsync(assigned2);

                    Dock? found = await db.Docks.FindAvailableAsync(setup.Vessel.Id);

                    AssertNull(found);
                }
            });

            await RunTest("Dock_Delete", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;
                    Dock dock = new Dock(setup.Vessel.Id);
                    await db.Docks.CreateAsync(dock);

                    await db.Docks.DeleteAsync(dock.Id);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);
                    AssertNull(result);
                }
            });

            await RunTest("Dock_ReadNotFound", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;

                    Dock? result = await db.Docks.ReadAsync("dck_nonexistent");

                    AssertNull(result);
                }
            });

            await RunTest("Dock_ExistsNotFound", async () =>
            {
                TestDatabaseSetup setup = await SetupWithVesselAsync();
                using (setup.Database)
                {
                    SqliteDatabaseDriver db = setup.Database.Driver;

                    bool exists = await db.Docks.ExistsAsync("dck_nonexistent");

                    AssertFalse(exists);
                }
            });
        }

        #endregion

        #region Private-Methods

        private async Task<TestDatabaseSetup> SetupWithVesselAsync()
        {
            TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
            SqliteDatabaseDriver db = testDb.Driver;

            Fleet fleet = new Fleet("TestFleet");
            await db.Fleets.CreateAsync(fleet);

            Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel);

            return new TestDatabaseSetup(testDb, vessel);
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Helper to hold test database and vessel references.
        /// </summary>
        private class TestDatabaseSetup
        {
            /// <summary>
            /// The test database instance.
            /// </summary>
            public TestDatabase Database { get; }

            /// <summary>
            /// The vessel created for test setup.
            /// </summary>
            public Vessel Vessel { get; }

            /// <summary>
            /// Instantiate.
            /// </summary>
            /// <param name="database">Test database.</param>
            /// <param name="vessel">Vessel.</param>
            public TestDatabaseSetup(TestDatabase database, Vessel vessel)
            {
                Database = database ?? throw new ArgumentNullException(nameof(database));
                Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
            }
        }

        #endregion
    }
}
