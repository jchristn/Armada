namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class DefaultSeedingTests : TestSuite
    {
        public override string Name => "Default Data Seeding";

        protected override async Task RunTestsAsync()
        {
            await RunTest("InitializeAsync seeds default tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata? tenant = await db.Tenants.ReadAsync(Constants.DefaultTenantId);
                    AssertNotNull(tenant);
                    AssertEqual(Constants.DefaultTenantId, tenant!.Id);
                    AssertEqual(Constants.DefaultTenantName, tenant.Name);
                    AssertTrue(tenant.Active);
                }
            });

            await RunTest("InitializeAsync seeds default user", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    UserMaster? user = await db.Users.ReadAsync(Constants.DefaultTenantId, Constants.DefaultUserId);
                    AssertNotNull(user);
                    AssertEqual(Constants.DefaultUserId, user!.Id);
                    AssertEqual(Constants.DefaultTenantId, user.TenantId);
                    AssertEqual(Constants.DefaultUserEmail, user.Email);
                    AssertTrue(user.IsAdmin, "Default user should be admin");
                    AssertTrue(user.Active);
                }
            });

            await RunTest("Default user has correct password hash", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    UserMaster? user = await db.Users.ReadAsync(Constants.DefaultTenantId, Constants.DefaultUserId);
                    AssertNotNull(user);

                    string expectedHash = UserMaster.ComputePasswordHash(Constants.DefaultUserPassword);
                    AssertEqual(expectedHash, user!.PasswordSha256);
                    AssertTrue(user.VerifyPassword(Constants.DefaultUserPassword));
                }
            });

            await RunTest("InitializeAsync seeds default credential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Credential? cred = await db.Credentials.ReadAsync(Constants.DefaultTenantId, Constants.DefaultCredentialId);
                    AssertNotNull(cred);
                    AssertEqual(Constants.DefaultCredentialId, cred!.Id);
                    AssertEqual(Constants.DefaultTenantId, cred.TenantId);
                    AssertEqual(Constants.DefaultUserId, cred.UserId);
                    AssertEqual(Constants.DefaultBearerToken, cred.BearerToken);
                    AssertTrue(cred.Active);
                }
            });

            await RunTest("Default credential is retrievable by bearer token", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Credential? cred = await db.Credentials.ReadByBearerTokenAsync(Constants.DefaultBearerToken);
                    AssertNotNull(cred);
                    AssertEqual(Constants.DefaultCredentialId, cred!.Id);
                }
            });

            await RunTest("ExistsAnyAsync returns true after seeding", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    AssertTrue(await db.Tenants.ExistsAnyAsync());
                }
            });

            await RunTest("Second InitializeAsync does not duplicate seed data", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    // The database was already initialized once.
                    // Count tenants before
                    List<TenantMetadata> before = await db.Tenants.EnumerateAsync();

                    // Re-initialize should be idempotent (ExistsAnyAsync returns true so seeding skips)
                    await db.InitializeAsync();

                    List<TenantMetadata> after = await db.Tenants.EnumerateAsync();
                    AssertEqual(before.Count, after.Count, "Tenant count should not change on re-initialize");
                }
            });
        }
    }
}
