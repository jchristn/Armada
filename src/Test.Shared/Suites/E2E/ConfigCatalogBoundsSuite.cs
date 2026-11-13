namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Bounded enumeration for the configuration catalogs (docs/ENUMERATION.md, audit group G): configuration lists on
    /// workflow profiles, pipelines and project profiles are rejected over CollectionLimits.MaxConfigItems on every REST
    /// and MCP write path with a typed CollectionLimitExceeded error (and nothing is stored); workflow-profile bulk delete
    /// rejects more than MaxRequestItems ids; and a regular user's pages of pipelines, prompt templates, workflow profiles
    /// and playbooks (REST, MCP enumerate, MCP list_prompt_templates) are full pages with exact totals, because the
    /// visibility filter runs in the data source instead of after the page was cut.
    /// </summary>
    public sealed class ConfigCatalogBoundsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ConfigCatalogBounds";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("workflow_profile_lists_limited", "Workflow-profile lists over MaxConfigItems are a typed 400 / InvalidArgument on create, update and validate; at the limit they are accepted", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-wfp", true).ConfigureAwait(false);
                List<string> over = Strings(CollectionLimits.MaxConfigItems + 1);

                using (HttpClient http = admin.CreateClient(fx.BaseUrl))
                using (McpToolClient mcp = Mcp(fx, admin))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);

                    await ExpectRestLimitAsync(await http.PostAsync("/api/v1/workflow-profiles", JsonHelper.ToJsonContent(new { Name = "too-many-hints", BuildCommand = "make", LanguageHints = over })).ConfigureAwait(false),
                        "LanguageHints", CollectionLimits.MaxConfigItems, over.Count).ConfigureAwait(false);
                    await ExpectRestLimitAsync(await http.PostAsync("/api/v1/workflow-profiles/validate", JsonHelper.ToJsonContent(new { Name = "too-many-envs", BuildCommand = "make", Environments = Environments(CollectionLimits.MaxConfigItems + 1) })).ConfigureAwait(false),
                        "Environments", CollectionLimits.MaxConfigItems, CollectionLimits.MaxConfigItems + 1).ConfigureAwait(false);

                    HttpResponseMessage listed = await http.GetAsync("/api/v1/workflow-profiles?pageSize=1000").ConfigureAwait(false);
                    EnumerationResult<WorkflowProfile> page = await JsonHelper.DeserializeAsync<EnumerationResult<WorkflowProfile>>(listed).ConfigureAwait(false);
                    AssertFalse(page.Objects.Any(p => p.Name == "too-many-hints"), "a rejected create stores nothing");

                    // At the limit: 256 environments are accepted and the previews stay within MaxCommandPreviews.
                    HttpResponseMessage atLimit = await http.PostAsync("/api/v1/workflow-profiles/validate", JsonHelper.ToJsonContent(new { Name = "at-limit", BuildCommand = "make", Environments = Environments(CollectionLimits.MaxConfigItems) })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, atLimit, "256 environments validate");
                    WorkflowProfileValidationResult validation = await JsonHelper.DeserializeAsync<WorkflowProfileValidationResult>(atLimit).ConfigureAwait(false);
                    AssertEqual(1 + 6 * CollectionLimits.MaxConfigItems, validation.CommandPreviews.Count, "one build preview plus six per environment");
                    AssertTrue(validation.CommandPreviews.Count <= WorkflowProfileService.MaxCommandPreviews, "previews within the declared bound");

                    await ExpectMcpLimitAsync(mcp, "create_workflow_profile", new { name = "mcp-too-many", buildCommand = "make", expectedArtifacts = over }).ConfigureAwait(false);
                    await ExpectMcpLimitAsync(mcp, "validate_workflow_profile", new { name = "mcp-too-many", buildCommand = "make", languageHints = over }).ConfigureAwait(false);

                    WorkflowProfile created = await OkAsync<WorkflowProfile>(mcp, "create_workflow_profile", new { name = "mcp-ok", buildCommand = "make" }).ConfigureAwait(false);
                    await ExpectMcpLimitAsync(mcp, "update_workflow_profile", new { id = created.Id, requiredInputs = over.Select(k => new { key = k }).ToList() }).ConfigureAwait(false);
                    await ExpectRestLimitAsync(await http.PutAsync("/api/v1/workflow-profiles/" + created.Id, JsonHelper.ToJsonContent(new { Name = "mcp-ok", BuildCommand = "make", ExpectedArtifacts = over })).ConfigureAwait(false),
                        "ExpectedArtifacts", CollectionLimits.MaxConfigItems, over.Count).ConfigureAwait(false);
                    WorkflowProfile unchanged = await OkAsync<WorkflowProfile>(mcp, "get_workflow_profile", new { id = created.Id }).ConfigureAwait(false);
                    AssertEqual(0, unchanged.RequiredInputs.Count, "a rejected update stores nothing");
                    AssertEqual(0, unchanged.ExpectedArtifacts.Count, "a rejected PUT stores nothing");
                }
            }));

            cases.Add(CaseAsync("workflow_profile_bulk_delete_limited", "Workflow-profile bulk delete with more than MaxRequestItems ids is a typed 400 / InvalidArgument and deletes nothing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-wfpdel", true).ConfigureAwait(false);
                using (HttpClient http = admin.CreateClient(fx.BaseUrl))
                using (McpToolClient mcp = Mcp(fx, admin))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    WorkflowProfile created = await OkAsync<WorkflowProfile>(mcp, "create_workflow_profile", new { name = "keep-me", buildCommand = "make" }).ConfigureAwait(false);
                    List<string> ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "wfp_none" + i).ToList();
                    ids.Add(created.Id);

                    await ExpectRestLimitAsync(await http.PostAsync("/api/v1/workflow-profiles/delete/multiple", JsonHelper.ToJsonContent(new { Ids = ids })).ConfigureAwait(false),
                        "Ids", CollectionLimits.MaxRequestItems, ids.Count).ConfigureAwait(false);
                    await ExpectMcpLimitAsync(mcp, "delete_workflow_profiles", new { ids = ids }).ConfigureAwait(false);
                    await OkAsync<WorkflowProfile>(mcp, "get_workflow_profile", new { id = created.Id }).ConfigureAwait(false);

                    // At the limit the request is processed; Skipped holds one entry per id at most.
                    List<string> atLimit = ids.Take(CollectionLimits.MaxRequestItems).ToList();
                    HttpResponseMessage ok = await http.PostAsync("/api/v1/workflow-profiles/delete/multiple", JsonHelper.ToJsonContent(new { Ids = atLimit })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, ok, "1000 ids are accepted");
                    DeleteMultipleResult result = await JsonHelper.DeserializeAsync<DeleteMultipleResult>(ok).ConfigureAwait(false);
                    AssertEqual(CollectionLimits.MaxRequestItems, result.Skipped.Count, "every missing id skipped");
                }
            }));

            cases.Add(CaseAsync("pipeline_stages_limited", "Pipeline Stages over MaxConfigItems are a typed 400 / InvalidArgument on REST create and update and MCP create and update", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-ppl", true).ConfigureAwait(false);
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                List<object> stages = Enumerable.Range(1, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { Order = i, PersonaName = "Worker" }).ToList();
                List<object> mcpStages = Enumerable.Range(1, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { personaName = "Worker" }).ToList();

                using (HttpClient http = admin.CreateClient(fx.BaseUrl))
                using (McpToolClient mcp = Mcp(fx, admin))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    string tooMany = "ccb-too-many-" + suffix;
                    await ExpectRestLimitAsync(await http.PostAsync("/api/v1/pipelines", JsonHelper.ToJsonContent(new { Name = tooMany, Stages = stages })).ConfigureAwait(false),
                        "Stages", CollectionLimits.MaxConfigItems, stages.Count).ConfigureAwait(false);
                    HttpResponseMessage missing = await http.GetAsync("/api/v1/pipelines/" + tooMany).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.NotFound, missing, "a rejected create stores nothing");
                    await ExpectMcpLimitAsync(mcp, "create_pipeline", new { name = tooMany, stages = mcpStages }).ConfigureAwait(false);

                    string ok = "ccb-ok-" + suffix;
                    HttpResponseMessage created = await http.PostAsync("/api/v1/pipelines", JsonHelper.ToJsonContent(new { Name = ok, Stages = new[] { new { Order = 1, PersonaName = "Worker" } } })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.Created, created, "a small pipeline is created");
                    await ExpectRestLimitAsync(await http.PutAsync("/api/v1/pipelines/" + ok, JsonHelper.ToJsonContent(new { Stages = stages })).ConfigureAwait(false),
                        "Stages", CollectionLimits.MaxConfigItems, stages.Count).ConfigureAwait(false);
                    await ExpectMcpLimitAsync(mcp, "update_pipeline", new { name = ok, stages = mcpStages }).ConfigureAwait(false);
                    Pipeline stored = await OkAsync<Pipeline>(mcp, "get_pipeline", new { name = ok }).ConfigureAwait(false);
                    AssertEqual(1, stored.Stages.Count, "rejected updates store nothing");
                }
            }));

            cases.Add(CaseAsync("project_profile_lists_limited", "Project-profile Skills and PersonaOverrides over MaxConfigItems are a typed 400 on create, update and validate", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-ppf", true).ConfigureAwait(false);
                List<string> skills = Strings(CollectionLimits.MaxConfigItems + 1);
                List<object> overrides = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { PersonaName = "Persona" + i }).ToList();

                using (HttpClient http = admin.CreateClient(fx.BaseUrl))
                {
                    await ExpectRestLimitAsync(await http.PostAsync("/api/v1/project-profiles", JsonHelper.ToJsonContent(new { Name = "too-many-skills", Skills = skills })).ConfigureAwait(false),
                        "Skills", CollectionLimits.MaxConfigItems, skills.Count).ConfigureAwait(false);
                    await ExpectRestLimitAsync(await http.PostAsync("/api/v1/project-profiles/validate", JsonHelper.ToJsonContent(new { Name = "too-many-overrides", PersonaOverrides = overrides })).ConfigureAwait(false),
                        "PersonaOverrides", CollectionLimits.MaxConfigItems, overrides.Count).ConfigureAwait(false);

                    HttpResponseMessage created = await http.PostAsync("/api/v1/project-profiles", JsonHelper.ToJsonContent(new { Name = "ok" })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.Created, created, "a small profile is created");
                    ProjectProfile profile = await JsonHelper.DeserializeAsync<ProjectProfile>(created).ConfigureAwait(false);
                    await ExpectRestLimitAsync(await http.PutAsync("/api/v1/project-profiles/" + profile.Id, JsonHelper.ToJsonContent(new { Name = "ok", Skills = skills })).ConfigureAwait(false),
                        "Skills", CollectionLimits.MaxConfigItems, skills.Count).ConfigureAwait(false);
                    HttpResponseMessage read = await http.GetAsync("/api/v1/project-profiles/" + profile.Id).ConfigureAwait(false);
                    ProjectProfile stored = await JsonHelper.DeserializeAsync<ProjectProfile>(read).ConfigureAwait(false);
                    AssertEqual(0, stored.Skills.Count, "a rejected update stores nothing");
                }
            }));

            cases.Add(CaseAsync("regular_user_pages_are_full_with_exact_totals", "A regular user's REST and MCP pages of pipelines, workflow profiles and playbooks are full pages with exact totals", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-vis-admin", true).ConfigureAwait(false);
                E2ETenantUser viewer = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-vis-viewer", false, admin.TenantId).ConfigureAwait(false);
                E2ETenantUser peer = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-vis-peer", false, admin.TenantId).ConfigureAwait(false);
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

                using (HttpClient adminHttp = admin.CreateClient(fx.BaseUrl))
                using (HttpClient viewerHttp = viewer.CreateClient(fx.BaseUrl))
                using (HttpClient peerHttp = peer.CreateClient(fx.BaseUrl))
                using (McpToolClient viewerMcp = Mcp(fx, viewer))
                {
                    await viewerMcp.InitializeAsync().ConfigureAwait(false);
                    HashSet<string> visible = new HashSet<string>();
                    // The peer's private pipelines come first, so a page cut before filtering would be mostly invisible rows.
                    for (int i = 0; i < 4; i++) await CreatePipelineAsync(peerHttp, "ccb-peer-" + suffix + "-" + i).ConfigureAwait(false);
                    for (int i = 0; i < 2; i++) visible.Add(await CreatePipelineAsync(adminHttp, "ccb-shared-" + suffix + "-" + i).ConfigureAwait(false));
                    for (int i = 0; i < 1; i++) visible.Add(await CreatePipelineAsync(viewerHttp, "ccb-own-" + suffix + "-" + i).ConfigureAwait(false));

                    List<string> restSeen = new List<string>();
                    for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
                    {
                        HttpResponseMessage response = await viewerHttp.GetAsync("/api/v1/pipelines?pageSize=1&pageNumber=" + pageNumber).ConfigureAwait(false);
                        EnumerationResult<Pipeline> page = await JsonHelper.DeserializeAsync<EnumerationResult<Pipeline>>(response).ConfigureAwait(false);
                        AssertEqual((long)visible.Count, page.TotalRecords, "REST TotalRecords counts only visible pipelines");
                        AssertEqual(1, page.Objects.Count, "REST page " + pageNumber + " is full");
                        restSeen.AddRange(page.Objects.Select(p => p.Id));
                    }
                    AssertTrue(visible.SetEquals(restSeen), "REST pages cover exactly the visible pipelines");

                    List<string> mcpSeen = new List<string>();
                    for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
                    {
                        EnumerationResult<Pipeline> page = await OkAsync<EnumerationResult<Pipeline>>(viewerMcp, "enumerate", new { entityType = "pipelines", pageSize = 1, pageNumber = pageNumber }).ConfigureAwait(false);
                        AssertEqual((long)visible.Count, page.TotalRecords, "MCP TotalRecords counts only visible pipelines");
                        AssertEqual(visible.Count, page.TotalPages, "MCP TotalPages");
                        AssertEqual(1, page.Objects.Count, "MCP page " + pageNumber + " is full");
                        mcpSeen.AddRange(page.Objects.Select(p => p.Id));
                    }
                    AssertTrue(visible.SetEquals(mcpSeen), "MCP pages cover exactly the visible pipelines");
                    EnumerationResult<Pipeline> past = await OkAsync<EnumerationResult<Pipeline>>(viewerMcp, "enumerate", new { entityType = "pipelines", pageSize = 1, pageNumber = 4 }).ConfigureAwait(false);
                    AssertEqual(0, past.Objects.Count, "a page past the end is empty");
                    AssertEqual((long)visible.Count, past.TotalRecords, "with accurate totals");

                    EnumerationResult<WorkflowProfile> profiles = await OkAsync<EnumerationResult<WorkflowProfile>>(viewerMcp, "enumerate", new { entityType = "workflow_profiles", pageSize = 1000 }).ConfigureAwait(false);
                    AssertEqual((long)profiles.Objects.Count, profiles.TotalRecords, "workflow_profiles TotalRecords equals the visible rows");
                    EnumerationResult<Playbook> playbooks = await OkAsync<EnumerationResult<Playbook>>(viewerMcp, "enumerate", new { entityType = "playbooks", pageSize = 1000, includeDescription = true }).ConfigureAwait(false);
                    AssertEqual((long)playbooks.Objects.Count, playbooks.TotalRecords, "playbooks TotalRecords equals the visible rows");
                }
            }));

            cases.Add(CaseAsync("list_prompt_templates_pages_in_data_source", "MCP list_prompt_templates pages through every visible template with the standard 1..1000 page contract", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-tpl", true).ConfigureAwait(false);
                using (McpToolClient mcp = Mcp(fx, admin))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    EnumerationResult<PromptTemplate> first = await OkAsync<EnumerationResult<PromptTemplate>>(mcp, "list_prompt_templates", new { pageSize = 3 }).ConfigureAwait(false);
                    AssertEqual(3, first.PageSize, "page size used");
                    AssertTrue(first.TotalRecords > 3, "the seeded templates span several pages");
                    List<string> names = new List<string>();
                    for (int pageNumber = 1; pageNumber <= first.TotalPages; pageNumber++)
                    {
                        EnumerationResult<PromptTemplate> page = await OkAsync<EnumerationResult<PromptTemplate>>(mcp, "list_prompt_templates", new { pageSize = 3, pageNumber = pageNumber }).ConfigureAwait(false);
                        names.AddRange(page.Objects.Select(t => t.Name));
                    }
                    AssertEqual((int)first.TotalRecords, names.Count, "paging reaches every template");
                    AssertEqual(names.Count, names.Distinct().Count(), "pages never overlap");

                    EnumerationResult<PromptTemplate> clamped = await OkAsync<EnumerationResult<PromptTemplate>>(mcp, "list_prompt_templates", new { pageSize = 5000 }).ConfigureAwait(false);
                    AssertEqual(CollectionLimits.MaxPageSize, clamped.PageSize, "an oversized page size is normalized to 1000");
                    EnumerationResult<PromptTemplate> tiny = await OkAsync<EnumerationResult<PromptTemplate>>(mcp, "list_prompt_templates", new { pageSize = 0 }).ConfigureAwait(false);
                    AssertEqual(CollectionLimits.MinPageSize, tiny.PageSize, "a zero page size is normalized to 1");
                }
            }));

            cases.Add(CaseAsync("enumerate_pages_have_standard_shape", "Every MCP enumerate entity type in this area returns PageNumber, PageSize, TotalPages, TotalRecords and Objects", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "ccb-shape", true).ConfigureAwait(false);
                using (McpToolClient mcp = Mcp(fx, admin))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    string[] types = new[] { "pipelines", "playbooks", "prompt_templates", "workflow_profiles", "project_profiles", "skills", "harbors", "model_endpoints", "missions", "voyages" };
                    foreach (string type in types)
                    {
                        McpToolCallResult result = await mcp.CallToolResultAsync("enumerate", JsonHelper.Serialize(new { entityType = type, pageSize = 2, includeDescription = true })).ConfigureAwait(false);
                        AssertFalse(result.IsError, type + ": " + result.Text);
                        foreach (string field in new[] { "pageNumber", "pageSize", "totalPages", "totalRecords", "objects" })
                            AssertTrue(result.Text.Contains("\"" + field + "\"", StringComparison.OrdinalIgnoreCase), type + " carries " + field + ": " + result.Text);
                        foreach (string unbounded in new[] { "PlaybookSnapshots", "SelectedPlaybooks", "AssignmentBlocker" })
                            AssertFalse(result.Text.Contains("\"" + unbounded + "\"", StringComparison.OrdinalIgnoreCase), type + " embeds no " + unbounded);
                    }
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Configuration catalogs are bounded", cases);
        }

        #endregion

        #region Private-Methods

        private static List<string> Strings(int count)
        {
            return Enumerable.Range(0, count).Select(i => "v" + i).ToList();
        }

        private static List<object> Environments(int count)
        {
            return Enumerable.Range(0, count).Select(i => (object)new
            {
                EnvironmentName = "env" + i,
                DeployCommand = "deploy " + i,
                RollbackCommand = "rollback " + i,
                SmokeTestCommand = "smoke " + i,
                HealthCheckCommand = "health " + i,
                DeploymentVerificationCommand = "verify " + i,
                RollbackVerificationCommand = "verify-rollback " + i
            }).ToList();
        }

        private static async Task<string> CreatePipelineAsync(HttpClient http, string name)
        {
            HttpResponseMessage response = await http.PostAsync("/api/v1/pipelines", JsonHelper.ToJsonContent(new { Name = name, Stages = new[] { new { Order = 1, PersonaName = "Worker" } } })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, response, "create pipeline " + name);
            Pipeline pipeline = await JsonHelper.DeserializeAsync<Pipeline>(response).ConfigureAwait(false);
            return pipeline.Id;
        }

        private static async Task ExpectRestLimitAsync(HttpResponseMessage response, string field, int limit, long actual)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, body);
            AssertTrue(error.Data!.Field != null && error.Data.Field.EndsWith(field, StringComparison.Ordinal), "field " + field + ": " + body);
            AssertEqual(limit, error.Data.Limit, "limit: " + body);
            AssertEqual(actual, error.Data.Actual, "actual: " + body);
        }

        private static async Task ExpectMcpLimitAsync(McpToolClient client, string tool, object args)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            McpToolError error = JsonHelper.Deserialize<McpToolError>(result.Text);
            AssertEqual(McpToolErrorCodeEnum.InvalidArgument, error.ErrorCode, tool + ": " + result.Text);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Code, tool + ": " + result.Text);
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

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd, tag });
        }

        #endregion
    }
}
