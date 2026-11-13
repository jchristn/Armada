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
    /// The configuration catalogs (pipelines, playbooks, prompt templates, workflow profiles, project profiles, skills)
    /// apply the caller's visibility in the data source before counting (docs/ENUMERATION.md). Before the fix the REST
    /// routes and the MCP enumerate tool read one page and then dropped the rows a regular user may not see, so pages
    /// came back short, TotalRecords counted invisible rows, and a client paging to the end (listAll) stopped early.
    /// Every case seeds one tenant with tenant-wide rows, rows owned by the viewer, rows owned by another user, and a row
    /// in another tenant, all with the same creation time so the id tie-break decides the order, then pages through
    /// with a small page size and checks exact totals, no overlap, no gaps, a final short page and an empty page past
    /// the end. Runs against every database provider.
    /// </summary>
    public sealed class ConfigVisibilityPagingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.ConfigVisibilityPaging";

        private static readonly DateTime _SameCreatedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("pipelines_visible_paging", "Pipelines: a regular user's pages hold exactly the tenant-wide and own pipelines, counted in the data source", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    VisibleSet expected = new VisibleSet();
                    int n = 0;
                    foreach (Seed seed in world.Seeds())
                    {
                        Pipeline pipeline = new Pipeline("cvp-" + world.Suffix + "-" + (n++));
                        pipeline.TenantId = seed.TenantId;
                        pipeline.UserId = seed.UserId;
                        pipeline.Scope = seed.Scope;
                        pipeline.CreatedUtc = _SameCreatedUtc;
                        pipeline.Stages = new List<PipelineStage> { new PipelineStage(1, "Worker") };
                        pipeline = await db.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
                        expected.Add(seed, pipeline.Id);
                    }

                    await ExpectPagesAsync("pipelines viewer", expected.ForViewer, (p, s) => db.Pipelines.EnumerateVisibleAsync(world.TenantId, world.ViewerId, new EnumerationQuery { PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    await ExpectPagesAsync("pipelines tenant admin", expected.ForTenant, (p, s) => db.Pipelines.EnumerateVisibleAsync(world.TenantId, null, new EnumerationQuery { PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    EnumerationResult<Pipeline> page = await db.Pipelines.EnumerateVisibleAsync(world.TenantId, world.ViewerId, new EnumerationQuery { PageSize = 1 }).ConfigureAwait(false);
                    AssertEqual(1, page.Objects.Single().Stages.Count, "a paged pipeline carries its stages");
                }
            }));

            cases.Add(CaseAsync("playbooks_visible_paging", "Playbooks: a regular user's pages hold exactly the tenant-wide and own playbooks", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    VisibleSet expected = new VisibleSet();
                    int n = 0;
                    foreach (Seed seed in world.Seeds())
                    {
                        Playbook playbook = new Playbook("cvp-" + world.Suffix + "-" + (n++) + ".md", "# playbook");
                        playbook.TenantId = seed.TenantId;
                        playbook.UserId = seed.UserId;
                        playbook.Scope = seed.Scope;
                        playbook.CreatedUtc = _SameCreatedUtc;
                        playbook = await db.Playbooks.CreateAsync(playbook).ConfigureAwait(false);
                        expected.Add(seed, playbook.Id);
                    }

                    await ExpectPagesAsync("playbooks viewer", expected.ForViewer, (p, s) => db.Playbooks.EnumerateVisibleAsync(world.TenantId, world.ViewerId, new EnumerationQuery { PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    await ExpectPagesAsync("playbooks tenant admin", expected.ForTenant, (p, s) => db.Playbooks.EnumerateVisibleAsync(world.TenantId, null, new EnumerationQuery { PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("prompt_templates_visible_paging", "Prompt templates: visibility, category filter and the category-and-name order are applied in the data source", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    VisibleSet expected = new VisibleSet();
                    string category = "cvp-" + world.Suffix;
                    int n = 0;
                    foreach (Seed seed in world.Seeds())
                    {
                        PromptTemplate template = new PromptTemplate("cvp." + world.Suffix + "." + (n++), "content");
                        template.TenantId = seed.TenantId;
                        template.UserId = seed.UserId;
                        template.Scope = seed.Scope;
                        template.Category = category;
                        template.CreatedUtc = _SameCreatedUtc;
                        template = await db.PromptTemplates.CreateAsync(template).ConfigureAwait(false);
                        expected.Add(seed, template.Id);
                    }

                    await ExpectPagesAsync("templates viewer", expected.ForViewer, (p, s) => db.PromptTemplates.EnumerateVisibleAsync(new PromptTemplateQuery { TenantId = world.TenantId, ViewerUserId = world.ViewerId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    await ExpectPagesAsync("templates tenant admin", expected.ForTenant, (p, s) => db.PromptTemplates.EnumerateVisibleAsync(new PromptTemplateQuery { TenantId = world.TenantId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);

                    // Category filter plus untenanted rows (the MCP list_prompt_templates view) with name order.
                    PromptTemplate shared = new PromptTemplate("cvp." + world.Suffix + ".shared", "content");
                    shared.TenantId = null;
                    shared.Category = category.ToUpperInvariant();
                    shared = await db.PromptTemplates.CreateAsync(shared).ConfigureAwait(false);
                    List<string> withShared = expected.ForViewer.Concat(new[] { shared.Id }).ToList();
                    await ExpectPagesAsync("templates with untenanted", withShared, (p, s) => db.PromptTemplates.EnumerateVisibleAsync(new PromptTemplateQuery
                    {
                        TenantId = world.TenantId,
                        ViewerUserId = world.ViewerId,
                        IncludeUntenanted = true,
                        Category = category,
                        OrderByCategoryAndName = true,
                        PageNumber = p,
                        PageSize = s
                    }), x => x.Id).ConfigureAwait(false);

                    EnumerationResult<PromptTemplate> ordered = await db.PromptTemplates.EnumerateVisibleAsync(new PromptTemplateQuery
                    {
                        TenantId = world.TenantId,
                        ViewerUserId = world.ViewerId,
                        IncludeUntenanted = true,
                        Category = category,
                        OrderByCategoryAndName = true,
                        PageSize = 100
                    }).ConfigureAwait(false);
                    List<string> names = ordered.Objects.Select(t => t.Name.ToLowerInvariant()).ToList();
                    AssertEqual(String.Join(",", names.OrderBy(x => x, StringComparer.Ordinal)), String.Join(",", names), "ordered by name within the category");
                }
            }));

            cases.Add(CaseAsync("workflow_profiles_visible_paging", "Workflow profiles: ViewerUserId restricts to tenant-wide and own profiles before counting", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    VisibleSet expected = new VisibleSet();
                    foreach (Seed seed in world.Seeds())
                    {
                        // Same name and timestamps on every row, so only the id tie-break orders them.
                        WorkflowProfile profile = new WorkflowProfile { Name = "same-name", BuildCommand = "make" };
                        profile.TenantId = seed.TenantId;
                        profile.UserId = seed.UserId;
                        profile.OwnershipScope = seed.Scope;
                        profile.CreatedUtc = _SameCreatedUtc;
                        profile.LastUpdateUtc = _SameCreatedUtc;
                        profile = await db.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);
                        expected.Add(seed, profile.Id);
                    }

                    await ExpectPagesAsync("workflow viewer", expected.ForViewer, (p, s) => db.WorkflowProfiles.EnumerateAsync(new WorkflowProfileQuery { TenantId = world.TenantId, ViewerUserId = world.ViewerId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    await ExpectPagesAsync("workflow tenant", expected.ForTenant, (p, s) => db.WorkflowProfiles.EnumerateAsync(new WorkflowProfileQuery { TenantId = world.TenantId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("project_profiles_visible_paging", "Project profiles: ViewerUserId restricts to tenant-wide and own profiles before counting", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    VisibleSet expected = new VisibleSet();
                    foreach (Seed seed in world.Seeds())
                    {
                        ProjectProfile profile = new ProjectProfile { Name = "same-name" };
                        profile.TenantId = seed.TenantId;
                        profile.UserId = seed.UserId;
                        profile.OwnershipScope = seed.Scope;
                        profile.CreatedUtc = _SameCreatedUtc;
                        profile.LastUpdateUtc = _SameCreatedUtc;
                        profile = await db.ProjectProfiles.CreateAsync(profile).ConfigureAwait(false);
                        expected.Add(seed, profile.Id);
                    }

                    await ExpectPagesAsync("project viewer", expected.ForViewer, (p, s) => db.ProjectProfiles.EnumerateAsync(new ProjectProfileQuery { TenantId = world.TenantId, ViewerUserId = world.ViewerId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    await ExpectPagesAsync("project tenant", expected.ForTenant, (p, s) => db.ProjectProfiles.EnumerateAsync(new ProjectProfileQuery { TenantId = world.TenantId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("skills_visible_paging", "Skills: ViewerUserId restricts to tenant-wide and own skills before counting, ordered by name then id", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    VisibleSet expected = new VisibleSet();
                    int n = 0;
                    foreach (Seed seed in world.Seeds())
                    {
                        Skill skill = new Skill { Name = "cvp-" + world.Suffix + "-" + (n++), Content = "skill" };
                        skill.TenantId = seed.TenantId;
                        skill.UserId = seed.UserId;
                        skill.Scope = seed.Scope;
                        skill = await db.Skills.CreateAsync(skill).ConfigureAwait(false);
                        expected.Add(seed, skill.Id);
                    }

                    await ExpectPagesAsync("skills viewer", expected.ForViewer, (p, s) => db.Skills.EnumerateAsync(new SkillQuery { TenantId = world.TenantId, RestrictToViewer = true, ViewerUserId = world.ViewerId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                    await ExpectPagesAsync("skills tenant", expected.ForTenant, (p, s) => db.Skills.EnumerateAsync(new SkillQuery { TenantId = world.TenantId, PageNumber = p, PageSize = s }), x => x.Id).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("page_size_is_normalized", "Out-of-range page sizes are normalized to 1..1000 and reported in the result", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    World world = await CreateWorldAsync(db).ConfigureAwait(false);
                    EnumerationResult<Pipeline> tiny = await db.Pipelines.EnumerateVisibleAsync(world.TenantId, world.ViewerId, new EnumerationQuery { PageSize = 0 }).ConfigureAwait(false);
                    AssertEqual(1, tiny.PageSize, "pipelines page size 0 normalized to 1");
                    EnumerationResult<PromptTemplate> huge = await db.PromptTemplates.EnumerateVisibleAsync(new PromptTemplateQuery { TenantId = world.TenantId, PageSize = 5000 }).ConfigureAwait(false);
                    AssertEqual(1000, huge.PageSize, "templates page size 5000 normalized to 1000");
                    EnumerationResult<WorkflowProfile> wf = await db.WorkflowProfiles.EnumerateAsync(new WorkflowProfileQuery { TenantId = world.TenantId, PageSize = 5000 }).ConfigureAwait(false);
                    AssertEqual(1000, wf.PageSize, "workflow page size 5000 normalized to 1000");
                    AssertEqual(10, new WorkflowProfileQuery().PageSize, "workflow default page size");
                    AssertEqual(10, new ProjectProfileQuery().PageSize, "project default page size");
                    AssertEqual(10, new SkillQuery().PageSize, "skill default page size");
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Configuration catalogs page with visibility in the data source", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task ExpectPagesAsync<T>(string label, List<string> expected, Func<int, int, Task<EnumerationResult<T>>> fetch, Func<T, string> idOf)
        {
            const int pageSize = 2;
            int expectedPages = (int)Math.Ceiling(expected.Count / (double)pageSize);
            List<string> seen = new List<string>();
            List<string> firstPass = new List<string>();
            for (int pageNumber = 1; pageNumber <= expectedPages + 1; pageNumber++)
            {
                EnumerationResult<T> page = await fetch(pageNumber, pageSize).ConfigureAwait(false);
                AssertEqual((long)expected.Count, page.TotalRecords, label + " TotalRecords on page " + pageNumber);
                AssertEqual(expectedPages, page.TotalPages, label + " TotalPages");
                int expectedCount = pageNumber <= expectedPages ? Math.Min(pageSize, expected.Count - (pageNumber - 1) * pageSize) : 0;
                AssertEqual(expectedCount, page.Objects.Count, label + " objects on page " + pageNumber);
                seen.AddRange(page.Objects.Select(idOf));
            }

            AssertEqual(seen.Count, seen.Distinct().Count(), label + ": pages never overlap");
            AssertEqual(String.Join(",", expected.OrderBy(x => x, StringComparer.Ordinal)), String.Join(",", seen.OrderBy(x => x, StringComparer.Ordinal)), label + ": pages cover exactly the visible rows");

            for (int pageNumber = 1; pageNumber <= expectedPages; pageNumber++)
            {
                EnumerationResult<T> again = await fetch(pageNumber, pageSize).ConfigureAwait(false);
                firstPass.AddRange(again.Objects.Select(idOf));
            }
            AssertEqual(String.Join(",", seen), String.Join(",", firstPass), label + ": order is deterministic across reads");
        }

        private static async Task<World> CreateWorldAsync(DatabaseDriver db)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("cvp-" + suffix)).ConfigureAwait(false);
            TenantMetadata otherTenant = await db.Tenants.CreateAsync(new TenantMetadata("cvp-other-" + suffix)).ConfigureAwait(false);
            UserMaster viewer = await db.Users.CreateAsync(new UserMaster(tenant.Id, "cvp-viewer-" + suffix + "@example.com", "password")).ConfigureAwait(false);
            UserMaster peer = await db.Users.CreateAsync(new UserMaster(tenant.Id, "cvp-peer-" + suffix + "@example.com", "password")).ConfigureAwait(false);
            UserMaster outsider = await db.Users.CreateAsync(new UserMaster(otherTenant.Id, "cvp-out-" + suffix + "@example.com", "password")).ConfigureAwait(false);
            return new World(suffix, tenant.Id, viewer.Id, peer.Id, otherTenant.Id, outsider.Id);
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

        #region Private-Classes

        private sealed class Seed
        {
            public string TenantId { get; set; } = "";
            public string UserId { get; set; } = "";
            public ScopeEnum Scope { get; set; } = ScopeEnum.TenantWide;
            public bool VisibleToViewer { get; set; } = false;
            public bool InTenant { get; set; } = false;
        }

        private sealed class VisibleSet
        {
            public List<string> ForViewer { get; } = new List<string>();
            public List<string> ForTenant { get; } = new List<string>();

            public void Add(Seed seed, string id)
            {
                if (seed.VisibleToViewer) ForViewer.Add(id);
                if (seed.InTenant) ForTenant.Add(id);
            }
        }

        private sealed class World
        {
            public string Suffix { get; }
            public string TenantId { get; }
            public string ViewerId { get; }
            private readonly string _PeerId;
            private readonly string _OtherTenantId;
            private readonly string _OutsiderId;

            public World(string suffix, string tenantId, string viewerId, string peerId, string otherTenantId, string outsiderId)
            {
                Suffix = suffix;
                TenantId = tenantId;
                ViewerId = viewerId;
                _PeerId = peerId;
                _OtherTenantId = otherTenantId;
                _OutsiderId = outsiderId;
            }

            public List<Seed> Seeds()
            {
                // 3 tenant-wide (by the peer), 2 owned by the viewer, 3 private to the peer, 1 in another tenant:
                // the viewer sees 5 (three pages of 2), the tenant admin 8 (four pages of 2).
                List<Seed> seeds = new List<Seed>();
                for (int i = 0; i < 3; i++) seeds.Add(new Seed { TenantId = TenantId, UserId = _PeerId, Scope = ScopeEnum.TenantWide, VisibleToViewer = true, InTenant = true });
                for (int i = 0; i < 2; i++) seeds.Add(new Seed { TenantId = TenantId, UserId = ViewerId, Scope = ScopeEnum.UserSpecific, VisibleToViewer = true, InTenant = true });
                for (int i = 0; i < 3; i++) seeds.Add(new Seed { TenantId = TenantId, UserId = _PeerId, Scope = ScopeEnum.UserSpecific, VisibleToViewer = false, InTenant = true });
                seeds.Add(new Seed { TenantId = _OtherTenantId, UserId = _OutsiderId, Scope = ScopeEnum.TenantWide, VisibleToViewer = false, InTenant = false });
                return seeds;
            }
        }

        #endregion
    }
}
