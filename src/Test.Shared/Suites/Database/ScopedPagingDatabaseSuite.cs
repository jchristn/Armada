namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Data-source paging for the Harbor, model endpoint, persona, skill, CLI permission rule, and Harbor job reads
    /// (docs/ENUMERATION.md): the page is counted and cut in the provider with the caller's visibility applied before the
    /// COUNT, pages traverse without gaps or overlap under equal timestamps (id tie-break), a page past the end is empty with
    /// accurate totals, and the created-time filters and order apply. Runs against every provider.
    /// </summary>
    public sealed class ScopedPagingDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.ScopedPaging";
        private static readonly DateTime _Fixed = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("harbors_page_in_the_database", "Harbor pages traverse every tenant row once under equal timestamps, end with an empty page, and honor the tenant filter, order, and created-time filters", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string tother = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_other")).ConfigureAwait(false)).Id;
                string tpa = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_pa")).ConfigureAwait(false)).Id;
                List<string> ids = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    Harbor harbor = new Harbor { TenantId = tpa, UserId = "usr_pa", Name = "harbor-" + i, CreatedUtc = _Fixed, LastUpdateUtc = _Fixed };
                    harbor.Capabilities.Add(new HarborCapability { Name = "git", Available = true });
                    await db.Harbors.CreateAsync(harbor).ConfigureAwait(false);
                    ids.Add(harbor.Id);
                }

                Harbor late = new Harbor { TenantId = tpa, Name = "late", CreatedUtc = _Fixed.AddHours(1), LastUpdateUtc = _Fixed };
                await db.Harbors.CreateAsync(late).ConfigureAwait(false);
                await db.Harbors.CreateAsync(new Harbor { TenantId = tother, Name = "elsewhere", CreatedUtc = _Fixed, LastUpdateUtc = _Fixed }).ConfigureAwait(false);

                ScopedEnumerationFilter tenant = ScopedEnumerationFilter.ForTenant(tpa);
                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    EnumerationResult<Harbor> result = await db.Harbors.EnumeratePageAsync(new EnumerationQuery { PageNumber = page, PageSize = 2 }, tenant).ConfigureAwait(false);
                    AssertEqual(6L, result.TotalRecords, "tenant total on page " + page);
                    AssertEqual(3, result.TotalPages, "pages");
                    AssertTrue(result.Objects.Count <= 2, "page bounded");
                    foreach (Harbor h in result.Objects)
                    {
                        AssertEqual(tpa, h.TenantId, "tenant scoped");
                        seen.Add(h.Id);
                    }
                }

                AssertEqual(6, seen.Count, "every row read");
                AssertEqual(6, seen.Distinct().Count(), "no row read twice");
                AssertEqual(late.Id, seen[0], "newest first");
                List<string> tied = seen.Skip(1).ToList();
                AssertTrue(tied.SequenceEqual(tied.OrderByDescending(x => x, StringComparer.Ordinal)), "equal timestamps break by id, descending");

                EnumerationResult<Harbor> past = await db.Harbors.EnumeratePageAsync(new EnumerationQuery { PageNumber = 9, PageSize = 2 }, tenant).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "past the end is empty");
                AssertEqual(6L, past.TotalRecords, "past the end keeps the total");

                EnumerationResult<Harbor> ascending = await db.Harbors.EnumeratePageAsync(new EnumerationQuery { PageSize = 10, Order = EnumerationOrderEnum.CreatedAscending }, tenant).ConfigureAwait(false);
                AssertEqual(late.Id, ascending.Objects.Last().Id, "oldest first puts the newest last");
                AssertEqual(1, ascending.Objects.First().Capabilities.Count, "capabilities load with the page");

                EnumerationResult<Harbor> after = await db.Harbors.EnumeratePageAsync(new EnumerationQuery { PageSize = 10, CreatedAfter = _Fixed.AddMinutes(30) }, tenant).ConfigureAwait(false);
                AssertEqual(1L, after.TotalRecords, "createdAfter filters before counting");

                EnumerationResult<Harbor> all = await db.Harbors.EnumeratePageAsync(new EnumerationQuery { PageSize = 1000 }, new ScopedEnumerationFilter()).ConfigureAwait(false);
                AssertTrue(all.TotalRecords >= 7, "an unrestricted filter sees every tenant");
            }));

            cases.Add(CaseAsync("model_endpoints_page_with_viewer_scope", "A regular user's endpoint page counts tenant-wide endpoints plus their own only; a tenant admin sees the tenant", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string tme = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_me")).ConfigureAwait(false)).Id;
                string tx = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_x")).ConfigureAwait(false)).Id;
                await db.ModelEndpoints.CreateAsync(Endpoint(tme, "usr_a", ScopeEnum.TenantWide, "shared")).ConfigureAwait(false);
                await db.ModelEndpoints.CreateAsync(Endpoint(tme, "usr_a", ScopeEnum.UserSpecific, "mine")).ConfigureAwait(false);
                await db.ModelEndpoints.CreateAsync(Endpoint(tme, "usr_b", ScopeEnum.UserSpecific, "theirs-1")).ConfigureAwait(false);
                await db.ModelEndpoints.CreateAsync(Endpoint(tme, "usr_b", ScopeEnum.UserSpecific, "theirs-2")).ConfigureAwait(false);
                await db.ModelEndpoints.CreateAsync(Endpoint(tx, "usr_a", ScopeEnum.TenantWide, "other tenant")).ConfigureAwait(false);

                AuthContext regular = new AuthContext { IsAuthenticated = true, TenantId = tme, UserId = "usr_a" };
                EnumerationResult<ModelEndpoint> first = await db.ModelEndpoints.EnumeratePageAsync(new EnumerationQuery { PageSize = 1 }, ScopedEnumerationFilter.ForScopedEntity(regular)).ConfigureAwait(false);
                AssertEqual(2L, first.TotalRecords, "tenant-wide plus own, counted before paging");
                AssertEqual(2, first.TotalPages, "pages over the visible rows only");
                EnumerationResult<ModelEndpoint> second = await db.ModelEndpoints.EnumeratePageAsync(new EnumerationQuery { PageNumber = 2, PageSize = 1 }, ScopedEnumerationFilter.ForScopedEntity(regular)).ConfigureAwait(false);
                AssertEqual(1, second.Objects.Count, "second page is full");
                AssertTrue(new[] { first.Objects[0].Name, second.Objects[0].Name }.OrderBy(n => n).SequenceEqual(new[] { "mine", "shared" }), "the visible rows");

                AuthContext tenantAdmin = new AuthContext { IsAuthenticated = true, TenantId = tme, UserId = "usr_c", IsTenantAdmin = true };
                EnumerationResult<ModelEndpoint> tenantPage = await db.ModelEndpoints.EnumeratePageAsync(new EnumerationQuery { PageSize = 100 }, ScopedEnumerationFilter.ForScopedEntity(tenantAdmin)).ConfigureAwait(false);
                AssertEqual(4L, tenantPage.TotalRecords, "a tenant admin counts the tenant");
            }));

            cases.Add(CaseAsync("personas_page_with_viewer_scope", "Persona pages apply the viewer scope before counting and carry their captain priorities", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string tenantId = (await db.Tenants.CreateAsync(new TenantMetadata("paging-personas")).ConfigureAwait(false)).Id;
                Persona shared = PersonaFor(tenantId, "usr_a", ScopeEnum.TenantWide, "pp-shared");
                shared.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                await db.Personas.CreateAsync(shared).ConfigureAwait(false);
                await db.Personas.CreateAsync(PersonaFor(tenantId, "usr_a", ScopeEnum.UserSpecific, "pp-mine")).ConfigureAwait(false);
                for (int i = 0; i < 3; i++)
                    await db.Personas.CreateAsync(PersonaFor(tenantId, "usr_b", ScopeEnum.UserSpecific, "pp-theirs-" + i)).ConfigureAwait(false);

                AuthContext regular = new AuthContext { IsAuthenticated = true, TenantId = tenantId, UserId = "usr_a" };
                EnumerationResult<Persona> page = await db.Personas.EnumeratePageAsync(new EnumerationQuery { PageSize = 10 }, ScopedEnumerationFilter.ForScopedEntity(regular)).ConfigureAwait(false);
                AssertEqual(2L, page.TotalRecords, "invisible personas are not counted");
                AssertEqual(2, page.Objects.Count, "and not returned");
                Persona? loaded = page.Objects.FirstOrDefault(p => p.Name == "pp-shared");
                AssertNotNull(loaded, "shared persona visible");
                AssertEqual(1, loaded!.CaptainPriorities.Count, "priorities load with the page");

                AuthContext admin = new AuthContext { IsAuthenticated = true, IsAdmin = true };
                EnumerationResult<Persona> adminPage = await db.Personas.EnumeratePageAsync(new EnumerationQuery { PageSize = 1000 }, ScopedEnumerationFilter.ForScopedEntity(admin)).ConfigureAwait(false);
                AssertTrue(adminPage.TotalRecords >= 5, "an admin counts every persona");
            }));

            cases.Add(CaseAsync("skills_restrict_to_viewer", "A skill query restricted to a viewer counts tenant-wide skills plus the viewer's own, ordered by name", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string tsk = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_sk")).ConfigureAwait(false)).Id;
                await db.Skills.CreateAsync(SkillFor(tsk, "usr_a", ScopeEnum.TenantWide, "b-shared")).ConfigureAwait(false);
                await db.Skills.CreateAsync(SkillFor(tsk, "usr_a", ScopeEnum.UserSpecific, "a-mine")).ConfigureAwait(false);
                await db.Skills.CreateAsync(SkillFor(tsk, "usr_b", ScopeEnum.UserSpecific, "c-theirs")).ConfigureAwait(false);

                EnumerationResult<Skill> page = await db.Skills.EnumerateAsync(new SkillQuery { TenantId = tsk, RestrictToViewer = true, ViewerUserId = "usr_a", PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(2L, page.TotalRecords, "viewer total");
                AssertEqual("a-mine", page.Objects.Single().Name, "ordered by name");
                EnumerationResult<Skill> tenant = await db.Skills.EnumerateAsync(new SkillQuery { TenantId = tsk, PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(3L, tenant.TotalRecords, "without the viewer restriction the tenant counts");
            }));

            cases.Add(CaseAsync("cli_rules_page_in_the_database", "CLI permission rule pages count the tenant plus every-tenant rules, clamp the page size, and traverse newest first", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string tr = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_r")).ConfigureAwait(false)).Id;
                string ts = (await db.Tenants.CreateAsync(new TenantMetadata("paging-ten_s")).ConfigureAwait(false)).Id;
                for (int i = 0; i < 4; i++)
                    await db.CliPermissionRules.CreateAsync(new CliPermissionRule { TenantId = tr, Pattern = "Bash(git status:" + i + ")", CreatedUtc = _Fixed.AddMinutes(i) }).ConfigureAwait(false);
                await db.CliPermissionRules.CreateAsync(new CliPermissionRule { TenantId = null, Pattern = "Bash(ls:*)", CreatedUtc = _Fixed.AddMinutes(10) }).ConfigureAwait(false);
                await db.CliPermissionRules.CreateAsync(new CliPermissionRule { TenantId = ts, Pattern = "Bash(pwd:*)", CreatedUtc = _Fixed }).ConfigureAwait(false);

                CliPermissionRuleQuery query = new CliPermissionRuleQuery { TenantId = tr };
                EnumerationResult<CliPermissionRule> first = await db.CliPermissionRules.EnumeratePageAsync(query, 1, 2).ConfigureAwait(false);
                AssertEqual(5L, first.TotalRecords, "tenant plus every-tenant");
                AssertEqual(3, first.TotalPages, "pages");
                AssertEqual("Bash(ls:*)", first.Objects[0].Pattern, "newest first");
                EnumerationResult<CliPermissionRule> last = await db.CliPermissionRules.EnumeratePageAsync(query, 3, 2).ConfigureAwait(false);
                AssertEqual(1, last.Objects.Count, "final page is partial");
                EnumerationResult<CliPermissionRule> clamped = await db.CliPermissionRules.EnumeratePageAsync(query, 0, 5000).ConfigureAwait(false);
                AssertEqual(1, clamped.PageNumber, "page number normalized");
                AssertEqual(1000, clamped.PageSize, "page size clamped");
                AssertEqual(5, clamped.Objects.Count, "everything on one clamped page");
            }));

            cases.Add(CaseAsync("harbor_jobs_page_in_the_database", "Harbor job pages count the window or the open jobs in the database, newest launch first, with id tie-breaks", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                for (int i = 0; i < 5; i++)
                {
                    HarborJobRecord job = new HarborJobRecord { JobId = "job_" + i, HarborId = "hbr_pj", Runtime = "Codex", LaunchedUtc = _Fixed.AddMinutes(i) };
                    if (i < 3)
                    {
                        job.EndedUtc = _Fixed.AddMinutes(i + 1);
                        job.Outcome = HarborJobOutcomeEnum.Succeeded;
                    }

                    await db.HarborJobs.CreateAsync(job).ConfigureAwait(false);
                }

                await db.HarborJobs.CreateAsync(new HarborJobRecord { JobId = "job_other", HarborId = "hbr_other", LaunchedUtc = _Fixed }).ConfigureAwait(false);

                DateTime far = new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                EnumerationResult<HarborJobRecord> first = await db.HarborJobs.EnumeratePageAsync("hbr_pj", false, _Fixed.AddDays(-1), far, new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(5L, first.TotalRecords, "window total");
                AssertEqual("job_4", first.Objects[0].JobId, "newest launch first");
                EnumerationResult<HarborJobRecord> open = await db.HarborJobs.EnumeratePageAsync("hbr_pj", true, DateTime.MinValue, far, new EnumerationQuery { PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(2L, open.TotalRecords, "open jobs only");
                EnumerationResult<HarborJobRecord> window = await db.HarborJobs.EnumeratePageAsync("hbr_pj", false, _Fixed.AddMinutes(3).AddSeconds(30), far, new EnumerationQuery { PageSize = 10, Order = EnumerationOrderEnum.CreatedAscending }).ConfigureAwait(false);
                AssertEqual(2L, window.TotalRecords, "ended-before-window jobs are excluded");
                AssertEqual("job_3", window.Objects[0].JobId, "oldest first on request");
                EnumerationResult<HarborJobRecord> past = await db.HarborJobs.EnumeratePageAsync("hbr_pj", false, _Fixed.AddDays(-1), far, new EnumerationQuery { PageNumber = 4, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "past the end is empty");
                AssertEqual(5L, past.TotalRecords, "with the total");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Data-source paging for Harbors, endpoints, personas, skills, CLI rules, and Harbor jobs",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint Endpoint(string tenantId, string userId, ScopeEnum scope, string name)
        {
            return new ModelEndpoint
            {
                TenantId = tenantId,
                UserId = userId,
                Scope = scope,
                Name = name,
                BaseUrl = "http://localhost:11434",
                Model = "m",
                CreatedUtc = _Fixed,
                LastUpdateUtc = _Fixed
            };
        }

        private static Persona PersonaFor(string tenantId, string userId, ScopeEnum scope, string name)
        {
            return new Persona
            {
                TenantId = tenantId,
                UserId = userId,
                Scope = scope,
                Name = name,
                PromptTemplateName = "persona.worker",
                CreatedUtc = _Fixed,
                LastUpdateUtc = _Fixed
            };
        }

        private static Skill SkillFor(string tenantId, string userId, ScopeEnum scope, string name)
        {
            return new Skill
            {
                TenantId = tenantId,
                UserId = userId,
                Scope = scope,
                Name = name,
                Content = "content",
                CreatedUtc = _Fixed,
                LastUpdateUtc = _Fixed
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
