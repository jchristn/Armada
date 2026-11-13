namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes.Mcp;
    using Armada.Server.Routes;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end stage outcomes and failure-cause rules through the real server: list, read and classify outcomes,
    /// re-apply the rules, draft a rule from an outcome, and manage rules over REST and MCP, with tenant isolation (another
    /// tenant's outcome or rule is not found), admin-only reclassification, global-admin-only edits of rules for every
    /// tenant and of built-in rules, validation (400 / InvalidArgument), and seeding that restores a deleted built-in and
    /// keeps an edited one.
    /// </summary>
    public sealed class StageOutcomeApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.StageOutcomes";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("query_defaults", "A querystring with no filters parses to the defaults; bad values are rejected", () =>
            {
                StageOutcomeQuery empty = StageOutcomeRoutes.ParseQuery(key => null);
                AssertNull(empty.TenantId, "tenant");
                AssertNull(empty.MissionId, "mission");
                AssertNull(empty.SuspectedCause, "cause");
                AssertNull(empty.CauseSource, "source");
                AssertNull(empty.Disposition, "disposition");
                AssertNull(empty.IsEvaluation, "evaluation");
                AssertNull(empty.FromUtc, "from");
                AssertNull(empty.ToUtc, "to");
                AssertEqual(1, empty.PageNumber, "page number");
                AssertEqual(100, empty.PageSize, "page size");

                Dictionary<string, string?> full = new Dictionary<string, string?>
                {
                    ["missionId"] = " msn_1 ", ["persona"] = "Worker", ["suspectedCause"] = "protocol", ["causeSource"] = "Manual",
                    ["disposition"] = "Relaunched", ["isEvaluation"] = "false", ["fromUtc"] = "2026-10-01T00:00:00Z",
                    ["toUtc"] = "2026-10-02T00:00:00Z", ["pageNumber"] = "2", ["pageSize"] = "5", ["model"] = "  "
                };
                StageOutcomeQuery parsed = StageOutcomeRoutes.ParseQuery(key => full.TryGetValue(key, out string? v) ? v : null);
                AssertEqual("msn_1", parsed.MissionId, "trimmed mission");
                AssertNull(parsed.Model, "blank model is no filter");
                AssertEqual(FailureCauseEnum.Protocol, parsed.SuspectedCause!.Value, "case-insensitive cause");
                AssertEqual(FailureCauseSourceEnum.Manual, parsed.CauseSource!.Value);
                AssertEqual(StageDispositionEnum.Relaunched, parsed.Disposition!.Value);
                AssertEqual(false, parsed.IsEvaluation!.Value, "evaluation");
                AssertEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), parsed.FromUtc!.Value, "from");
                AssertEqual(DateTimeKind.Utc, parsed.FromUtc!.Value.Kind, "UTC kind");
                AssertEqual(2, parsed.PageNumber);
                AssertEqual(5, parsed.PageSize);

                foreach (KeyValuePair<string, string> bad in new Dictionary<string, string> { ["suspectedCause"] = "Sloppy", ["disposition"] = "Done", ["isEvaluation"] = "maybe", ["fromUtc"] = "yesterday", ["pageSize"] = "ten" })
                    AssertThrows<ArgumentException>(() => StageOutcomeRoutes.ParseQuery(key => key == bad.Key ? bad.Value : null), bad.Key);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("rest_outcomes_tenant_isolation", "Outcomes are listed and read within the caller's tenant; another tenant's outcome is not found, and global admins filter by tenant", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "soi-user-a", false).ConfigureAwait(false);
                E2ETenantUser userB = await E2ETenantUser.CreateAsync(fx.AuthClient, "soi-user-b", false).ConfigureAwait(false);
                StageOutcome outcomeA = await SeedAsync(fx, userA.TenantId, o => { o.MissionId = "msn_soi_a"; o.Persona = "Worker"; }).ConfigureAwait(false);
                StageOutcome outcomeA2 = await SeedAsync(fx, userA.TenantId, o => { o.MissionId = "msn_soi_a2"; o.Persona = "Judge"; }).ConfigureAwait(false);
                StageOutcome outcomeB = await SeedAsync(fx, userB.TenantId, o => o.MissionId = "msn_soi_b").ConfigureAwait(false);
                using HttpClient a = userA.CreateClient(fx.BaseUrl);
                using HttpClient b = userB.CreateClient(fx.BaseUrl);

                // A regular user cannot widen the scope with tenantId.
                EnumerationResult<StageOutcome> pageA = await GetAsync<EnumerationResult<StageOutcome>>(a, "/api/v1/stage-outcomes?tenantId=" + userB.TenantId).ConfigureAwait(false);
                AssertEqual(2, (int)pageA.TotalRecords, "tenant A sees its two outcomes");
                AssertTrue(pageA.Objects.All(o => o.TenantId == userA.TenantId), "only tenant A rows");
                AssertFalse(pageA.Objects.Any(o => o.Id == outcomeB.Id), "tenant B's outcome is not listed");

                EnumerationResult<StageOutcome> filtered = await GetAsync<EnumerationResult<StageOutcome>>(a, "/api/v1/stage-outcomes?persona=judge&pageSize=1").ConfigureAwait(false);
                AssertEqual(1, (int)filtered.TotalRecords, "persona filter");
                AssertEqual(outcomeA2.Id, filtered.Objects.Single().Id);
                AssertEqual(1, filtered.PageSize, "page size echoed");

                StageOutcome read = await GetAsync<StageOutcome>(a, "/api/v1/stage-outcomes/" + outcomeA.Id).ConfigureAwait(false);
                AssertEqual("msn_soi_a", read.MissionId);
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/stage-outcomes/" + outcomeB.Id).ConfigureAwait(false), "A cannot read B's outcome");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync("/api/v1/stage-outcomes/" + outcomeA.Id).ConfigureAwait(false), "B cannot read A's outcome");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PutAsync("/api/v1/stage-outcomes/" + outcomeA.Id + "/cause", JsonHelper.ToJsonContent(new { Cause = "Protocol" })).ConfigureAwait(false), "B cannot classify A's outcome");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PostAsync("/api/v1/stage-outcomes/" + outcomeA.Id + "/rule", null).ConfigureAwait(false), "B cannot draft from A's outcome");
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/stage-outcomes/sto_missing").ConfigureAwait(false), "missing id");
                StageOutcome untouched = await GetAsync<StageOutcome>(a, "/api/v1/stage-outcomes/" + outcomeA.Id).ConfigureAwait(false);
                AssertEqual(FailureCauseSourceEnum.Rule, untouched.CauseSource, "B's refused classify changed nothing");

                EnumerationResult<StageOutcome> global = await GetAsync<EnumerationResult<StageOutcome>>(fx.AuthClient, "/api/v1/stage-outcomes?tenantId=" + userB.TenantId).ConfigureAwait(false);
                AssertEqual(outcomeB.Id, global.Objects.Single().Id, "a global admin filters by tenant");
                StageOutcome globalRead = await GetAsync<StageOutcome>(fx.AuthClient, "/api/v1/stage-outcomes/" + outcomeA.Id).ConfigureAwait(false);
                AssertEqual(outcomeA.Id, globalRead.Id, "a global admin reads any tenant");

                AssertStatusCode(HttpStatusCode.BadRequest, await a.GetAsync("/api/v1/stage-outcomes?suspectedCause=Sloppy").ConfigureAwait(false), "invalid cause");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.GetAsync("/api/v1/stage-outcomes?fromUtc=2026-10-02T00:00:00Z&toUtc=2026-10-01T00:00:00Z").ConfigureAwait(false), "from after to");
                AssertStatusCode(HttpStatusCode.Unauthorized, await fx.UnauthClient.GetAsync("/api/v1/stage-outcomes").ConfigureAwait(false), "unauthenticated");
            }));

            cases.Add(CaseAsync("rest_classify_and_hand_back", "Classifying by hand records the caller and survives; None is rejected; a null cause hands the outcome back to the rules", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "soc-user", false).ConfigureAwait(false);
                StageOutcome outcome = await SeedAsync(fx, user.TenantId, o => { o.FailureKind = MissionFailureKindEnum.Infra; o.Disposition = StageDispositionEnum.Failed; }).ConfigureAwait(false);
                using HttpClient c = user.CreateClient(fx.BaseUrl);

                HttpResponseMessage classified = await c.PutAsync("/api/v1/stage-outcomes/" + outcome.Id + "/cause", JsonHelper.ToJsonContent(new { Cause = "InstructionFollowing", Note = "ignored the brief" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, classified);
                StageOutcome manual = await JsonHelper.DeserializeAsync<StageOutcome>(classified).ConfigureAwait(false);
                AssertEqual(FailureCauseEnum.InstructionFollowing, manual.SuspectedCause);
                AssertEqual(FailureCauseSourceEnum.Manual, manual.CauseSource);
                AssertEqual(user.UserId, manual.ClassifiedByUserId, "records the calling user");
                AssertEqual("ignored the brief", manual.CauseNote);
                AssertNotNull(manual.ClassifiedUtc, "classified time");
                StageOutcome stored = await GetAsync<StageOutcome>(c, "/api/v1/stage-outcomes/" + outcome.Id).ConfigureAwait(false);
                AssertEqual(FailureCauseSourceEnum.Manual, stored.CauseSource, "persisted");

                HttpResponseMessage none = await c.PutAsync("/api/v1/stage-outcomes/" + outcome.Id + "/cause", JsonHelper.ToJsonContent(new { Cause = "None" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, none, "cause None");
                AssertContains("null cause", await none.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.BadRequest, await c.PutAsync("/api/v1/stage-outcomes/" + outcome.Id + "/cause", JsonHelper.ToJsonContent(new { Cause = "Sloppy" })).ConfigureAwait(false), "unknown cause");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PutAsync("/api/v1/stage-outcomes/sto_missing/cause", JsonHelper.ToJsonContent(new { Cause = "Protocol" })).ConfigureAwait(false), "missing outcome");

                HttpResponseMessage handedBack = await c.PutAsync("/api/v1/stage-outcomes/" + outcome.Id + "/cause", new StringContent("{\"Cause\": null}", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, handedBack);
                StageOutcome ruled = await JsonHelper.DeserializeAsync<StageOutcome>(handedBack).ConfigureAwait(false);
                AssertEqual(FailureCauseEnum.NotTheModel, ruled.SuspectedCause, "the Infra built-in rule applies again");
                AssertEqual(FailureCauseSourceEnum.Rule, ruled.CauseSource);
                AssertNotNull(ruled.CauseRuleId, "rule recorded");
                AssertNull(ruled.ClassifiedByUserId, "no classifier");

                // An empty body is the minimal request: no cause, so it also hands back to the rules.
                HttpResponseMessage empty = await c.PutAsync("/api/v1/stage-outcomes/" + outcome.Id + "/cause", new StringContent("", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, empty, "empty body");
                AssertEqual(FailureCauseSourceEnum.Rule, (await JsonHelper.DeserializeAsync<StageOutcome>(empty).ConfigureAwait(false)).CauseSource);
            }));

            cases.Add(CaseAsync("rest_reclassify_admin_only", "Re-applying the rules is refused for a non-admin, limited to the tenant for a tenant admin, keeps manual causes, and spans tenants for a global admin", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "sor-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "sor-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser userB = await E2ETenantUser.CreateAsync(fx.AuthClient, "sor-user-b", false).ConfigureAwait(false);
                // Stale causes: each would be NotTheModel under the built-in Infra rule.
                StageOutcome staleA = await SeedAsync(fx, adminA.TenantId, o => Stale(o)).ConfigureAwait(false);
                StageOutcome manualA = await SeedAsync(fx, adminA.TenantId, o => { Stale(o); o.CauseSource = FailureCauseSourceEnum.Manual; o.SuspectedCause = FailureCauseEnum.Capability; }).ConfigureAwait(false);
                StageOutcome staleB = await SeedAsync(fx, userB.TenantId, o => Stale(o)).ConfigureAwait(false);
                using HttpClient admin = adminA.CreateClient(fx.BaseUrl);
                using HttpClient user = userA.CreateClient(fx.BaseUrl);

                AssertStatusCode(HttpStatusCode.Forbidden, await user.PostAsync("/api/v1/stage-outcomes/reclassify", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false), "a non-admin cannot reclassify");
                AssertEqual(FailureCauseEnum.Unknown, (await GetAsync<StageOutcome>(user, "/api/v1/stage-outcomes/" + staleA.Id).ConfigureAwait(false)).SuspectedCause, "nothing changed");

                HttpResponseMessage tenantRun = await admin.PostAsync("/api/v1/stage-outcomes/reclassify", JsonHelper.ToJsonContent(new { TenantId = userB.TenantId })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, tenantRun);
                StageOutcomeReclassifyResult result = await JsonHelper.DeserializeAsync<StageOutcomeReclassifyResult>(tenantRun).ConfigureAwait(false);
                AssertEqual(2, result.Examined, "a tenant admin's run covers only their tenant");
                AssertEqual(1, result.Changed);
                AssertEqual(1, result.SkippedManual);
                AssertEqual(FailureCauseEnum.NotTheModel, (await GetAsync<StageOutcome>(admin, "/api/v1/stage-outcomes/" + staleA.Id).ConfigureAwait(false)).SuspectedCause, "stale A reclassified");
                AssertEqual(FailureCauseEnum.Capability, (await GetAsync<StageOutcome>(admin, "/api/v1/stage-outcomes/" + manualA.Id).ConfigureAwait(false)).SuspectedCause, "manual kept");
                AssertEqual(FailureCauseEnum.Unknown, (await GetAsync<StageOutcome>(fx.AuthClient, "/api/v1/stage-outcomes/" + staleB.Id).ConfigureAwait(false)).SuspectedCause, "tenant B untouched");

                HttpResponseMessage globalRun = await fx.AuthClient.PostAsync("/api/v1/stage-outcomes/reclassify", JsonHelper.ToJsonContent(new { TenantId = userB.TenantId })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, globalRun);
                AssertEqual(1, (await JsonHelper.DeserializeAsync<StageOutcomeReclassifyResult>(globalRun).ConfigureAwait(false)).Changed, "global admin reaches tenant B");
                AssertEqual(FailureCauseEnum.NotTheModel, (await GetAsync<StageOutcome>(fx.AuthClient, "/api/v1/stage-outcomes/" + staleB.Id).ConfigureAwait(false)).SuspectedCause);
                AssertStatusCode(HttpStatusCode.BadRequest, await admin.PostAsync("/api/v1/stage-outcomes/reclassify", new StringContent("{\"SuspectedCause\": \"Sloppy\"}", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false), "bad body");
            }));

            cases.Add(CaseAsync("rest_rules_crud_and_validation", "Tenant rules are created, read, updated and deleted; invalid rules are 400 with the reason; another tenant cannot see them", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "sru-user-a", false).ConfigureAwait(false);
                E2ETenantUser userB = await E2ETenantUser.CreateAsync(fx.AuthClient, "sru-user-b", false).ConfigureAwait(false);
                using HttpClient a = userA.CreateClient(fx.BaseUrl);
                using HttpClient b = userB.CreateClient(fx.BaseUrl);

                EnumerationResult<FailureCauseRule> visible = await GetAsync<EnumerationResult<FailureCauseRule>>(a, "/api/v1/failure-cause-rules?pageSize=1000").ConfigureAwait(false);
                AssertTrue(visible.Objects.Count(r => r.IsBuiltIn && r.TenantId == null) >= 13, "built-ins are visible: " + visible.Objects.Count);
                AssertTrue(visible.Objects.Select(r => r.Priority).SequenceEqual(visible.Objects.Select(r => r.Priority).OrderBy(p => p)), "evaluation order");
                EnumerationResult<FailureCauseRule> firstPage = await GetAsync<EnumerationResult<FailureCauseRule>>(a, "/api/v1/failure-cause-rules?pageSize=2").ConfigureAwait(false);
                AssertEqual(2, firstPage.Objects.Count, "paged");
                AssertEqual(visible.TotalRecords, firstPage.TotalRecords, "total");
                List<string> walked = new List<string>();
                for (int pageNumber = 1; pageNumber <= firstPage.TotalPages; pageNumber++)
                    walked.AddRange((await GetAsync<EnumerationResult<FailureCauseRule>>(a, "/api/v1/failure-cause-rules?pageSize=2&pageNumber=" + pageNumber).ConfigureAwait(false)).Objects.Select(r => r.Id));
                AssertEqual(String.Join(",", visible.Objects.Select(r => r.Id)), String.Join(",", walked), "database pages walk the evaluation order without gaps or overlap");
                EnumerationResult<FailureCauseRule> pastEnd = await GetAsync<EnumerationResult<FailureCauseRule>>(a, "/api/v1/failure-cause-rules?pageSize=2&pageNumber=" + (firstPage.TotalPages + 1)).ConfigureAwait(false);
                AssertEqual(0, pastEnd.Objects.Count, "a page past the end is empty");
                AssertEqual(visible.TotalRecords, pastEnd.TotalRecords, "with an accurate total");
                HttpResponseMessage tooMany = await a.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(new { Name = "Too many personas", Cause = "Protocol", Match = new { Personas = Enumerable.Range(0, 257).Select(i => "p" + i).ToArray() } })).ConfigureAwait(false);
                string tooManyBody = await tooMany.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, tooMany, "Match list over the limit");
                E2eLimitErrorBody limit = JsonHelper.Deserialize<E2eLimitErrorBody>(tooManyBody);
                AssertEqual(CollectionLimitExceededException.ErrorCode, limit.Data?.Code, tooManyBody);
                AssertEqual("Match.Personas", limit.Data!.Field);

                // A regular user's rule belongs to their tenant even when the body asks for every tenant.
                HttpResponseMessage created = await a.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(new { Name = "Prose result", TenantId = (string?)null, Priority = 5, Cause = "Protocol", Match = new { FailureKinds = new[] { "InvalidOutput" }, Personas = new[] { " Worker ", " " }, TextPattern = "result block" } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created);
                FailureCauseRule rule = await JsonHelper.DeserializeAsync<FailureCauseRule>(created).ConfigureAwait(false);
                AssertEqual(userA.TenantId, rule.TenantId, "tenant rule");
                AssertFalse(rule.IsBuiltIn, "not built-in");
                AssertEqual(userA.UserId, rule.CreatedByUserId);
                AssertEqual("Worker", rule.Match.Personas.Single(), "personas trimmed, blanks dropped");
                AssertEqual(FailureCauseEnum.Protocol, rule.Cause);

                await AssertBadRuleAsync(a, new { Name = "No criteria", Cause = "Protocol", Match = new { } }, "criterion").ConfigureAwait(false);
                await AssertBadRuleAsync(a, new { Name = "Blank persona only", Cause = "Protocol", Match = new { Personas = new[] { "  " } } }, "criterion").ConfigureAwait(false);
                await AssertBadRuleAsync(a, new { Name = "None cause", Cause = "None", Match = new { FailureKinds = new[] { "Timeout" } } }, "None").ConfigureAwait(false);
                await AssertBadRuleAsync(a, new { Name = "Bad regex", Cause = "Protocol", Match = new { TextPattern = "(unclosed" } }, "regular expression").ConfigureAwait(false);
                await AssertBadRuleAsync(a, new { Name = new string('n', 201), Cause = "Protocol", Match = new { FailureKinds = new[] { "Timeout" } } }, "200").ConfigureAwait(false);
                await AssertBadRuleAsync(a, new { Name = "Bad enum", Cause = "Protocol", Match = new { FailureKinds = new[] { "Sloppy" } } }, "Invalid request body").ConfigureAwait(false);
                await AssertBadRuleAsync(a, new { }, "Name").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Conflict, await a.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(new { Name = "prose RESULT", Cause = "ToolUse", Match = new { FailureKinds = new[] { "Timeout" } } })).ConfigureAwait(false), "duplicate name in the tenant");
                HttpResponseMessage sameNameB = await b.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(new { Name = "Prose result", Cause = "ToolUse", Match = new { FailureKinds = new[] { "Timeout" } } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, sameNameB, "the same name in another tenant is fine");

                AssertEqual(rule.Id, (await GetAsync<FailureCauseRule>(a, "/api/v1/failure-cause-rules/" + rule.Id).ConfigureAwait(false)).Id);
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync("/api/v1/failure-cause-rules/" + rule.Id).ConfigureAwait(false), "B cannot read A's rule");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PutAsync("/api/v1/failure-cause-rules/" + rule.Id, JsonHelper.ToJsonContent(new { Name = "x", Cause = "Protocol", Match = new { FailureKinds = new[] { "Timeout" } } })).ConfigureAwait(false), "B cannot edit A's rule");
                AssertStatusCode(HttpStatusCode.NotFound, await b.DeleteAsync("/api/v1/failure-cause-rules/" + rule.Id).ConfigureAwait(false), "B cannot delete A's rule");
                EnumerationResult<FailureCauseRule> listB = await GetAsync<EnumerationResult<FailureCauseRule>>(b, "/api/v1/failure-cause-rules?pageSize=1000").ConfigureAwait(false);
                AssertFalse(listB.Objects.Any(r => r.Id == rule.Id), "A's rule is not listed for B");

                HttpResponseMessage updated = await a.PutAsync("/api/v1/failure-cause-rules/" + rule.Id, JsonHelper.ToJsonContent(new { Name = "Prose result v2", Priority = 7, Enabled = false, Cause = "InstructionFollowing", Match = new { Dispositions = new[] { "Failed" } }, TenantId = (string?)null, IsBuiltIn = true })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, updated);
                FailureCauseRule changed = await JsonHelper.DeserializeAsync<FailureCauseRule>(updated).ConfigureAwait(false);
                AssertEqual("Prose result v2", changed.Name);
                AssertEqual(7, changed.Priority);
                AssertFalse(changed.Enabled, "disabled");
                AssertEqual(FailureCauseEnum.InstructionFollowing, changed.Cause);
                AssertEqual(StageDispositionEnum.Failed, changed.Match.Dispositions.Single(), "match replaced");
                AssertEqual(0, changed.Match.FailureKinds.Count, "old criteria gone");
                AssertEqual(userA.TenantId, changed.TenantId, "tenant is fixed");
                AssertFalse(changed.IsBuiltIn, "built-in flag is fixed");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PutAsync("/api/v1/failure-cause-rules/" + rule.Id, JsonHelper.ToJsonContent(new { Name = "Prose result v2", Cause = "Protocol", Match = new { } })).ConfigureAwait(false), "invalid update");
                AssertStatusCode(HttpStatusCode.NotFound, await a.PutAsync("/api/v1/failure-cause-rules/fcr_missing", JsonHelper.ToJsonContent(new { Name = "x", Cause = "Protocol", Match = new { FailureKinds = new[] { "Timeout" } } })).ConfigureAwait(false), "missing rule");

                AssertStatusCode(HttpStatusCode.NoContent, await a.DeleteAsync("/api/v1/failure-cause-rules/" + rule.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/failure-cause-rules/" + rule.Id).ConfigureAwait(false), "deleted");
                AssertStatusCode(HttpStatusCode.NotFound, await a.DeleteAsync("/api/v1/failure-cause-rules/" + rule.Id).ConfigureAwait(false), "deleted twice");
            }));

            cases.Add(CaseAsync("rest_global_and_builtin_rules", "Rules for every tenant and built-in rules are edited and deleted only by a global admin; seeding keeps an edited built-in and restores a deleted one", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(fx.AuthClient, "sgb-admin", true).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "sgb-user", false, tenantAdmin.TenantId).ConfigureAwait(false);
                using HttpClient ta = tenantAdmin.CreateClient(fx.BaseUrl);
                using HttpClient u = user.CreateClient(fx.BaseUrl);

                EnumerationResult<FailureCauseRule> all = await GetAsync<EnumerationResult<FailureCauseRule>>(u, "/api/v1/failure-cause-rules?pageSize=1000").ConfigureAwait(false);
                FailureCauseRule builtIn = all.Objects.First(r => r.IsBuiltIn && r.Name == "Interrupted or cancelled");
                object edit = new { Name = builtIn.Name, Description = builtIn.Description, Priority = builtIn.Priority, Enabled = false, Cause = builtIn.Cause.ToString(), Match = builtIn.Match };
                AssertStatusCode(HttpStatusCode.Forbidden, await u.PutAsync("/api/v1/failure-cause-rules/" + builtIn.Id, JsonHelper.ToJsonContent(edit)).ConfigureAwait(false), "a user cannot edit a built-in");
                AssertStatusCode(HttpStatusCode.Forbidden, await ta.PutAsync("/api/v1/failure-cause-rules/" + builtIn.Id, JsonHelper.ToJsonContent(edit)).ConfigureAwait(false), "a tenant admin cannot edit a built-in");
                AssertStatusCode(HttpStatusCode.Forbidden, await ta.DeleteAsync("/api/v1/failure-cause-rules/" + builtIn.Id).ConfigureAwait(false), "a tenant admin cannot delete a built-in");

                HttpResponseMessage globalCreated = await fx.AuthClient.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(new { Name = "Global rule " + Guid.NewGuid().ToString("N"), Cause = "Context", Match = new { TextPattern = "context window" } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, globalCreated);
                FailureCauseRule global = await JsonHelper.DeserializeAsync<FailureCauseRule>(globalCreated).ConfigureAwait(false);
                AssertNull(global.TenantId, "a global admin's rule without tenantId is for every tenant");
                AssertEqual(global.Id, (await GetAsync<FailureCauseRule>(u, "/api/v1/failure-cause-rules/" + global.Id).ConfigureAwait(false)).Id, "users read rules for every tenant");
                AssertStatusCode(HttpStatusCode.Forbidden, await u.PutAsync("/api/v1/failure-cause-rules/" + global.Id, JsonHelper.ToJsonContent(new { Name = "mine", Cause = "Context", Match = new { TextPattern = "x" } })).ConfigureAwait(false), "a non-admin cannot edit a rule for every tenant");
                AssertStatusCode(HttpStatusCode.Forbidden, await ta.DeleteAsync("/api/v1/failure-cause-rules/" + global.Id).ConfigureAwait(false), "a tenant admin cannot delete a rule for every tenant");
                AssertStatusCode(HttpStatusCode.NoContent, await fx.AuthClient.DeleteAsync("/api/v1/failure-cause-rules/" + global.Id).ConfigureAwait(false), "a global admin can");

                HttpResponseMessage edited = await fx.AuthClient.PutAsync("/api/v1/failure-cause-rules/" + builtIn.Id, JsonHelper.ToJsonContent(edit)).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, edited, "a global admin edits a built-in");
                FailureCauseRule disabled = await JsonHelper.DeserializeAsync<FailureCauseRule>(edited).ConfigureAwait(false);
                AssertFalse(disabled.Enabled, "disabled");
                AssertTrue(disabled.IsBuiltIn, "still built-in");
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    StageOutcomeService seeding = new StageOutcomeService(db, Quiet());
                    AssertEqual(0, await seeding.EnsureBuiltInRulesAsync().ConfigureAwait(false), "seeding adds nothing");
                    FailureCauseRule? kept = await db.FailureCauseRules.ReadAsync(builtIn.Id).ConfigureAwait(false);
                    AssertFalse(kept!.Enabled, "seeding keeps the edit");

                    AssertStatusCode(HttpStatusCode.NoContent, await fx.AuthClient.DeleteAsync("/api/v1/failure-cause-rules/" + builtIn.Id).ConfigureAwait(false), "a global admin deletes a built-in");
                    AssertStatusCode(HttpStatusCode.NotFound, await u.GetAsync("/api/v1/failure-cause-rules/" + builtIn.Id).ConfigureAwait(false), "gone");
                    AssertEqual(1, await seeding.EnsureBuiltInRulesAsync().ConfigureAwait(false), "seeding restores it");
                }

                EnumerationResult<FailureCauseRule> after = await GetAsync<EnumerationResult<FailureCauseRule>>(u, "/api/v1/failure-cause-rules?pageSize=1000").ConfigureAwait(false);
                FailureCauseRule restored = after.Objects.Single(r => r.IsBuiltIn && r.Name == builtIn.Name);
                AssertTrue(restored.Enabled, "restored as shipped");
                AssertEqual(builtIn.Priority, restored.Priority);
            }));

            cases.Add(CaseAsync("rest_draft_rule_from_outcome", "A rule drafted from a classified outcome carries its cause and signals, is not stored, and can be created as is", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "sdr-user", false).ConfigureAwait(false);
                StageOutcome outcome = await SeedAsync(fx, user.TenantId, o =>
                {
                    o.Persona = "Worker";
                    o.FailureKind = MissionFailureKindEnum.InvalidOutput;
                    o.RuntimeFailureKind = RuntimeFailureKindEnum.Clean;
                    o.StageCheckStatus = StageCheckStatusEnum.Skipped;
                    o.StageCheckReason = StageCheckReasonEnum.NoCommands;
                    o.Disposition = StageDispositionEnum.Relaunched;
                }).ConfigureAwait(false);
                StageOutcome clean = await SeedAsync(fx, user.TenantId, o => { o.Disposition = StageDispositionEnum.Completed; o.SuspectedCause = FailureCauseEnum.None; o.CauseSource = FailureCauseSourceEnum.None; }).ConfigureAwait(false);
                using HttpClient c = user.CreateClient(fx.BaseUrl);
                AssertStatusCode(HttpStatusCode.OK, await c.PutAsync("/api/v1/stage-outcomes/" + outcome.Id + "/cause", JsonHelper.ToJsonContent(new { Cause = "Protocol", Note = "prose instead of the block" })).ConfigureAwait(false));

                HttpResponseMessage drafted = await c.PostAsync("/api/v1/stage-outcomes/" + outcome.Id + "/rule", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, drafted);
                FailureCauseRule draft = await JsonHelper.DeserializeAsync<FailureCauseRule>(drafted).ConfigureAwait(false);
                AssertEqual(FailureCauseEnum.Protocol, draft.Cause);
                AssertEqual(user.TenantId, draft.TenantId);
                AssertEqual(5, draft.Priority);
                AssertFalse(draft.IsBuiltIn, "not built-in");
                AssertEqual("Worker", draft.Match.Personas.Single());
                AssertEqual(MissionFailureKindEnum.InvalidOutput, draft.Match.FailureKinds.Single());
                AssertEqual(RuntimeFailureKindEnum.Clean, draft.Match.RuntimeFailureKinds.Single());
                AssertEqual(StageCheckStatusEnum.Skipped, draft.Match.StageCheckStatuses.Single());
                AssertEqual(StageCheckReasonEnum.NoCommands, draft.Match.StageCheckReasons.Single());
                AssertEqual(StageDispositionEnum.Relaunched, draft.Match.Dispositions.Single());
                AssertContains(outcome.Id, draft.Name);
                AssertContains("prose instead of the block", draft.Description ?? "");
                AssertStatusCode(HttpStatusCode.NotFound, await c.GetAsync("/api/v1/failure-cause-rules/" + draft.Id).ConfigureAwait(false), "the draft is not stored");

                HttpResponseMessage created = await c.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(draft)).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created, "the draft is a valid rule");
                FailureCauseRule rule = await JsonHelper.DeserializeAsync<FailureCauseRule>(created).ConfigureAwait(false);
                AssertNotEqual(draft.Id, rule.Id, "a new id is assigned");

                AssertStatusCode(HttpStatusCode.Conflict, await c.PostAsync("/api/v1/stage-outcomes/" + clean.Id + "/rule", null).ConfigureAwait(false), "no cause to draft from");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/stage-outcomes/sto_missing/rule", null).ConfigureAwait(false), "missing outcome");
            }));

            cases.Add(CaseAsync("mcp_tools", "The MCP tools list, read, classify and reclassify outcomes and manage rules with the REST rules and typed errors", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "smc-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "smc-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser userB = await E2ETenantUser.CreateAsync(fx.AuthClient, "smc-user-b", false).ConfigureAwait(false);
                StageOutcome outcomeA = await SeedAsync(fx, adminA.TenantId, o => { Stale(o); o.Persona = "Worker"; }).ConfigureAwait(false);
                StageOutcome outcomeB = await SeedAsync(fx, userB.TenantId, o => Stale(o)).ConfigureAwait(false);

                using (McpToolClient a = Mcp(fx, userA))
                using (McpToolClient admin = Mcp(fx, adminA))
                using (McpToolClient b = Mcp(fx, userB))
                using (McpToolClient global = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }))
                {
                    await a.InitializeAsync().ConfigureAwait(false);
                    await admin.InitializeAsync().ConfigureAwait(false);
                    await b.InitializeAsync().ConfigureAwait(false);
                    await global.InitializeAsync().ConfigureAwait(false);

                    EnumerationResult<StageOutcome> listed = await OkAsync<EnumerationResult<StageOutcome>>(a, "list_stage_outcomes", new { tenantId = userB.TenantId }).ConfigureAwait(false);
                    AssertEqual(outcomeA.Id, listed.Objects.Single().Id, "own tenant only");
                    AssertEqual(25, listed.PageSize, "MCP default page size");
                    AssertEqual(outcomeA.Id, (await OkAsync<StageOutcome>(a, "get_stage_outcome", new { outcomeId = outcomeA.Id }).ConfigureAwait(false)).Id);
                    await ErrorAsync(a, "get_stage_outcome", new { outcomeId = outcomeB.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(b, "classify_stage_outcome", new { outcomeId = outcomeA.Id, cause = "Protocol" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(a, "list_stage_outcomes", new { suspectedCause = "Sloppy" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);

                    StageOutcome manual = await OkAsync<StageOutcome>(a, "classify_stage_outcome", new { outcomeId = outcomeA.Id, cause = "toolUse", note = "wrong tool" }).ConfigureAwait(false);
                    AssertEqual(FailureCauseEnum.ToolUse, manual.SuspectedCause);
                    AssertEqual(FailureCauseSourceEnum.Manual, manual.CauseSource);
                    AssertEqual(userA.UserId, manual.ClassifiedByUserId);
                    await ErrorAsync(a, "classify_stage_outcome", new { outcomeId = outcomeA.Id, cause = "None" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    await ErrorAsync(a, "classify_stage_outcome", new { outcomeId = outcomeA.Id, cause = "Sloppy" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);

                    FailureCauseRule draft = await OkAsync<FailureCauseRule>(a, "draft_failure_cause_rule", new { outcomeId = outcomeA.Id }).ConfigureAwait(false);
                    AssertEqual(FailureCauseEnum.ToolUse, draft.Cause, "drafted from the hand classification");
                    AssertEqual("Worker", draft.Match.Personas.Single());

                    StageOutcome ruled = await OkAsync<StageOutcome>(a, "classify_stage_outcome", new { outcomeId = outcomeA.Id }).ConfigureAwait(false);
                    AssertEqual(FailureCauseSourceEnum.Rule, ruled.CauseSource, "no cause hands back to the rules");
                    AssertEqual(FailureCauseEnum.NotTheModel, ruled.SuspectedCause);

                    await ErrorAsync(a, "reclassify_stage_outcomes", new { }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    StageOutcomeReclassifyResult run = await OkAsync<StageOutcomeReclassifyResult>(admin, "reclassify_stage_outcomes", new { tenantId = userB.TenantId }).ConfigureAwait(false);
                    AssertEqual(1, run.Examined, "a tenant admin's run covers only their tenant");
                    StageOutcomeReclassifyResult globalRun = await OkAsync<StageOutcomeReclassifyResult>(global, "reclassify_stage_outcomes", new { tenantId = userB.TenantId }).ConfigureAwait(false);
                    AssertEqual(1, globalRun.Changed, "a global admin reaches tenant B");

                    EnumerationResult<FailureCauseRule> rules = await OkAsync<EnumerationResult<FailureCauseRule>>(a, "list_failure_cause_rules", new { }).ConfigureAwait(false);
                    AssertTrue(rules.Objects.Count(r => r.IsBuiltIn) >= 13, "built-ins listed");
                    FailureCauseRule builtIn = rules.Objects.First(r => r.IsBuiltIn);
                    await ErrorAsync(a, "update_failure_cause_rule", new { ruleId = builtIn.Id, enabled = false }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    await ErrorAsync(admin, "delete_failure_cause_rule", new { ruleId = builtIn.Id }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);

                    await ErrorAsync(a, "create_failure_cause_rule", new { name = "No match", cause = "Protocol", match = new { } }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    // A missing required argument is refused by the MCP schema check before the tool runs.
                    McpToolCallResult noCause = await a.CallToolResultAsync("create_failure_cause_rule", JsonHelper.Serialize(new { name = "No cause", match = new { failureKinds = new[] { "Timeout" } } })).ConfigureAwait(false);
                    AssertTrue(noCause.IsError, "missing cause is an error: " + noCause.Text);
                    AssertContains("cause", noCause.Text);
                    await ErrorAsync(a, "create_failure_cause_rule", new { name = "Bad regex", cause = "Protocol", match = new { textPattern = "[" } }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    await ErrorAsync(a, "create_failure_cause_rule", new { name = "Bad kind", cause = "Protocol", match = new { failureKinds = new[] { "Sloppy" } } }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    FailureCauseRule created = await OkAsync<FailureCauseRule>(a, "create_failure_cause_rule", new { name = "Timeouts on Worker", cause = "Capability", priority = 6, match = new { personas = new[] { "Worker" }, failureKinds = new[] { "Timeout" } } }).ConfigureAwait(false);
                    AssertEqual(userA.TenantId, created.TenantId);
                    AssertEqual(6, created.Priority);
                    await ErrorAsync(a, "create_failure_cause_rule", new { name = "timeouts on worker", cause = "Capability", match = new { failureKinds = new[] { "Timeout" } } }, McpToolErrorCodeEnum.Conflict).ConfigureAwait(false);

                    FailureCauseRule partial = await OkAsync<FailureCauseRule>(a, "update_failure_cause_rule", new { ruleId = created.Id, enabled = false }).ConfigureAwait(false);
                    AssertFalse(partial.Enabled, "updated");
                    AssertEqual("Timeouts on Worker", partial.Name, "omitted fields kept");
                    AssertEqual(FailureCauseEnum.Capability, partial.Cause, "omitted cause kept");
                    AssertEqual(MissionFailureKindEnum.Timeout, partial.Match.FailureKinds.Single(), "omitted match kept");
                    AssertEqual(created.Id, (await OkAsync<FailureCauseRule>(a, "get_failure_cause_rule", new { ruleId = created.Id }).ConfigureAwait(false)).Id);
                    await ErrorAsync(b, "get_failure_cause_rule", new { ruleId = created.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(b, "delete_failure_cause_rule", new { ruleId = created.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);

                    McpToolCallResult deleted = await a.CallToolResultAsync("delete_failure_cause_rule", JsonHelper.Serialize(new { ruleId = created.Id })).ConfigureAwait(false);
                    AssertFalse(deleted.IsError, deleted.Text);
                    AssertContains("\"deleted\":true", deleted.Text.Replace(" ", ""));
                    await ErrorAsync(a, "get_failure_cause_rule", new { ruleId = created.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("rest_and_mcp_filter_contract", "Every outcome filter excludes a known nonmatching row over REST and MCP; date boundaries and paging are exact", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "sfc-user", false).ConfigureAwait(false);
                DateTime ended = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
                StageOutcome wanted = await SeedAsync(fx, user.TenantId, o =>
                {
                    o.MissionId = "msn_filter_wanted"; o.VoyageId = "vyg_filter_wanted"; o.VesselId = "vsl_filter_wanted";
                    o.CaptainId = "cpt_filter_wanted"; o.Persona = "Judge"; o.Model = "FilterModel";
                    o.SuspectedCause = FailureCauseEnum.Protocol; o.CauseSource = FailureCauseSourceEnum.Manual;
                    o.Disposition = StageDispositionEnum.Relaunched; o.IsEvaluation = true; o.EndedUtc = ended;
                }).ConfigureAwait(false);
                StageOutcome other = await SeedAsync(fx, user.TenantId, o =>
                {
                    o.Model = "OtherModel"; o.EndedUtc = ended.AddHours(-1);
                }).ConfigureAwait(false);
                using HttpClient rest = user.CreateClient(fx.BaseUrl);
                using McpToolClient mcp = Mcp(fx, user);
                await mcp.InitializeAsync().ConfigureAwait(false);
                EnumerationResult<StageOutcome> baseline = await GetAsync<EnumerationResult<StageOutcome>>(rest, "/api/v1/stage-outcomes").ConfigureAwait(false);
                AssertEqual(2, (int)baseline.TotalRecords, "both fixtures are visible before filters");
                AssertEqual(wanted.Id, baseline.Objects[0].Id, "newest first");
                AssertEqual(other.Id, baseline.Objects[1].Id, "older row present");
                foreach (KeyValuePair<string, string> filter in new Dictionary<string, string>
                {
                    ["missionId"] = wanted.MissionId, ["voyageId"] = wanted.VoyageId!, ["vesselId"] = wanted.VesselId!,
                    ["captainId"] = wanted.CaptainId!, ["persona"] = "judge", ["model"] = "filtermodel",
                    ["suspectedCause"] = "Protocol", ["causeSource"] = "Manual", ["disposition"] = "Relaunched"
                })
                {
                    EnumerationResult<StageOutcome> page = await GetAsync<EnumerationResult<StageOutcome>>(rest, "/api/v1/stage-outcomes?" + filter.Key + "=" + Uri.EscapeDataString(filter.Value)).ConfigureAwait(false);
                    AssertEqual(1, (int)page.TotalRecords, "REST " + filter.Key + " total");
                    AssertEqual(wanted.Id, page.Objects.Single().Id, "REST " + filter.Key);
                    EnumerationResult<StageOutcome> toolPage = await OkAsync<EnumerationResult<StageOutcome>>(mcp, "list_stage_outcomes", new Dictionary<string, object> { [filter.Key] = filter.Value }).ConfigureAwait(false);
                    AssertEqual(1, (int)toolPage.TotalRecords, "MCP " + filter.Key + " total");
                    AssertEqual(wanted.Id, toolPage.Objects.Single().Id, "MCP " + filter.Key);
                }
                foreach (bool evaluation in new[] { true, false })
                {
                    string expectedId = evaluation ? wanted.Id : other.Id;
                    EnumerationResult<StageOutcome> page = await GetAsync<EnumerationResult<StageOutcome>>(rest, "/api/v1/stage-outcomes?isEvaluation=" + evaluation.ToString().ToLowerInvariant()).ConfigureAwait(false);
                    AssertEqual(expectedId, page.Objects.Single().Id, "REST boolean filter");
                    EnumerationResult<StageOutcome> toolPage = await OkAsync<EnumerationResult<StageOutcome>>(mcp, "list_stage_outcomes", new { isEvaluation = evaluation }).ConfigureAwait(false);
                    AssertEqual(expectedId, toolPage.Objects.Single().Id, "MCP boolean filter");
                }
                EnumerationResult<StageOutcome> from = await GetAsync<EnumerationResult<StageOutcome>>(rest, "/api/v1/stage-outcomes?fromUtc=2026-10-15T12:00:00Z").ConfigureAwait(false);
                AssertEqual(wanted.Id, from.Objects.Single().Id, "inclusive from boundary");
                EnumerationResult<StageOutcome> to = await OkAsync<EnumerationResult<StageOutcome>>(mcp, "list_stage_outcomes", new { toUtc = "2026-10-15T12:00:00Z" }).ConfigureAwait(false);
                AssertEqual(other.Id, to.Objects.Single().Id, "exclusive to boundary");
                EnumerationResult<StageOutcome> second = await GetAsync<EnumerationResult<StageOutcome>>(rest, "/api/v1/stage-outcomes?pageNumber=2&pageSize=1").ConfigureAwait(false);
                AssertEqual(other.Id, second.Objects.Single().Id, "second page");
                AssertEqual(2, second.TotalPages);
                EnumerationResult<StageOutcome> empty = await OkAsync<EnumerationResult<StageOutcome>>(mcp, "list_stage_outcomes", new { missionId = wanted.MissionId, persona = "Worker" }).ConfigureAwait(false);
                AssertEqual(0, (int)empty.TotalRecords, "filters combine with AND");
                AssertEqual(0, empty.Objects.Count, "successful empty page, not an error");
            }));

            cases.Add(CaseAsync("rest_rule_deserialization_and_rejected_updates", "Rule defaults and explicit overrides survive deserialization; rejected updates leave the stored rule intact", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "srd-user", false).ConfigureAwait(false);
                using HttpClient client = user.CreateClient(fx.BaseUrl);

                // Omitted optional fields inherit the model defaults through the real HTTP deserializer.
                using HttpResponseMessage response = await client.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(new
                {
                    Name = "  Deserialized defaults  ",
                    Match = new { FailureKinds = new[] { "Timeout" } }
                })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, response);
                FailureCauseRule original = await JsonHelper.DeserializeAsync<FailureCauseRule>(response).ConfigureAwait(false);
                AssertEqual("Deserialized defaults", original.Name);
                AssertEqual(100, original.Priority, "omitted priority");
                AssertTrue(original.Enabled, "omitted enabled");
                AssertEqual(FailureCauseEnum.Unknown, original.Cause, "omitted cause inherits Unknown");
                AssertNull(original.Description, "omitted description");
                AssertNull(original.Match.TextPattern, "omitted regex");
                AssertEqual(0, original.Match.Personas.Count, "omitted criteria lists");
                AssertEqual(user.TenantId, original.TenantId, "omitted tenant is caller's tenant");

                // Null lists and blank optional strings must not become criteria or crash classification.
                using HttpResponseMessage update = await client.PutAsync("/api/v1/failure-cause-rules/" + original.Id, JsonHelper.ToJsonContent(new
                {
                    Name = original.Name, Priority = 0, Enabled = false, Cause = "Context", Description = "  ",
                    Match = new { FailureKinds = new[] { "Timeout" }, Personas = (string[]?)null, Runtimes = (string[]?)null, TextPattern = "  " }
                })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, update);
                FailureCauseRule stored = await GetAsync<FailureCauseRule>(client, "/api/v1/failure-cause-rules/" + original.Id).ConfigureAwait(false);
                AssertEqual(0, stored.Priority, "explicit zero overrides priority default");
                AssertFalse(stored.Enabled, "explicit false overrides enabled default");
                AssertEqual(FailureCauseEnum.Context, stored.Cause);
                AssertNull(stored.Description, "blank description normalized");
                AssertNull(stored.Match.TextPattern, "blank regex normalized");
                AssertEqual(0, stored.Match.Personas.Count, "null personas normalized");
                AssertEqual(0, stored.Match.Runtimes.Count, "null runtimes normalized");

                foreach (object invalid in new object[]
                {
                    new { Name = original.Name, Cause = "Protocol", Match = new { } },
                    new { Name = original.Name, Cause = "None", Match = new { FailureKinds = new[] { "Timeout" } } },
                    new { Name = original.Name, Cause = "Protocol", Match = new { TextPattern = "[" } },
                    new { Name = new string('n', 201), Cause = "Protocol", Match = new { FailureKinds = new[] { "Timeout" } } },
                    new { Name = original.Name, Cause = "Protocol", Match = (object?)null }
                })
                {
                    using HttpResponseMessage rejected = await client.PutAsync("/api/v1/failure-cause-rules/" + original.Id, JsonHelper.ToJsonContent(invalid)).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.BadRequest, rejected, "invalid replacement");
                    FailureCauseRule unchanged = await GetAsync<FailureCauseRule>(client, "/api/v1/failure-cause-rules/" + original.Id).ConfigureAwait(false);
                    AssertEqual(stored.Name, unchanged.Name, "rejected name not saved");
                    AssertEqual(stored.Cause, unchanged.Cause, "rejected cause not saved");
                    AssertEqual(stored.Priority, unchanged.Priority, "rejected defaults not saved");
                    AssertFalse(unchanged.Enabled, "rejected enabled default not saved");
                    AssertEqual(MissionFailureKindEnum.Timeout, unchanged.Match.FailureKinds.Single(), "rejected match not saved");
                    AssertNull(unchanged.Match.TextPattern, "bad regex not saved");
                }

                await AssertBadRuleAsync(client, new { Name = "Null match", Cause = "Protocol", Match = (object?)null }, "criterion").ConfigureAwait(false);
                foreach (string pattern in new[] { "", "  " })
                    await AssertBadRuleAsync(client, new { Name = "Blank pattern", Cause = "Protocol", Match = new { TextPattern = pattern } }, "criterion").ConfigureAwait(false);
                foreach (string name in new[] { "", "  " })
                    await AssertBadRuleAsync(client, new { Name = name, Cause = "Protocol", Match = new { FailureKinds = new[] { "Timeout" } } }, "Name").ConfigureAwait(false);

                EnumerationResult<FailureCauseRule> rules = await GetAsync<EnumerationResult<FailureCauseRule>>(client, "/api/v1/failure-cause-rules?pageSize=1000").ConfigureAwait(false);
                AssertEqual(1, rules.Objects.Count(r => r.TenantId == user.TenantId), "invalid creates stored nothing");
                AssertStatusCode(HttpStatusCode.NoContent, await client.DeleteAsync("/api/v1/failure-cause-rules/" + original.Id).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("mcp_validation_and_missing_ids", "MCP rejects every required invalid rule case and missing IDs without mutating stored rules", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "smv-user", false).ConfigureAwait(false);
                using McpToolClient client = Mcp(fx, user);
                await client.InitializeAsync().ConfigureAwait(false);
                FailureCauseRule original = await OkAsync<FailureCauseRule>(client, "create_failure_cause_rule", new
                {
                    name = "MCP validation anchor", cause = "Protocol", match = new { failureKinds = new[] { "Timeout" } }
                }).ConfigureAwait(false);
                AssertEqual(100, original.Priority, "MCP omitted priority");
                AssertTrue(original.Enabled, "MCP omitted enabled");

                foreach (object invalid in new object[]
                {
                    new { name = "No criteria", cause = "Protocol", match = new { } },
                    new { name = "None cause", cause = "None", match = new { failureKinds = new[] { "Timeout" } } },
                    new { name = "Bad regex", cause = "Protocol", match = new { textPattern = "[" } },
                    new { name = new string('n', 201), cause = "Protocol", match = new { failureKinds = new[] { "Timeout" } } }
                })
                    await ErrorAsync(client, "create_failure_cause_rule", invalid, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);

                await ErrorAsync(client, "update_failure_cause_rule", new { ruleId = original.Id, match = new { } }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                await ErrorAsync(client, "update_failure_cause_rule", new { ruleId = original.Id, cause = "None" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                FailureCauseRule unchanged = await OkAsync<FailureCauseRule>(client, "get_failure_cause_rule", new { ruleId = original.Id }).ConfigureAwait(false);
                AssertEqual(original.Name, unchanged.Name);
                AssertEqual(original.Cause, unchanged.Cause, "rejected MCP cause not saved");
                AssertEqual(MissionFailureKindEnum.Timeout, unchanged.Match.FailureKinds.Single(), "rejected MCP match not saved");

                FailureCauseRule overridden = await OkAsync<FailureCauseRule>(client, "update_failure_cause_rule", new { ruleId = original.Id, priority = 0, enabled = false }).ConfigureAwait(false);
                AssertEqual(0, overridden.Priority, "MCP explicit zero");
                AssertFalse(overridden.Enabled, "MCP explicit false");
                AssertEqual(original.Cause, overridden.Cause, "MCP omitted cause preserved");
                foreach (string tool in new[] { "get_stage_outcome", "classify_stage_outcome", "draft_failure_cause_rule" })
                    await ErrorAsync(client, tool, new { outcomeId = "sto_missing", cause = "Protocol" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                foreach (string tool in new[] { "get_failure_cause_rule", "update_failure_cause_rule", "delete_failure_cause_rule" })
                    await ErrorAsync(client, tool, new { ruleId = "fcr_missing", enabled = false }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);

                EnumerationResult<FailureCauseRule> rules = await OkAsync<EnumerationResult<FailureCauseRule>>(client, "list_failure_cause_rules", new { pageSize = 1000 }).ConfigureAwait(false);
                AssertEqual(1, rules.Objects.Count(r => r.TenantId == user.TenantId), "invalid MCP creates stored nothing");
                McpToolCallResult deleted = await client.CallToolResultAsync("delete_failure_cause_rule", JsonHelper.Serialize(new { ruleId = original.Id })).ConfigureAwait(false);
                AssertFalse(deleted.IsError, deleted.Text);
                await ErrorAsync(client, "get_failure_cause_rule", new { ruleId = original.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Stage outcomes and failure-cause rules API", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void Stale(StageOutcome outcome)
        {
            outcome.FailureKind = MissionFailureKindEnum.Infra;
            outcome.Disposition = StageDispositionEnum.Failed;
            outcome.SuspectedCause = FailureCauseEnum.Unknown;
            outcome.CauseSource = FailureCauseSourceEnum.None;
        }

        private static async Task<StageOutcome> SeedAsync(E2EServerFixture fx, string tenantId, Action<StageOutcome> configure)
        {
            StageOutcome outcome = new StageOutcome
            {
                TenantId = tenantId,
                MissionId = "msn_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                CaptainId = "cpt_seeded",
                Persona = "Worker",
                Disposition = StageDispositionEnum.Failed,
                FailureKind = MissionFailureKindEnum.Timeout,
                SuspectedCause = FailureCauseEnum.Unknown,
                CauseSource = FailureCauseSourceEnum.Rule,
                EndedUtc = DateTime.UtcNow
            };
            configure(outcome);
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                return await db.StageOutcomes.CreateAsync(outcome).ConfigureAwait(false);
            }
        }

        private static async Task<T> GetAsync<T>(HttpClient client, string path)
        {
            HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "GET " + path);
            return await JsonHelper.DeserializeAsync<T>(response).ConfigureAwait(false);
        }

        private static async Task AssertBadRuleAsync(HttpClient client, object body, string expected)
        {
            HttpResponseMessage response = await client.PostAsync("/api/v1/failure-cause-rules", JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.BadRequest, response, "invalid rule (" + expected + "): " + text);
            AssertContains(expected, text);
        }

        private static McpToolClient Mcp(E2EServerFixture fx, E2ETenantUser user)
        {
            Dictionary<string, string> headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + user.BearerToken };
            return new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null, headers);
        }

        private static async Task<T> OkAsync<T>(McpToolClient client, string tool, object args)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertFalse(result.IsError, tool + ": " + result.Text);
            AssertFalse(result.Text.Contains("\"errorCode\"", StringComparison.OrdinalIgnoreCase), tool + " returned an error: " + result.Text);
            return JsonHelper.Deserialize<T>(result.Text);
        }

        private static async Task ErrorAsync(McpToolClient client, string tool, object args, McpToolErrorCodeEnum expected)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertTrue(result.Text.TrimStart().StartsWith("{", StringComparison.Ordinal), tool + " returned no McpToolError: " + result.Text);
            McpToolError error = JsonHelper.Deserialize<McpToolError>(result.Text);
            AssertEqual(expected, error.ErrorCode, tool + ": " + result.Text);
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, Quiet());
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private static LoggingModule Quiet()
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            return quiet;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd });
        }

        #endregion
    }
}
