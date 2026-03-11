namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class DatabaseInitializationTests : TestSuite
    {
        public override string Name => "Database Initialization";

        protected override async Task RunTestsAsync()
        {
            await RunTest("InitializeAsync creates all tables", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    AssertNotNull(db.Fleets);
                    AssertNotNull(db.Vessels);
                    AssertNotNull(db.Captains);
                    AssertNotNull(db.Missions);
                    AssertNotNull(db.Voyages);
                    AssertNotNull(db.Docks);
                    AssertNotNull(db.Signals);
                    AssertNotNull(db.Events);
                }
            });

            await RunTest("InitializeAsync tables are queryable", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    List<Fleet> fleets = await db.Fleets.EnumerateAsync();
                    AssertEqual(0, fleets.Count);

                    List<Vessel> vessels = await db.Vessels.EnumerateAsync();
                    AssertEqual(0, vessels.Count);

                    List<Captain> captains = await db.Captains.EnumerateAsync();
                    AssertEqual(0, captains.Count);

                    List<Mission> missions = await db.Missions.EnumerateAsync();
                    AssertEqual(0, missions.Count);

                    List<Voyage> voyages = await db.Voyages.EnumerateAsync();
                    AssertEqual(0, voyages.Count);

                    List<Dock> docks = await db.Docks.EnumerateAsync();
                    AssertEqual(0, docks.Count);
                }
            });
        }
    }
}
