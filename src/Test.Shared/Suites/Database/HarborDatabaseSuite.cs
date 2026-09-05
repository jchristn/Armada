namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for Harbor persistence and entity invariants over a live database. Positive cases assert
    /// create/read round-trips including the capability child rows, capability replacement on update,
    /// tenant-scoped enumeration without cross-tenant leakage, and cascade delete; negative cases assert the
    /// audited null-id/null-entity paths and the entity's name/id guard clauses.
    /// </summary>
    public sealed class HarborDatabaseSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Harbor database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_read_roundtrips_with_capabilities", "CreateAsync/ReadAsync round-trips a Harbor with capabilities", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Harbor harbor = NewHarbor("ten_hbr", "usr_hbr", "Workstation");
                harbor.Capabilities.Add(new HarborCapability { Name = "claude", Available = true, Detail = "2.0" });
                harbor.Capabilities.Add(new HarborCapability { Name = "git", Available = true });

                Harbor created = await testDb.Driver.Harbors.CreateAsync(harbor).ConfigureAwait(false);
                AssertStartsWith("hbr_", created.Id);

                Harbor? reloaded = await testDb.Driver.Harbors.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected Harbor to reload.");
                AssertEqual("Workstation", reloaded!.Name);
                AssertEqual(HarborConnectionStatusEnum.Unknown, reloaded.ConnectionStatus);
                AssertEqual(2, reloaded.Capabilities.Count);
                AssertTrue(reloaded.Capabilities.Exists(c => c.Name == "claude" && c.Available), "Expected the claude capability to round-trip.");
            }));

            cases.Add(CaseAsync("update_replaces_capabilities", "UpdateAsync replaces the capability set", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Harbor harbor = NewHarbor("ten_hbr_u", "usr_hbr_u", "Laptop");
                harbor.Capabilities.Add(new HarborCapability { Name = "claude" });
                harbor.Capabilities.Add(new HarborCapability { Name = "codex" });
                Harbor created = await testDb.Driver.Harbors.CreateAsync(harbor).ConfigureAwait(false);

                created.Capabilities = new List<HarborCapability> { new HarborCapability { Name = "opencode" } };
                created.ConnectionStatus = HarborConnectionStatusEnum.Connected;
                await testDb.Driver.Harbors.UpdateAsync(created).ConfigureAwait(false);

                Harbor? reloaded = await testDb.Driver.Harbors.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected Harbor to reload.");
                AssertEqual(1, reloaded!.Capabilities.Count);
                AssertEqual("opencode", reloaded.Capabilities[0].Name);
                AssertEqual(HarborConnectionStatusEnum.Connected, reloaded.ConnectionStatus);
            }));

            cases.Add(CaseAsync("enumerate_is_tenant_scoped", "EnumerateAsync is tenant-scoped", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                await testDb.Driver.Harbors.CreateAsync(NewHarbor("ten_a", "usr_a", "A1")).ConfigureAwait(false);
                await testDb.Driver.Harbors.CreateAsync(NewHarbor("ten_a", "usr_a", "A2")).ConfigureAwait(false);
                await testDb.Driver.Harbors.CreateAsync(NewHarbor("ten_b", "usr_b", "B1")).ConfigureAwait(false);

                List<Harbor> tenantA = await testDb.Driver.Harbors.EnumerateAsync("ten_a").ConfigureAwait(false);
                AssertEqual(2, tenantA.Count);
                AssertTrue(tenantA.TrueForAll(h => h.TenantId == "ten_a"), "Expected no cross-tenant leakage.");
            }));

            cases.Add(CaseAsync("delete_cascades_capabilities", "DeleteAsync removes the Harbor and its capabilities", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Harbor harbor = NewHarbor("ten_hbr_d", "usr_hbr_d", "Doomed");
                harbor.Capabilities.Add(new HarborCapability { Name = "claude" });
                Harbor created = await testDb.Driver.Harbors.CreateAsync(harbor).ConfigureAwait(false);

                await testDb.Driver.Harbors.DeleteAsync(created.Id).ConfigureAwait(false);

                Harbor? reloaded = await testDb.Driver.Harbors.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNull(reloaded, "Expected Harbor to be deleted.");
            }));

            cases.Add(CaseAsync("max_concurrent_jobs_clamped", "MaxConcurrentJobs clamps to a minimum of 1", TestTags.Positive, () =>
            {
                Harbor harbor = new Harbor();
                harbor.MaxConcurrentJobs = 0;
                AssertEqual(1, harbor.MaxConcurrentJobs);
                harbor.MaxConcurrentJobs = 12;
                AssertEqual(12, harbor.MaxConcurrentJobs);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("read_null_id_throws", "ReadAsync NullId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.Harbors.ReadAsync(null!));
            }));

            cases.Add(CaseAsync("create_null_throws", "CreateAsync NullEntity Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.Harbors.CreateAsync(null!));
            }));

            cases.Add(CaseAsync("name_empty_throws", "Harbor Name empty throws", TestTags.Negative, () =>
            {
                Harbor harbor = new Harbor();
                AssertThrows<ArgumentNullException>(() => harbor.Name = "   ");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("id_empty_throws", "Harbor Id empty throws", TestTags.Negative, () =>
            {
                Harbor harbor = new Harbor();
                AssertThrows<ArgumentNullException>(() => harbor.Id = "");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: "Database.Harbor",
                displayName: "Harbor Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Harbor NewHarbor(string tenantId, string userId, string name)
        {
            return new Harbor
            {
                TenantId = tenantId,
                UserId = userId,
                Name = name
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Database.Harbor",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
