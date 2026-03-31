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

    public class TenantScopedEnumerationTests : TestSuite
    {
        public override string Name => "Tenant-Scoped Paginated Enumeration";

        #region Private-Helpers

        private async Task<string> CreateTestTenantAsync(SqliteDatabaseDriver db, string name = "Test Tenant")
        {
            TenantMetadata tenant = new TenantMetadata(name);
            await db.Tenants.CreateAsync(tenant);
            return tenant.Id;
        }

        private async Task<(string tenantId, string userId)> CreateTestTenantAndUserAsync(
            SqliteDatabaseDriver db, string tenantName = "Test Tenant")
        {
            string tenantId = await CreateTestTenantAsync(db, tenantName);
            UserMaster user = new UserMaster(tenantId, "user_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com", "password");
            await db.Users.CreateAsync(user);
            return (tenantId, user.Id);
        }

        #endregion

        protected override async Task RunTestsAsync()
        {
            // ================================================================
            // User tenant-scoped paginated enumeration
            // ================================================================

            await RunTest("User enumerate page 1: Objects.Count==2, TotalRecords==5, TotalPages==3", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");
                    string t2 = await CreateTestTenantAsync(db, "T2");

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t1, "t1user" + i + "@example.com", "pass"));
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t2, "t2user" + i + "@example.com", "pass"));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t1, query);

                    AssertEqual(2, result.Objects.Count, "Objects.Count");
                    AssertEqual(5L, result.TotalRecords, "TotalRecords");
                    AssertEqual(3, result.TotalPages, "TotalPages");
                    AssertEqual(2, result.PageSize, "PageSize");
                    AssertEqual(1, result.PageNumber, "PageNumber");
                }
            });

            await RunTest("User enumerate page 2: Objects.Count==2, TotalRecords==5", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");
                    string t2 = await CreateTestTenantAsync(db, "T2");

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t1, "t1user" + i + "@example.com", "pass"));
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t2, "t2user" + i + "@example.com", "pass"));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 2;
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t1, query);

                    AssertEqual(2, result.Objects.Count, "Objects.Count");
                    AssertEqual(5L, result.TotalRecords, "TotalRecords");
                    AssertEqual(3, result.TotalPages, "TotalPages");
                    AssertEqual(2, result.PageNumber, "PageNumber");
                }
            });

            await RunTest("User enumerate page 3 (remainder): Objects.Count==1", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");
                    string t2 = await CreateTestTenantAsync(db, "T2");

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t1, "t1user" + i + "@example.com", "pass"));
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t2, "t2user" + i + "@example.com", "pass"));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 3;
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count, "Objects.Count");
                    AssertEqual(5L, result.TotalRecords, "TotalRecords");
                    AssertEqual(3, result.TotalPages, "TotalPages");
                }
            });

            await RunTest("User enumerate beyond-range page: Objects.Count==0, TotalRecords==5", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");
                    string t2 = await CreateTestTenantAsync(db, "T2");

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t1, "t1user" + i + "@example.com", "pass"));
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t2, "t2user" + i + "@example.com", "pass"));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 10;
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t1, query);

                    AssertEqual(0, result.Objects.Count, "Objects.Count");
                    AssertEqual(5L, result.TotalRecords, "TotalRecords");
                    AssertEqual(3, result.TotalPages, "TotalPages");
                }
            });

            await RunTest("User enumerate tenant t2 paginated: Objects.Count==2, TotalRecords==2", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");
                    string t2 = await CreateTestTenantAsync(db, "T2");

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t1, "t1user" + i + "@example.com", "pass"));
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        await db.Users.CreateAsync(new UserMaster(t2, "t2user" + i + "@example.com", "pass"));
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t2, query);

                    AssertEqual(2, result.Objects.Count, "Objects.Count");
                    AssertEqual(2L, result.TotalRecords, "TotalRecords");
                    AssertEqual(1, result.TotalPages, "TotalPages");
                }
            });

            await RunTest("User enumerate CreatedAfter filter returns subset", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");

                    // Create users with time gaps via explicit CreatedUtc
                    DateTime baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
                    for (int i = 0; i < 5; i++)
                    {
                        UserMaster user = new UserMaster(t1, "timeuser" + i + "@example.com", "pass");
                        user.CreatedUtc = baseTime.AddHours(i);
                        await db.Users.CreateAsync(user);
                    }

                    // Filter: CreatedAfter the 2nd user (index 1), so users 2,3,4 should match
                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.CreatedAfter = baseTime.AddHours(1);
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t1, query);

                    AssertEqual(3, result.Objects.Count, "Objects.Count for CreatedAfter filter");
                    AssertEqual(3L, result.TotalRecords, "TotalRecords for CreatedAfter filter");
                }
            });

            await RunTest("User enumerate CreatedAscending vs default order: first result differs", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");

                    DateTime baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
                    for (int i = 0; i < 3; i++)
                    {
                        UserMaster user = new UserMaster(t1, "orderuser" + i + "@example.com", "pass");
                        user.CreatedUtc = baseTime.AddHours(i);
                        await db.Users.CreateAsync(user);
                    }

                    EnumerationQuery descQuery = new EnumerationQuery();
                    descQuery.PageSize = 10;
                    descQuery.Order = EnumerationOrderEnum.CreatedDescending;
                    EnumerationResult<UserMaster> descResult = await db.Users.EnumerateAsync(t1, descQuery);

                    EnumerationQuery ascQuery = new EnumerationQuery();
                    ascQuery.PageSize = 10;
                    ascQuery.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<UserMaster> ascResult = await db.Users.EnumerateAsync(t1, ascQuery);

                    AssertEqual(3, descResult.Objects.Count, "descending count");
                    AssertEqual(3, ascResult.Objects.Count, "ascending count");
                    AssertNotEqual(descResult.Objects[0].Id, ascResult.Objects[0].Id, "first result should differ between asc/desc");
                }
            });

            await RunTest("User enumerate full property validation", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string t1 = await CreateTestTenantAsync(db, "T1");

                    UserMaster user = new UserMaster(t1, "fullprop@example.com", "securepass");
                    user.FirstName = "Alice";
                    user.LastName = "Smith";
                    user.IsAdmin = true;
                    user.Active = true;
                    await db.Users.CreateAsync(user);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    EnumerationResult<UserMaster> result = await db.Users.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count, "Objects.Count");
                    AssertEqual(1L, result.TotalRecords, "TotalRecords");

                    UserMaster fetched = result.Objects[0];
                    AssertEqual(user.Id, fetched.Id, "Id");
                    AssertEqual(t1, fetched.TenantId, "TenantId");
                    AssertEqual("fullprop@example.com", fetched.Email, "Email");
                    AssertEqual("Alice", fetched.FirstName, "FirstName");
                    AssertEqual("Smith", fetched.LastName, "LastName");
                    AssertTrue(fetched.IsAdmin, "IsAdmin");
                    AssertTrue(fetched.Active, "Active");
                    AssertTrue(fetched.CreatedUtc != default(DateTime), "CreatedUtc is not default");
                    AssertTrue(fetched.LastUpdateUtc != default(DateTime), "LastUpdateUtc is not default");
                    AssertNotNull(fetched.PasswordSha256, "PasswordSha256 is not null");
                    AssertTrue(fetched.PasswordSha256.Length > 0, "PasswordSha256 is not empty");
                }
            });

            // ================================================================
            // Credential tenant-scoped paginated enumeration
            // ================================================================

            await RunTest("Credential enumerate page 1: Objects.Count==2, TotalRecords==4, TotalPages==2", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string u1) = await CreateTestTenantAndUserAsync(db, "T1");
                    (string t2, string u2) = await CreateTestTenantAndUserAsync(db, "T2");

                    for (int i = 0; i < 4; i++)
                    {
                        Credential cred = new Credential(t1, u1);
                        cred.Name = "T1-Cred-" + i;
                        await db.Credentials.CreateAsync(cred);
                    }
                    Credential t2Cred = new Credential(t2, u2);
                    t2Cred.Name = "T2-Cred-0";
                    await db.Credentials.CreateAsync(t2Cred);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    EnumerationResult<Credential> result = await db.Credentials.EnumerateAsync(t1, query);

                    AssertEqual(2, result.Objects.Count, "Objects.Count");
                    AssertEqual(4L, result.TotalRecords, "TotalRecords");
                    AssertEqual(2, result.TotalPages, "TotalPages");
                    AssertEqual(2, result.PageSize, "PageSize");
                    AssertEqual(1, result.PageNumber, "PageNumber");
                }
            });

            await RunTest("Credential enumerate page 2: Objects.Count==2, TotalRecords==4", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string u1) = await CreateTestTenantAndUserAsync(db, "T1");
                    (string t2, string u2) = await CreateTestTenantAndUserAsync(db, "T2");

                    for (int i = 0; i < 4; i++)
                    {
                        Credential cred = new Credential(t1, u1);
                        cred.Name = "T1-Cred-" + i;
                        await db.Credentials.CreateAsync(cred);
                    }
                    Credential t2Cred = new Credential(t2, u2);
                    t2Cred.Name = "T2-Cred-0";
                    await db.Credentials.CreateAsync(t2Cred);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 2;
                    EnumerationResult<Credential> result = await db.Credentials.EnumerateAsync(t1, query);

                    AssertEqual(2, result.Objects.Count, "Objects.Count");
                    AssertEqual(4L, result.TotalRecords, "TotalRecords");
                    AssertEqual(2, result.TotalPages, "TotalPages");
                    AssertEqual(2, result.PageNumber, "PageNumber");
                }
            });

            await RunTest("Credential enumerate beyond-range page: Objects.Count==0", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string u1) = await CreateTestTenantAndUserAsync(db, "T1");
                    (string t2, string u2) = await CreateTestTenantAndUserAsync(db, "T2");

                    for (int i = 0; i < 4; i++)
                    {
                        Credential cred = new Credential(t1, u1);
                        cred.Name = "T1-Cred-" + i;
                        await db.Credentials.CreateAsync(cred);
                    }
                    Credential t2Cred = new Credential(t2, u2);
                    t2Cred.Name = "T2-Cred-0";
                    await db.Credentials.CreateAsync(t2Cred);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 10;
                    EnumerationResult<Credential> result = await db.Credentials.EnumerateAsync(t1, query);

                    AssertEqual(0, result.Objects.Count, "Objects.Count");
                    AssertEqual(4L, result.TotalRecords, "TotalRecords");
                    AssertEqual(2, result.TotalPages, "TotalPages");
                }
            });

            await RunTest("Credential enumerate tenant t2: TotalRecords==1", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string u1) = await CreateTestTenantAndUserAsync(db, "T1");
                    (string t2, string u2) = await CreateTestTenantAndUserAsync(db, "T2");

                    for (int i = 0; i < 4; i++)
                    {
                        Credential cred = new Credential(t1, u1);
                        cred.Name = "T1-Cred-" + i;
                        await db.Credentials.CreateAsync(cred);
                    }
                    Credential t2Cred = new Credential(t2, u2);
                    t2Cred.Name = "T2-Cred-0";
                    await db.Credentials.CreateAsync(t2Cred);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<Credential> result = await db.Credentials.EnumerateAsync(t2, query);

                    AssertEqual(1, result.Objects.Count, "Objects.Count");
                    AssertEqual(1L, result.TotalRecords, "TotalRecords");
                    AssertEqual(1, result.TotalPages, "TotalPages");
                }
            });

            await RunTest("Credential enumerate full property validation", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string t1, string u1) = await CreateTestTenantAndUserAsync(db, "T1");

                    Credential cred = new Credential(t1, u1);
                    cred.Name = "My API Key";
                    await db.Credentials.CreateAsync(cred);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    EnumerationResult<Credential> result = await db.Credentials.EnumerateAsync(t1, query);

                    AssertEqual(1, result.Objects.Count, "Objects.Count");
                    AssertEqual(1L, result.TotalRecords, "TotalRecords");

                    Credential fetched = result.Objects[0];
                    AssertEqual(cred.Id, fetched.Id, "Id");
                    AssertEqual(t1, fetched.TenantId, "TenantId");
                    AssertEqual(u1, fetched.UserId, "UserId");
                    AssertEqual("My API Key", fetched.Name, "Name");
                    AssertEqual(64, fetched.BearerToken.Length, "BearerToken length");
                    AssertTrue(fetched.Active, "Active");
                    AssertTrue(fetched.CreatedUtc != default(DateTime), "CreatedUtc is not default");
                    AssertTrue(fetched.LastUpdateUtc != default(DateTime), "LastUpdateUtc is not default");
                }
            });

            // ================================================================
            // Tenant paginated enumeration (stronger assertions)
            // ================================================================

            await RunTest("Tenant enumerate paginated: 5 created + 1 seeded default", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Tenants.CreateAsync(new TenantMetadata("Tenant-" + i));
                    }

                    // Total should be 5 created + 1 default from seeding = 6
                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 2;
                    query.PageNumber = 1;
                    EnumerationResult<TenantMetadata> result = await db.Tenants.EnumerateAsync(query);

                    AssertEqual(2, result.Objects.Count, "Page 1 Objects.Count");
                    AssertEqual(6L, result.TotalRecords, "TotalRecords (5 created + 1 default)");
                    AssertEqual(3, result.TotalPages, "TotalPages ceil(6/2)");
                    AssertEqual(2, result.PageSize, "PageSize");
                    AssertEqual(1, result.PageNumber, "PageNumber");
                }
            });

            await RunTest("Tenant enumerate read-back property validation", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    TenantMetadata tenant = new TenantMetadata("PropCheck Tenant");
                    await db.Tenants.CreateAsync(tenant);

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 100;
                    EnumerationResult<TenantMetadata> result = await db.Tenants.EnumerateAsync(query);

                    // Find our tenant in the results (there is also the default seeded tenant)
                    TenantMetadata? fetched = null;
                    foreach (TenantMetadata t in result.Objects)
                    {
                        if (t.Id == tenant.Id)
                        {
                            fetched = t;
                            break;
                        }
                    }

                    AssertNotNull(fetched, "Tenant found in enumeration results");
                    AssertEqual("PropCheck Tenant", fetched!.Name, "Name");
                    AssertTrue(fetched.Active, "Active");
                    AssertTrue(fetched.CreatedUtc != default(DateTime), "CreatedUtc is not default");
                }
            });
        }
    }
}
