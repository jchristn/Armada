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
    /// End-to-end Evaluate (probe-only Quick model probes) through the real server with canned probe answers (no model
    /// runs): the REST and MCP contract (201 queued run, a finished run with one result per probe, list filters and
    /// paging), Full Evaluate parameter validation (unknown pack, disallowed persona, non-admin Full of an endpoint) and
    /// cancel, missing ids, authorization (a non-admin cannot evaluate a
    /// captain), tenant isolation (another tenant's run is not found; a global admin sees both), the could-not-run path
    /// (a runtime that cannot start gives CouldNotRun results in a Completed run, never Failed), and that an Evaluate run
    /// creates no stage outcome.
    /// </summary>
    public sealed class ModelProbeApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ModelProbes";

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
                ModelProbeRunQuery empty = ModelProbeRoutes.ParseQuery(key => null);
                AssertNull(empty.TenantId, "tenant");
                AssertNull(empty.CaptainId, "captain");
                AssertNull(empty.ModelEndpointId, "endpoint");
                AssertNull(empty.Status, "status");
                AssertEqual(1, empty.PageNumber, "page number");
                AssertEqual(100, empty.PageSize, "page size");

                Dictionary<string, string?> full = new Dictionary<string, string?>
                {
                    ["captainId"] = " cpt_1 ", ["modelEndpointId"] = "  ", ["status"] = "completed", ["pageNumber"] = "2", ["pageSize"] = "5"
                };
                ModelProbeRunQuery parsed = ModelProbeRoutes.ParseQuery(key => full.TryGetValue(key, out string? v) ? v : null);
                AssertEqual("cpt_1", parsed.CaptainId, "trimmed");
                AssertNull(parsed.ModelEndpointId, "blank is null");
                AssertEqual(ModelProbeRunStatusEnum.Completed, parsed.Status, "status case-insensitive");
                AssertEqual(2, parsed.PageNumber, "page number");
                AssertEqual(5, parsed.PageSize, "page size");

                AssertThrows<ArgumentException>(() => ModelProbeRoutes.ParseQuery(key => key == "status" ? "Done" : null), "bad status");
                AssertThrows<ArgumentException>(() => ModelProbeRoutes.ParseQuery(key => key == "pageSize" ? "x" : null), "bad page size");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("rest_contract", "REST: evaluate returns 201 with a queued run; get returns six results; list filters and pages; no stage outcome is created", async () =>
            {
                E2EServerFixture fx = await AcquireAsync(new CannedModelProbeExecutor()).ConfigureAwait(false);
                HttpClient c = fx.AuthClient;
                string tenantId = (await E2ETenantUser.CreateAsync(fx.AuthClient, "mpr-rest", true).ConfigureAwait(false)).TenantId;
                Captain captain = await SeedCaptainAsync(fx, tenantId).ConfigureAwait(false);
                long outcomesBefore = await StageOutcomeCountAsync(c).ConfigureAwait(false);

                HttpResponseMessage created = await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = "Quick" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created, "evaluate captain");
                ModelProbeRun queued = await JsonHelper.DeserializeAsync<ModelProbeRun>(created).ConfigureAwait(false);
                AssertFalse(String.IsNullOrEmpty(queued.Id), "id");
                AssertEqual(captain.Id, queued.CaptainId, "captain");
                AssertEqual(tenantId, queued.TenantId, "the captain's tenant");
                AssertEqual(ModelProbeTargetEnum.Captain, queued.TargetKind, "target");
                AssertEqual(ModelProbeDepthEnum.Quick, queued.Depth, "depth");
                AssertTrue(queued.IsEvaluation, "tagged as evaluation");
                AssertFalse(String.IsNullOrEmpty(queued.ProbeSetVersion), "probe-set version");
                AssertEqual("ClaudeCode", queued.Runtime, "runtime");

                HttpResponseMessage noBody = await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, noBody, "no body defaults to Quick");
                ModelProbeRun second = await JsonHelper.DeserializeAsync<ModelProbeRun>(noBody).ConfigureAwait(false);

                ModelProbeRun done = await WaitRestAsync(c, queued.Id).ConfigureAwait(false);
                AssertEqual(ModelProbeRunStatusEnum.Completed, done.Status, "completed: " + done.FailureReason);
                AssertEqual(6, done.Results.Count, "one result per probe");
                AssertEqual(6, done.Results.Select(r => r.Probe).Distinct().Count(), "six distinct probes");
                AssertTrue(done.Results.All(r => r.RunId == done.Id), "results belong to the run");
                AssertEqual(6, done.PassedCount + done.FailedCount + done.CouldNotRunCount, "counts add up");
                await WaitRestAsync(c, second.Id).ConfigureAwait(false);

                EnumerationResult<ModelProbeRun> page = await GetAsync<EnumerationResult<ModelProbeRun>>(c, "/api/v1/model-probe-runs?captainId=" + captain.Id + "&pageSize=1").ConfigureAwait(false);
                AssertEqual(2, (int)page.TotalRecords, "two runs for the captain");
                AssertEqual(1, page.Objects.Count, "page of one");
                AssertEqual(2, page.TotalPages, "two pages");
                EnumerationResult<ModelProbeRun> completed = await GetAsync<EnumerationResult<ModelProbeRun>>(c, "/api/v1/model-probe-runs?captainId=" + captain.Id + "&status=Completed").ConfigureAwait(false);
                AssertEqual(2, (int)completed.TotalRecords, "status filter");
                EnumerationResult<ModelProbeRun> none = await GetAsync<EnumerationResult<ModelProbeRun>>(c, "/api/v1/model-probe-runs?captainId=" + captain.Id + "&status=Failed").ConfigureAwait(false);
                AssertEqual(0, (int)none.TotalRecords, "nothing failed");
                AssertStatusCode(HttpStatusCode.BadRequest, await c.GetAsync("/api/v1/model-probe-runs?status=Done").ConfigureAwait(false), "bad status");

                HttpResponseMessage full = await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = "Full", packs = new[] { "no-such-pack" } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, full, "Full with an unknown pack");
                AssertContains("no-such-pack", await full.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.BadRequest, await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = "Full", runCount = 11 })).ConfigureAwait(false), "Full run count out of range");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = "Full", evaluatorCaptainId = "cpt_missing" })).ConfigureAwait(false), "Full with an unknown evaluator");
                ModelProbeRun cancelledDone = await JsonHelper.DeserializeAsync<ModelProbeRun>(await c.PostAsync("/api/v1/model-probe-runs/" + queued.Id + "/cancel", null).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(ModelProbeRunStatusEnum.Completed, cancelledDone.Status, "cancelling a finished run leaves it unchanged");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/model-probe-runs/mpr_missing/cancel", null).ConfigureAwait(false), "cancel a missing run");
                AssertStatusCode(HttpStatusCode.BadRequest, await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = "Deep" })).ConfigureAwait(false), "unknown depth");
                AssertStatusCode(HttpStatusCode.BadRequest, await c.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = 7 })).ConfigureAwait(false), "undefined depth number");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/captains/cpt_missing/evaluate", null).ConfigureAwait(false), "missing captain");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/model-endpoints/mep_missing/evaluate", null).ConfigureAwait(false), "missing endpoint");
                AssertStatusCode(HttpStatusCode.NotFound, await c.GetAsync("/api/v1/model-probe-runs/mpr_missing").ConfigureAwait(false), "missing run");

                AssertEqual(outcomesBefore, await StageOutcomeCountAsync(c).ConfigureAwait(false), "Evaluate runs create no stage outcome");
            }));

            cases.Add(CaseAsync("endpoint_evaluate", "REST: evaluating a model endpoint runs a transient API-endpoint captain and records the endpoint", async () =>
            {
                CannedModelProbeExecutor executor = new CannedModelProbeExecutor();
                E2EServerFixture fx = await AcquireAsync(executor).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpr-ep-user", false).ConfigureAwait(false);
                ModelEndpoint endpoint = await SeedEndpointAsync(fx, user.TenantId, user.UserId).ConfigureAwait(false);
                using HttpClient c = user.CreateClient(fx.BaseUrl);

                HttpResponseMessage created = await c.PostAsync("/api/v1/model-endpoints/" + endpoint.Id + "/evaluate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created, "a user who can read the endpoint may evaluate it");
                ModelProbeRun queued = await JsonHelper.DeserializeAsync<ModelProbeRun>(created).ConfigureAwait(false);
                AssertEqual(ModelProbeTargetEnum.ModelEndpoint, queued.TargetKind, "target");
                AssertEqual(endpoint.Id, queued.ModelEndpointId, "endpoint");
                AssertNull(queued.CaptainId, "no saved captain");
                AssertEqual("ApiEndpoint", queued.Runtime, "runtime");
                AssertEqual("canned-model", queued.Model, "model");

                ModelProbeRun done = await WaitRestAsync(c, queued.Id).ConfigureAwait(false);
                AssertEqual(ModelProbeRunStatusEnum.Completed, done.Status, "completed: " + done.FailureReason);
                AssertEqual(6, done.Results.Count, "results");
                AssertTrue(executor.Requests.Count > 0, "probes ran through the executor");
                AssertTrue(executor.Requests.All(r => r.Captain.Runtime == AgentRuntimeEnum.ApiEndpoint && r.Captain.ModelEndpointId == endpoint.Id), "transient endpoint captain");

                // Full dispatches battery missions and spends the evaluation budget: a non-admin who can read the endpoint is
                // refused, and cannot cancel runs either.
                HttpResponseMessage full = await c.PostAsync("/api/v1/model-endpoints/" + endpoint.Id + "/evaluate", JsonHelper.ToJsonContent(new { depth = "Full" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Forbidden, full, "Full by a non-admin");
                AssertStatusCode(HttpStatusCode.Forbidden, await c.PostAsync("/api/v1/model-probe-runs/" + queued.Id + "/cancel", null).ConfigureAwait(false), "cancel by a non-admin");
            }));

            cases.Add(CaseAsync("authorization_isolation", "A non-admin cannot evaluate a captain; another tenant's captain, endpoint and run are not found; a global admin sees both tenants", async () =>
            {
                E2EServerFixture fx = await AcquireAsync(new CannedModelProbeExecutor()).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpr-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpr-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpr-admin-b", true).ConfigureAwait(false);
                Captain captainA = await SeedCaptainAsync(fx, adminA.TenantId).ConfigureAwait(false);
                Captain captainB = await SeedCaptainAsync(fx, adminB.TenantId).ConfigureAwait(false);
                ModelEndpoint endpointA = await SeedEndpointAsync(fx, adminA.TenantId, userA.UserId).ConfigureAwait(false);
                using HttpClient a = adminA.CreateClient(fx.BaseUrl);
                using HttpClient ua = userA.CreateClient(fx.BaseUrl);
                using HttpClient b = adminB.CreateClient(fx.BaseUrl);

                AssertStatusCode(HttpStatusCode.Forbidden, await ua.PostAsync("/api/v1/captains/" + captainA.Id + "/evaluate", null).ConfigureAwait(false), "non-admin cannot evaluate a captain");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PostAsync("/api/v1/captains/" + captainA.Id + "/evaluate", null).ConfigureAwait(false), "another tenant's captain");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PostAsync("/api/v1/model-endpoints/" + endpointA.Id + "/evaluate", null).ConfigureAwait(false), "another tenant's endpoint");

                HttpResponseMessage createdA = await a.PostAsync("/api/v1/captains/" + captainA.Id + "/evaluate", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, createdA, "tenant admin evaluates own captain");
                ModelProbeRun runA = await JsonHelper.DeserializeAsync<ModelProbeRun>(createdA).ConfigureAwait(false);
                AssertEqual(adminA.TenantId, runA.TenantId, "tenant A run");
                ModelProbeRun runB = await JsonHelper.DeserializeAsync<ModelProbeRun>(await b.PostAsync("/api/v1/captains/" + captainB.Id + "/evaluate", null).ConfigureAwait(false)).ConfigureAwait(false);
                await WaitRestAsync(a, runA.Id).ConfigureAwait(false);
                await WaitRestAsync(b, runB.Id).ConfigureAwait(false);

                AssertStatusCode(HttpStatusCode.OK, await ua.GetAsync("/api/v1/model-probe-runs/" + runA.Id).ConfigureAwait(false), "same-tenant user reads the run");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync("/api/v1/model-probe-runs/" + runA.Id).ConfigureAwait(false), "tenant B cannot read tenant A's run");
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/model-probe-runs/" + runB.Id).ConfigureAwait(false), "tenant A cannot read tenant B's run");

                EnumerationResult<ModelProbeRun> listB = await GetAsync<EnumerationResult<ModelProbeRun>>(b, "/api/v1/model-probe-runs?pageSize=1000").ConfigureAwait(false);
                AssertTrue(listB.Objects.All(r => r.TenantId == adminB.TenantId), "tenant B lists only its runs");
                AssertTrue(listB.Objects.Any(r => r.Id == runB.Id), "tenant B sees its run");
                EnumerationResult<ModelProbeRun> filtered = await GetAsync<EnumerationResult<ModelProbeRun>>(b, "/api/v1/model-probe-runs?captainId=" + captainA.Id).ConfigureAwait(false);
                AssertEqual(0, (int)filtered.TotalRecords, "tenant B filtering by tenant A's captain sees nothing");

                AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.GetAsync("/api/v1/model-probe-runs/" + runA.Id).ConfigureAwait(false), "global admin reads A");
                AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.GetAsync("/api/v1/model-probe-runs/" + runB.Id).ConfigureAwait(false), "global admin reads B");
                EnumerationResult<ModelProbeRun> all = await GetAsync<EnumerationResult<ModelProbeRun>>(fx.AuthClient, "/api/v1/model-probe-runs?pageSize=1000").ConfigureAwait(false);
                AssertTrue(all.Objects.Any(r => r.Id == runA.Id) && all.Objects.Any(r => r.Id == runB.Id), "global admin lists both tenants");

                AssertStatusCode(HttpStatusCode.Unauthorized, await fx.UnauthClient.GetAsync("/api/v1/model-probe-runs").ConfigureAwait(false), "anonymous");
            }));

            cases.Add(CaseAsync("mcp_tools", "MCP: evaluate_captain, get_model_probe_run, list_model_probe_runs and cancel_model_probe_run apply the REST rules with typed errors", async () =>
            {
                E2EServerFixture fx = await AcquireAsync(new CannedModelProbeExecutor()).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpm-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpm-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "mpm-admin-b", true).ConfigureAwait(false);
                Captain captainA = await SeedCaptainAsync(fx, adminA.TenantId).ConfigureAwait(false);
                Captain captainB = await SeedCaptainAsync(fx, adminB.TenantId).ConfigureAwait(false);
                ModelEndpoint endpointA = await SeedEndpointAsync(fx, adminA.TenantId, userA.UserId).ConfigureAwait(false);

                using (McpToolClient admin = Mcp(fx, adminA))
                using (McpToolClient user = Mcp(fx, userA))
                using (McpToolClient b = Mcp(fx, adminB))
                using (McpToolClient global = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }))
                {
                    await admin.InitializeAsync().ConfigureAwait(false);
                    await user.InitializeAsync().ConfigureAwait(false);
                    await b.InitializeAsync().ConfigureAwait(false);
                    await global.InitializeAsync().ConfigureAwait(false);

                    ModelProbeRun run = await OkAsync<ModelProbeRun>(admin, "evaluate_captain", new { captainId = captainA.Id }).ConfigureAwait(false);
                    AssertEqual(captainA.Id, run.CaptainId, "captain run");
                    AssertTrue(run.Status == ModelProbeRunStatusEnum.Queued || run.Status == ModelProbeRunStatusEnum.Running, "started: " + run.Status);
                    ModelProbeRun endpointRun = await OkAsync<ModelProbeRun>(user, "evaluate_captain", new { modelEndpointId = endpointA.Id, depth = "quick" }).ConfigureAwait(false);
                    AssertEqual(endpointA.Id, endpointRun.ModelEndpointId, "endpoint run");

                    await ErrorAsync(admin, "evaluate_captain", new { }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    await ErrorAsync(admin, "evaluate_captain", new { captainId = captainA.Id, modelEndpointId = endpointA.Id }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    await ErrorAsync(admin, "evaluate_captain", new { captainId = captainA.Id, depth = "Deep" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    McpToolCallResult full = await admin.CallToolResultAsync("evaluate_captain", JsonHelper.Serialize(new { captainId = captainA.Id, depth = "Full", packs = new[] { "no-such-pack" } })).ConfigureAwait(false);
                    AssertEqual(McpToolErrorCodeEnum.InvalidArgument, JsonHelper.Deserialize<McpToolError>(full.Text).ErrorCode, "Full with an unknown pack: " + full.Text);
                    AssertContains("no-such-pack", full.Text);
                    await ErrorAsync(admin, "evaluate_captain", new { captainId = captainA.Id, depth = "Full", evaluatorCaptainId = "cpt_missing" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(user, "evaluate_captain", new { modelEndpointId = endpointA.Id, depth = "Full" }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    await ErrorAsync(user, "evaluate_captain", new { captainId = captainA.Id }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    await ErrorAsync(b, "evaluate_captain", new { captainId = captainA.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(b, "evaluate_captain", new { modelEndpointId = endpointA.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    // Full: another tenant's candidate is not found, and neither is another tenant's evaluator.
                    await ErrorAsync(b, "evaluate_captain", new { captainId = captainA.Id, depth = "Full" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(b, "evaluate_captain", new { captainId = captainB.Id, depth = "Full", evaluatorCaptainId = captainA.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(admin, "evaluate_captain", new { captainId = "cpt_missing" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);

                    await WaitRestAsync(fx.AuthClient, run.Id).ConfigureAwait(false);
                    await WaitRestAsync(fx.AuthClient, endpointRun.Id).ConfigureAwait(false);
                    ModelProbeRun read = await OkAsync<ModelProbeRun>(user, "get_model_probe_run", new { id = run.Id }).ConfigureAwait(false);
                    AssertEqual(ModelProbeRunStatusEnum.Completed, read.Status, "completed");
                    AssertEqual(6, read.Results.Count, "results");
                    await ErrorAsync(b, "get_model_probe_run", new { id = run.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(admin, "get_model_probe_run", new { id = "mpr_missing" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    AssertEqual(run.Id, (await OkAsync<ModelProbeRun>(global, "get_model_probe_run", new { id = run.Id }).ConfigureAwait(false)).Id, "global admin reads any tenant");

                    EnumerationResult<ModelProbeRun> page = await OkAsync<EnumerationResult<ModelProbeRun>>(admin, "list_model_probe_runs", new { pageSize = 1 }).ConfigureAwait(false);
                    AssertEqual(2, (int)page.TotalRecords, "tenant A has two runs");
                    AssertEqual(1, page.Objects.Count, "page of one");
                    EnumerationResult<ModelProbeRun> byEndpoint = await OkAsync<EnumerationResult<ModelProbeRun>>(admin, "list_model_probe_runs", new { modelEndpointId = endpointA.Id, status = "Completed" }).ConfigureAwait(false);
                    AssertEqual(endpointRun.Id, byEndpoint.Objects.Single().Id, "endpoint filter");
                    EnumerationResult<ModelProbeRun> defaults = await OkAsync<EnumerationResult<ModelProbeRun>>(admin, "list_model_probe_runs", new { }).ConfigureAwait(false);
                    AssertEqual(25, defaults.PageSize, "MCP default page size");
                    EnumerationResult<ModelProbeRun> leaked = await OkAsync<EnumerationResult<ModelProbeRun>>(b, "list_model_probe_runs", new { captainId = captainA.Id }).ConfigureAwait(false);
                    AssertEqual(0, (int)leaked.TotalRecords, "tenant B sees none of tenant A's runs");
                    await ErrorAsync(admin, "list_model_probe_runs", new { status = "Done" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);

                    ModelProbeRun unchanged = await OkAsync<ModelProbeRun>(admin, "cancel_model_probe_run", new { id = run.Id }).ConfigureAwait(false);
                    AssertEqual(ModelProbeRunStatusEnum.Completed, unchanged.Status, "cancelling a finished run leaves it unchanged");
                    await ErrorAsync(b, "cancel_model_probe_run", new { id = run.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(admin, "cancel_model_probe_run", new { id = "mpr_missing" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("could_not_run", "A runtime that cannot start records CouldNotRun with the reason for every probe; the run completes and is never Failed", async () =>
            {
                CannedModelProbeExecutor executor = new CannedModelProbeExecutor();
                executor.Answer = request => ModelProbeExchange.NotStarted("The ClaudeCode CLI ('claude') is not installed on the Admiral host.");
                E2EServerFixture fx = await AcquireAsync(executor).ConfigureAwait(false);
                string tenantId = (await E2ETenantUser.CreateAsync(fx.AuthClient, "mpr-cnr", true).ConfigureAwait(false)).TenantId;
                Captain captain = await SeedCaptainAsync(fx, tenantId).ConfigureAwait(false);

                HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/captains/" + captain.Id + "/evaluate", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created, "evaluate");
                ModelProbeRun queued = await JsonHelper.DeserializeAsync<ModelProbeRun>(created).ConfigureAwait(false);
                ModelProbeRun done = await WaitRestAsync(fx.AuthClient, queued.Id).ConfigureAwait(false);

                AssertEqual(ModelProbeRunStatusEnum.Completed, done.Status, "completed, not failed: " + done.FailureReason);
                AssertNull(done.FailureReason, "no failure reason");
                AssertEqual(6, done.Results.Count, "every probe recorded");
                AssertEqual(6, done.CouldNotRunCount, "all could not run");
                AssertEqual(0, done.FailedCount, "none failed");
                AssertEqual(0, done.PassedCount, "none passed");
                foreach (ModelProbeResult result in done.Results)
                {
                    AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, result.Outcome, result.Probe + " outcome");
                    AssertFalse(String.IsNullOrWhiteSpace(result.Reason), result.Probe + " has a reason");
                }

                AssertTrue(done.Results.Any(r => (r.Reason ?? "").Contains("not installed", StringComparison.Ordinal)), "the launch failure is the reason");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "E2E Evaluate (model probe runs) API", cases: cases);
        }

        #endregion

        #region Private-Methods

        private async Task<E2EServerFixture> AcquireAsync(CannedModelProbeExecutor executor)
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
            fx.Server.ModelProbeExecutorOverride = executor;
            return fx;
        }

        private static async Task<Captain> SeedCaptainAsync(E2EServerFixture fx, string tenantId)
        {
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                Captain captain = new Captain { Name = "probe-" + Guid.NewGuid().ToString("N").Substring(0, 8), Runtime = AgentRuntimeEnum.ClaudeCode, TenantId = tenantId };
                return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
            }
        }

        private static async Task<ModelEndpoint> SeedEndpointAsync(E2EServerFixture fx, string tenantId, string userId)
        {
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "probe-ep-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    BaseUrl = "http://127.0.0.1:9",
                    Model = "canned-model",
                    TenantId = tenantId,
                    UserId = userId
                };
                return await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
            }
        }

        private static async Task<long> StageOutcomeCountAsync(HttpClient c)
        {
            EnumerationResult<StageOutcome> page = await GetAsync<EnumerationResult<StageOutcome>>(c, "/api/v1/stage-outcomes?pageSize=1").ConfigureAwait(false);
            return page.TotalRecords;
        }

        private static async Task<T> GetAsync<T>(HttpClient c, string path)
        {
            HttpResponseMessage response = await c.GetAsync(path).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, path);
            return await JsonHelper.DeserializeAsync<T>(response).ConfigureAwait(false);
        }

        private static async Task<ModelProbeRun> WaitRestAsync(HttpClient c, string runId)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                ModelProbeRun run = await GetAsync<ModelProbeRun>(c, "/api/v1/model-probe-runs/" + runId).ConfigureAwait(false);
                if (run.Status == ModelProbeRunStatusEnum.Completed || run.Status == ModelProbeRunStatusEnum.Failed) return run;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("run " + runId + " still " + run.Status);
                await Task.Delay(100).ConfigureAwait(false);
            }
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
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
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
