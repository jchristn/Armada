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
    /// Data-source paging for the delivery and account lists (docs/ENUMERATION.md): incidents, runbook executions,
    /// runbooks (playbook rows), push devices, and the login tenant lookup are filtered, ordered, paged, and counted in
    /// the database. Each case runs against whichever provider the run selects, so the parity runner covers SQLite,
    /// PostgreSQL, MySQL, and SQL Server.
    /// </summary>
    public sealed class DeliveryEnumerationDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.DeliveryEnumeration";
        private static readonly DateTime _Base = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("incidents_page_in_order_with_exact_totals", "Incidents page newest first with an id tie-break, exact totals, and an empty page past the end", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                List<string> ids = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    // Two pairs share a timestamp so the id tie-break decides their order.
                    Incident incident = NewIncident("ten_pg", "usr_pg", "Paged incident " + i, _Base.AddMinutes(i / 2));
                    await db.Incidents.CreateAsync(incident).ConfigureAwait(false);
                    ids.Add(incident.Id);
                }

                await db.Incidents.CreateAsync(NewIncident("ten_other", "usr_other", "Other tenant", _Base.AddMinutes(10))).ConfigureAwait(false);

                List<string> seen = new List<string>();
                List<DateTime> seenUpdated = new List<DateTime>();
                for (int page = 1; page <= 3; page++)
                {
                    EnumerationResult<Incident> result = await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_pg", PageNumber = page, PageSize = 2 }).ConfigureAwait(false);
                    AssertEqual(5L, result.TotalRecords, "total for the tenant only");
                    AssertEqual(3, result.TotalPages, "pages");
                    AssertEqual(page < 3 ? 2 : 1, result.Objects.Count, "page " + page + " size");
                    seen.AddRange(result.Objects.Select(item => item.Id));
                    seenUpdated.AddRange(result.Objects.Select(item => item.LastUpdateUtc));
                }

                // Equal timestamps are ordered by id in the provider's collation; the pages never overlap or skip.
                AssertEqual(5, seen.Distinct(StringComparer.Ordinal).Count(), "no overlap across pages");
                AssertTrue(ids.All(id => seen.Contains(id)), "no gaps across pages");
                for (int i = 1; i < seenUpdated.Count; i++)
                    AssertTrue(seenUpdated[i - 1] >= seenUpdated[i], "newest first at position " + i);
                List<string> again = new List<string>();
                for (int page = 1; page <= 3; page++)
                    again.AddRange((await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_pg", PageNumber = page, PageSize = 2 }).ConfigureAwait(false)).Objects.Select(item => item.Id));
                AssertEqual(String.Join(",", seen), String.Join(",", again), "the same order on every read");

                EnumerationResult<Incident> past = await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_pg", PageNumber = 9, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "past the end is empty");
                AssertEqual(5L, past.TotalRecords, "past the end keeps the total");

                IncidentQuery clamped = new IncidentQuery { PageSize = 5000, PageNumber = -3 };
                AssertEqual(1000, clamped.PageSize, "page size normalized to the maximum");
                AssertEqual(1, clamped.PageNumber, "page number normalized to 1");
            }));

            cases.Add(CaseAsync("incidents_filter_and_scope_in_the_database", "Incident filters (status, severity, failure kind, search) and tenant/user scope apply before counting", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                Incident open = NewIncident("ten_f", "usr_a", "Database outage", _Base);
                open.Postmortem = "Connection pool exhausted";
                Incident closed = NewIncident("ten_f", "usr_b", "Cache miss storm", _Base.AddMinutes(1));
                closed.Status = IncidentStatusEnum.Closed;
                closed.Severity = IncidentSeverityEnum.Low;
                Incident recovery = NewIncident("ten_f", "usr_a", "Mission recovery: build", _Base.AddMinutes(2));
                recovery.FailureKind = "BuildFailed";
                await db.Incidents.CreateAsync(open).ConfigureAwait(false);
                await db.Incidents.CreateAsync(closed).ConfigureAwait(false);
                await db.Incidents.CreateAsync(recovery).ConfigureAwait(false);

                AssertEqual(1L, (await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", Status = IncidentStatusEnum.Closed }).ConfigureAwait(false)).TotalRecords, "status");
                AssertEqual(1L, (await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", Severity = IncidentSeverityEnum.Low }).ConfigureAwait(false)).TotalRecords, "severity");
                EnumerationResult<Incident> withKind = await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", HasFailureKind = true }).ConfigureAwait(false);
                AssertEqual(1L, withKind.TotalRecords, "recovery incidents only");
                AssertEqual(recovery.Id, withKind.Objects[0].Id);
                AssertEqual(2L, (await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", HasFailureKind = false }).ConfigureAwait(false)).TotalRecords, "without failure kind");
                EnumerationResult<Incident> bySearch = await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", Search = "POOL EXHAUSTED" }).ConfigureAwait(false);
                AssertEqual(1L, bySearch.TotalRecords, "search ignores case and reads the postmortem");
                AssertEqual(open.Id, bySearch.Objects[0].Id);
                AssertEqual(2L, (await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", UserId = "usr_a" }).ConfigureAwait(false)).TotalRecords, "user scope");

                AssertNotNull(await db.Incidents.ReadAsync(open.Id, new IncidentQuery { TenantId = "ten_f", UserId = "usr_a" }).ConfigureAwait(false), "owner reads");
                AssertNull(await db.Incidents.ReadAsync(open.Id, new IncidentQuery { TenantId = "ten_f", UserId = "usr_b" }).ConfigureAwait(false), "another user does not");
                AssertNull(await db.Incidents.ReadAsync(open.Id, new IncidentQuery { TenantId = "ten_x" }).ConfigureAwait(false), "another tenant does not");

                open.Status = IncidentStatusEnum.Mitigated;
                open.RescueMissionIds = new List<string> { "msn_1", "msn_2" };
                open.LastUpdateUtc = _Base.AddMinutes(30);
                await db.Incidents.UpdateAsync(open).ConfigureAwait(false);
                Incident? reread = await db.Incidents.ReadAsync(open.Id).ConfigureAwait(false);
                AssertEqual(IncidentStatusEnum.Mitigated, reread!.Status, "update persisted");
                AssertEqual("msn_1,msn_2", String.Join(",", reread.RescueMissionIds), "list persisted");
                AssertEqual(open.Id, (await db.Incidents.EnumerateAsync(new IncidentQuery { TenantId = "ten_f", PageSize = 1 }).ConfigureAwait(false)).Objects[0].Id, "newest update first");

                AssertTrue(await db.Incidents.DeleteAsync(open.Id).ConfigureAwait(false), "deleted");
                AssertEqual(2L, await db.Incidents.CountAsync().ConfigureAwait(false), "count after delete");
            }));

            cases.Add(CaseAsync("runbook_executions_page_and_filter_in_the_database", "Runbook executions filter by runbook, status, and search, and page with exact totals", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                for (int i = 0; i < 3; i++)
                {
                    RunbookExecution execution = NewExecution("ten_rx", "usr_rx", "rbk_one", "Release run " + i, _Base.AddMinutes(i));
                    if (i == 2) execution.Status = RunbookExecutionStatusEnum.Completed;
                    await db.RunbookExecutions.CreateAsync(execution).ConfigureAwait(false);
                }

                await db.RunbookExecutions.CreateAsync(NewExecution("ten_rx", "usr_rx", "rbk_two", "Rollback drill", _Base.AddMinutes(5))).ConfigureAwait(false);

                EnumerationResult<RunbookExecution> first = await db.RunbookExecutions.EnumerateAsync(new RunbookExecutionQuery { TenantId = "ten_rx", RunbookId = "rbk_one", PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, first.TotalRecords, "runbook filter");
                AssertEqual(2, first.Objects.Count, "page");
                AssertEqual("Release run 2", first.Objects[0].Title, "newest first");
                EnumerationResult<RunbookExecution> second = await db.RunbookExecutions.EnumerateAsync(new RunbookExecutionQuery { TenantId = "ten_rx", RunbookId = "rbk_one", PageSize = 2, PageNumber = 2 }).ConfigureAwait(false);
                AssertEqual(1, second.Objects.Count, "final page");
                AssertEqual(1L, (await db.RunbookExecutions.EnumerateAsync(new RunbookExecutionQuery { TenantId = "ten_rx", Status = RunbookExecutionStatusEnum.Completed }).ConfigureAwait(false)).TotalRecords, "status");
                AssertEqual(1L, (await db.RunbookExecutions.EnumerateAsync(new RunbookExecutionQuery { TenantId = "ten_rx", Search = "drill" }).ConfigureAwait(false)).TotalRecords, "search");
                AssertEqual(0L, (await db.RunbookExecutions.EnumerateAsync(new RunbookExecutionQuery { TenantId = "ten_none" }).ConfigureAwait(false)).TotalRecords, "tenant scope");
            }));

            cases.Add(CaseAsync("runbooks_page_in_the_playbooks_table", "Runbooks page in the playbooks table: metadata filters, search, scope, and file-name order", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await EnsureTenantAndUserAsync(db, "ten_rb", "usr_rb_a").ConfigureAwait(false);
                await EnsureTenantAndUserAsync(db, "ten_rb", "usr_rb_b").ConfigureAwait(false);

                await db.Playbooks.CreateAsync(NewPlaybook("ten_rb", "usr_rb_a", "a-deploy.md", ScopeEnum.TenantWide, "wp_deploy_1", "Deploy the service")).ConfigureAwait(false);
                await db.Playbooks.CreateAsync(NewPlaybook("ten_rb", "usr_rb_a", "b-private.md", ScopeEnum.UserSpecific, "wp_other", "Private steps")).ConfigureAwait(false);
                await db.Playbooks.CreateAsync(NewPlaybook("ten_rb", "usr_rb_b", "c-theirs.md", ScopeEnum.UserSpecific, "wp_deploy_1", "Someone else")).ConfigureAwait(false);
                // An underscore in the filter must match literally, not as a single-character wildcard.
                await db.Playbooks.CreateAsync(NewPlaybook("ten_rb", "usr_rb_a", "d-lookalike.md", ScopeEnum.TenantWide, "wpXdeploy_1", "Lookalike")).ConfigureAwait(false);

                EnumerationResult<Playbook> all = await db.Runbooks.EnumerateAsync(new RunbookQuery { TenantId = "ten_rb", PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(4L, all.TotalRecords, "tenant admin view");
                AssertEqual("a-deploy.md,b-private.md", String.Join(",", all.Objects.Select(p => p.FileName)), "file-name order");

                EnumerationResult<Playbook> visible = await db.Runbooks.EnumerateAsync(new RunbookQuery { TenantId = "ten_rb", VisibleToUserId = "usr_rb_a", PageSize = 10 }).ConfigureAwait(false);
                AssertEqual("a-deploy.md,b-private.md,d-lookalike.md", String.Join(",", visible.Objects.Select(p => p.FileName)), "tenant-wide plus own");

                EnumerationResult<Playbook> byProfile = await db.Runbooks.EnumerateAsync(new RunbookQuery { TenantId = "ten_rb", WorkflowProfileId = "wp_deploy_1", PageSize = 10 }).ConfigureAwait(false);
                AssertEqual("a-deploy.md,c-theirs.md", String.Join(",", byProfile.Objects.Select(p => p.FileName)), "metadata filter, literal underscore");

                EnumerationResult<Playbook> bySearch = await db.Runbooks.EnumerateAsync(new RunbookQuery { TenantId = "ten_rb", Search = "PRIVATE STEPS", PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(1L, bySearch.TotalRecords, "search reads content and ignores case");

                EnumerationResult<Playbook> inactive = await db.Runbooks.EnumerateAsync(new RunbookQuery { TenantId = "ten_rb", Active = false }).ConfigureAwait(false);
                AssertEqual(0L, inactive.TotalRecords, "active filter");
            }));

            cases.Add(CaseAsync("push_devices_page_oldest_first", "Push devices page oldest first with exact totals inside the owner scope", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                List<string> ids = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    PushDevice device = NewDevice("ten_pd", "usr_pd", _Base.AddMinutes(i));
                    await db.PushDevices.CreateAsync(device).ConfigureAwait(false);
                    ids.Add(device.Id);
                }

                await db.PushDevices.CreateAsync(NewDevice("ten_pd", "usr_other", _Base)).ConfigureAwait(false);
                PushDeviceQuery scope = new PushDeviceQuery { TenantId = "ten_pd", UserId = "usr_pd" };

                EnumerationResult<PushDevice> first = await db.PushDevices.EnumeratePageAsync(scope, new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                EnumerationResult<PushDevice> second = await db.PushDevices.EnumeratePageAsync(scope, new EnumerationQuery { PageSize = 2, PageNumber = 2 }).ConfigureAwait(false);
                AssertEqual(3L, first.TotalRecords, "owner total");
                AssertEqual(2, first.TotalPages, "pages");
                AssertEqual(String.Join(",", ids), String.Join(",", first.Objects.Concat(second.Objects).Select(d => d.Id)), "oldest first across pages");
                AssertEqual(4L, (await db.PushDevices.EnumeratePageAsync(new PushDeviceQuery { TenantId = "ten_pd" }, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "tenant scope");
            }));

            cases.Add(CaseAsync("tenant_lookup_pages_active_tenants", "The login tenant lookup pages active, non-system tenants for an email in name order", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string email = "lookup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@armada.test";
                await CreateTenantWithUserAsync(db, "ten_lk_b", "Bravo", email, true).ConfigureAwait(false);
                await CreateTenantWithUserAsync(db, "ten_lk_a", "Alpha", email, true).ConfigureAwait(false);
                await CreateTenantWithUserAsync(db, "ten_lk_c", "Charlie", email, true).ConfigureAwait(false);
                await CreateTenantWithUserAsync(db, "ten_lk_off", "Inactive", email, false).ConfigureAwait(false);

                EnumerationResult<TenantListEntry> first = await db.Users.EnumerateTenantsByEmailAsync(email, new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                EnumerationResult<TenantListEntry> second = await db.Users.EnumerateTenantsByEmailAsync(email, new EnumerationQuery { PageSize = 2, PageNumber = 2 }).ConfigureAwait(false);
                EnumerationResult<TenantListEntry> third = await db.Users.EnumerateTenantsByEmailAsync(email, new EnumerationQuery { PageSize = 2, PageNumber = 3 }).ConfigureAwait(false);
                AssertEqual(3L, first.TotalRecords, "inactive tenant excluded");
                AssertEqual(2, first.TotalPages, "pages");
                AssertEqual("Alpha,Bravo,Charlie", String.Join(",", first.Objects.Concat(second.Objects).Select(t => t.Name)), "name order");
                AssertEqual(0, third.Objects.Count, "past the end");
                AssertEqual(0L, (await db.Users.EnumerateTenantsByEmailAsync("nobody-" + email, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "unknown email");
            }));

            return new TestSuiteDescriptor(SuiteId, "Delivery and account lists page in the database", cases);
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
                tags: new List<string> { tag, TestTags.Database });
        }

        private static Incident NewIncident(string tenantId, string userId, string title, DateTime updated)
        {
            return new Incident
            {
                TenantId = tenantId,
                UserId = userId,
                Title = title,
                Status = IncidentStatusEnum.Open,
                Severity = IncidentSeverityEnum.High,
                DetectedUtc = updated,
                LastUpdateUtc = updated
            };
        }

        private static RunbookExecution NewExecution(string tenantId, string userId, string runbookId, string title, DateTime updated)
        {
            return new RunbookExecution
            {
                TenantId = tenantId,
                UserId = userId,
                RunbookId = runbookId,
                PlaybookId = runbookId,
                Title = title,
                StartedUtc = updated,
                LastUpdateUtc = updated
            };
        }

        private static Playbook NewPlaybook(string tenantId, string userId, string fileName, ScopeEnum scope, string workflowProfileId, string body)
        {
            string metadata = "{\"Title\":\"" + fileName + "\",\"WorkflowProfileId\":\"" + workflowProfileId + "\",\"EnvironmentId\":null,\"EnvironmentName\":null,\"DefaultCheckType\":null,\"Parameters\":[],\"Steps\":[]}";
            return new Playbook
            {
                TenantId = tenantId,
                UserId = userId,
                FileName = fileName,
                Scope = scope,
                Content = "<!-- armada:runbook" + Environment.NewLine + metadata + Environment.NewLine + "-->" + Environment.NewLine + Environment.NewLine + body,
                Active = true,
                CreatedUtc = _Base,
                LastUpdateUtc = _Base
            };
        }

        private static PushDevice NewDevice(string tenantId, string userId, DateTime created)
        {
            PushDevice device = new PushDevice();
            device.TenantId = tenantId;
            device.UserId = userId;
            device.ExpoPushToken = "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
            device.Categories = new List<PushCategoryEnum> { PushCategoryEnum.MissionFailed };
            device.CreatedUtc = created;
            device.LastSeenUtc = created;
            return device;
        }

        private static async Task EnsureTenantAndUserAsync(DatabaseDriver db, string tenantId, string userId)
        {
            if (await db.Tenants.ReadAsync(tenantId).ConfigureAwait(false) == null)
                await db.Tenants.CreateAsync(new TenantMetadata { Id = tenantId, Name = tenantId }).ConfigureAwait(false);
            if (await db.Users.ReadByIdAsync(userId).ConfigureAwait(false) == null)
            {
                await db.Users.CreateAsync(new UserMaster
                {
                    Id = userId,
                    TenantId = tenantId,
                    Email = userId + "@armada.test",
                    PasswordSha256 = UserMaster.ComputePasswordHash("password")
                }).ConfigureAwait(false);
            }
        }

        private static async Task CreateTenantWithUserAsync(DatabaseDriver db, string tenantId, string name, string email, bool active)
        {
            await db.Tenants.CreateAsync(new TenantMetadata { Id = tenantId, Name = name, Active = active }).ConfigureAwait(false);
            await db.Users.CreateAsync(new UserMaster
            {
                TenantId = tenantId,
                Email = email,
                PasswordSha256 = UserMaster.ComputePasswordHash("password")
            }).ConfigureAwait(false);
        }

        #endregion
    }
}
