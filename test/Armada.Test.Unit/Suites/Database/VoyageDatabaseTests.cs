namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class VoyageDatabaseTests : TestSuite
    {
        public override string Name => "Voyage Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns voyage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("Test Voyage", "Description");
                    Voyage result = await db.Voyages.CreateAsync(voyage);

                    AssertNotNull(result);
                    AssertEqual("Test Voyage", result.Title);
                }
            });

            await RunTest("ReadAsync returns created voyage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("Read Test");
                    await db.Voyages.CreateAsync(voyage);

                    Voyage? result = await db.Voyages.ReadAsync(voyage.Id);
                    AssertNotNull(result);
                    AssertEqual(voyage.Id, result!.Id);
                    AssertEqual(VoyageStatusEnum.Open, result.Status);
                }
            });

            await RunTest("UpdateAsync modifies voyage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("Original");
                    await db.Voyages.CreateAsync(voyage);

                    voyage.Title = "Updated";
                    voyage.Status = VoyageStatusEnum.Complete;
                    voyage.CompletedUtc = DateTime.UtcNow;
                    await db.Voyages.UpdateAsync(voyage);

                    Voyage? result = await db.Voyages.ReadAsync(voyage.Id);
                    AssertEqual("Updated", result!.Title);
                    AssertEqual(VoyageStatusEnum.Complete, result.Status);
                    AssertNotNull(result.CompletedUtc);
                }
            });

            await RunTest("DeleteAsync removes voyage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("ToDelete");
                    await db.Voyages.CreateAsync(voyage);

                    await db.Voyages.DeleteAsync(voyage.Id);
                    AssertNull(await db.Voyages.ReadAsync(voyage.Id));
                }
            });

            await RunTest("EnumerateByStatusAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage v1 = new Voyage("Open 1");
                    Voyage v2 = new Voyage("Complete");
                    v2.Status = VoyageStatusEnum.Complete;
                    Voyage v3 = new Voyage("Open 2");

                    await db.Voyages.CreateAsync(v1);
                    await db.Voyages.CreateAsync(v2);
                    await db.Voyages.CreateAsync(v3);

                    List<Voyage> open = await db.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.Open);
                    AssertEqual(2, open.Count);

                    List<Voyage> complete = await db.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.Complete);
                    AssertEqual(1, complete.Count);
                }
            });

            await RunTest("ExistsAsync works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("Exists Test");
                    await db.Voyages.CreateAsync(voyage);

                    AssertTrue(await db.Voyages.ExistsAsync(voyage.Id));
                    AssertFalse(await db.Voyages.ExistsAsync("vyg_nonexistent"));
                }
            });
        }
    }
}
