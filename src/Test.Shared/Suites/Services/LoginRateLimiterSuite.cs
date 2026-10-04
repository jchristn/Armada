namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Unit coverage for <see cref="LoginRateLimiter"/> (O-05) on a manual clock: per-account and per-address lockout,
    /// counter reset on success, window expiry, exponential backoff with a cap, the lookup budget, settings clamping, and
    /// the disabled switch.
    /// </summary>
    public sealed class LoginRateLimiterSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LoginRateLimiter";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("defaults", "Defaults: enabled, 10 per account, 50 per address, 15 minute window and lockout", TestTags.Positive, () =>
            {
                LoginRateLimitSettings settings = new LoginRateLimitSettings();
                AssertTrue(settings.Enabled);
                AssertEqual(10, settings.MaxFailuresPerAccount);
                AssertEqual(50, settings.MaxFailuresPerAddress);
                AssertEqual(15, settings.WindowMinutes);
                AssertEqual(15, settings.LockoutMinutes);
                AssertEqual(1440, settings.MaxLockoutMinutes);
                AssertEqual(10, new ArmadaSettings().LoginRateLimit.MaxFailuresPerAccount);
            }));

            cases.Add(Case("settings_clamped", "Settings values are clamped to their ranges", TestTags.Negative, () =>
            {
                LoginRateLimitSettings settings = new LoginRateLimitSettings();
                settings.MaxFailuresPerAccount = 0;
                settings.MaxFailuresPerAddress = -5;
                settings.WindowMinutes = 100000;
                settings.LockoutMinutes = 0;
                settings.MaxLockoutMinutes = 999999;
                AssertEqual(1, settings.MaxFailuresPerAccount);
                AssertEqual(1, settings.MaxFailuresPerAddress);
                AssertEqual(1440, settings.WindowMinutes);
                AssertEqual(1, settings.LockoutMinutes);
                AssertEqual(10080, settings.MaxLockoutMinutes);
            }));

            cases.Add(Case("account_lockout", "The account locks after MaxFailuresPerAccount failures for LockoutMinutes", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                LoginRateLimiter limiter = new LoginRateLimiter(Settings(3, 100), () => now);
                for (int i = 0; i < 2; i++)
                {
                    AssertNull(limiter.CheckPasswordLogin("ten", "a@x", "1.1.1.1"));
                    limiter.RecordPasswordFailure("ten", "A@X", "1.1.1.1");
                }

                AssertNull(limiter.CheckPasswordLogin("ten", "a@x", "1.1.1.1"), "below the limit");
                limiter.RecordPasswordFailure("ten", "a@x", "1.1.1.1");
                TimeSpan? locked = limiter.CheckPasswordLogin("ten", "a@x", "2.2.2.2");
                AssertNotNull(locked, "locked from any address");
                AssertEqual(TimeSpan.FromMinutes(15), locked!.Value);
                AssertEqual("900", LoginRateLimiter.ToRetryAfterSeconds(locked.Value));
                AssertNull(limiter.CheckPasswordLogin("ten", "b@x", "1.1.1.1"), "another account is not locked");
                AssertNull(limiter.CheckPasswordLogin("other", "a@x", "1.1.1.1"), "same email in another tenant is not locked");

                now = now.AddMinutes(15).AddSeconds(1);
                AssertNull(limiter.CheckPasswordLogin("ten", "a@x", "1.1.1.1"), "lockout expired");
            }));

            cases.Add(Case("success_resets", "A successful login clears the account counter", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                LoginRateLimiter limiter = new LoginRateLimiter(Settings(3, 100), () => now);
                limiter.RecordPasswordFailure("ten", "a@x", "1.1.1.1");
                limiter.RecordPasswordFailure("ten", "a@x", "1.1.1.1");
                limiter.RecordPasswordSuccess("ten", "a@x");
                limiter.RecordPasswordFailure("ten", "a@x", "1.1.1.1");
                limiter.RecordPasswordFailure("ten", "a@x", "1.1.1.1");
                AssertNull(limiter.CheckPasswordLogin("ten", "a@x", "1.1.1.1"));
            }));

            cases.Add(Case("window_expiry", "Failures older than the window are forgotten", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                LoginRateLimiter limiter = new LoginRateLimiter(Settings(3, 100), () => now);
                limiter.RecordPasswordFailure("ten", "a@x", null);
                limiter.RecordPasswordFailure("ten", "a@x", null);
                now = now.AddMinutes(16);
                limiter.RecordPasswordFailure("ten", "a@x", null);
                limiter.RecordPasswordFailure("ten", "a@x", null);
                AssertNull(limiter.CheckPasswordLogin("ten", "a@x", null));
            }));

            cases.Add(Case("exponential_backoff", "Each further lockout doubles, capped at MaxLockoutMinutes", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                LoginRateLimitSettings settings = Settings(2, 100);
                settings.MaxLockoutMinutes = 50;
                LoginRateLimiter limiter = new LoginRateLimiter(settings, () => now);
                int[] expected = new int[] { 15, 30, 50, 50 };
                foreach (int minutes in expected)
                {
                    limiter.RecordPasswordFailure("ten", "a@x", null);
                    limiter.RecordPasswordFailure("ten", "a@x", null);
                    TimeSpan? locked = limiter.CheckPasswordLogin("ten", "a@x", null);
                    AssertNotNull(locked);
                    AssertEqual(TimeSpan.FromMinutes(minutes), locked!.Value);
                    limiter.RecordPasswordFailure("ten", "a@x", null);
                    AssertEqual(TimeSpan.FromMinutes(minutes), limiter.CheckPasswordLogin("ten", "a@x", null)!.Value, "failures while locked do not extend");
                    now = now.AddMinutes(minutes).AddSeconds(1);
                }
            }));

            cases.Add(Case("address_lockout", "Failures from one address lock the address for password and credential checks", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                LoginRateLimiter limiter = new LoginRateLimiter(Settings(100, 3), () => now);
                limiter.RecordAddressFailure("9.9.9.9");
                limiter.RecordPasswordFailure("ten", "a@x", "9.9.9.9");
                AssertNull(limiter.CheckAddress("9.9.9.9"));
                limiter.RecordPasswordFailure("ten", "b@x", "9.9.9.9");
                AssertNotNull(limiter.CheckAddress("9.9.9.9"));
                AssertNotNull(limiter.CheckPasswordLogin("ten", "c@x", "9.9.9.9"), "any account from the locked address");
                AssertNull(limiter.CheckPasswordLogin("ten", "c@x", "8.8.8.8"), "other address");
                AssertNull(limiter.CheckAddress(null));
            }));

            cases.Add(Case("lookup_budget", "Tenant lookup and onboarding requests have their own per-address budget", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                LoginRateLimiter limiter = new LoginRateLimiter(Settings(100, 3), () => now);
                AssertNull(limiter.CheckAndCountLookup("7.7.7.7"));
                AssertNull(limiter.CheckAndCountLookup("7.7.7.7"));
                AssertNull(limiter.CheckAndCountLookup("7.7.7.7"));
                AssertNotNull(limiter.CheckAndCountLookup("7.7.7.7"));
                AssertNull(limiter.CheckAddress("7.7.7.7"), "lookups do not lock password or credential authentication");
            }));

            cases.Add(Case("disabled", "Enabled=false never locks out", TestTags.Positive, () =>
            {
                LoginRateLimitSettings settings = Settings(1, 1);
                settings.Enabled = false;
                LoginRateLimiter limiter = new LoginRateLimiter(settings);
                for (int i = 0; i < 5; i++)
                {
                    limiter.RecordPasswordFailure("ten", "a@x", "1.1.1.1");
                    limiter.RecordAddressFailure("1.1.1.1");
                    AssertNull(limiter.CheckAndCountLookup("1.1.1.1"));
                }

                AssertNull(limiter.CheckPasswordLogin("ten", "a@x", "1.1.1.1"));
                AssertNull(limiter.CheckAddress("1.1.1.1"));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Login Rate Limiter",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoginRateLimitSettings Settings(int perAccount, int perAddress)
        {
            LoginRateLimitSettings settings = new LoginRateLimitSettings();
            settings.MaxFailuresPerAccount = perAccount;
            settings.MaxFailuresPerAddress = perAddress;
            return settings;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
