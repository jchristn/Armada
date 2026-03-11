namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class ConcurrentAccessTests : TestSuite
    {
        public override string Name => "Concurrent Access";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Concurrent creates all succeed", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    int count = 20;
                    Task<Fleet>[] tasks = new Task<Fleet>[count];

                    for (int i = 0; i < count; i++)
                    {
                        int idx = i;
                        tasks[i] = db.Fleets.CreateAsync(new Fleet("Concurrent Fleet " + idx));
                    }

                    Fleet[] results = await Task.WhenAll(tasks);

                    AssertEqual(count, results.Length);
                    List<Fleet> allFleets = await db.Fleets.EnumerateAsync();
                    AssertEqual(count, allFleets.Count);
                }
            });

            await RunTest("Concurrent reads all succeed", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Read Target");
                    await db.Fleets.CreateAsync(fleet);

                    int count = 20;
                    Task<Fleet?>[] tasks = new Task<Fleet?>[count];

                    for (int i = 0; i < count; i++)
                    {
                        tasks[i] = db.Fleets.ReadAsync(fleet.Id);
                    }

                    Fleet?[] results = await Task.WhenAll(tasks);

                    foreach (Fleet? result in results)
                    {
                        AssertNotNull(result);
                        AssertEqual("Read Target", result!.Name);
                    }
                }
            });

            await RunTest("Concurrent mixed operations all succeed", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Fleets.CreateAsync(new Fleet("Initial " + i));
                    }

                    List<Task> tasks = new List<Task>();
                    for (int i = 0; i < 10; i++)
                    {
                        int idx = i;
                        tasks.Add(db.Fleets.CreateAsync(new Fleet("Mixed " + idx)));
                        tasks.Add(db.Fleets.EnumerateAsync());
                    }

                    await Task.WhenAll(tasks);

                    List<Fleet> allFleets = await db.Fleets.EnumerateAsync();
                    AssertEqual(15, allFleets.Count);
                }
            });
        }
    }
}
