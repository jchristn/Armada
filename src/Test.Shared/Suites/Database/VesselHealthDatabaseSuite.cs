namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for vessel health persistence over a live database: health row upsert and read, the LEFT JOIN
    /// that surfaces never-evaluated vessels, tenant isolation, paging, every whitelisted sort column (with status
    /// rank and null-last ordering), every filter, the KPI summary, replace-for-vessel semantics for findings and
    /// dependencies, override upsert uniqueness, and cascade deletion when a vessel is deleted.
    /// </summary>
    public sealed class VesselHealthDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.VesselHealth";
        private static readonly DateTime _Fixed = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Vessel Health database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("upsert_inserts_then_updates_in_place", "UpsertAsync inserts once, then updates keeping Id and CreatedUtc", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Fleet fleet = await CreateFleetAsync(db, Constants.DefaultTenantId, "Upsert Fleet").ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(db, Constants.DefaultTenantId, "upsert-vessel", fleet.Id, true).ConfigureAwait(false);

                VesselHealth health = NewHealth(vessel);
                health.Id = null;
                health.CreatedUtc = _Fixed;
                health.OverallStatus = VesselHealthStatusEnum.Warn;
                health.EvaluatedUtc = _Fixed;
                health.EvaluationDurationMs = 1234;
                health.ErrorCode = "None";
                health.EvaluatedPath = "/code/upsert";
                health.CurrentBranch = "main";
                health.IsDirty = true;
                health.UntrackedCount = 2;
                health.AheadOfDefault = 1;
                health.BehindDefault = 3;
                health.AheadOfUpstream = 0;
                health.BehindUpstream = 4;
                health.LastCommitUtc = _Fixed.AddDays(-1);
                health.BranchCount = 5;
                health.StaleBranchCount = 2;
                health.ArmadaBranchCount = 1;
                health.PrimaryLanguage = "CSharp";
                health.ProjectCount = 3;
                health.OutdatedCount = 4;
                health.OutdatedMajorCount = 1;
                health.VulnerableCount = 2;
                health.MaxVulnerabilitySeverity = VulnerabilitySeverityEnum.Critical;
                health.DependencyStatus = VesselHealthStatusEnum.Fail;
                health.VulnerabilityStatus = VesselHealthStatusEnum.Fail;
                health.TestInfraStatus = VesselHealthStatusEnum.Pass;
                health.CiStatus = VesselHealthStatusEnum.NotApplicable;
                health.DivergenceStatus = VesselHealthStatusEnum.Warn;
                health.WorkingTreeStatus = VesselHealthStatusEnum.Fail;
                health.BranchStatus = VesselHealthStatusEnum.Pass;
                health.ReadinessStatus = VesselHealthStatusEnum.Pass;
                health.MissionOutcomeStatus = VesselHealthStatusEnum.Warn;
                health.LastCheckRunStatus = "Passed";
                health.HasCiConfig = false;
                health.HasLicense = true;
                health.HasReadme = true;
                health.ReadinessErrorCount = 0;
                health.RecentMissionFailureCount = 1;
                health.ManifestHash = "abc123";
                health.DependenciesEvaluatedUtc = _Fixed;

                VesselHealth created = await db.VesselHealth.UpsertAsync(health).ConfigureAwait(false);
                AssertStartsWith("vhl_", created.Id!);
                string originalId = created.Id!;

                VesselHealth? read = await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertNotNull(read, "health");
                AssertEqual(originalId, read!.Id);
                AssertEqual("upsert-vessel", read.VesselName);
                AssertEqual(fleet.Id, read.FleetId);
                AssertEqual("Upsert Fleet", read.FleetName);
                AssertEqual(VesselHealthStatusEnum.Warn, read.OverallStatus);
                AssertEqual(1234L, read.EvaluationDurationMs!.Value);
                AssertEqual("/code/upsert", read.EvaluatedPath);
                AssertEqual(true, read.IsDirty);
                AssertEqual(2, read.UntrackedCount!.Value);
                AssertEqual(3, read.BehindDefault!.Value);
                AssertEqual(4, read.BehindUpstream!.Value);
                AssertSameInstant(_Fixed.AddDays(-1), read.LastCommitUtc!.Value, "LastCommitUtc");
                AssertEqual(1, read.ArmadaBranchCount!.Value);
                AssertEqual("CSharp", read.PrimaryLanguage);
                AssertEqual(VulnerabilitySeverityEnum.Critical, read.MaxVulnerabilitySeverity);
                AssertEqual(VesselHealthStatusEnum.Fail, read.DependencyStatus);
                AssertEqual(VesselHealthStatusEnum.NotApplicable, read.CiStatus);
                AssertEqual(VesselHealthStatusEnum.Warn, read.MissionOutcomeStatus);
                AssertEqual("Passed", read.LastCheckRunStatus);
                AssertEqual(false, read.HasCiConfig);
                AssertEqual(true, read.HasLicense);
                AssertEqual("abc123", read.ManifestHash);
                AssertSameInstant(_Fixed, read.CreatedUtc, "CreatedUtc");

                VesselHealth second = NewHealth(vessel);
                second.OverallStatus = VesselHealthStatusEnum.Pass;
                second.BranchCount = 9;
                await db.VesselHealth.UpsertAsync(second).ConfigureAwait(false);
                AssertEqual(originalId, second.Id, "upsert keeps the identifier");

                VesselHealth? reread = await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertEqual(originalId, reread!.Id);
                AssertEqual(VesselHealthStatusEnum.Pass, reread.OverallStatus);
                AssertEqual(9, reread.BranchCount!.Value);
                AssertNull(reread.IsDirty, "replaced row clears IsDirty");
                AssertSameInstant(_Fixed, reread.CreatedUtc, "CreatedUtc preserved");

                EnumerationResult<VesselHealth> all = await db.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, new VesselHealthEnumerateRequest()).ConfigureAwait(false);
                AssertEqual(1L, all.TotalRecords, "one row per vessel");
            }));

            cases.Add(CaseAsync("upsert_requires_tenant_and_vessel", "UpsertAsync rejects a row without a tenant or vessel", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.VesselHealth.UpsertAsync(null!));
                VesselHealth noVessel = new VesselHealth();
                noVessel.TenantId = Constants.DefaultTenantId;
                await AssertThrowsAsync<ArgumentException>(() => testDb.Driver.VesselHealth.UpsertAsync(noVessel));
                VesselHealth noTenant = new VesselHealth();
                noTenant.VesselId = "vsl_x";
                await AssertThrowsAsync<ArgumentException>(() => testDb.Driver.VesselHealth.UpsertAsync(noTenant));
            }));

            cases.Add(CaseAsync("enumerate_left_join_includes_unevaluated", "EnumerateAsync includes never-evaluated vessels with a null Id and Unknown statuses", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                VesselHealthTestFixture f = await SeedAsync(testDb.Driver).ConfigureAwait(false);

                EnumerationResult<VesselHealth> page = await testDb.Driver.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, new VesselHealthEnumerateRequest()).ConfigureAwait(false);
                AssertEqual(5L, page.TotalRecords, "active vessels in tenant");
                VesselHealth? epsilon = page.Objects.FirstOrDefault(h => h.VesselId == f.Epsilon.Id);
                AssertNotNull(epsilon, "unevaluated vessel present");
                AssertNull(epsilon!.Id, "unevaluated Id is null");
                AssertEqual(VesselHealthStatusEnum.Unknown, epsilon.OverallStatus);
                AssertEqual(VesselHealthStatusEnum.Unknown, epsilon.DependencyStatus);
                AssertNull(epsilon.BranchCount, "no measurements");
                AssertEqual("epsilon", epsilon.VesselName);
                AssertEqual("Beta Fleet", epsilon.FleetName);
                AssertEqual(Constants.DefaultTenantId, epsilon.TenantId);

                VesselHealthEnumerateRequest inactive = new VesselHealthEnumerateRequest();
                inactive.IncludeInactive = true;
                EnumerationResult<VesselHealth> withInactive = await testDb.Driver.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, inactive).ConfigureAwait(false);
                AssertEqual(6L, withInactive.TotalRecords, "IncludeInactive");
            }));

            cases.Add(CaseAsync("tenant_isolation", "Another tenant's vessels are not read or enumerated", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselHealthTestFixture f = await SeedAsync(db).ConfigureAwait(false);

                AssertNull(await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, f.OtherTenantVessel.Id).ConfigureAwait(false), "cross-tenant read");
                AssertNull(await db.VesselHealth.ReadByVesselAsync(f.OtherTenantId, f.Alpha.Id).ConfigureAwait(false), "cross-tenant read reversed");

                EnumerationResult<VesselHealth> mine = await db.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, Request(r => r.NameContains = "alpha")).ConfigureAwait(false);
                AssertEqual(1L, mine.TotalRecords, "other tenant's alpha excluded");
                EnumerationResult<VesselHealth> theirs = await db.VesselHealth.EnumerateAsync(f.OtherTenantId, new VesselHealthEnumerateRequest()).ConfigureAwait(false);
                AssertEqual(1L, theirs.TotalRecords, "other tenant sees only its own vessel");

                await db.VesselHealth.DeleteByVesselAsync(f.OtherTenantId, f.Alpha.Id).ConfigureAwait(false);
                AssertNotNull(await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, f.Alpha.Id).ConfigureAwait(false), "cross-tenant delete is a no-op");

                VesselHealthSummary otherSummary = await db.VesselHealth.CountByOverallStatusAsync(f.OtherTenantId).ConfigureAwait(false);
                AssertEqual(1L, otherSummary.TotalVessels, "summary is tenant-scoped");
            }));

            cases.Add(CaseAsync("enumerate_pages", "EnumerateAsync pages in SQL", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedAsync(testDb.Driver).ConfigureAwait(false);

                VesselHealthEnumerateRequest request = new VesselHealthEnumerateRequest();
                request.PageSize = 2;
                request.PageNumber = 3;
                EnumerationResult<VesselHealth> page = await testDb.Driver.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, request).ConfigureAwait(false);
                AssertEqual(5L, page.TotalRecords);
                AssertEqual(3, page.TotalPages);
                AssertEqual(3, page.PageNumber);
                AssertEqual(2, page.PageSize);
                AssertEqual(1, page.Objects.Count);
                AssertEqual("gamma", page.Objects[0].VesselName, "default sort is vessel name ascending");
            }));

            cases.Add(CaseAsync("every_sort_column_executes", "Every whitelisted sort column executes in both directions", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedAsync(testDb.Driver).ConfigureAwait(false);

                foreach (VesselHealthSortEnum sort in Enum.GetValues(typeof(VesselHealthSortEnum)).Cast<VesselHealthSortEnum>())
                {
                    foreach (bool descending in new bool[] { false, true })
                    {
                        VesselHealthEnumerateRequest request = new VesselHealthEnumerateRequest();
                        request.SortBy = sort;
                        request.SortDescending = descending;
                        EnumerationResult<VesselHealth> page = await testDb.Driver.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, request).ConfigureAwait(false);
                        AssertEqual(5, page.Objects.Count, sort + (descending ? " desc" : " asc"));
                    }
                }
            }));

            cases.Add(CaseAsync("sort_orders_are_correct", "Status sorts rank Fail, Warn, Pass, Unknown; null measurements sort last", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedAsync(testDb.Driver).ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AssertNames(await SortedAsync(db, VesselHealthSortEnum.OverallStatus, true).ConfigureAwait(false), "alpha_one", "beta", "gamma", "delta", "epsilon");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.OverallStatus, false).ConfigureAwait(false), "delta", "epsilon", "gamma", "beta", "alpha_one");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.BranchCount, false).ConfigureAwait(false), "gamma", "delta", "beta", "alpha_one", "epsilon");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.BranchCount, true).ConfigureAwait(false), "alpha_one", "beta", "delta", "gamma", "epsilon");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.FleetName, false).ConfigureAwait(false), "alpha_one", "delta", "beta", "epsilon", "gamma");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.Divergence, true).ConfigureAwait(false), "beta", "alpha_one", "gamma", "delta", "epsilon");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.LastCommitUtc, true).ConfigureAwait(false), "gamma", "alpha_one", "beta", "delta", "epsilon");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.TestInfraStatus, true).ConfigureAwait(false), "beta", "gamma", "alpha_one", "delta", "epsilon");
                AssertNames(await SortedAsync(db, VesselHealthSortEnum.VesselName, true).ConfigureAwait(false), "gamma", "epsilon", "delta", "beta", "alpha_one");
            }));

            cases.Add(CaseAsync("every_filter_works", "Every filter narrows the result set correctly", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselHealthTestFixture f = await SeedAsync(db).ConfigureAwait(false);

                await AssertFilterAsync(db, r => r.NameContains = "ALPHA", "alpha_one").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.NameContains = "_", "alpha_one").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.NameContains = "%", new string[0]).ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.FleetId = f.FleetAlpha.Id, "alpha_one", "delta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.PrimaryLanguage = "csharp", "alpha_one", "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.CurrentBranchContains = "FEATURE", "beta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.OverallStatus = new List<VesselHealthStatusEnum> { VesselHealthStatusEnum.Fail }, "alpha_one").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.OverallStatus = new List<VesselHealthStatusEnum> { VesselHealthStatusEnum.Unknown }, "delta", "epsilon").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.OverallStatus = new List<VesselHealthStatusEnum> { VesselHealthStatusEnum.Pass, VesselHealthStatusEnum.Warn }, "beta", "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.DependencyStatus = new List<VesselHealthStatusEnum> { VesselHealthStatusEnum.Fail }, "alpha_one").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.TestInfraStatus = new List<VesselHealthStatusEnum> { VesselHealthStatusEnum.Fail }, "beta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.IsDirty = true, "alpha_one").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.IsDirty = false, "beta", "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.HasCiConfig = true, "alpha_one", "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.Divergence = VesselDivergenceFilterEnum.Ahead, "alpha_one").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.Divergence = VesselDivergenceFilterEnum.Behind, "beta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.Divergence = VesselDivergenceFilterEnum.Diverged, "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.Divergence = VesselDivergenceFilterEnum.Even, "delta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.MinBranchCount = 3, "alpha_one", "beta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.MaxBranchCount = 2, "delta", "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => { r.MinBranchCount = 2; r.MaxBranchCount = 3; }, "beta", "delta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.LastCommitAfterUtc = _Fixed.AddDays(-1), "alpha_one", "gamma").ConfigureAwait(false);
                await AssertFilterAsync(db, r => r.LastCommitBeforeUtc = _Fixed, "beta").ConfigureAwait(false);
                await AssertFilterAsync(db, r => { r.FleetId = f.FleetAlpha.Id; r.OverallStatus = new List<VesselHealthStatusEnum> { VesselHealthStatusEnum.Unknown }; }, "delta").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("summary_counts", "CountByOverallStatusAsync counts active vessels by effective status", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedAsync(testDb.Driver).ConfigureAwait(false);

                VesselHealthSummary summary = await testDb.Driver.VesselHealth.CountByOverallStatusAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(5L, summary.TotalVessels, "TotalVessels");
                AssertEqual(1L, summary.NotEvaluated, "NotEvaluated");
                AssertEqual(1L, summary.Pass, "Pass");
                AssertEqual(1L, summary.Warn, "Warn");
                AssertEqual(1L, summary.Fail, "Fail");
                AssertEqual(1L, summary.Unknown, "Unknown");
                AssertEqual(0L, summary.NotApplicable, "NotApplicable");
                AssertEqual(1L, summary.OutdatedMajorVessels, "OutdatedMajorVessels");
                AssertEqual(1L, summary.HighOrCriticalVulnerabilityVessels, "HighOrCriticalVulnerabilityVessels");

                TenantMetadata empty = new TenantMetadata("Empty " + Guid.NewGuid().ToString("N").Substring(0, 6));
                await testDb.Driver.Tenants.CreateAsync(empty).ConfigureAwait(false);
                VesselHealthSummary none = await testDb.Driver.VesselHealth.CountByOverallStatusAsync(empty.Id).ConfigureAwait(false);
                AssertEqual(0L, none.TotalVessels, "empty tenant");
                AssertEqual(0L, none.Fail, "empty tenant");
            }));

            cases.Add(CaseAsync("findings_replace_for_vessel", "ReplaceForVesselAsync replaces only that vessel's findings", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel a = await CreateVesselAsync(db, Constants.DefaultTenantId, "find-a", null, true).ConfigureAwait(false);
                Vessel b = await CreateVesselAsync(db, Constants.DefaultTenantId, "find-b", null, true).ConfigureAwait(false);

                await db.VesselHealthFindings.ReplaceForVesselAsync(Constants.DefaultTenantId, a.Id, new List<VesselHealthFinding>
                {
                    NewFinding(VesselHealthCriterionEnum.Dependencies, VesselHealthStatusEnum.Warn, "OutdatedPackages", 7, 2),
                    NewFinding(VesselHealthCriterionEnum.Branches, VesselHealthStatusEnum.Pass, null, null, null),
                    NewFinding(VesselHealthCriterionEnum.WorkingTree, VesselHealthStatusEnum.Fail, "ModifiedFiles", 3, null)
                }).ConfigureAwait(false);
                await db.VesselHealthFindings.ReplaceForVesselAsync(Constants.DefaultTenantId, b.Id, new List<VesselHealthFinding>
                {
                    NewFinding(VesselHealthCriterionEnum.Branches, VesselHealthStatusEnum.Warn, null, null, null)
                }).ConfigureAwait(false);

                List<VesselHealthFinding> first = await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(3, first.Count);
                VesselHealthFinding deps = first.First(x => x.Criterion == VesselHealthCriterionEnum.Dependencies);
                AssertStartsWith("vhf_", deps.Id);
                AssertEqual(VesselHealthStatusEnum.Warn, deps.Status);
                AssertEqual("OutdatedPackages", deps.DetailCode);
                AssertEqual(7L, deps.ValueA!.Value);
                AssertEqual(2L, deps.ValueB!.Value);
                AssertEqual(Constants.DefaultTenantId, deps.TenantId);
                AssertEqual(a.Id, deps.VesselId);

                await db.VesselHealthFindings.ReplaceForVesselAsync(Constants.DefaultTenantId, a.Id, new List<VesselHealthFinding>
                {
                    NewFinding(VesselHealthCriterionEnum.Dependencies, VesselHealthStatusEnum.Pass, null, null, null)
                }).ConfigureAwait(false);
                List<VesselHealthFinding> replaced = await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(1, replaced.Count, "replaced, not appended");
                AssertEqual(VesselHealthStatusEnum.Pass, replaced[0].Status);
                AssertEqual(1, (await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, b.Id).ConfigureAwait(false)).Count, "other vessel untouched");

                TenantMetadata other = new TenantMetadata("Find Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
                await db.Tenants.CreateAsync(other).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselHealthFindings.ReadByVesselAsync(other.Id, a.Id).ConfigureAwait(false)).Count, "cross-tenant read");

                await db.VesselHealthFindings.ReplaceForVesselAsync(Constants.DefaultTenantId, a.Id, new List<VesselHealthFinding>()).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false)).Count, "replace with empty clears");
                await db.VesselHealthFindings.DeleteByVesselAsync(Constants.DefaultTenantId, b.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, b.Id).ConfigureAwait(false)).Count, "DeleteByVessel");
            }));

            cases.Add(CaseAsync("dependencies_replace_for_vessel", "ReplaceForVesselAsync replaces a vessel's dependency rows", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel a = await CreateVesselAsync(db, Constants.DefaultTenantId, "dep-a", null, true).ConfigureAwait(false);

                VesselDependency major = NewDependency("NuGet", "Newtonsoft.Json", DependencyDriftEnum.Major);
                major.ProjectPath = "src/App/App.csproj";
                major.CurrentVersion = "12.0.1";
                major.LatestVersion = "13.0.3";
                major.IsVulnerable = true;
                major.Severity = VulnerabilitySeverityEnum.High;
                major.AdvisoryUrl = "https://example.com/advisory";
                await db.VesselDependencies.ReplaceForVesselAsync(Constants.DefaultTenantId, a.Id, new List<VesselDependency>
                {
                    NewDependency("npm", "react", DependencyDriftEnum.Minor),
                    major
                }).ConfigureAwait(false);

                List<VesselDependency> rows = await db.VesselDependencies.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(2, rows.Count);
                VesselDependency read = rows.First(x => x.PackageName == "Newtonsoft.Json");
                AssertStartsWith("vdp_", read.Id);
                AssertEqual("NuGet", read.Ecosystem);
                AssertEqual("src/App/App.csproj", read.ProjectPath);
                AssertEqual("12.0.1", read.CurrentVersion);
                AssertEqual("13.0.3", read.LatestVersion);
                AssertEqual(DependencyDriftEnum.Major, read.Drift);
                AssertTrue(read.IsVulnerable, "IsVulnerable");
                AssertEqual(VulnerabilitySeverityEnum.High, read.Severity);
                AssertEqual("https://example.com/advisory", read.AdvisoryUrl);

                await db.VesselDependencies.ReplaceForVesselAsync(Constants.DefaultTenantId, a.Id, new List<VesselDependency> { NewDependency("npm", "vite", DependencyDriftEnum.Patch) }).ConfigureAwait(false);
                List<VesselDependency> replaced = await db.VesselDependencies.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(1, replaced.Count, "replaced, not appended");
                AssertEqual("vite", replaced[0].PackageName);

                await db.VesselDependencies.DeleteByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselDependencies.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false)).Count, "DeleteByVessel");
            }));

            cases.Add(CaseAsync("override_upsert_is_unique_per_criterion", "Override UpsertAsync keeps one row per tenant, vessel, and criterion", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel a = await CreateVesselAsync(db, Constants.DefaultTenantId, "ovr-a", null, true).ConfigureAwait(false);

                VesselHealthOverride first = NewOverride(a, VesselHealthCriterionEnum.TestInfrastructure, VesselHealthStatusEnum.Pass, "tests live elsewhere");
                first.CreatedUtc = _Fixed;
                await db.VesselHealthOverrides.UpsertAsync(first).ConfigureAwait(false);
                AssertStartsWith("vho_", first.Id);

                VesselHealthOverride second = NewOverride(a, VesselHealthCriterionEnum.TestInfrastructure, VesselHealthStatusEnum.Warn, "changed my mind");
                await db.VesselHealthOverrides.UpsertAsync(second).ConfigureAwait(false);
                AssertEqual(first.Id, second.Id, "same identifier for the same criterion");

                List<VesselHealthOverride> rows = await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(1, rows.Count, "unique per criterion");
                AssertEqual(VesselHealthStatusEnum.Warn, rows[0].Status);
                AssertEqual("changed my mind", rows[0].Note);
                AssertEqual(Constants.DefaultUserId, rows[0].UserId);
                AssertSameInstant(_Fixed, rows[0].CreatedUtc, "CreatedUtc preserved");

                await db.VesselHealthOverrides.UpsertAsync(NewOverride(a, VesselHealthCriterionEnum.ContinuousIntegration, VesselHealthStatusEnum.NotApplicable, null)).ConfigureAwait(false);
                AssertEqual(2, (await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false)).Count, "second criterion");

                TenantMetadata other = new TenantMetadata("Ovr Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
                await db.Tenants.CreateAsync(other).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselHealthOverrides.ReadByVesselAsync(other.Id, a.Id).ConfigureAwait(false)).Count, "cross-tenant read");
                await db.VesselHealthOverrides.DeleteAsync(other.Id, a.Id, VesselHealthCriterionEnum.TestInfrastructure).ConfigureAwait(false);
                AssertEqual(2, (await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false)).Count, "cross-tenant delete is a no-op");

                await db.VesselHealthOverrides.DeleteAsync(Constants.DefaultTenantId, a.Id, VesselHealthCriterionEnum.TestInfrastructure).ConfigureAwait(false);
                List<VesselHealthOverride> remaining = await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(1, remaining.Count, "DeleteAsync by criterion");
                AssertEqual(VesselHealthCriterionEnum.ContinuousIntegration, remaining[0].Criterion);

                await db.VesselHealthOverrides.DeleteByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, a.Id).ConfigureAwait(false)).Count, "DeleteByVessel");
            }));

            cases.Add(CaseAsync("vessel_delete_cascades", "Deleting a vessel deletes its health row, findings, dependencies, and overrides", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel doomed = await CreateVesselAsync(db, Constants.DefaultTenantId, "cascade-doomed", null, true).ConfigureAwait(false);
                Vessel survivor = await CreateVesselAsync(db, Constants.DefaultTenantId, "cascade-survivor", null, true).ConfigureAwait(false);

                foreach (Vessel vessel in new Vessel[] { doomed, survivor })
                {
                    await db.VesselHealth.UpsertAsync(NewHealth(vessel)).ConfigureAwait(false);
                    await db.VesselHealthFindings.ReplaceForVesselAsync(Constants.DefaultTenantId, vessel.Id, new List<VesselHealthFinding> { NewFinding(VesselHealthCriterionEnum.Branches, VesselHealthStatusEnum.Pass, null, null, null) }).ConfigureAwait(false);
                    await db.VesselDependencies.ReplaceForVesselAsync(Constants.DefaultTenantId, vessel.Id, new List<VesselDependency> { NewDependency("npm", "left-pad", DependencyDriftEnum.Major) }).ConfigureAwait(false);
                    await db.VesselHealthOverrides.UpsertAsync(NewOverride(vessel, VesselHealthCriterionEnum.Branches, VesselHealthStatusEnum.Pass, null)).ConfigureAwait(false);
                }

                await db.Vessels.DeleteAsync(Constants.DefaultTenantId, doomed.Id).ConfigureAwait(false);

                AssertNull(await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, doomed.Id).ConfigureAwait(false), "health cascaded");
                AssertEqual(0, (await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, doomed.Id).ConfigureAwait(false)).Count, "findings cascaded");
                AssertEqual(0, (await db.VesselDependencies.ReadByVesselAsync(Constants.DefaultTenantId, doomed.Id).ConfigureAwait(false)).Count, "dependencies cascaded");
                AssertEqual(0, (await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, doomed.Id).ConfigureAwait(false)).Count, "overrides cascaded");

                AssertNotNull(await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, survivor.Id).ConfigureAwait(false), "survivor health kept");
                AssertEqual(1, (await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, survivor.Id).ConfigureAwait(false)).Count, "survivor findings kept");
                AssertEqual(1, (await db.VesselDependencies.ReadByVesselAsync(Constants.DefaultTenantId, survivor.Id).ConfigureAwait(false)).Count, "survivor dependencies kept");
                AssertEqual(1, (await db.VesselHealthOverrides.ReadByVesselAsync(Constants.DefaultTenantId, survivor.Id).ConfigureAwait(false)).Count, "survivor overrides kept");
            }));

            cases.Add(CaseAsync("health_delete_by_vessel", "DeleteByVesselAsync removes the health row so the vessel reads as unevaluated", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await CreateVesselAsync(db, Constants.DefaultTenantId, "reset-me", null, true).ConfigureAwait(false);
                await db.VesselHealth.UpsertAsync(NewHealth(vessel)).ConfigureAwait(false);
                await db.VesselHealth.DeleteByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);

                AssertNull(await db.VesselHealth.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false), "row deleted");
                EnumerationResult<VesselHealth> page = await db.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, new VesselHealthEnumerateRequest()).ConfigureAwait(false);
                AssertEqual(1L, page.TotalRecords);
                AssertNull(page.Objects[0].Id, "vessel now unevaluated");
            }));

            cases.Add(CaseAsync("request_and_settings_clamp", "Enumerate request and health settings clamp to their documented ranges", TestTags.Positive, () =>
            {
                VesselHealthEnumerateRequest request = new VesselHealthEnumerateRequest();
                AssertEqual(25, request.PageSize, "default page size");
                request.PageSize = 0;
                AssertEqual(1, request.PageSize);
                request.PageSize = 10000;
                AssertEqual(500, request.PageSize);
                request.PageNumber = -2;
                AssertEqual(1, request.PageNumber);
                request.OverallStatus = null!;
                AssertNotNull(request.OverallStatus, "null-safe list");
                request.MinBranchCount = -1;
                AssertEqual(0, request.MinBranchCount!.Value);

                Armada.Core.Settings.ArmadaSettings settings = new Armada.Core.Settings.ArmadaSettings();
                AssertEqual(6, settings.Import.MaxDepth);
                settings.Import.MaxDepth = 99;
                AssertEqual(16, settings.Import.MaxDepth);
                AssertTrue(settings.Import.ExcludedDirectoryNames.Contains("node_modules"), "default excludes");
                AssertEqual(25, settings.Import.InlineBatchLimit);
                AssertEqual(8, settings.FleetActions.MaxConcurrency);
                settings.FleetActions.MaxOutputBytes = 1;
                AssertEqual(1024, settings.FleetActions.MaxOutputBytes);
                AssertEqual(360, settings.RepositoryHealth.IntervalMinutes);
                settings.RepositoryHealth.IntervalMinutes = -5;
                AssertEqual(0, settings.RepositoryHealth.IntervalMinutes);
                AssertFalse(settings.RepositoryHealth.ScoredCriteria.Contains(VesselHealthCriterionEnum.ContinuousIntegration), "CI not scored by default");
                AssertFalse(settings.RepositoryHealth.ScoredCriteria.Contains(VesselHealthCriterionEnum.CommitRecency), "recency not scored by default");
                settings.RepositoryHealth.ScoredCriteria = null!;
                AssertNotNull(settings.RepositoryHealth.ScoredCriteria, "null-safe criteria");
                AssertEqual(21, settings.RepositoryHealth.Thresholds.BehindFail);
                settings.RepositoryHealth.Thresholds = null!;
                AssertEqual(11, settings.RepositoryHealth.Thresholds.StaleBranchFail);
                settings.Import = null!;
                AssertNotNull(settings.Import, "null-safe nested settings");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<VesselHealthTestFixture> SeedAsync(DatabaseDriver db)
        {
            VesselHealthTestFixture f = new VesselHealthTestFixture();
            f.FleetAlpha = await CreateFleetAsync(db, Constants.DefaultTenantId, "Alpha Fleet").ConfigureAwait(false);
            f.FleetBeta = await CreateFleetAsync(db, Constants.DefaultTenantId, "Beta Fleet").ConfigureAwait(false);

            f.Alpha = await CreateVesselAsync(db, Constants.DefaultTenantId, "alpha_one", f.FleetAlpha.Id, true).ConfigureAwait(false);
            VesselHealth alpha = NewHealth(f.Alpha);
            alpha.OverallStatus = VesselHealthStatusEnum.Fail;
            alpha.AheadOfDefault = 2;
            alpha.BehindDefault = 0;
            alpha.IsDirty = true;
            alpha.BranchCount = 10;
            alpha.StaleBranchCount = 5;
            alpha.OutdatedCount = 7;
            alpha.OutdatedMajorCount = 2;
            alpha.VulnerableCount = 1;
            alpha.MaxVulnerabilitySeverity = VulnerabilitySeverityEnum.High;
            alpha.DependencyStatus = VesselHealthStatusEnum.Fail;
            alpha.TestInfraStatus = VesselHealthStatusEnum.Pass;
            alpha.CiStatus = VesselHealthStatusEnum.Pass;
            alpha.HasCiConfig = true;
            alpha.PrimaryLanguage = "CSharp";
            alpha.CurrentBranch = "main";
            alpha.LastCommitUtc = _Fixed;
            alpha.EvaluatedUtc = _Fixed.AddHours(1);
            await db.VesselHealth.UpsertAsync(alpha).ConfigureAwait(false);

            f.Beta = await CreateVesselAsync(db, Constants.DefaultTenantId, "beta", f.FleetBeta.Id, true).ConfigureAwait(false);
            VesselHealth beta = NewHealth(f.Beta);
            beta.OverallStatus = VesselHealthStatusEnum.Warn;
            beta.AheadOfDefault = 0;
            beta.BehindDefault = 5;
            beta.IsDirty = false;
            beta.BranchCount = 3;
            beta.StaleBranchCount = 0;
            beta.OutdatedCount = 2;
            beta.OutdatedMajorCount = 0;
            beta.VulnerableCount = 0;
            beta.DependencyStatus = VesselHealthStatusEnum.Warn;
            beta.TestInfraStatus = VesselHealthStatusEnum.Fail;
            beta.CiStatus = VesselHealthStatusEnum.Fail;
            beta.HasCiConfig = false;
            beta.PrimaryLanguage = "TypeScript";
            beta.CurrentBranch = "feature/x";
            beta.LastCommitUtc = _Fixed.AddDays(-30);
            beta.EvaluatedUtc = _Fixed;
            await db.VesselHealth.UpsertAsync(beta).ConfigureAwait(false);

            f.Gamma = await CreateVesselAsync(db, Constants.DefaultTenantId, "gamma", null, true).ConfigureAwait(false);
            VesselHealth gamma = NewHealth(f.Gamma);
            gamma.OverallStatus = VesselHealthStatusEnum.Pass;
            gamma.AheadOfDefault = 1;
            gamma.BehindDefault = 1;
            gamma.IsDirty = false;
            gamma.BranchCount = 1;
            gamma.DependencyStatus = VesselHealthStatusEnum.Pass;
            gamma.TestInfraStatus = VesselHealthStatusEnum.Warn;
            gamma.CiStatus = VesselHealthStatusEnum.NotApplicable;
            gamma.HasCiConfig = true;
            gamma.PrimaryLanguage = "CSharp";
            gamma.CurrentBranch = "main";
            gamma.LastCommitUtc = _Fixed.AddDays(1);
            gamma.EvaluatedUtc = _Fixed;
            await db.VesselHealth.UpsertAsync(gamma).ConfigureAwait(false);

            f.Delta = await CreateVesselAsync(db, Constants.DefaultTenantId, "delta", f.FleetAlpha.Id, true).ConfigureAwait(false);
            VesselHealth delta = NewHealth(f.Delta);
            delta.OverallStatus = VesselHealthStatusEnum.Unknown;
            delta.AheadOfDefault = 0;
            delta.BehindDefault = 0;
            delta.BranchCount = 2;
            await db.VesselHealth.UpsertAsync(delta).ConfigureAwait(false);

            f.Epsilon = await CreateVesselAsync(db, Constants.DefaultTenantId, "epsilon", f.FleetBeta.Id, true).ConfigureAwait(false);

            Vessel zeta = await CreateVesselAsync(db, Constants.DefaultTenantId, "zeta", null, false).ConfigureAwait(false);
            VesselHealth zetaHealth = NewHealth(zeta);
            zetaHealth.OverallStatus = VesselHealthStatusEnum.Fail;
            await db.VesselHealth.UpsertAsync(zetaHealth).ConfigureAwait(false);

            TenantMetadata other = new TenantMetadata("Health Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(other).ConfigureAwait(false);
            f.OtherTenantId = other.Id;
            f.OtherTenantVessel = await CreateVesselAsync(db, other.Id, "alpha_other", null, true).ConfigureAwait(false);
            VesselHealth otherHealth = NewHealth(f.OtherTenantVessel);
            otherHealth.OverallStatus = VesselHealthStatusEnum.Fail;
            await db.VesselHealth.UpsertAsync(otherHealth).ConfigureAwait(false);
            return f;
        }

        private static async Task<Fleet> CreateFleetAsync(DatabaseDriver db, string tenantId, string name)
        {
            Fleet fleet = new Fleet(name + " " + Guid.NewGuid().ToString("N").Substring(0, 4));
            fleet.Name = name;
            fleet.TenantId = tenantId;
            return await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
        }

        private static async Task<Vessel> CreateVesselAsync(DatabaseDriver db, string tenantId, string name, string? fleetId, bool active)
        {
            Vessel vessel = new Vessel(name, "https://example.com/" + name + ".git");
            vessel.TenantId = tenantId;
            vessel.FleetId = fleetId;
            vessel.Active = active;
            return await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static VesselHealth NewHealth(Vessel vessel)
        {
            VesselHealth health = new VesselHealth();
            health.TenantId = vessel.TenantId;
            health.VesselId = vessel.Id;
            return health;
        }

        private static VesselHealthFinding NewFinding(VesselHealthCriterionEnum criterion, VesselHealthStatusEnum status, string? detailCode, long? valueA, long? valueB)
        {
            VesselHealthFinding finding = new VesselHealthFinding();
            finding.Criterion = criterion;
            finding.Status = status;
            finding.DetailCode = detailCode;
            finding.ValueA = valueA;
            finding.ValueB = valueB;
            finding.EvaluatedUtc = _Fixed;
            return finding;
        }

        private static VesselDependency NewDependency(string ecosystem, string packageName, DependencyDriftEnum drift)
        {
            VesselDependency dependency = new VesselDependency();
            dependency.Ecosystem = ecosystem;
            dependency.PackageName = packageName;
            dependency.Drift = drift;
            return dependency;
        }

        private static VesselHealthOverride NewOverride(Vessel vessel, VesselHealthCriterionEnum criterion, VesselHealthStatusEnum status, string? note)
        {
            VesselHealthOverride healthOverride = new VesselHealthOverride();
            healthOverride.TenantId = vessel.TenantId;
            healthOverride.VesselId = vessel.Id;
            healthOverride.UserId = Constants.DefaultUserId;
            healthOverride.Criterion = criterion;
            healthOverride.Status = status;
            healthOverride.Note = note;
            return healthOverride;
        }

        private static VesselHealthEnumerateRequest Request(Action<VesselHealthEnumerateRequest> configure)
        {
            VesselHealthEnumerateRequest request = new VesselHealthEnumerateRequest();
            request.PageSize = 100;
            configure(request);
            return request;
        }

        private static async Task<List<VesselHealth>> SortedAsync(DatabaseDriver db, VesselHealthSortEnum sort, bool descending)
        {
            VesselHealthEnumerateRequest request = Request(r =>
            {
                r.SortBy = sort;
                r.SortDescending = descending;
            });
            EnumerationResult<VesselHealth> page = await db.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, request).ConfigureAwait(false);
            return page.Objects;
        }

        private static async Task AssertFilterAsync(DatabaseDriver db, Action<VesselHealthEnumerateRequest> configure, params string[] expectedNames)
        {
            VesselHealthEnumerateRequest request = Request(configure);
            EnumerationResult<VesselHealth> page = await db.VesselHealth.EnumerateAsync(Constants.DefaultTenantId, request).ConfigureAwait(false);
            List<string> actual = page.Objects.Select(h => h.VesselName ?? String.Empty).OrderBy(n => n, StringComparer.Ordinal).ToList();
            List<string> expected = expectedNames.OrderBy(n => n, StringComparer.Ordinal).ToList();
            AssertEqual(String.Join(",", expected), String.Join(",", actual), "filter result");
            AssertEqual((long)expected.Count, page.TotalRecords, "filter count");
        }

        private static void AssertNames(List<VesselHealth> rows, params string[] expected)
        {
            AssertEqual(String.Join(",", expected), String.Join(",", rows.Select(h => h.VesselName)), "sort order");
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
    }
}
