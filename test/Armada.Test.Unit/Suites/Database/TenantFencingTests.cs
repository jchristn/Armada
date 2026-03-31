namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class TenantFencingTests : TestSuite
    {
        public override string Name => "Tenant Fencing (Isolation)";

        protected override async Task RunTestsAsync()
        {
            await RunTest("User in TenantA not visible via ReadAsync in TenantB", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster user = new UserMaster(tenantA, "alice@a.com", "pass");
                    await db.Users.CreateAsync(user);

                    // Should find it in TenantA
                    AssertNotNull(await db.Users.ReadAsync(tenantA, user.Id));

                    // Should NOT find it in TenantB
                    AssertNull(await db.Users.ReadAsync(tenantB, user.Id));
                }
            });

            await RunTest("User in TenantA not visible via ExistsAsync in TenantB", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster user = new UserMaster(tenantA, "bob@a.com", "pass");
                    await db.Users.CreateAsync(user);

                    AssertTrue(await db.Users.ExistsAsync(tenantA, user.Id));
                    AssertFalse(await db.Users.ExistsAsync(tenantB, user.Id));
                }
            });

            await RunTest("User in TenantA not visible via ReadByEmailAsync in TenantB", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster user = new UserMaster(tenantA, "carol@a.com", "pass");
                    await db.Users.CreateAsync(user);

                    AssertNotNull(await db.Users.ReadByEmailAsync(tenantA, "carol@a.com"));
                    AssertNull(await db.Users.ReadByEmailAsync(tenantB, "carol@a.com"));
                }
            });

            await RunTest("EnumerateAsync only returns users in requested tenant", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    await db.Users.CreateAsync(new UserMaster(tenantA, "a1@a.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenantA, "a2@a.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenantB, "b1@b.com", "pass"));

                    List<UserMaster> aUsers = await db.Users.EnumerateAsync(tenantA);
                    AssertEqual(2, aUsers.Count);
                    foreach (UserMaster u in aUsers)
                    {
                        AssertEqual(tenantA, u.TenantId, "All enumerated users should belong to TenantA");
                    }

                    List<UserMaster> bUsers = await db.Users.EnumerateAsync(tenantB);
                    AssertEqual(1, bUsers.Count);
                    AssertEqual(tenantB, bUsers[0].TenantId);
                }
            });

            await RunTest("DeleteAsync in TenantB does not affect TenantA user", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster user = new UserMaster(tenantA, "dan@a.com", "pass");
                    await db.Users.CreateAsync(user);

                    // Try to delete from wrong tenant
                    await db.Users.DeleteAsync(tenantB, user.Id);

                    // User should still exist in TenantA
                    AssertNotNull(await db.Users.ReadAsync(tenantA, user.Id));
                }
            });

            await RunTest("Credential in TenantA not visible via ReadAsync in TenantB", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster userA = new UserMaster(tenantA, "cred-user@a.com", "pass");
                    await db.Users.CreateAsync(userA);

                    Credential cred = new Credential(tenantA, userA.Id);
                    await db.Credentials.CreateAsync(cred);

                    AssertNotNull(await db.Credentials.ReadAsync(tenantA, cred.Id));
                    AssertNull(await db.Credentials.ReadAsync(tenantB, cred.Id));
                }
            });

            await RunTest("Credential EnumerateAsync only returns tenant-scoped results", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster userA = new UserMaster(tenantA, "enum-user@a.com", "pass");
                    await db.Users.CreateAsync(userA);
                    UserMaster userB = new UserMaster(tenantB, "enum-user@b.com", "pass");
                    await db.Users.CreateAsync(userB);

                    await db.Credentials.CreateAsync(new Credential(tenantA, userA.Id));
                    await db.Credentials.CreateAsync(new Credential(tenantA, userA.Id));
                    await db.Credentials.CreateAsync(new Credential(tenantB, userB.Id));

                    List<Credential> aCreds = await db.Credentials.EnumerateAsync(tenantA);
                    AssertEqual(2, aCreds.Count);
                    foreach (Credential c in aCreds)
                    {
                        AssertEqual(tenantA, c.TenantId, "All credentials should belong to TenantA");
                    }

                    List<Credential> bCreds = await db.Credentials.EnumerateAsync(tenantB);
                    AssertEqual(1, bCreds.Count);
                }
            });

            await RunTest("Credential DeleteAsync in TenantB does not affect TenantA credential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, string tenantB) = await CreateTwoTenantsAsync(db);

                    UserMaster userA = new UserMaster(tenantA, "del-cred@a.com", "pass");
                    await db.Users.CreateAsync(userA);

                    Credential cred = new Credential(tenantA, userA.Id);
                    await db.Credentials.CreateAsync(cred);

                    // Try to delete from wrong tenant
                    await db.Credentials.DeleteAsync(tenantB, cred.Id);

                    // Should still exist
                    AssertNotNull(await db.Credentials.ReadAsync(tenantA, cred.Id));
                }
            });

            await RunTest("ReadByBearerTokenAsync is global (not tenant-fenced)", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantA, _) = await CreateTwoTenantsAsync(db);

                    UserMaster userA = new UserMaster(tenantA, "bearer@a.com", "pass");
                    await db.Users.CreateAsync(userA);

                    Credential cred = new Credential(tenantA, userA.Id);
                    await db.Credentials.CreateAsync(cred);

                    // ReadByBearerTokenAsync is global lookup (for auth)
                    Credential? result = await db.Credentials.ReadByBearerTokenAsync(cred.BearerToken);
                    AssertNotNull(result);
                    AssertEqual(tenantA, result!.TenantId);
                }
            });
        }

        private async Task<(string tenantA, string tenantB)> CreateTwoTenantsAsync(SqliteDatabaseDriver db)
        {
            TenantMetadata tA = new TenantMetadata("Tenant A " + Guid.NewGuid().ToString("N").Substring(0, 6));
            TenantMetadata tB = new TenantMetadata("Tenant B " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(tA);
            await db.Tenants.CreateAsync(tB);
            return (tA.Id, tB.Id);
        }
    }
}
