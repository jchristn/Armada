namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    public class AuthenticationServiceTests : TestSuite
    {
        public override string Name => "AuthenticationService";

        protected override async Task RunTestsAsync()
        {
            // ----------------------------------------------------------------
            // Bearer token tests
            // ----------------------------------------------------------------

            await RunTest("AuthenticateAsync BearerToken ValidToken ReturnsAuthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId, string bearerToken) = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + bearerToken, null, null);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated");
                    AssertEqual(tenantId, ctx.TenantId);
                    AssertEqual(userId, ctx.UserId);
                    AssertEqual("Bearer", ctx.AuthMethod);
                }
            });

            await RunTest("AuthenticateAsync BearerToken InvalidToken ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer invalidtoken", null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with invalid token");
                }
            });

            await RunTest("AuthenticateAsync BearerToken InactiveUser ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId, string bearerToken) = await CreateTestEntitiesAsync(db, userActive: false);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + bearerToken, null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with inactive user");
                }
            });

            await RunTest("AuthenticateAsync BearerToken InactiveCredential ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId, string bearerToken) = await CreateTestEntitiesAsync(db, credentialActive: false);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + bearerToken, null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with inactive credential");
                }
            });

            // ----------------------------------------------------------------
            // Session token (X-Token) tests
            // ----------------------------------------------------------------

            await RunTest("AuthenticateAsync SessionToken ValidToken ReturnsAuthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId, _) = await CreateTestEntitiesAsync(db);

                    SessionTokenService tokenSvc = new SessionTokenService();
                    AuthenticateResult tokenResult = tokenSvc.CreateToken(tenantId, userId);

                    AuthenticationService svc = CreateService(db, sessionTokenService: tokenSvc);
                    AuthContext ctx = await svc.AuthenticateAsync(null, tokenResult.Token, null);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with valid session token");
                    AssertEqual(tenantId, ctx.TenantId);
                    AssertEqual(userId, ctx.UserId);
                    AssertEqual("Session", ctx.AuthMethod);
                }
            });

            await RunTest("AuthenticateAsync SessionToken InvalidToken ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync(null, "garbage-session-token", null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with garbage session token");
                }
            });

            // ----------------------------------------------------------------
            // API key (X-Api-Key) tests
            // ----------------------------------------------------------------

            await RunTest("AuthenticateAsync ApiKey ValidKey ReturnsAuthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    AuthenticationService svc = CreateService(db, apiKey: "testkey");
                    AuthContext ctx = await svc.AuthenticateAsync(null, null, "testkey");

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with valid API key");
                    AssertEqual(Constants.SystemTenantId, ctx.TenantId);
                    AssertEqual(Constants.SystemUserId, ctx.UserId);
                    AssertTrue(ctx.IsAdmin, "API key auth should grant admin");
                    AssertEqual("ApiKey", ctx.AuthMethod);
                }
            });

            await RunTest("AuthenticateAsync ApiKey WrongKey ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    AuthenticationService svc = CreateService(db, apiKey: "testkey");
                    AuthContext ctx = await svc.AuthenticateAsync(null, null, "wrongkey");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with wrong API key");
                }
            });

            // ----------------------------------------------------------------
            // No auth headers
            // ----------------------------------------------------------------

            await RunTest("AuthenticateAsync NoHeaders ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync(null, null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with no headers");
                }
            });

            // ----------------------------------------------------------------
            // Auth priority: Bearer wins over API key
            // ----------------------------------------------------------------

            await RunTest("AuthenticateAsync Priority BearerWinsOverApiKey", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, string userId, string bearerToken) = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db, apiKey: "testkey");
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + bearerToken, null, "testkey");

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated");
                    AssertEqual("Bearer", ctx.AuthMethod, "Bearer should take priority over ApiKey");
                    AssertEqual(tenantId, ctx.TenantId);
                    AssertEqual(userId, ctx.UserId);
                }
            });

            // ----------------------------------------------------------------
            // AuthenticateWithCredentialsAsync tests
            // ----------------------------------------------------------------

            await RunTest("AuthenticateWithCredentialsAsync ValidCredentials ReturnsAuthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    string password = "secretPassword123";
                    (string tenantId, string userId, _) = await CreateTestEntitiesAsync(db, password: password);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(tenantId, "test@example.com", password);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with correct credentials");
                    AssertEqual(tenantId, ctx.TenantId);
                    AssertEqual(userId, ctx.UserId);
                    AssertEqual("Credentials", ctx.AuthMethod);
                }
            });

            await RunTest("AuthenticateWithCredentialsAsync WrongPassword ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, _, _) = await CreateTestEntitiesAsync(db, password: "correctPassword");

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(tenantId, "test@example.com", "wrongPassword");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with wrong password");
                }
            });

            await RunTest("AuthenticateWithCredentialsAsync UnknownEmail ReturnsUnauthenticated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    (string tenantId, _, _) = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(tenantId, "unknown@example.com", "password");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with unknown email");
                }
            });
        }

        #region Private-Helpers

        private AuthenticationService CreateService(
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

        #endregion
    }
}
