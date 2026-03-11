namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class FleetDatabaseTests : TestSuite
    {
        public override string Name => "Fleet Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns fleet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("TestFleet");
                    Fleet result = await db.Fleets.CreateAsync(fleet);

                    AssertNotNull(result);
                    AssertEqual("TestFleet", result.Name);
                }
            });

            await RunTest("ReadAsync returns created fleet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ReadTest");
                    await db.Fleets.CreateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadAsync(fleet.Id);
                    AssertNotNull(result);
                    AssertEqual(fleet.Id, result!.Id);
                    AssertEqual("ReadTest", result.Name);
                }
            });

            await RunTest("ReadAsync non-existent returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet? result = await db.Fleets.ReadAsync("flt_nonexistent");
                    AssertNull(result);
                }
            });

            await RunTest("ReadByNameAsync returns correct fleet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("NameLookup");
                    await db.Fleets.CreateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadByNameAsync("NameLookup");
                    AssertNotNull(result);
                    AssertEqual(fleet.Id, result!.Id);
                }
            });

            await RunTest("ReadByNameAsync non-existent returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet? result = await db.Fleets.ReadByNameAsync("NoSuchFleet");
                    AssertNull(result);
                }
            });

            await RunTest("UpdateAsync modifies fleet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Original");
                    await db.Fleets.CreateAsync(fleet);

                    fleet.Name = "Updated";
                    fleet.Description = "New description";
                    fleet.Active = false;
                    await db.Fleets.UpdateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadAsync(fleet.Id);
                    AssertNotNull(result);
                    AssertEqual("Updated", result!.Name);
                    AssertEqual("New description", result.Description);
                    AssertFalse(result.Active);
                }
            });

            await RunTest("DeleteAsync removes fleet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ToDelete");
                    await db.Fleets.CreateAsync(fleet);

                    await db.Fleets.DeleteAsync(fleet.Id);

                    Fleet? result = await db.Fleets.ReadAsync(fleet.Id);
                    AssertNull(result);
                }
            });

            await RunTest("EnumerateAsync returns all fleets", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await db.Fleets.CreateAsync(new Fleet("Alpha"));
                    await db.Fleets.CreateAsync(new Fleet("Beta"));
                    await db.Fleets.CreateAsync(new Fleet("Charlie"));

                    List<Fleet> results = await db.Fleets.EnumerateAsync();
                    AssertEqual(3, results.Count);
                }
            });

            await RunTest("EnumerateAsync ordered by name", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await db.Fleets.CreateAsync(new Fleet("Charlie"));
                    await db.Fleets.CreateAsync(new Fleet("Alpha"));
                    await db.Fleets.CreateAsync(new Fleet("Beta"));

                    List<Fleet> results = await db.Fleets.EnumerateAsync();
                    AssertEqual("Alpha", results[0].Name);
                    AssertEqual("Beta", results[1].Name);
                    AssertEqual("Charlie", results[2].Name);
                }
            });

            await RunTest("ExistsAsync returns true for existing", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ExistTest");
                    await db.Fleets.CreateAsync(fleet);

                    bool exists = await db.Fleets.ExistsAsync(fleet.Id);
                    AssertTrue(exists);
                }
            });

            await RunTest("ExistsAsync returns false for non-existent", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    bool exists = await db.Fleets.ExistsAsync("flt_nonexistent");
                    AssertFalse(exists);
                }
            });
        }
    }
}
