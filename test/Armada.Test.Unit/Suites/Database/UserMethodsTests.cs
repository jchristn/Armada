namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class UserMethodsTests : TestSuite
    {
        public override string Name => "User Database Methods";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns user", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "alice@example.com", "password123");
                    UserMaster result = await db.Users.CreateAsync(user);

                    AssertNotNull(result);
                    AssertEqual("alice@example.com", result.Email);
                    AssertEqual(tenantId, result.TenantId);
                }
            });

            await RunTest("ReadAsync returns user by tenant and id", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "bob@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadAsync(tenantId, user.Id);
                    AssertNotNull(result);
                    AssertEqual(user.Id, result!.Id);
                    AssertEqual("bob@example.com", result.Email);
                }
            });

            await RunTest("ReadAsync wrong tenant returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);
                    string otherTenantId = await CreateTestTenantAsync(db, "Other Tenant");

                    UserMaster user = new UserMaster(tenantId, "carol@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    // Try to read with wrong tenant
                    UserMaster? result = await db.Users.ReadAsync(otherTenantId, user.Id);
                    AssertNull(result);
                }
            });

            await RunTest("ReadByIdAsync returns user without tenant filter", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "dan@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadByIdAsync(user.Id);
                    AssertNotNull(result);
                    AssertEqual(user.Id, result!.Id);
                }
            });

            await RunTest("ReadByEmailAsync returns user within tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "eve@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadByEmailAsync(tenantId, "eve@example.com");
                    AssertNotNull(result);
                    AssertEqual(user.Id, result!.Id);
                }
            });

            await RunTest("ReadByEmailAsync wrong tenant returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);
                    string otherTenantId = await CreateTestTenantAsync(db, "Other Tenant");

                    UserMaster user = new UserMaster(tenantId, "frank@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    UserMaster? result = await db.Users.ReadByEmailAsync(otherTenantId, "frank@example.com");
                    AssertNull(result);
                }
            });

            await RunTest("ReadByEmailAnyTenantAsync returns users across tenants", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenant1 = await CreateTestTenantAsync(db, "Tenant 1");
                    string tenant2 = await CreateTestTenantAsync(db, "Tenant 2");

                    string sharedEmail = "shared@example.com";
                    await db.Users.CreateAsync(new UserMaster(tenant1, sharedEmail, "pass1"));
                    await db.Users.CreateAsync(new UserMaster(tenant2, sharedEmail, "pass2"));

                    List<UserMaster> results = await db.Users.ReadByEmailAnyTenantAsync(sharedEmail);
                    AssertEqual(2, results.Count);
                }
            });

            await RunTest("UpdateAsync modifies user", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "gina@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    user.FirstName = "Gina";
                    user.LastName = "Smith";
                    user.IsAdmin = true;
                    await db.Users.UpdateAsync(user);

                    UserMaster? result = await db.Users.ReadAsync(tenantId, user.Id);
                    AssertEqual("Gina", result!.FirstName);
                    AssertEqual("Smith", result.LastName);
                    AssertTrue(result.IsAdmin);
                }
            });

            await RunTest("DeleteAsync removes user", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);

                    UserMaster user = new UserMaster(tenantId, "henry@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    await db.Users.DeleteAsync(tenantId, user.Id);
                    AssertNull(await db.Users.ReadAsync(tenantId, user.Id));
                }
            });

            await RunTest("EnumerateAsync returns tenant-scoped users", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenant1 = await CreateTestTenantAsync(db, "Enum Tenant 1");
                    string tenant2 = await CreateTestTenantAsync(db, "Enum Tenant 2");

                    await db.Users.CreateAsync(new UserMaster(tenant1, "a@t1.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenant1, "b@t1.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenant2, "c@t2.com", "pass"));

                    List<UserMaster> t1Users = await db.Users.EnumerateAsync(tenant1);
                    AssertEqual(2, t1Users.Count);

                    List<UserMaster> t2Users = await db.Users.EnumerateAsync(tenant2);
                    AssertEqual(1, t2Users.Count);
                }
            });

            await RunTest("ExistsAsync correct for tenant-scoped lookup", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTestTenantAsync(db);
                    string otherTenantId = await CreateTestTenantAsync(db, "Other");

                    UserMaster user = new UserMaster(tenantId, "exists@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    AssertTrue(await db.Users.ExistsAsync(tenantId, user.Id));
                    AssertFalse(await db.Users.ExistsAsync(otherTenantId, user.Id));
                    AssertFalse(await db.Users.ExistsAsync(tenantId, "usr_nonexistent"));
                }
            });
        }

        private async Task<string> CreateTestTenantAsync(SqliteDatabaseDriver db, string name = "Test Tenant")
        {
            TenantMetadata tenant = new TenantMetadata(name);
            await db.Tenants.CreateAsync(tenant);
            return tenant.Id;
        }
    }
}
