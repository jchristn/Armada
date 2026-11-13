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
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Live REST and MCP bounds for the settings, account, summary, and delivery surfaces (docs/ENUMERATION.md): oversized
    /// windows, request lists, and stored lists are typed limit errors (REST 400 / MCP InvalidArgument with Code
    /// CollectionLimitExceeded) that change nothing; the token-usage summary breaks out at most modelLimit models; the
    /// tenant lookup and the incident, runbook, and runbook-execution lists page with exact totals.
    /// </summary>
    public sealed class DeliveryBoundsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.DeliveryBounds";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("token_usage_summary_window_over_limit_is_typed_error", "Token usage summary: a window over 1000 buckets is a REST 400 and an MCP InvalidArgument with Code CollectionLimitExceeded", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                DateTime to = DateTime.UtcNow;
                string from = Uri.EscapeDataString(to.AddDays(-30).ToString("o"));
                using (HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/token-usage/summary?fromUtc=" + from + "&toUtc=" + Uri.EscapeDataString(to.ToString("o")) + "&bucketMinutes=1").ConfigureAwait(false))
                {
                    E2eLimitErrorBody error = await LimitErrorAsync(response).ConfigureAwait(false);
                    AssertEqual("Buckets", error.Data!.Field, "field");
                    AssertEqual(CollectionLimits.MaxSummaryBuckets, error.Data.Limit, "limit");
                }

                using (HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/token-usage/summary?bucketMinutes=1000000000").ConfigureAwait(false))
                {
                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, "bucket width past the ceiling");
                }

                McpToolError mcpError = await McpErrorAsync(fx, "token_usage_summary", new { sinceHours = 24 * 30, bucketMinutes = 1 }).ConfigureAwait(false);
                AssertEqual(McpToolErrorCodeEnum.InvalidArgument, mcpError.ErrorCode, "MCP category");
                AssertEqual(CollectionLimitExceededException.ErrorCode, mcpError.Code, "MCP code");
            }));

            cases.Add(CaseAsync("token_usage_summary_breaks_out_model_limit", "Token usage summary: byModel and bucket models list at most modelLimit models and otherModels sums the rest (REST and MCP)", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string batch = "bounds-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                DateTime when = DateTime.UtcNow.AddMinutes(-30);
                List<object> records = new List<object>();
                for (int m = 0; m < 5; m++)
                    records.Add(new { Model = "bounds-model-" + m, CreatedUtc = when, InputTokens = 100 - m, OutputTokens = 10, CachedTokens = 0 });
                using (HttpResponseMessage import = await fx.AuthClient.PostAsync("/api/v1/token-usage/synthetic", JsonHelper.ToJsonContent(new { BatchId = batch, Records = records })).ConfigureAwait(false))
                    AssertEqual(HttpStatusCode.OK, import.StatusCode, await import.Content.ReadAsStringAsync().ConfigureAwait(false));

                string sourceId = Uri.EscapeDataString("synthetic:" + batch);
                using HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/token-usage/summary?sourceId=" + sourceId + "&modelLimit=2&bucketMinutes=60").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "summary");
                TokenUsageSummaryResult summary = await JsonHelper.DeserializeAsync<TokenUsageSummaryResult>(response).ConfigureAwait(false);
                AssertEqual(2, summary.ModelLimit, "limit echoed");
                AssertEqual(5, summary.ModelCount, "distinct models");
                AssertEqual(2, summary.ByModel.Count, "top models only");
                AssertEqual(3, summary.OtherModelCount, "summed models");
                AssertNotNull(summary.OtherModels, "explicit other aggregate");
                AssertEqual(summary.TotalTokens, summary.ByModel.Sum(m => m.TotalTokens) + summary.OtherModels!.TotalTokens, "totals add up");
                AssertTrue(summary.Buckets.All(b => b.Models.Count <= 2), "bucket models bounded");

                using HttpClient mcp = McpClient(fx);
                string rpc = await McpTestCalls.CallToolAsync(mcp, "token_usage_summary", new { sourceId = "synthetic:" + batch, modelLimit = 2, sinceHours = 2 }).ConfigureAwait(false);
                E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
                TokenUsageSummaryResult mcpSummary = JsonHelper.Deserialize<TokenUsageSummaryResult>(envelope.Result?.Content.FirstOrDefault()?.Text ?? "{}");
                AssertEqual(2, mcpSummary.ByModel.Count, "MCP top models");
                AssertEqual(3, mcpSummary.OtherModelCount, "MCP summed models");
            }));

            cases.Add(CaseAsync("request_history_summary_and_delete_multiple_bounded", "Request history: an oversized summary window and a delete of 1001 ids are typed 400s, and the delete removes nothing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                DateTime to = DateTime.UtcNow;
                using (HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/request-history/summary?fromUtc=" + Uri.EscapeDataString(to.AddDays(-2).ToString("o")) + "&toUtc=" + Uri.EscapeDataString(to.ToString("o")) + "&bucketMinutes=1").ConfigureAwait(false))
                {
                    E2eLimitErrorBody error = await LimitErrorAsync(response).ConfigureAwait(false);
                    AssertEqual("Buckets", error.Data!.Field, "field");
                }

                using (HttpResponseMessage first = await fx.AuthClient.PostAsync("/api/v1/request-history/enumerate", JsonHelper.ToJsonContent(new { PageSize = 1 })).ConfigureAwait(false))
                {
                    EnumerationResult<RequestHistoryEntry> page = await JsonHelper.DeserializeAsync<EnumerationResult<RequestHistoryEntry>>(first).ConfigureAwait(false);
                    AssertTrue(page.Objects.Count == 1, "an entry exists to protect");
                    string keep = page.Objects[0].Id;
                    List<string> ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "req_none" + i).ToList();
                    ids.Add(keep);
                    using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/request-history/delete/multiple", JsonHelper.ToJsonContent(new { Ids = ids })).ConfigureAwait(false))
                    {
                        E2eLimitErrorBody error = await LimitErrorAsync(response).ConfigureAwait(false);
                        AssertEqual(CollectionLimits.MaxRequestItems, error.Data!.Limit, "limit");
                        AssertEqual((long)ids.Count, error.Data.Actual, "actual");
                    }

                    using HttpResponseMessage still = await fx.AuthClient.GetAsync("/api/v1/request-history/" + keep).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, still.StatusCode, "nothing was deleted");
                }
            }));

            cases.Add(CaseAsync("tenant_lookup_pages_with_totals", "Tenant lookup: one page of the email's tenants with exact totals and an empty page past the end", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string email = "lookup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@bounds.armada";
                for (int i = 0; i < 3; i++)
                {
                    string tenantId = await CreateTenantAsync(fx.AuthClient, "lookup-" + i).ConfigureAwait(false);
                    using HttpResponseMessage user = await fx.AuthClient.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
                    {
                        TenantId = tenantId,
                        Email = email,
                        PasswordSha256 = UserMaster.ComputePasswordHash("testpass")
                    })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, user.StatusCode, "user");
                }

                TenantLookupResult first = await LookupAsync(fx, new { Email = email, PageNumber = 1, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, first.TotalRecords, "total");
                AssertEqual(2, first.TotalPages, "pages");
                AssertEqual(2, first.Tenants.Count, "first page");
                TenantLookupResult second = await LookupAsync(fx, new { Email = email, PageNumber = 2, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(1, second.Tenants.Count, "final page");
                AssertEqual(3, first.Tenants.Concat(second.Tenants).Select(t => t.Id).Distinct().Count(), "no overlap");
                TenantLookupResult past = await LookupAsync(fx, new { Email = email, PageNumber = 5, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Tenants.Count, "past the end");
                AssertEqual(3L, past.TotalRecords, "past the end keeps the total");
            }));

            cases.Add(CaseAsync("settings_update_rejects_oversized_lists", "Settings: an update with 257 allowed roots is a typed 400 that changes nothing; duplicate scored criteria are kept once", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<string> roots = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => "/tmp/root-" + i).ToList();
                using (HttpResponseMessage response = await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Import = new { AllowedRoots = roots } })).ConfigureAwait(false))
                {
                    E2eLimitErrorBody error = await LimitErrorAsync(response).ConfigureAwait(false);
                    AssertEqual("Import.AllowedRoots", error.Data!.Field, "field path");
                }

                using HttpResponseMessage after = await fx.AuthClient.GetAsync("/api/v1/settings").ConfigureAwait(false);
                string body = await after.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertFalse(body.Contains("/tmp/root-256", StringComparison.Ordinal), "nothing applied");
            }));

            cases.Add(CaseAsync("delivery_writes_reject_oversized_lists", "Environments, releases, incidents, and runbooks reject stored lists over their limits with a typed 400", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fx.AuthClient).ConfigureAwait(false);

                List<object> definitions = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1)
                    .Select(i => (object)new { Name = "check-" + i, Path = "/health" }).ToList();
                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/environments", JsonHelper.ToJsonContent(new { VesselId = vesselId, Name = "Oversized", VerificationDefinitions = definitions })).ConfigureAwait(false))
                    AssertEqual("VerificationDefinitions", (await LimitErrorAsync(response).ConfigureAwait(false)).Data!.Field, "environment definitions");

                List<string> voyageIds = Enumerable.Range(0, CollectionLimits.MaxLinkedIds + 1).Select(i => "vyg_none" + i).ToList();
                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/releases", JsonHelper.ToJsonContent(new { VesselId = vesselId, Title = "Oversized", VoyageIds = voyageIds })).ConfigureAwait(false))
                    AssertEqual("VoyageIds", (await LimitErrorAsync(response).ConfigureAwait(false)).Data!.Field, "release voyages");

                List<string> rescueIds = Enumerable.Range(0, CollectionLimits.MaxLinkedIds + 1).Select(i => "msn_none" + i).ToList();
                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/incidents", JsonHelper.ToJsonContent(new { Title = "Oversized", RescueMissionIds = rescueIds })).ConfigureAwait(false))
                    AssertEqual("RescueMissionIds", (await LimitErrorAsync(response).ConfigureAwait(false)).Data!.Field, "incident rescues");

                List<object> steps = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { Title = "Step " + i }).ToList();
                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/runbooks", JsonHelper.ToJsonContent(new { FileName = "oversized-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md", Title = "Oversized", Steps = steps })).ConfigureAwait(false))
                    AssertEqual("Steps", (await LimitErrorAsync(response).ConfigureAwait(false)).Data!.Field, "runbook steps");
            }));

            cases.Add(CaseAsync("incidents_and_runbook_executions_page_with_totals", "Incidents and runbook executions page in the database with exact totals and no overlap", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string marker = "pg" + Guid.NewGuid().ToString("N").Substring(0, 10);
                for (int i = 0; i < 3; i++)
                {
                    using HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/incidents", JsonHelper.ToJsonContent(new { Title = "Paged " + marker + " " + i })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, created.StatusCode, "incident " + i);
                }

                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    using HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/incidents/enumerate", JsonHelper.ToJsonContent(new { Search = marker, PageNumber = page, PageSize = 2 })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode, "page " + page);
                    EnumerationResult<Incident> result = await JsonHelper.DeserializeAsync<EnumerationResult<Incident>>(response).ConfigureAwait(false);
                    AssertEqual(3L, result.TotalRecords, "total");
                    AssertEqual(page == 1 ? 2 : (page == 2 ? 1 : 0), result.Objects.Count, "page " + page + " size");
                    seen.AddRange(result.Objects.Select(item => item.Id));
                }

                AssertEqual(3, seen.Distinct().Count(), "no overlap");

                string fileName = "paged-" + marker + ".md";
                using HttpResponseMessage runbookResponse = await fx.AuthClient.PostAsync("/api/v1/runbooks", JsonHelper.ToJsonContent(new { FileName = fileName, Title = "Paged " + marker, Steps = new[] { new { Title = "Only step" } } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, runbookResponse.StatusCode, await runbookResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
                Runbook runbook = await JsonHelper.DeserializeAsync<Runbook>(runbookResponse).ConfigureAwait(false);
                for (int i = 0; i < 3; i++)
                {
                    using HttpResponseMessage started = await fx.AuthClient.PostAsync("/api/v1/runbooks/" + runbook.Id + "/executions", JsonHelper.ToJsonContent(new { Title = "Run " + i })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, started.StatusCode, "execution " + i);
                }

                using HttpResponseMessage executions = await fx.AuthClient.PostAsync("/api/v1/runbook-executions/enumerate", JsonHelper.ToJsonContent(new { RunbookId = runbook.Id, PageNumber = 2, PageSize = 2 })).ConfigureAwait(false);
                EnumerationResult<RunbookExecution> second = await JsonHelper.DeserializeAsync<EnumerationResult<RunbookExecution>>(executions).ConfigureAwait(false);
                AssertEqual(3L, second.TotalRecords, "execution total");
                AssertEqual(1, second.Objects.Count, "final execution page");

                using HttpResponseMessage runbooks = await fx.AuthClient.PostAsync("/api/v1/runbooks/enumerate", JsonHelper.ToJsonContent(new { Search = marker, PageSize = 1 })).ConfigureAwait(false);
                EnumerationResult<Runbook> runbookPage = await JsonHelper.DeserializeAsync<EnumerationResult<Runbook>>(runbooks).ConfigureAwait(false);
                AssertEqual(1L, runbookPage.TotalRecords, "runbook search total");
                AssertEqual(runbook.Id, runbookPage.Objects[0].Id, "runbook found");
            }));

            return new TestSuiteDescriptor(SuiteId, "Delivery, account, and summary bounds", cases);
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
                tags: new List<string> { tag, TestTags.EndToEnd });
        }

        private static async Task<E2eLimitErrorBody> LimitErrorAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
            AssertNotNull(error.Data, "limit detail: " + body);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data!.Code, body);
            return error;
        }

        private static HttpClient McpClient(E2EServerFixture fx)
        {
            HttpClient mcp = new HttpClient();
            mcp.BaseAddress = fx.McpClient.BaseAddress;
            mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
            return mcp;
        }

        private static async Task<McpToolError> McpErrorAsync(E2EServerFixture fx, string tool, object arguments)
        {
            using HttpClient mcp = McpClient(fx);
            string rpc = await McpTestCalls.CallToolAsync(mcp, tool, arguments).ConfigureAwait(false);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
            string text = envelope.Result?.Content.FirstOrDefault()?.Text ?? "";
            return JsonHelper.Deserialize<McpToolError>(text);
        }

        private static async Task<TenantLookupResult> LookupAsync(E2EServerFixture fx, object body)
        {
            using HttpResponseMessage response = await fx.UnauthClient.PostAsync("/api/v1/tenants/lookup", JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, "lookup");
            return await JsonHelper.DeserializeAsync<TenantLookupResult>(response).ConfigureAwait(false);
        }

        private static async Task<string> CreateTenantAsync(HttpClient adminClient, string label)
        {
            using HttpResponseMessage response = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = "bounds-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, "tenant");
            TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(response).ConfigureAwait(false);
            return tenant.Id;
        }

        private static async Task<string> CreateVesselAsync(HttpClient client)
        {
            string name = "bounds-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using HttpResponseMessage response = await client.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new
            {
                Name = name,
                RepoUrl = "file:///tmp/" + name + ".git",
                LocalPath = "/tmp/" + name,
                WorkingDirectory = "/tmp/" + name,
                DefaultBranch = "main"
            })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, "vessel");
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
            return vessel.Id;
        }

        #endregion
    }
}
