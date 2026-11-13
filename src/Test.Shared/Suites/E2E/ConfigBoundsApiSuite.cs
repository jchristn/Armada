namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Live REST and MCP behavior of the bounded captain, Harbor, runtime, model endpoint, and persona surfaces
    /// (docs/ENUMERATION.md): over-limit writes answer 400 (MCP InvalidArgument) with Code CollectionLimitExceeded and
    /// store nothing, server-owned health history cannot be set by a caller, Harbors page in the database across pages,
    /// and the captain tools and Mux endpoint reads return pages instead of whole lists.
    /// </summary>
    public sealed class ConfigBoundsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ConfigBoundsApi";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("captains_bulk_delete_rejects_over_limit", "Captain bulk delete with 1001 ids is a REST 400 and an MCP InvalidArgument with Code CollectionLimitExceeded, and deletes nothing", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Captain captain = await CreateCaptainAsync(fx, "bounds-bulk-" + Guid.NewGuid().ToString("N").Substring(0, 6)).ConfigureAwait(false);
                List<string> ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "cpt_none" + i).ToList();
                ids.Add(captain.Id);

                await ExpectLimitAsync(fx, "/api/v1/captains/delete/multiple", new { Ids = ids }, "Ids", CollectionLimits.MaxRequestItems).ConfigureAwait(false);
                await ExpectMcpLimitAsync(fx, "delete_captains", new { ids = ids }).ConfigureAwait(false);
                using HttpResponseMessage still = await fx.AuthClient.GetAsync("/api/v1/captains/" + captain.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, still.StatusCode, "a rejected request deletes nothing");
            }));

            cases.Add(Case("harbor_create_rejects_capabilities_and_pages", "A Harbor create with 257 capabilities is a 400 CollectionLimitExceeded; Harbors page in the database without gaps", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<object> capabilities = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { Name = "tool-" + i, Available = true }).ToList();
                await ExpectLimitAsync(fx, "/api/v1/harbors", new { Name = "too-capable", Capabilities = capabilities }, "Capabilities", CollectionLimits.MaxConfigItems).ConfigureAwait(false);

                string prefix = "pg-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                for (int i = 0; i < 3; i++)
                {
                    using HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/harbors", JsonHelper.ToJsonContent(new { Name = prefix + "-" + i })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, created.StatusCode, "create harbor");
                }

                EnumerationResult<Harbor> first = await PostPageAsync<Harbor>(fx, "/api/v1/harbors/enumerate", new { PageNumber = 1, PageSize = 2 }).ConfigureAwait(false);
                AssertTrue(first.TotalRecords >= 3, "total counts every Harbor");
                AssertEqual(2, first.Objects.Count, "page size honored");
                HashSet<string> seen = new HashSet<string>();
                for (int page = 1; page <= first.TotalPages; page++)
                {
                    EnumerationResult<Harbor> result = await PostPageAsync<Harbor>(fx, "/api/v1/harbors/enumerate", new { PageNumber = page, PageSize = 2 }).ConfigureAwait(false);
                    foreach (Harbor h in result.Objects) AssertTrue(seen.Add(h.Id), "no Harbor on two pages");
                }

                AssertEqual(first.TotalRecords, (long)seen.Count, "every Harbor read once");
                EnumerationResult<Harbor> past = await PostPageAsync<Harbor>(fx, "/api/v1/harbors/enumerate", new { PageNumber = first.TotalPages + 5, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "past the end is empty");
            }));

            cases.Add(Case("model_endpoint_health_history_and_headers", "A model endpoint create stores no caller health history and rejects 257 headers with CollectionLimitExceeded", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<object> history = Enumerable.Range(0, 700).Select(i => (object)new { TimestampUtc = DateTime.UtcNow.AddMinutes(-i), Success = true }).ToList();
                object body = new { Name = "history-" + Guid.NewGuid().ToString("N").Substring(0, 6), Kind = "Inference", Provider = "Ollama", BaseUrl = "http://localhost:11434", Model = "qwen3", HealthHistory = history };
                using (HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/model-endpoints", JsonHelper.ToJsonContent(body)).ConfigureAwait(false))
                {
                    string text = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, created.StatusCode, text);
                    ModelEndpoint endpoint = JsonHelper.Deserialize<ModelEndpoint>(text);
                    AssertEqual(0, endpoint.HealthHistory.Count, "server-owned history");
                }

                List<object> headers = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { Name = "X-Extra-" + i, Value = "v" }).ToList();
                await ExpectLimitAsync(fx, "/api/v1/model-endpoints", new { Name = "headers", Kind = "Inference", Provider = "Ollama", BaseUrl = "http://localhost:11434", Model = "qwen3", Headers = headers }, "Headers", CollectionLimits.MaxConfigItems).ConfigureAwait(false);
            }));

            cases.Add(Case("persona_priorities_rejected_over_limit", "A persona create with 257 captain priorities is a 400 CollectionLimitExceeded and no persona is stored", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string name = "bounds-persona-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                List<object> priorities = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { Runtime = "Codex", Model = "m-" + i }).ToList();
                await ExpectLimitAsync(fx, "/api/v1/personas", new { Name = name, PromptTemplateName = "persona.worker", CaptainPriorities = priorities }, "CaptainPriorities", CollectionLimits.MaxConfigItems).ConfigureAwait(false);
                using HttpResponseMessage missing = await fx.AuthClient.GetAsync("/api/v1/personas/" + name).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, missing.StatusCode, "nothing stored");
            }));

            cases.Add(Case("captain_tools_return_a_page", "Captain tools answer with Tools as a page that honors pageSize and rejects an unknown sourceKind; the POST twin rejects a malformed body", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Captain captain = await CreateCaptainAsync(fx, "bounds-tools-" + Guid.NewGuid().ToString("N").Substring(0, 6)).ConfigureAwait(false);
                using (HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/captains/" + captain.Id + "/tools?pageSize=1&pageNumber=3").ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode, text);
                    CaptainToolAccessResult result = JsonHelper.Deserialize<CaptainToolAccessResult>(text);
                    AssertEqual(1, result.Tools.PageSize, "page size");
                    AssertEqual(3, result.Tools.PageNumber, "page number");
                    AssertTrue(result.Tools.Objects.Count <= 1, "one page");
                    AssertEqual((long)result.ToolCount, result.Tools.TotalRecords, "unfiltered total matches the count");
                }

                using (HttpResponseMessage bad = await fx.AuthClient.GetAsync("/api/v1/captains/" + captain.Id + "/tools?sourceKind=Nope").ConfigureAwait(false))
                    AssertEqual(HttpStatusCode.BadRequest, bad.StatusCode, "unknown sourceKind");
                using (HttpResponseMessage malformed = await fx.AuthClient.PostAsync("/api/v1/captains/" + captain.Id + "/tools/enumerate", new StringContent("{\"PageSize\":", Encoding.UTF8, "application/json")).ConfigureAwait(false))
                    AssertEqual(HttpStatusCode.BadRequest, malformed.StatusCode, "malformed body");
                using (HttpResponseMessage viaPost = await fx.AuthClient.PostAsync("/api/v1/captains/" + captain.Id + "/tools/enumerate", JsonHelper.ToJsonContent(new { PageSize = 2, SourceKind = "McpServer" })).ConfigureAwait(false))
                {
                    string text = await viaPost.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, viaPost.StatusCode, text);
                    CaptainToolAccessResult result = JsonHelper.Deserialize<CaptainToolAccessResult>(text);
                    AssertEqual(2, result.Tools.PageSize, "POST page size");
                    AssertEqual((long)result.McpToolCount, result.Tools.TotalRecords, "filtered total");
                }
            }));

            cases.Add(Case("mux_endpoints_return_a_page", "GET /runtimes/mux/endpoints answers with a page shape (Objects and totals), never the old Endpoints list", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string directory = TestTemp.NewDirectory("mux-bounds");
                using HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/runtimes/mux/endpoints?pageSize=3&configDirectory=" + Uri.EscapeDataString(directory)).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertFalse(text.Contains("\"Endpoints\"", StringComparison.Ordinal), "no whole Endpoints list: " + text);
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    MuxEndpointPage page = JsonHelper.Deserialize<MuxEndpointPage>(text);
                    AssertTrue(page.Objects.Count <= 3, "bounded page");
                    if (response.StatusCode == HttpStatusCode.OK) AssertEqual(3, page.PageSize, "page size echoed");
                }
                else
                {
                    AssertTrue((int)response.StatusCode >= 400, "mux unavailable maps to an error: " + text);
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Bounded captain, Harbor, runtime, endpoint, and persona APIs", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Captain> CreateCaptainAsync(E2EServerFixture fx, string name)
        {
            using HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = name, Runtime = "ClaudeCode" })).ConfigureAwait(false);
            string text = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(created.IsSuccessStatusCode, "create captain: " + text);
            return JsonHelper.Deserialize<Captain>(text);
        }

        private static async Task ExpectLimitAsync(E2EServerFixture fx, string route, object body, string field, int limit)
        {
            using HttpResponseMessage response = await fx.AuthClient.PostAsync(route, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, route + ": " + text);
            E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(text);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, route + ": " + text);
            AssertTrue(error.Data!.Field != null && error.Data.Field.EndsWith(field, StringComparison.Ordinal), route + " names " + field + ": " + error.Data.Field);
            AssertEqual(limit, error.Data.Limit, route + " limit");
        }

        private static async Task ExpectMcpLimitAsync(E2EServerFixture fx, string tool, object args)
        {
            using HttpClient mcp = new HttpClient();
            mcp.BaseAddress = fx.McpClient.BaseAddress;
            mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
            string rpc = await McpTestCalls.CallToolAsync(mcp, tool, args).ConfigureAwait(false);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
            string text = envelope.Result?.Content.FirstOrDefault()?.Text ?? "";
            McpToolError error = JsonHelper.Deserialize<McpToolError>(text);
            AssertEqual(McpToolErrorCodeEnum.InvalidArgument, error.ErrorCode, text);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Code, text);
        }

        private static async Task<EnumerationResult<T>> PostPageAsync<T>(E2EServerFixture fx, string route, object body)
        {
            using HttpResponseMessage response = await fx.AuthClient.PostAsync(route, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, route + ": " + text);
            return JsonHelper.Deserialize<EnumerationResult<T>>(text);
        }

        private TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, (CancellationToken ct) => body());
        }

        #endregion
    }
}
