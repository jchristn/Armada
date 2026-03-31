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

    /// <summary>
    /// Fleet database CRUD test suite.
    /// </summary>
    public class FleetDatabaseTests : TestSuite
    {
        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Fleet Database";

        /// <summary>
        /// Run all fleet database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Fleet_Create", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("TestFleet");
                    Fleet result = await db.Fleets.CreateAsync(fleet);

                    AssertNotNull(result, "Created fleet");
                    AssertNotNull(result.Id, "Fleet ID");
                    AssertStartsWith("flt_", result.Id, "Fleet ID prefix");
                    AssertEqual("TestFleet", result.Name, "Fleet name");
                    AssertTrue(result.CreatedUtc <= DateTime.UtcNow, "CreatedUtc is set");
                    AssertTrue(result.LastUpdateUtc <= DateTime.UtcNow, "LastUpdateUtc is set");
                }
            });

            await RunTest("Fleet_Read", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ReadTest");
                    fleet.Description = "A test fleet";
                    fleet.Active = true;
                    Fleet created = await db.Fleets.CreateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadAsync(created.Id);
                    AssertNotNull(result, "Read fleet");
                    AssertEqual(created.Id, result!.Id, "Fleet ID");
                    AssertEqual("ReadTest", result.Name, "Fleet name");
                    AssertEqual("A test fleet", result.Description, "Fleet description");
                    AssertTrue(result.Active, "Fleet active");
                }
            });

            await RunTest("Fleet_ReadByName", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("NameLookup");
                    Fleet created = await db.Fleets.CreateAsync(fleet);

                    Fleet? result = await db.Fleets.ReadByNameAsync("NameLookup");
                    AssertNotNull(result, "Read by name");
                    AssertEqual(created.Id, result!.Id, "Fleet ID matches");
                    AssertEqual("NameLookup", result.Name, "Fleet name");
                }
            });

            await RunTest("Fleet_Update", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Original");
                    Fleet created = await db.Fleets.CreateAsync(fleet);
                    DateTime originalLastUpdate = created.LastUpdateUtc;

                    await Task.Delay(50).ConfigureAwait(false);

                    created.Name = "Updated";
                    created.Description = "New description";
                    created.Active = false;
                    Fleet updated = await db.Fleets.UpdateAsync(created);

                    Fleet? result = await db.Fleets.ReadAsync(created.Id);
                    AssertNotNull(result, "Updated fleet");
                    AssertEqual("Updated", result!.Name, "Updated name");
                    AssertEqual("New description", result.Description, "Updated description");
                    AssertFalse(result.Active, "Updated active");
                    AssertTrue(result.LastUpdateUtc >= originalLastUpdate, "LastUpdateUtc changed");
                }
            });

            await RunTest("Fleet_Exists", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ExistTest");
                    Fleet created = await db.Fleets.CreateAsync(fleet);

                    bool exists = await db.Fleets.ExistsAsync(created.Id);
                    AssertTrue(exists, "Existing fleet");

                    bool notExists = await db.Fleets.ExistsAsync("flt_nonexistent");
                    AssertFalse(notExists, "Non-existing fleet");
                }
            });

            await RunTest("Fleet_Enumerate", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet1 = await db.Fleets.CreateAsync(new Fleet("Alpha"));
                    Fleet fleet2 = await db.Fleets.CreateAsync(new Fleet("Beta"));
                    Fleet fleet3 = await db.Fleets.CreateAsync(new Fleet("Charlie"));

                    List<Fleet> results = await db.Fleets.EnumerateAsync();
                    AssertEqual(3, results.Count, "Fleet count");

                    List<string> names = new List<string>();
                    foreach (Fleet f in results)
                    {
                        names.Add(f.Name);
                    }

                    AssertTrue(names.Contains("Alpha"), "Contains Alpha");
                    AssertTrue(names.Contains("Beta"), "Contains Beta");
                    AssertTrue(names.Contains("Charlie"), "Contains Charlie");
                }
            });

            await RunTest("Fleet_EnumeratePaginated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await db.Fleets.CreateAsync(new Fleet("Page1"));
                    await db.Fleets.CreateAsync(new Fleet("Page2"));
                    await db.Fleets.CreateAsync(new Fleet("Page3"));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 1;
                    query.PageNumber = 1;
                    query.Order = EnumerationOrderEnum.CreatedAscending;

                    EnumerationResult<Fleet> page1 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(1, page1.Objects.Count, "Page 1 count");
                    AssertEqual(3, (int)page1.TotalRecords, "Total records");
                    AssertEqual(3, page1.TotalPages, "Total pages");
                    AssertEqual(1, page1.PageNumber, "Page number");

                    query.PageNumber = 2;
                    EnumerationResult<Fleet> page2 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(1, page2.Objects.Count, "Page 2 count");
                    AssertNotEqual(page1.Objects[0].Id, page2.Objects[0].Id, "Different fleet on page 2");

                    query.PageNumber = 3;
                    EnumerationResult<Fleet> page3 = await db.Fleets.EnumerateAsync(query);
                    AssertEqual(1, page3.Objects.Count, "Page 3 count");
                    AssertNotEqual(page1.Objects[0].Id, page3.Objects[0].Id, "Different fleet on page 3");
                    AssertNotEqual(page2.Objects[0].Id, page3.Objects[0].Id, "Different fleet on page 3 vs 2");
                }
            });

            await RunTest("Fleet_Delete", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ToDelete");
                    Fleet created = await db.Fleets.CreateAsync(fleet);

                    await db.Fleets.DeleteAsync(created.Id);

                    Fleet? result = await db.Fleets.ReadAsync(created.Id);
                    AssertNull(result, "Deleted fleet");
                }
            });

            await RunTest("Fleet_ReadNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet? result = await db.Fleets.ReadAsync("flt_nonexistent_id_12345");
                    AssertNull(result, "Non-existent fleet");
                }
            });

            await RunTest("Fleet_ExistsNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    bool exists = await db.Fleets.ExistsAsync("flt_nonexistent_id_12345");
                    AssertFalse(exists, "Non-existent fleet");
                }
            });
        }
    }
}
