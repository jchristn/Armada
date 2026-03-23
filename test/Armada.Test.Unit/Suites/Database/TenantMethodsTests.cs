namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class TenantMethodsTests : TestSuite
    {
        public override string Name => "Tenant Database Methods";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Test Tenant");
                    TenantMetadata result = await db.Tenants.CreateAsync(tenant);

                    AssertNotNull(result);
                    AssertEqual("Test Tenant", result.Name);
                    AssertTrue(result.Active);
                }
            });

            await RunTest("ReadAsync returns created tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Read Test");
                    await db.Tenants.CreateAsync(tenant);

                    TenantMetadata? result = await db.Tenants.ReadAsync(tenant.Id);
                    AssertNotNull(result);
                    AssertEqual(tenant.Id, result!.Id);
                    AssertEqual("Read Test", result.Name);
                }
            });

            await RunTest("ReadAsync nonexistent returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata? result = await db.Tenants.ReadAsync("ten_nonexistent");
                    AssertNull(result);
                }
            });

            await RunTest("ReadByNameAsync returns correct tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Unique Name Lookup");
                    await db.Tenants.CreateAsync(tenant);

                    TenantMetadata? result = await db.Tenants.ReadByNameAsync("Unique Name Lookup");
                    AssertNotNull(result);
                    AssertEqual(tenant.Id, result!.Id);
                }
            });

            await RunTest("ReadByNameAsync nonexistent returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata? result = await db.Tenants.ReadByNameAsync("Does Not Exist");
                    AssertNull(result);
                }
            });

            await RunTest("UpdateAsync modifies tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Before Update");
                    await db.Tenants.CreateAsync(tenant);

                    tenant.Name = "After Update";
                    tenant.Active = false;
                    await db.Tenants.UpdateAsync(tenant);

                    TenantMetadata? result = await db.Tenants.ReadAsync(tenant.Id);
                    AssertEqual("After Update", result!.Name);
                    AssertFalse(result.Active);
                }
            });

            await RunTest("DeleteAsync removes tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("To Delete");
                    await db.Tenants.CreateAsync(tenant);

                    await db.Tenants.DeleteAsync(tenant.Id);
                    AssertNull(await db.Tenants.ReadAsync(tenant.Id));
                }
            });

            await RunTest("ExistsAsync true for existing tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Exists Test");
                    await db.Tenants.CreateAsync(tenant);

                    AssertTrue(await db.Tenants.ExistsAsync(tenant.Id));
                }
            });

            await RunTest("ExistsAsync false for nonexistent tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    AssertFalse(await db.Tenants.ExistsAsync("ten_nonexistent"));
                }
            });

            await RunTest("ExistsAnyAsync true after seeding", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    // InitializeAsync seeds a default tenant
                    AssertTrue(await db.Tenants.ExistsAnyAsync());
                }
            });

            await RunTest("EnumerateAsync returns created tenants", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata t1 = new TenantMetadata("Alpha Corp");
                    TenantMetadata t2 = new TenantMetadata("Beta Corp");
                    await db.Tenants.CreateAsync(t1);
                    await db.Tenants.CreateAsync(t2);

                    List<TenantMetadata> results = await db.Tenants.EnumerateAsync();
                    // Should include t1, t2, and the seeded default tenant
                    AssertTrue(results.Count >= 2, "Should have at least 2 tenants");
                }
            });

            await RunTest("EnumerateAsync with query supports pagination", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 5; i++)
                    {
                        await db.Tenants.CreateAsync(new TenantMetadata("Paginated " + i));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    EnumerationResult<TenantMetadata> result = await db.Tenants.EnumerateAsync(query);

                    AssertNotNull(result);
                    AssertTrue(result.Objects.Count <= 2, "Should respect page size");
                    AssertTrue(result.TotalRecords >= 5, "Total count should reflect all tenants");
                }
            });
        }
    }
}
