namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors verifying that database initialization seeds the expected default records: the
    /// default tenant, admin user (with the correct computed password hash and verification), and
    /// credential (retrievable by bearer token), that ExistsAnyAsync reports data present, and that
    /// re-initialization is idempotent and does not duplicate seed rows. Positive cases assert the
    /// seeded data is present and correct; a negative audit case confirms an unknown tenant read
    /// returns null.
    /// </summary>
    public sealed class DefaultSeedingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.DefaultSeeding";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Default Data Seeding suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("initialize_seeds_default_tenant", "InitializeAsync seeds default tenant", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TenantMetadata? tenant = await db.Tenants.ReadAsync(Constants.DefaultTenantId);
                    AssertNotNull(tenant);
                    AssertEqual(Constants.DefaultTenantId, tenant!.Id);
                    AssertEqual(Constants.DefaultTenantName, tenant.Name);
                    AssertTrue(tenant.Active);
                }
            }));

            cases.Add(CaseAsync("initialize_seeds_default_user", "InitializeAsync seeds default user", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    UserMaster? user = await db.Users.ReadAsync(Constants.DefaultTenantId, Constants.DefaultUserId);
                    AssertNotNull(user);
                    AssertEqual(Constants.DefaultUserId, user!.Id);
                    AssertEqual(Constants.DefaultTenantId, user.TenantId);
                    AssertEqual(Constants.DefaultUserEmail, user.Email);
                    AssertTrue(user.IsAdmin, "Default user should be admin");
                    AssertTrue(user.Active);
                }
            }));

            cases.Add(CaseAsync("default_user_has_correct_password_hash", "Default user has correct password hash", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    UserMaster? user = await db.Users.ReadAsync(Constants.DefaultTenantId, Constants.DefaultUserId);
                    AssertNotNull(user);

                    // Seeded salted and stretched (PBKDF2), never as the unsalted SHA-256 of the default password.
                    AssertTrue(Armada.Core.Services.PasswordHasher.IsAdaptiveHash(user!.PasswordSha256), "seeded hash is PBKDF2");
                    AssertFalse(user.PasswordSha256.Contains(UserMaster.ComputePasswordHash(Constants.DefaultUserPassword)), "no unsalted digest");
                    AssertTrue(user.VerifyPassword(Constants.DefaultUserPassword));
                    AssertTrue(user.UsesDefaultPassword());
                }
            }));

            cases.Add(CaseAsync("initialize_seeds_default_credential", "InitializeAsync seeds default credential", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Credential? cred = await db.Credentials.ReadAsync(Constants.DefaultTenantId, Constants.DefaultCredentialId);
                    AssertNotNull(cred);
                    AssertEqual(Constants.DefaultCredentialId, cred!.Id);
                    AssertEqual(Constants.DefaultTenantId, cred.TenantId);
                    AssertEqual(Constants.DefaultUserId, cred.UserId);
                    AssertEqual(Constants.DefaultBearerToken, cred.BearerToken);
                    AssertEqual(Constants.DefaultCredentialName, cred.Name);
                    AssertTrue(cred.Active);
                }
            }));

            cases.Add(CaseAsync("default_credential_retrievable_by_bearer_token", "Default credential is retrievable by bearer token", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Credential? cred = await db.Credentials.ReadByBearerTokenAsync(Constants.DefaultBearerToken);
                    AssertNotNull(cred);
                    AssertEqual(Constants.DefaultCredentialId, cred!.Id);
                }
            }));

            cases.Add(CaseAsync("exists_any_returns_true_after_seeding", "ExistsAnyAsync returns true after seeding", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AssertTrue(await db.Tenants.ExistsAnyAsync());
                }
            }));

            cases.Add(CaseAsync("second_initialize_does_not_duplicate_seed_data", "Second InitializeAsync does not duplicate seed data", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    // The database was already initialized once.
                    // Count tenants before
                    List<TenantMetadata> before = await db.Tenants.EnumerateAsync();

                    // Re-initialize should be idempotent (ExistsAnyAsync returns true so seeding skips)
                    await db.InitializeAsync();

                    List<TenantMetadata> after = await db.Tenants.EnumerateAsync();
                    AssertEqual(before.Count, after.Count, "Tenant count should not change on re-initialize");
                }
            }));

            cases.Add(CaseAsync("unknown_tenant_read_returns_null_audit", "ReadAsync UnknownTenant ReturnsNull", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TenantMetadata? tenant = await db.Tenants.ReadAsync("ten_nonexistent");
                    AssertNull(tenant);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Default Data Seeding",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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
    }
}
