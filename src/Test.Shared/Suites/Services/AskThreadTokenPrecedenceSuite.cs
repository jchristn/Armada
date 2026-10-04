namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// An Ask thread-scoped session token always wins over other credentials on the same request, so a captain whose
    /// CLI also sends the user's own bearer token cannot bypass the thread approval gate; a thread token combined with
    /// a credential for a different identity (another user's bearer token or the local API key) is refused.
    /// </summary>
    public sealed class AskThreadTokenPrecedenceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskThreadTokenPrecedence";
        private const string ApiKey = "precedence-test-api-key";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("thread_token_alone_is_a_thread_call", "A thread token alone authenticates as a thread call", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Fixture fx = await CreateFixtureAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext ctx = await fx.Service.AuthenticateAsync(null, fx.ThreadToken, null).ConfigureAwait(false);
                AssertTrue(ctx.IsAuthenticated, "authenticated");
                AssertEqual("ath_precedence", ctx.AskThreadId);
            }));

            cases.Add(CaseAsync("thread_token_wins_over_same_user_bearer", "With the same user's bearer token also present, the thread token wins", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Fixture fx = await CreateFixtureAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext ctx = await fx.Service.AuthenticateAsync("Bearer " + fx.OwnerBearer, fx.ThreadToken, null).ConfigureAwait(false);
                AssertTrue(ctx.IsAuthenticated, "authenticated");
                AssertEqual("ath_precedence", ctx.AskThreadId, "still a thread call, so the approval gate applies");
                AssertEqual(fx.OwnerUserId, ctx.UserId);
            }));

            cases.Add(CaseAsync("thread_token_with_other_identity_is_refused", "A thread token with another user's bearer token, the API key, or an invalid bearer is refused", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Fixture fx = await CreateFixtureAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext otherUser = await fx.Service.AuthenticateAsync("Bearer " + fx.OtherBearer, fx.ThreadToken, null).ConfigureAwait(false);
                AssertFalse(otherUser.IsAuthenticated, "other user's bearer");
                AuthContext apiKey = await fx.Service.AuthenticateAsync(null, fx.ThreadToken, ApiKey).ConfigureAwait(false);
                AssertFalse(apiKey.IsAuthenticated, "API key (system admin)");
                AuthContext invalid = await fx.Service.AuthenticateAsync("Bearer not-a-token", fx.ThreadToken, null).ConfigureAwait(false);
                AssertFalse(invalid.IsAuthenticated, "invalid bearer");
            }));

            cases.Add(CaseAsync("plain_session_token_keeps_bearer_precedence", "Without a thread token, the bearer credential keeps precedence as before", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Fixture fx = await CreateFixtureAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext ctx = await fx.Service.AuthenticateAsync("Bearer " + fx.OtherBearer, fx.PlainToken, null).ConfigureAwait(false);
                AssertTrue(ctx.IsAuthenticated, "authenticated");
                AssertEqual(fx.OtherUserId, ctx.UserId, "bearer wins over a plain session token");
                AssertNull(ctx.AskThreadId);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Ask thread token precedence",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Fixture> CreateFixtureAsync(DatabaseDriver db)
        {
            TenantMetadata tenant = new TenantMetadata("Precedence Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(tenant).ConfigureAwait(false);

            UserMaster owner = new UserMaster(tenant.Id, "owner-" + Guid.NewGuid().ToString("N").Substring(0, 6) + "@example.com", "password");
            await db.Users.CreateAsync(owner).ConfigureAwait(false);
            Credential ownerCredential = new Credential(tenant.Id, owner.Id);
            await db.Credentials.CreateAsync(ownerCredential).ConfigureAwait(false);

            UserMaster other = new UserMaster(tenant.Id, "other-" + Guid.NewGuid().ToString("N").Substring(0, 6) + "@example.com", "password");
            await db.Users.CreateAsync(other).ConfigureAwait(false);
            Credential otherCredential = new Credential(tenant.Id, other.Id);
            await db.Credentials.CreateAsync(otherCredential).ConfigureAwait(false);

            SessionTokenService tokens = new SessionTokenService();
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.ApiKey = ApiKey;

            Fixture fx = new Fixture();
            fx.Service = new AuthenticationService(db, tokens, settings, logging);
            fx.OwnerUserId = owner.Id;
            fx.OtherUserId = other.Id;
            fx.OwnerBearer = ownerCredential.BearerToken;
            fx.OtherBearer = otherCredential.BearerToken;
            fx.ThreadToken = tokens.CreateThreadScopedToken(tenant.Id, owner.Id, "ath_precedence", TimeSpan.FromMinutes(30)).Token!;
            fx.PlainToken = tokens.CreateToken(tenant.Id, owner.Id).Token!;
            return fx;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Nested-Types

        private sealed class Fixture
        {
            public AuthenticationService Service { get; set; } = null!;

            public string OwnerUserId { get; set; } = String.Empty;

            public string OtherUserId { get; set; } = String.Empty;

            public string OwnerBearer { get; set; } = String.Empty;

            public string OtherBearer { get; set; } = String.Empty;

            public string ThreadToken { get; set; } = String.Empty;

            public string PlainToken { get; set; } = String.Empty;
        }

        #endregion
    }
}
