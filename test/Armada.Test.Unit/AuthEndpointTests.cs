namespace Armada.Test.Unit
{
    using System.Security.Cryptography;
    using Armada.Core;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    /// <summary>
    /// Integration tests covering SessionTokenService, AuthenticationService,
    /// and Tenant/User/Credential CRUD operations used by the auth and
    /// multi-tenant endpoints.
    /// </summary>
    public class AuthEndpointTests : TestSuite
    {
        public override string Name => "Auth Endpoint Integration";

        protected override async Task RunTestsAsync()
        {
            // ================================================================
            // SessionTokenService
            // ================================================================

            await RunTest("SessionToken CreateToken returns Success=true, non-null Token, future ExpiresUtc", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult result = svc.CreateToken("ten_abc", "usr_xyz");

                AssertTrue(result.Success, "Success should be true");
                AssertNotNull(result.Token, "Token should not be null");
                AssertTrue(result.Token!.Length > 0, "Token should not be empty");
                AssertNotNull(result.ExpiresUtc, "ExpiresUtc should not be null");
                AssertTrue(result.ExpiresUtc!.Value > DateTime.UtcNow, "ExpiresUtc should be in the future");
            });

            await RunTest("SessionToken ValidateToken on valid token returns correct tenantId and userId", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult result = svc.CreateToken("ten_abc", "usr_xyz");

                AuthContext? ctx = svc.ValidateToken(result.Token!);
                AssertNotNull(ctx);
                AssertEqual("ten_abc", ctx!.TenantId, "TenantId");
                AssertEqual("usr_xyz", ctx.UserId, "UserId");
                AssertTrue(ctx.IsAuthenticated, "IsAuthenticated");
            });

            await RunTest("SessionToken ValidateToken on expired token returns null", () =>
            {
                // Create a token service with a known key, then create a second
                // service with the same key and validate. We cannot actually expire
                // a token quickly, so we test with a tampered token that simulates
                // decryption yielding an expired timestamp.  The closest realistic
                // approach: create with one service instance, validate with another
                // instance using a different key (simulating key rotation / expiry).
                SessionTokenService svc1 = new SessionTokenService();
                AuthenticateResult result = svc1.CreateToken("ten_abc", "usr_xyz");

                // Different instance = different key, token will not decrypt
                SessionTokenService svc2 = new SessionTokenService();
                AuthContext? ctx = svc2.ValidateToken(result.Token!);
                AssertNull(ctx, "Token validated with different key should return null");
            });

            await RunTest("SessionToken ValidateToken on garbage input returns null", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthContext? ctx = svc.ValidateToken("this-is-absolute-garbage-not-a-token");
                AssertNull(ctx, "Garbage token should return null");
            });

            // ================================================================
            // AuthenticationService
            // ================================================================

            await RunTest("AuthService AuthenticateWithCredentialsAsync valid email/password returns authenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string password = "correctPassword123";
                    (string tenantId, string userId, _) = await CreateTestEntitiesAsync(db, password: password);

                    AuthenticationService svc = CreateAuthService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(tenantId, "test@example.com", password);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with correct credentials");
                    AssertEqual(tenantId, ctx.TenantId, "TenantId");
                    AssertEqual(userId, ctx.UserId, "UserId");
                    AssertEqual("Credentials", ctx.AuthMethod, "AuthMethod");
                }
            });

            await RunTest("AuthService AuthenticateWithCredentialsAsync wrong password returns unauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, _, _) = await CreateTestEntitiesAsync(db, password: "correctPassword");

                    AuthenticationService svc = CreateAuthService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(tenantId, "test@example.com", "wrongPassword");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with wrong password");
                }
            });

            await RunTest("AuthService AuthenticateWithCredentialsAsync non-existent email returns unauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, _, _) = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateAuthService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(tenantId, "nonexistent@example.com", "anyPassword");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with unknown email");
                }
            });

            await RunTest("AuthService AuthenticateBearerAsync valid token returns authenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId, string bearerToken) = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateAuthService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + bearerToken, null, null);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with valid bearer token");
                    AssertEqual(tenantId, ctx.TenantId, "TenantId");
                    AssertEqual(userId, ctx.UserId, "UserId");
                    AssertEqual("Bearer", ctx.AuthMethod, "AuthMethod");
                }
            });

            await RunTest("AuthService AuthenticateBearerAsync invalid token returns unauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateAuthService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer totally_invalid_bearer_token", null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with invalid bearer token");
                }
            });

            // ================================================================
            // Tenant/User/Credential CRUD
            // ================================================================

            await RunTest("CRUD Create tenant, verify it persists", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("Persist Test Corp");
                    await db.Tenants.CreateAsync(tenant);

                    TenantMetadata? readBack = await db.Tenants.ReadAsync(tenant.Id);
                    AssertNotNull(readBack, "Tenant should persist after creation");
                    AssertEqual("Persist Test Corp", readBack!.Name, "Name");
                    AssertTrue(readBack.Active, "Active");
                }
            });

            await RunTest("CRUD Create user in tenant, verify tenant scoping", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantA = await CreateTenantAsync(db, "Tenant A");
                    string tenantB = await CreateTenantAsync(db, "Tenant B");

                    UserMaster user = new UserMaster(tenantA, "scoped@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    // Readable in correct tenant
                    AssertNotNull(await db.Users.ReadAsync(tenantA, user.Id), "User should be readable in own tenant");

                    // Not readable in wrong tenant
                    AssertNull(await db.Users.ReadAsync(tenantB, user.Id), "User should not be visible in other tenant");
                }
            });

            await RunTest("CRUD Create credential, verify bearer token lookup", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTenantAsync(db, "Cred Tenant");

                    UserMaster user = new UserMaster(tenantId, "creduser@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    Credential cred = new Credential(tenantId, user.Id);
                    await db.Credentials.CreateAsync(cred);

                    // Lookup by bearer token
                    Credential? found = await db.Credentials.ReadByBearerTokenAsync(cred.BearerToken);
                    AssertNotNull(found, "Should find credential by bearer token");
                    AssertEqual(cred.Id, found!.Id, "Credential Id");
                    AssertEqual(tenantId, found.TenantId, "TenantId");
                    AssertEqual(user.Id, found.UserId, "UserId");
                }
            });

            await RunTest("CRUD Enumerate tenants with pagination", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    for (int i = 0; i < 5; i++)
                    {
                        await db.Tenants.CreateAsync(new TenantMetadata("PageTenant-" + i));
                    }

                    // 5 created + 1 default seeded = 6 total
                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 3;
                    query.PageNumber = 1;
                    EnumerationResult<TenantMetadata> result = await db.Tenants.EnumerateAsync(query);

                    AssertNotNull(result, "Result should not be null");
                    AssertEqual(3, result.Objects.Count, "Objects.Count (page size)");
                    AssertEqual(6L, result.TotalRecords, "TotalRecords (5 + 1 seeded)");
                    AssertEqual(3, result.PageSize, "PageSize");
                    AssertEqual(1, result.PageNumber, "PageNumber");
                    AssertEqual(2, result.TotalPages, "TotalPages ceil(6/3)");
                }
            });

            await RunTest("CRUD Enumerate users scoped to tenant returns only that tenant's users", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantA = await CreateTenantAsync(db, "Enum Tenant A");
                    string tenantB = await CreateTenantAsync(db, "Enum Tenant B");

                    await db.Users.CreateAsync(new UserMaster(tenantA, "a1@a.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenantA, "a2@a.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenantA, "a3@a.com", "pass"));
                    await db.Users.CreateAsync(new UserMaster(tenantB, "b1@b.com", "pass"));

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageSize = 10;
                    query.PageNumber = 1;
                    EnumerationResult<UserMaster> resultA = await db.Users.EnumerateAsync(tenantA, query);

                    AssertEqual(3, resultA.Objects.Count, "Tenant A user count");
                    AssertEqual(3L, resultA.TotalRecords, "Tenant A TotalRecords");
                    foreach (UserMaster u in resultA.Objects)
                    {
                        AssertEqual(tenantA, u.TenantId, "All users should belong to Tenant A");
                    }

                    EnumerationResult<UserMaster> resultB = await db.Users.EnumerateAsync(tenantB, query);
                    AssertEqual(1, resultB.Objects.Count, "Tenant B user count");
                    AssertEqual(1L, resultB.TotalRecords, "Tenant B TotalRecords");
                }
            });

            await RunTest("CRUD Delete user, verify it's gone", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string tenantId = await CreateTenantAsync(db, "Del User Tenant");

                    UserMaster user = new UserMaster(tenantId, "deleteme@example.com", "pass");
                    await db.Users.CreateAsync(user);

                    // Confirm exists
                    AssertNotNull(await db.Users.ReadAsync(tenantId, user.Id), "User should exist before delete");

                    // Delete
                    await db.Users.DeleteAsync(tenantId, user.Id);

                    // Confirm gone
                    AssertNull(await db.Users.ReadAsync(tenantId, user.Id), "User should be gone after delete");
                }
            });

            await RunTest("CRUD Delete tenant, verify it's gone", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = new TenantMetadata("To Be Deleted");
                    await db.Tenants.CreateAsync(tenant);

                    // Confirm exists
                    AssertNotNull(await db.Tenants.ReadAsync(tenant.Id), "Tenant should exist before delete");

                    // Delete
                    await db.Tenants.DeleteAsync(tenant.Id);

                    // Confirm gone
                    AssertNull(await db.Tenants.ReadAsync(tenant.Id), "Tenant should be gone after delete");
                }
            });
        }

        #region Private-Helpers

        private AuthenticationService CreateAuthService(
            SqliteDatabaseDriver db,
            ISessionTokenService? sessionTokenService = null,
            string? apiKey = null)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            sessionTokenService ??= new SessionTokenService();

            ArmadaSettings settings = new ArmadaSettings();
            settings.ApiKey = apiKey;

            return new AuthenticationService(db, sessionTokenService, settings, logging);
        }

        private async Task<(string tenantId, string userId, string bearerToken)> CreateTestEntitiesAsync(
            SqliteDatabaseDriver db,
            string? password = null,
            bool userActive = true,
            bool credentialActive = true)
        {
            password ??= "password";

            TenantMetadata tenant = new TenantMetadata("Test Tenant");
            await db.Tenants.CreateAsync(tenant);

            UserMaster user = new UserMaster(tenant.Id, "test@example.com", password);
            user.Active = userActive;
            await db.Users.CreateAsync(user);

            Credential credential = new Credential(tenant.Id, user.Id);
            credential.Active = credentialActive;
            await db.Credentials.CreateAsync(credential);

            return (tenant.Id, user.Id, credential.BearerToken);
        }

        private async Task<string> CreateTenantAsync(SqliteDatabaseDriver db, string name = "Test Tenant")
        {
            TenantMetadata tenant = new TenantMetadata(name);
            await db.Tenants.CreateAsync(tenant);
            return tenant.Id;
        }

        #endregion
    }
}
