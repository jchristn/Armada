namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Runtimes.Mcp;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for the fleet action REST routes: built-ins are listed, a non-admin cannot create or
    /// run a Command action (403) but can enumerate, an unknown template variable is a 400, a cross-tenant vessel in
    /// vesselIds rejects the whole run (404), a run is accepted with 202 and reaches a terminal status, another
    /// tenant cannot read the run (404), the target endpoint returns output fields, and cancelling a finished run is
    /// a 409. Cases run in order against this suite's own server and share state through instance fields.
    /// </summary>
    public sealed class FleetActionApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.FleetActionApi";

        private HttpClient? _AdminA;
        private HttpClient? _RegularA;
        private HttpClient? _AdminB;
        private string _VesselAId = String.Empty;
        private string _VesselBId = String.Empty;
        private string _RunId = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_tenants_and_vessels", "Create two tenants, users, and one vessel each", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                TenantMetadata tenantA = await CreateTenantAsync(fx.AuthClient, "fa-a").ConfigureAwait(false);
                TenantMetadata tenantB = await CreateTenantAsync(fx.AuthClient, "fa-b").ConfigureAwait(false);
                _AdminA = await CreateUserClientAsync(fx, tenantA.Id, "admin-a", true).ConfigureAwait(false);
                _RegularA = await CreateUserClientAsync(fx, tenantA.Id, "user-a", false).ConfigureAwait(false);
                _AdminB = await CreateUserClientAsync(fx, tenantB.Id, "admin-b", true).ConfigureAwait(false);
                _VesselAId = await CreateVesselAsync(_AdminA, "fa-vessel-a").ConfigureAwait(false);
                _VesselBId = await CreateVesselAsync(_AdminB, "fa-vessel-b").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("enumerate_lists_builtins", "Enumerate lists every seeded built-in, also for a regular user", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _RegularA!.PostAsync("/api/v1/fleet-actions/enumerate", JsonHelper.ToJsonContent(new { pageSize = 50 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                EnumerationResult<FleetAction> result = await JsonHelper.DeserializeAsync<EnumerationResult<FleetAction>>(response).ConfigureAwait(false);
                AssertEqual(FleetActionSeedService.BuildDefaults(OperatingSystem.IsWindows()).Count, result.Objects.Count(a => a.IsBuiltIn), "built-ins");
                AssertTrue(result.Objects.Any(a => a.BuiltInKey == "ff-default-branch"), "ff-default-branch present");
                AssertTrue(result.Objects.Any(a => a.BuiltInKey == FleetActionSeedService.UpdatePackageKey && a.ApplicabilityCommand != null), "update-package present with its applicability check");
                AssertTrue(result.Objects.Any(a => a.BuiltInKey == FleetActionSeedService.SecurityReviewKey && a.MissionMode == MissionModeEnum.Audit), "security-review is report-only");
            }));

            cases.Add(CaseAsync("non_admin_command_create_forbidden", "A non-admin creating a Command action gets 403", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _RegularA!.PostAsync("/api/v1/fleet-actions",
                    JsonHelper.ToJsonContent(new { name = "pull", kind = "Command", commandText = "git pull" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Forbidden, response);
            }));

            cases.Add(CaseAsync("non_admin_command_run_forbidden", "A non-admin running a Command action gets 403", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _RegularA!.PostAsync("/api/v1/fleet-actions/run",
                    JsonHelper.ToJsonContent(new
                    {
                        vesselIds = new[] { _VesselAId },
                        definition = new { name = "x", kind = "Command", commandText = "echo x" }
                    })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Forbidden, response);
            }));

            cases.Add(CaseAsync("unknown_variable_400", "An unknown template variable is rejected with 400 naming it", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-actions",
                    JsonHelper.ToJsonContent(new { name = "bad", kind = "Command", commandText = "echo {{vessel.secret}}" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, response);
                E2eFleetActionTemplateErrorBody error = JsonHelper.Deserialize<E2eFleetActionTemplateErrorBody>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(WatsonWebserver.Core.ApiResultEnum.BadRequest, error.Error, "error");
                AssertNotNull(error.Data, "the error carries a typed detail");
                AssertEqual(FleetActionTemplateErrorDetail.UnknownTemplateVariableCode, error.Data!.Code, "detail code");
                AssertEqual("vessel.secret", error.Data.VariableName, "the detail names the unknown variable");
            }));

            cases.Add(CaseAsync("cross_tenant_vessel_rejected", "A cross-tenant vessel rejects the whole run with 404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-actions/run",
                    JsonHelper.ToJsonContent(new
                    {
                        vesselIds = new[] { _VesselAId, _VesselBId },
                        definition = new { name = "x", kind = "Command", commandText = "echo x", requiresCleanWorkingTree = false }
                    })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NotFound, response);

                HttpResponseMessage runs = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<FleetActionRun> page = await JsonHelper.DeserializeAsync<EnumerationResult<FleetActionRun>>(runs).ConfigureAwait(false);
                AssertEqual(0L, page.TotalRecords, "no run created");
            }));

            cases.Add(CaseAsync("run_accepted_and_finishes", "An admin run is accepted with 202 and reaches a terminal status", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-actions/run",
                    JsonHelper.ToJsonContent(new
                    {
                        vesselIds = new[] { _VesselAId },
                        definition = new { name = "echo", kind = "Command", commandText = "echo e2e-{{vessel.name}}", requiresCleanWorkingTree = false }
                    })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Accepted, response);
                FleetActionRunStartResult start = await JsonHelper.DeserializeAsync<FleetActionRunStartResult>(response).ConfigureAwait(false);
                AssertStartsWith("far_", start.RunId);
                AssertEqual(1, start.TargetCount);
                _RunId = start.RunId;

                FleetActionRunDetail? detail = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (!deadline.Passed)
                {
                    HttpResponseMessage get = await _AdminA!.GetAsync("/api/v1/fleet-action-runs/" + _RunId).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, get);
                    detail = await JsonHelper.DeserializeAsync<FleetActionRunDetail>(get).ConfigureAwait(false);
                    if (detail.Run.CompletedUtc.HasValue) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }

                AssertNotNull(detail, "detail");
                AssertNotNull(detail!.Run.CompletedUtc, "run completed");
                AssertEqual(1, detail.Run.TargetCount);
                HttpResponseMessage rawRun = await _AdminA!.GetAsync("/api/v1/fleet-action-runs/" + _RunId).ConfigureAwait(false);
                string rawRunBody = await rawRun.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertFalse(rawRunBody.Contains("\"Targets\"", StringComparison.Ordinal), "the run read does not embed its targets");
                HttpResponseMessage firstPage = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/" + _RunId + "/targets/enumerate",
                    JsonHelper.ToJsonContent(new { pageSize = 1 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, firstPage);
                EnumerationResult<FleetActionRunTargetSummary> firstTargets = await JsonHelper.DeserializeAsync<EnumerationResult<FleetActionRunTargetSummary>>(firstPage).ConfigureAwait(false);
                AssertEqual(1, firstTargets.Objects.Count);
                AssertEqual(1L, firstTargets.TotalRecords);
                FleetActionRunTargetSummary summary = firstTargets.Objects[0];
                AssertTrue(summary.Status == FleetActionTargetStatusEnum.Succeeded
                    || (summary.Status == FleetActionTargetStatusEnum.Skipped && summary.SkipReason == "NoWorkingDirectory"),
                    "target succeeded or was skipped for lack of a working directory, got " + summary.Status);

                HttpResponseMessage targetResponse = await _AdminA!.GetAsync("/api/v1/fleet-action-runs/" + _RunId + "/targets/" + summary.Id).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, targetResponse);
                FleetActionRunTarget target = await JsonHelper.DeserializeAsync<FleetActionRunTarget>(targetResponse).ConfigureAwait(false);
                AssertEqual(summary.Id, target.Id);

                HttpResponseMessage targetsPage = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/" + _RunId + "/targets/enumerate",
                    JsonHelper.ToJsonContent(new { pageSize = 10 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, targetsPage);
            }));

            cases.Add(CaseAsync("other_tenant_cannot_read_run", "Another tenant reading the run gets 404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminB!.GetAsync("/api/v1/fleet-action-runs/" + _RunId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NotFound, response);
            }));

            cases.Add(CaseAsync("cancel_finished_run_conflict", "Cancelling a finished run returns 409", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/" + _RunId + "/cancel", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Conflict, response);
            }));

            cases.Add(CaseAsync("typed_inputs_rest_contract", "REST saves input definitions and rejects invalid run inputs with 400", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);
                HttpResponseMessage create = await _AdminA!.PostAsync("/api/v1/fleet-actions", JsonHelper.ToJsonContent(new FleetActionUpsertRequest {
                    Name = "typed inputs", CommandText = "echo {{input.version}}", CommandWindows = "Write-Output '{{input.version}}'", OutputParser = "npm-audit-json", RequiredTools = new() { "npm" }, RequiresNetwork = true,
                    Inputs = new() { new() { Name = "version", Type = "semver", Required = true } }
                }));
                AssertStatusCode(HttpStatusCode.Created, create);
                FleetAction action = await JsonHelper.DeserializeAsync<FleetAction>(create);
                AssertEqual("version", action.Inputs[0].Name); AssertEqual("npm", action.RequiredTools[0]); AssertTrue(action.RequiresNetwork, "requires network");
                foreach (var values in new[] { new Dictionary<string, string>(), new() { ["version"] = "not-semver" } })
                {
                    HttpResponseMessage invalid = await _AdminA.PostAsync("/api/v1/fleet-actions/" + action.Id + "/run", JsonHelper.ToJsonContent(new FleetActionRunRequest { VesselIds = new() { _VesselAId }, InputValues = values }));
                    AssertStatusCode(HttpStatusCode.BadRequest, invalid);
                    AssertContains("version", await invalid.Content.ReadAsStringAsync());
                }
                HttpResponseMessage valid = await _AdminA.PostAsync("/api/v1/fleet-actions/" + action.Id + "/run", JsonHelper.ToJsonContent(new FleetActionRunRequest { VesselIds = new() { _VesselAId }, InputValues = new() { ["version"] = "1.2.3" } }));
                AssertStatusCode(HttpStatusCode.Accepted, valid);
                HttpResponseMessage unknown = await _AdminA.PostAsync("/api/v1/fleet-actions", JsonHelper.ToJsonContent(new FleetActionUpsertRequest { Name = "unknown", CommandText = "{{input.unknown}}" }));
                AssertStatusCode(HttpStatusCode.BadRequest, unknown);
            }));

            cases.Add(CaseAsync("typed_inputs_mcp_contract", "MCP create and run preserve action options and validate supplied inputs", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                using McpToolClient client = new("http://127.0.0.1:" + fx.McpPort + "/mcp", null, new Dictionary<string, string> { ["Authorization"] = _AdminA!.DefaultRequestHeaders.Authorization!.ToString() });
                await client.InitializeAsync();
                McpToolCallResult create = await client.CallToolResultAsync("create_fleet_action", JsonHelper.Serialize(new { name = "mcp inputs", kind = "Mission", promptTemplate = "Release {{input.version}} {{scan.summary}}", scanSourceActionKey = "scan", inputs = new[] { new { name = "version", type = "semver", required = true } } }));
                FleetAction action = JsonHelper.Deserialize<FleetAction>(create.Text);
                AssertEqual("version", action.Inputs[0].Name); AssertEqual("scan", action.ScanSourceActionKey);
                McpToolCallResult invalid = await client.CallToolResultAsync("run_fleet_action", JsonHelper.Serialize(new { actionId = action.Id, vesselIds = new[] { _VesselAId }, inputValues = new { version = "bad" } }));
                AssertContains("version", invalid.Text); AssertContains("semantic version", invalid.Text);
                McpToolCallResult valid = await client.CallToolResultAsync("run_fleet_action", JsonHelper.Serialize(new { actionId = action.Id, vesselIds = new[] { _VesselAId }, inputValues = new { version = "1.2.3" } }));
                AssertFalse(valid.IsError, "valid MCP run accepted");
                AssertFalse(valid.Text.Contains("errorCode", StringComparison.OrdinalIgnoreCase), "no contract error");
                AssertContains("RunId", valid.Text);
            }));

            cases.Add(CaseAsync("collection_limits_rest", "REST rejects RequiredTools, Inputs, AllowedValues and InputValues over 256 and VesselIds over 1000 with 400 CollectionLimitExceeded", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);
                List<string> tools = Enumerable.Range(0, 257).Select(i => "tool" + i).ToList();
                HttpResponseMessage tooManyTools = await _AdminA!.PostAsync("/api/v1/fleet-actions", JsonHelper.ToJsonContent(new FleetActionUpsertRequest { Name = "many tools", CommandText = "echo", RequiredTools = tools }));
                await AssertLimitAsync(tooManyTools, "RequiredTools", 256, 257);
                List<FleetActionInput> inputs = Enumerable.Range(0, 257).Select(i => new FleetActionInput { Name = "i" + i }).ToList();
                HttpResponseMessage tooManyInputs = await _AdminA.PostAsync("/api/v1/fleet-actions", JsonHelper.ToJsonContent(new FleetActionUpsertRequest { Name = "many inputs", CommandText = "echo", Inputs = inputs }));
                await AssertLimitAsync(tooManyInputs, "Inputs", 256, 257);
                FleetActionInput wide = new FleetActionInput { Name = "choice", Type = "enum", AllowedValues = Enumerable.Range(0, 257).Select(i => "v" + i).ToList() };
                HttpResponseMessage tooManyValues = await _AdminA.PostAsync("/api/v1/fleet-actions", JsonHelper.ToJsonContent(new FleetActionUpsertRequest { Name = "wide enum", CommandText = "echo", Inputs = new() { wide } }));
                await AssertLimitAsync(tooManyValues, "Inputs[choice].AllowedValues", 256, 257);

                HttpResponseMessage create = await _AdminA.PostAsync("/api/v1/fleet-actions", JsonHelper.ToJsonContent(new FleetActionUpsertRequest { Name = "bounded", CommandText = "echo", RequiredTools = new() { "git" } }));
                AssertStatusCode(HttpStatusCode.Created, create);
                FleetAction action = await JsonHelper.DeserializeAsync<FleetAction>(create);
                HttpResponseMessage update = await _AdminA.PutAsync("/api/v1/fleet-actions/" + action.Id, JsonHelper.ToJsonContent(new FleetActionUpsertRequest { RequiredTools = tools }));
                await AssertLimitAsync(update, "RequiredTools", 256, 257);
                FleetAction? unchanged = await JsonHelper.DeserializeAsync<FleetAction>(await _AdminA.GetAsync("/api/v1/fleet-actions/" + action.Id));
                AssertEqual(1, unchanged!.RequiredTools.Count, "a rejected update changes nothing");

                Dictionary<string, string> values = Enumerable.Range(0, 257).ToDictionary(i => "k" + i, i => "v");
                HttpResponseMessage tooManyValuesRun = await _AdminA.PostAsync("/api/v1/fleet-actions/" + action.Id + "/run", JsonHelper.ToJsonContent(new FleetActionRunRequest { VesselIds = new() { _VesselAId }, InputValues = values }));
                await AssertLimitAsync(tooManyValuesRun, "InputValues", 256, 257);
                List<string> vessels = Enumerable.Range(0, 1001).Select(i => "vsl_missing" + i).ToList();
                HttpResponseMessage tooManyVessels = await _AdminA.PostAsync("/api/v1/fleet-actions/" + action.Id + "/run", JsonHelper.ToJsonContent(new FleetActionRunRequest { VesselIds = vessels }));
                await AssertLimitAsync(tooManyVessels, "VesselIds", 1000, 1001);
            }));

            cases.Add(CaseAsync("collection_limits_mcp", "MCP create, update and run reject oversized lists with InvalidArgument and Code CollectionLimitExceeded", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                using McpToolClient client = new("http://127.0.0.1:" + fx.McpPort + "/mcp", null, new Dictionary<string, string> { ["Authorization"] = _AdminA!.DefaultRequestHeaders.Authorization!.ToString() });
                await client.InitializeAsync();
                string[] tools = Enumerable.Range(0, 257).Select(i => "tool" + i).ToArray();
                McpToolCallResult create = await client.CallToolResultAsync("create_fleet_action", JsonHelper.Serialize(new { name = "mcp many tools", kind = "Command", commandText = "echo", requiredTools = tools }));
                AssertMcpLimit(create, "RequiredTools");
                McpToolCallResult ok = await client.CallToolResultAsync("create_fleet_action", JsonHelper.Serialize(new { name = "mcp bounded", kind = "Command", commandText = "echo" }));
                FleetAction action = JsonHelper.Deserialize<FleetAction>(ok.Text);
                McpToolCallResult update = await client.CallToolResultAsync("update_fleet_action", JsonHelper.Serialize(new { actionId = action.Id, requiredTools = tools }));
                AssertMcpLimit(update, "RequiredTools");
                McpToolCallResult run = await client.CallToolResultAsync("run_fleet_action", JsonHelper.Serialize(new { actionId = action.Id, vesselIds = Enumerable.Range(0, 1001).Select(i => "vsl_missing" + i).ToArray() }));
                AssertMcpLimit(run, "VesselIds");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Action API",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task AssertLimitAsync(HttpResponseMessage response, string field, int limit, long actual)
        {
            AssertStatusCode(HttpStatusCode.BadRequest, response);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            CollectionLimitErrorDetail? detail = JsonHelper.Deserialize<E2eLimitErrorBody>(body).Data;
            AssertNotNull(detail, "limit detail: " + body);
            AssertEqual(CollectionLimitExceededException.ErrorCode, detail!.Code, "typed limit code: " + body);
            AssertEqual(field, detail.Field, "field");
            AssertEqual(limit, detail.Limit, "limit");
            AssertEqual(actual, detail.Actual, "actual");
        }

        private static void AssertMcpLimit(McpToolCallResult result, string field)
        {
            AssertContains("InvalidArgument", result.Text);
            AssertContains(CollectionLimitExceededException.ErrorCode, result.Text);
            AssertContains(field, result.Text);
        }

        private static async Task<TenantMetadata> CreateTenantAsync(HttpClient adminClient, string label)
        {
            HttpResponseMessage response = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
            return await JsonHelper.DeserializeAsync<TenantMetadata>(response).ConfigureAwait(false);
        }

        private static async Task<HttpClient> CreateUserClientAsync(E2EServerFixture fx, string tenantId, string label, bool tenantAdmin)
        {
            HttpResponseMessage userResp = await fx.AuthClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenantId,
                    Email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@fa.armada",
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = tenantAdmin
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            HttpResponseMessage credResp = await fx.AuthClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new { TenantId = tenantId, UserId = user.Id, Name = label + "-cred" })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(fx.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.BearerToken);
            return client;
        }

        private static async Task<string> CreateVesselAsync(HttpClient client, string label)
        {
            HttpResponseMessage fleetResp = await client.PostAsync("/api/v1/fleets",
                JsonHelper.ToJsonContent(new { Name = label + "-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 6) })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, fleetResp, "fleet create");
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(fleetResp).ConfigureAwait(false);

            HttpResponseMessage vesselResp = await client.PostAsync("/api/v1/vessels",
                JsonHelper.ToJsonContent(new
                {
                    Name = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                    FleetId = fleet.Id,
                    RepoUrl = TestRepoHelper.GetLocalBareRepoUrl()
                })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, vesselResp, "vessel create");
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResp).ConfigureAwait(false);
            return vessel.Id;
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
