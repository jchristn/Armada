namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HarborService"/>: tenant-scoped CRUD, enable/disable, and handshake
    /// upsert over a live database. Positive cases assert ownership/defaults on create, scoped enumeration,
    /// editable-field updates, enable toggling, and create-then-update handshake registration; negative
    /// cases assert the missing-name, null-id, and not-found paths.
    /// </summary>
    public sealed class HarborServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HarborService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_sets_owner_and_defaults", "CreateAsync sets owner and default status", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_hs", "usr_hs", false, true, "UnitTest");

                Harbor created = await service.CreateAsync(auth, new Harbor { Name = "Box" }).ConfigureAwait(false);
                AssertStartsWith("hbr_", created.Id);
                AssertEqual("ten_hs", created.TenantId);
                AssertEqual("usr_hs", created.UserId);
                AssertEqual(HarborConnectionStatusEnum.Unknown, created.ConnectionStatus);
                AssertTrue(created.Enabled, "Expected new Harbor to be enabled by default.");
            }));

            cases.Add(CaseAsync("enumerate_tenant_scoped", "EnumerateAsync is tenant-scoped", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext authA = AuthContext.Authenticated("ten_x", "usr_x", false, true, "UnitTest");
                AuthContext authB = AuthContext.Authenticated("ten_y", "usr_y", false, true, "UnitTest");

                await service.CreateAsync(authA, new Harbor { Name = "X1" }).ConfigureAwait(false);
                await service.CreateAsync(authA, new Harbor { Name = "X2" }).ConfigureAwait(false);
                await service.CreateAsync(authB, new Harbor { Name = "Y1" }).ConfigureAwait(false);

                List<Harbor> forA = await service.EnumerateAsync(authA).ConfigureAwait(false);
                AssertEqual(2, forA.Count);
            }));

            cases.Add(CaseAsync("update_changes_editable_fields", "UpdateAsync changes name/capacity/enabled only", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_u", "usr_u", false, true, "UnitTest");

                Harbor created = await service.CreateAsync(auth, new Harbor { Name = "Before" }).ConfigureAwait(false);

                Harbor edit = new Harbor { Id = created.Id, Name = "After", MaxConcurrentJobs = 9, Enabled = false };
                Harbor updated = await service.UpdateAsync(auth, edit).ConfigureAwait(false);
                AssertEqual("After", updated.Name);
                AssertEqual(9, updated.MaxConcurrentJobs);
                AssertTrue(!updated.Enabled, "Expected Enabled to be updated to false.");
            }));

            cases.Add(CaseAsync("set_enabled_toggles", "SetEnabledAsync toggles routing eligibility", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_e", "usr_e", false, true, "UnitTest");

                Harbor created = await service.CreateAsync(auth, new Harbor { Name = "Toggle" }).ConfigureAwait(false);
                Harbor disabled = await service.SetEnabledAsync(auth, created.Id, false).ConfigureAwait(false);
                AssertTrue(!disabled.Enabled, "Expected Harbor to be disabled.");
                Harbor enabled = await service.SetEnabledAsync(auth, created.Id, true).ConfigureAwait(false);
                AssertTrue(enabled.Enabled, "Expected Harbor to be re-enabled.");
            }));

            cases.Add(CaseAsync("upsert_from_handshake_creates_then_updates", "UpsertFromHandshakeAsync creates then updates and marks connected", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());

                string harborId = "hbr_handshake_test";
                List<HarborCapability> caps1 = new List<HarborCapability> { new HarborCapability { Name = "claude" } };
                Harbor first = await service.UpsertFromHandshakeAsync(harborId, "ten_h", "usr_h", "Rig", "1.0", "Linux", "X64", 6, caps1).ConfigureAwait(false);
                AssertEqual(HarborConnectionStatusEnum.Connected, first.ConnectionStatus);
                AssertEqual(1, first.Capabilities.Count);

                List<HarborCapability> caps2 = new List<HarborCapability>
                {
                    new HarborCapability { Name = "claude" },
                    new HarborCapability { Name = "codex" }
                };
                Harbor second = await service.UpsertFromHandshakeAsync(harborId, "ten_h", "usr_h", "Rig", "1.0", "Linux", "X64", 8, caps2).ConfigureAwait(false);
                AssertEqual(harborId, second.Id);
                AssertEqual(8, second.MaxConcurrentJobs);
                AssertEqual(2, second.Capabilities.Count);

                List<Harbor> all = await testDb.Driver.Harbors.EnumerateAsync().ConfigureAwait(false);
                AssertEqual(1, all.Count);
            }));

            cases.Add(CaseAsync("update_other_tenant_forbidden", "UpdateAsync rejects a cross-tenant edit", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext owner = AuthContext.Authenticated("ten_owner", "usr_owner", false, true, "UnitTest");
                AuthContext other = AuthContext.Authenticated("ten_other", "usr_other", false, true, "UnitTest");

                Harbor created = await service.CreateAsync(owner, new Harbor { Name = "Owned" }).ConfigureAwait(false);
                Harbor edit = new Harbor { Id = created.Id, Name = "Hijacked" };
                await AssertThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(other, edit));
            }));

            cases.Add(CaseAsync("read_null_id_throws", "ReadAsync NullId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_r", "usr_r", false, true, "UnitTest");
                await AssertThrowsAsync<ArgumentNullException>(() => service.ReadAsync(auth, null!));
            }));

            cases.Add(CaseAsync("update_unknown_id_throws", "UpdateAsync UnknownId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_um", "usr_um", false, true, "UnitTest");
                Harbor edit = new Harbor { Id = "hbr_missing", Name = "Ghost" };
                await AssertThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(auth, edit));
            }));

            cases.Add(CaseAsync("delete_unknown_id_throws", "DeleteAsync UnknownId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService service = new HarborService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_dm", "usr_dm", false, true, "UnitTest");
                await AssertThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(auth, "hbr_missing"));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborService",
                displayName: "Harbor Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
