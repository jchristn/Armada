namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="AuthenticationService"/>: bearer-token, session-token, API-key,
    /// and username/password credential authentication over a live SQLite store. Positive cases
    /// assert successful authentication and priority ordering; negative cases assert rejection of
    /// invalid tokens, inactive users/credentials, wrong keys, wrong passwords, unknown emails,
    /// and missing headers. The audit adds an inactive-user credential rejection path.
    /// </summary>
    public sealed class AuthenticationServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the AuthenticationService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ----- Bearer token -----

            cases.Add(CaseAsync("bearer_valid_token_authenticated", "AuthenticateAsync BearerToken ValidToken ReturnsAuthenticated", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + entities.BearerToken, null, null);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated");
                    AssertEqual(entities.TenantId, ctx.TenantId);
                    AssertEqual(entities.UserId, ctx.UserId);
                    AssertEqual("Bearer", ctx.AuthMethod);
                }
            }));

            cases.Add(CaseAsync("bearer_invalid_token_unauthenticated", "AuthenticateAsync BearerToken InvalidToken ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer invalidtoken", null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with invalid token");
                }
            }));

            cases.Add(CaseAsync("bearer_inactive_user_unauthenticated", "AuthenticateAsync BearerToken InactiveUser ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db, userActive: false);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + entities.BearerToken, null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with inactive user");
                }
            }));

            cases.Add(CaseAsync("bearer_inactive_credential_unauthenticated", "AuthenticateAsync BearerToken InactiveCredential ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db, credentialActive: false);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + entities.BearerToken, null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with inactive credential");
                }
            }));

            // ----- Session token (X-Token) -----

            cases.Add(CaseAsync("session_valid_token_authenticated", "AuthenticateAsync SessionToken ValidToken ReturnsAuthenticated", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db);

                    SessionTokenService tokenSvc = new SessionTokenService();
                    AuthenticateResult tokenResult = tokenSvc.CreateToken(entities.TenantId, entities.UserId);

                    AuthenticationService svc = CreateService(db, sessionTokenService: tokenSvc);
                    AuthContext ctx = await svc.AuthenticateAsync(null, tokenResult.Token, null);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with valid session token");
                    AssertEqual(entities.TenantId, ctx.TenantId);
                    AssertEqual(entities.UserId, ctx.UserId);
                    AssertEqual("Session", ctx.AuthMethod);
                }
            }));

            cases.Add(CaseAsync("session_invalid_token_unauthenticated", "AuthenticateAsync SessionToken InvalidToken ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync(null, "garbage-session-token", null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with garbage session token");
                }
            }));

            // ----- API key (X-Api-Key) -----

            cases.Add(CaseAsync("apikey_valid_key_authenticated", "AuthenticateAsync ApiKey ValidKey ReturnsAuthenticated", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    AuthenticationService svc = CreateService(db, apiKey: "testkey");
                    AuthContext ctx = await svc.AuthenticateAsync(null, null, "testkey");

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with valid API key");
                    AssertEqual(Constants.SystemTenantId, ctx.TenantId);
                    AssertEqual(Constants.SystemUserId, ctx.UserId);
                    AssertTrue(ctx.IsAdmin, "API key auth should grant admin");
                    AssertEqual("ApiKey", ctx.AuthMethod);
                }
            }));

            cases.Add(CaseAsync("apikey_wrong_key_unauthenticated", "AuthenticateAsync ApiKey WrongKey ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    AuthenticationService svc = CreateService(db, apiKey: "testkey");
                    AuthContext ctx = await svc.AuthenticateAsync(null, null, "wrongkey");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with wrong API key");
                }
            }));

            // ----- No auth headers -----

            cases.Add(CaseAsync("no_headers_unauthenticated", "AuthenticateAsync NoHeaders ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateAsync(null, null, null);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with no headers");
                }
            }));

            // ----- Auth priority: Bearer wins over API key -----

            cases.Add(CaseAsync("priority_bearer_wins_over_apikey", "AuthenticateAsync Priority BearerWinsOverApiKey", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db, apiKey: "testkey");
                    AuthContext ctx = await svc.AuthenticateAsync("Bearer " + entities.BearerToken, null, "testkey");

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated");
                    AssertEqual("Bearer", ctx.AuthMethod, "Bearer should take priority over ApiKey");
                    AssertEqual(entities.TenantId, ctx.TenantId);
                    AssertEqual(entities.UserId, ctx.UserId);
                }
            }));

            // ----- AuthenticateWithCredentialsAsync -----

            cases.Add(CaseAsync("credentials_valid_authenticated", "AuthenticateWithCredentialsAsync ValidCredentials ReturnsAuthenticated", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string password = "secretPassword123";
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db, password: password);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(entities.TenantId, "test@example.com", password);

                    AssertTrue(ctx.IsAuthenticated, "Should be authenticated with correct credentials");
                    AssertEqual(entities.TenantId, ctx.TenantId);
                    AssertEqual(entities.UserId, ctx.UserId);
                    AssertEqual("Credentials", ctx.AuthMethod);
                }
            }));

            cases.Add(CaseAsync("credentials_wrong_password_unauthenticated", "AuthenticateWithCredentialsAsync WrongPassword ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db, password: "correctPassword");

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(entities.TenantId, "test@example.com", "wrongPassword");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with wrong password");
                }
            }));

            cases.Add(CaseAsync("credentials_unknown_email_unauthenticated", "AuthenticateWithCredentialsAsync UnknownEmail ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(entities.TenantId, "unknown@example.com", "password");

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated with unknown email");
                }
            }));

            // Audit addition: credentials for an inactive user are rejected (confirmed against source).

            cases.Add(CaseAsync("credentials_inactive_user_unauthenticated", "AuthenticateWithCredentialsAsync InactiveUser ReturnsUnauthenticated", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string password = "secretPassword123";
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db, password: password, userActive: false);

                    AuthenticationService svc = CreateService(db);
                    AuthContext ctx = await svc.AuthenticateWithCredentialsAsync(entities.TenantId, "test@example.com", password);

                    AssertFalse(ctx.IsAuthenticated, "Should not be authenticated when the user is inactive");
                }
            }));

            cases.Add(CaseAsync("mission_token_lifecycle_and_scope", "A mission-scoped token works only while its mission runs on its captain and never yields a global admin", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AuthFixtureResult entities = await CreateTestEntitiesAsync(db);
                    UserMaster? owner = await db.Users.ReadByIdAsync(entities.UserId);
                    owner!.IsAdmin = true;
                    await db.Users.UpdateAsync(owner);

                    Captain captain = new Captain("mission-token-captain");
                    captain.TenantId = entities.TenantId;
                    captain.UserId = entities.UserId;
                    captain = await db.Captains.CreateAsync(captain);
                    Mission mission = new Mission("mission-token", "x");
                    mission.TenantId = entities.TenantId;
                    mission.UserId = entities.UserId;
                    mission.CaptainId = captain.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission = await db.Missions.CreateAsync(mission);

                    SessionTokenService tokens = new SessionTokenService();
                    AuthenticationService svc = CreateService(db, tokens);
                    string token = tokens.CreateMissionScopedToken(entities.TenantId, entities.UserId, mission.Id, captain.Id, TimeSpan.FromMinutes(30)).Token!;

                    AuthContext running = await svc.AuthenticateAsync(null, token, null);
                    AssertTrue(running.IsAuthenticated, "valid while the mission is in progress");
                    AssertEqual(mission.Id, running.MissionId, "carries the mission");
                    AssertFalse(running.IsAdmin, "a global-admin owner does not make the captain a global admin");
                    AssertTrue(running.IsTenantAdmin, "the captain keeps the owner's tenant role");

                    string otherCaptainToken = tokens.CreateMissionScopedToken(entities.TenantId, entities.UserId, mission.Id, "cpt_other", TimeSpan.FromMinutes(30)).Token!;
                    AssertFalse((await svc.AuthenticateAsync(null, otherCaptainToken, null)).IsAuthenticated, "another captain's token is refused");

                    mission.Status = MissionStatusEnum.WorkProduced;
                    await db.Missions.UpdateAsync(mission);
                    AssertFalse((await svc.AuthenticateAsync(null, token, null)).IsAuthenticated, "refused once the mission leaves InProgress");

                    mission.Status = MissionStatusEnum.InProgress;
                    await db.Missions.UpdateAsync(mission);
                    await db.Missions.DeleteAsync(mission.Id);
                    AssertFalse((await svc.AuthenticateAsync(null, token, null)).IsAuthenticated, "refused once the mission is gone");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.AuthenticationService",
                displayName: "AuthenticationService",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AuthenticationService CreateService(
            DatabaseDriver db,
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

        private static async Task<AuthFixtureResult> CreateTestEntitiesAsync(
            DatabaseDriver db,
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

            return new AuthFixtureResult
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                BearerToken = credential.BearerToken
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.AuthenticationService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Types

        private sealed class AuthFixtureResult
        {
            public string TenantId { get; set; } = String.Empty;

            public string UserId { get; set; } = String.Empty;

            public string BearerToken { get; set; } = String.Empty;
        }

        #endregion
    }
}
