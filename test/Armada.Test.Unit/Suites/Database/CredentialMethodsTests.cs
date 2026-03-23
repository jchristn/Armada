namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class CredentialMethodsTests : TestSuite
    {
        public override string Name => "Credential Database Methods";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns credential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);

                    Credential cred = new Credential(tenantId, userId);
                    cred.Name = "Test Key";
                    Credential result = await db.Credentials.CreateAsync(cred);

                    AssertNotNull(result);
                    AssertEqual(tenantId, result.TenantId);
                    AssertEqual(userId, result.UserId);
                    AssertEqual("Test Key", result.Name);
                }
            });

            await RunTest("ReadAsync returns credential by tenant and id", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);

                    Credential cred = new Credential(tenantId, userId);
                    await db.Credentials.CreateAsync(cred);

                    Credential? result = await db.Credentials.ReadAsync(tenantId, cred.Id);
                    AssertNotNull(result);
                    AssertEqual(cred.Id, result!.Id);
                    AssertEqual(cred.BearerToken, result.BearerToken);
                }
            });

            await RunTest("ReadAsync wrong tenant returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);
                    (string otherTenantId, _) = await CreateTestTenantAndUserAsync(db, "Other Tenant");

                    Credential cred = new Credential(tenantId, userId);
                    await db.Credentials.CreateAsync(cred);

                    Credential? result = await db.Credentials.ReadAsync(otherTenantId, cred.Id);
                    AssertNull(result);
                }
            });

            await RunTest("ReadByIdAsync returns credential without tenant filter", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);

                    Credential cred = new Credential(tenantId, userId);
                    await db.Credentials.CreateAsync(cred);

                    Credential? result = await db.Credentials.ReadByIdAsync(cred.Id);
                    AssertNotNull(result);
                    AssertEqual(cred.Id, result!.Id);
                }
            });

            await RunTest("ReadByBearerTokenAsync returns matching credential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);

                    Credential cred = new Credential(tenantId, userId);
                    await db.Credentials.CreateAsync(cred);

                    Credential? result = await db.Credentials.ReadByBearerTokenAsync(cred.BearerToken);
                    AssertNotNull(result);
                    AssertEqual(cred.Id, result!.Id);
                    AssertEqual(tenantId, result.TenantId);
                }
            });

            await RunTest("ReadByBearerTokenAsync nonexistent returns null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Credential? result = await db.Credentials.ReadByBearerTokenAsync("nonexistent_token_value");
                    AssertNull(result);
                }
            });

            await RunTest("UpdateAsync modifies credential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);

                    Credential cred = new Credential(tenantId, userId);
                    await db.Credentials.CreateAsync(cred);

                    cred.Name = "Updated Name";
                    cred.Active = false;
                    await db.Credentials.UpdateAsync(cred);

                    Credential? result = await db.Credentials.ReadAsync(tenantId, cred.Id);
                    AssertEqual("Updated Name", result!.Name);
                    AssertFalse(result.Active);
                }
            });

            await RunTest("DeleteAsync removes credential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId) = await CreateTestTenantAndUserAsync(db);

                    Credential cred = new Credential(tenantId, userId);
                    await db.Credentials.CreateAsync(cred);

                    await db.Credentials.DeleteAsync(tenantId, cred.Id);
                    AssertNull(await db.Credentials.ReadAsync(tenantId, cred.Id));
                }
            });

            await RunTest("EnumerateAsync returns tenant-scoped credentials", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenant1, string user1) = await CreateTestTenantAndUserAsync(db, "Tenant 1");
                    (string tenant2, string user2) = await CreateTestTenantAndUserAsync(db, "Tenant 2");

                    await db.Credentials.CreateAsync(new Credential(tenant1, user1));
                    await db.Credentials.CreateAsync(new Credential(tenant1, user1));
                    await db.Credentials.CreateAsync(new Credential(tenant2, user2));

                    List<Credential> t1Creds = await db.Credentials.EnumerateAsync(tenant1);
                    AssertEqual(2, t1Creds.Count);

                    List<Credential> t2Creds = await db.Credentials.EnumerateAsync(tenant2);
                    AssertEqual(1, t2Creds.Count);
                }
            });

            await RunTest("EnumerateByUserAsync returns user-scoped credentials", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId1) = await CreateTestTenantAndUserAsync(db);

                    UserMaster user2 = new UserMaster(tenantId, "user2@example.com", "pass");
                    await db.Users.CreateAsync(user2);

                    await db.Credentials.CreateAsync(new Credential(tenantId, userId1));
                    await db.Credentials.CreateAsync(new Credential(tenantId, userId1));
                    await db.Credentials.CreateAsync(new Credential(tenantId, user2.Id));

                    List<Credential> u1Creds = await db.Credentials.EnumerateByUserAsync(tenantId, userId1);
                    AssertEqual(2, u1Creds.Count);

                    List<Credential> u2Creds = await db.Credentials.EnumerateByUserAsync(tenantId, user2.Id);
                    AssertEqual(1, u2Creds.Count);
                }
            });
        }

        private async Task<(string tenantId, string userId)> CreateTestTenantAndUserAsync(
            SqliteDatabaseDriver db, string tenantName = "Test Tenant")
        {
            TenantMetadata tenant = new TenantMetadata(tenantName);
            await db.Tenants.CreateAsync(tenant);

            UserMaster user = new UserMaster(tenant.Id, "user_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com", "password");
            await db.Users.CreateAsync(user);

            return (tenant.Id, user.Id);
        }
    }
}
