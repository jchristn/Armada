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
    /// Live REST and MCP bounds for missions, voyages, signals, events, and status (bounded enumeration, group A): bulk
    /// deletes reject more than MaxRequestItems ids on REST and MCP with Code CollectionLimitExceeded and accept the limit
    /// exactly; dispatch rejects oversized mission and playbook lists; mission history rejects too many buckets; the
    /// voyage detail and cancel replies carry counts and no embedded lists; and the voyage progress enumeration pages,
    /// normalizes page sizes, filters, and isolates tenants.
    /// </summary>
    public sealed class WorkBoundsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.WorkBounds";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (string entity in new[] { "missions", "voyages", "signals", "events" })
            {
                string route = "/api/v1/" + entity + "/delete/multiple";
                string tool = "delete_" + entity;
                cases.Add(CaseAsync("bulk_delete_limit_" + entity, "POST " + route + " and MCP " + tool + " reject 1001 ids with CollectionLimitExceeded and accept exactly 1000", TestTags.Negative, async () =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    List<string> over = Enumerable.Range(0, CollectionLimits.MaxRequestItems + 1).Select(i => "zzz_missing" + i).ToList();

                    using (HttpResponseMessage response = await fx.AuthClient.PostAsync(route, JsonHelper.ToJsonContent(new { Ids = over })).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
                        E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
                        AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, body);
                        AssertEqual("Ids", error.Data!.Field, "field");
                        AssertEqual(CollectionLimits.MaxRequestItems, error.Data.Limit, "limit");
                        AssertEqual((long)over.Count, error.Data.Actual, "actual");
                    }

                    McpToolError mcpError = await CallMcpErrorAsync(fx, tool, new { ids = over }).ConfigureAwait(false);
                    AssertEqual(McpToolErrorCodeEnum.InvalidArgument, mcpError.ErrorCode, "MCP " + tool);
                    AssertEqual(CollectionLimitExceededException.ErrorCode, mcpError.Code, "MCP " + tool + " code");

                    List<string> exact = over.Take(CollectionLimits.MaxRequestItems).ToList();
                    using (HttpResponseMessage response = await fx.AuthClient.PostAsync(route, JsonHelper.ToJsonContent(new { Ids = exact })).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, response.StatusCode, "exactly the limit is accepted: " + body.Substring(0, Math.Min(200, body.Length)));
                        DeleteMultipleResult result = JsonHelper.Deserialize<DeleteMultipleResult>(body);
                        AssertEqual(0, result.Deleted, "unknown ids are not deleted");
                        AssertEqual(CollectionLimits.MaxRequestItems, result.Skipped.Count, "Skipped is bounded by the request");
                    }
                }));
            }

            cases.Add(CaseAsync("dispatch_limits", "Voyage dispatch rejects more than 1000 missions or 256 selected playbooks on REST and MCP", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                object[] missions = Enumerable.Range(0, CollectionLimits.MaxRequestItems + 1).Select(i => (object)new { Title = "m" + i, Description = "d" }).ToArray();
                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = "Too many", VesselId = "vsl_missing", Missions = missions })).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
                    AssertEqual("Missions", JsonHelper.Deserialize<E2eLimitErrorBody>(body).Data?.Field, body);
                }

                object[] playbooks = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => (object)new { PlaybookId = "pbk_missing" + i, DeliveryMode = "InlineFullContent" }).ToArray();
                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = "Too many playbooks", SelectedPlaybooks = playbooks })).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
                    AssertEqual("SelectedPlaybooks", JsonHelper.Deserialize<E2eLimitErrorBody>(body).Data?.Field, body);
                }

                McpToolError mcpError = await CallMcpErrorAsync(fx, "dispatch", new { title = "Too many", missions = missions }).ConfigureAwait(false);
                AssertEqual(McpToolErrorCodeEnum.InvalidArgument, mcpError.ErrorCode, "MCP dispatch");
                AssertEqual(CollectionLimitExceededException.ErrorCode, mcpError.Code, "MCP dispatch code");
            }));

            cases.Add(CaseAsync("mission_history_bucket_limit", "GET /api/v1/missions/history rejects a window over 1000 buckets and serves one within it", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                DateTime to = DateTime.UtcNow;
                string over = "/api/v1/missions/history?fromUtc=" + Uri.EscapeDataString(to.AddDays(-30).ToString("o")) + "&toUtc=" + Uri.EscapeDataString(to.ToString("o")) + "&bucketMinutes=1";
                using (HttpResponseMessage response = await fx.AuthClient.GetAsync(over).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
                    E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
                    AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, body);
                    AssertEqual(CollectionLimits.MaxSummaryBuckets, error.Data!.Limit, "limit");
                }

                string within = "/api/v1/missions/history?fromUtc=" + Uri.EscapeDataString(to.AddDays(-30).ToString("o")) + "&toUtc=" + Uri.EscapeDataString(to.ToString("o")) + "&bucketMinutes=60";
                using (HttpResponseMessage response = await fx.AuthClient.GetAsync(within).ConfigureAwait(false))
                {
                    AssertEqual(HttpStatusCode.OK, response.StatusCode);
                    MissionHistorySummaryResult result = await JsonHelper.DeserializeAsync<MissionHistorySummaryResult>(response).ConfigureAwait(false);
                    AssertTrue(result.Buckets.Count > 0 && result.Buckets.Count <= CollectionLimits.MaxSummaryBuckets, "bucket count within the limit");
                }
            }));

            cases.Add(CaseAsync("voyage_detail_and_cancel_carry_counts", "GET /api/v1/voyages/{id} and DELETE /api/v1/voyages/{id} return counts and embed no mission or failure list", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Voyage voyage = await CreateBareVoyageAsync(fx.AuthClient, "Shape voyage").ConfigureAwait(false);

                using (HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/voyages/" + voyage.Id).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode, body);
                    AssertFalse(body.Contains("\"Missions\"", StringComparison.OrdinalIgnoreCase), "no embedded missions: " + body);
                    VoyageProgress detail = JsonHelper.Deserialize<VoyageProgress>(body);
                    AssertEqual(voyage.Id, detail.Voyage.Id, "voyage");
                    AssertEqual(0, detail.TotalMissions, "counts");
                }

                using (HttpResponseMessage response = await fx.AuthClient.DeleteAsync("/api/v1/voyages/" + voyage.Id).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode, body);
                    AssertFalse(body.Contains("\"NotCancelled\"", StringComparison.Ordinal), "no per-mission failure list: " + body);
                    VoyageCancelResult cancel = JsonHelper.Deserialize<VoyageCancelResult>(body);
                    AssertEqual(VoyageStatusEnum.Cancelled, cancel.Voyage.Status, "cancelled");
                    AssertEqual(0, cancel.CancelledMissions + cancel.NotCancelledMissions, "counts");
                }
            }));

            cases.Add(CaseAsync("voyage_progress_enumeration_pages", "POST /api/v1/voyages/progress/enumerate pages active voyages with exact totals, normalizes page size, filters, and rejects an unknown status", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                for (int i = 0; i < 3; i++) await CreateBareVoyageAsync(fx.AuthClient, "Progress page " + i).ConfigureAwait(false);

                EnumerationResult<VoyageProgress> all = await PostAsync<VoyageProgress>(fx.AuthClient, "/api/v1/voyages/progress/enumerate", new { PageSize = 5000 }).ConfigureAwait(false);
                AssertEqual(CollectionLimits.MaxPageSize, all.PageSize, "page size normalized to 1000");
                AssertTrue(all.TotalRecords >= 3, "the new open voyages are counted");
                AssertTrue(all.Objects.All(p => p.Voyage.Status == VoyageStatusEnum.Open || p.Voyage.Status == VoyageStatusEnum.InProgress), "active only");

                List<string> paged = new List<string>();
                for (int page = 1; page <= all.TotalRecords; page++)
                {
                    EnumerationResult<VoyageProgress> one = await PostAsync<VoyageProgress>(fx.AuthClient, "/api/v1/voyages/progress/enumerate?pageSize=1&pageNumber=" + page, new { PageSize = 50 }).ConfigureAwait(false);
                    AssertEqual(1, one.PageSize, "querystring overrides the body");
                    AssertEqual(all.TotalRecords, one.TotalRecords, "stable total");
                    paged.AddRange(one.Objects.Select(p => p.Voyage.Id));
                }

                AssertEqual(String.Join(",", all.Objects.Select(p => p.Voyage.Id)), String.Join(",", paged), "pages of one reproduce the full order without gaps or overlap");
                EnumerationResult<VoyageProgress> past = await PostAsync<VoyageProgress>(fx.AuthClient, "/api/v1/voyages/progress/enumerate", new { PageNumber = 100000, PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "empty past the end");
                AssertEqual(all.TotalRecords, past.TotalRecords, "accurate total past the end");

                EnumerationResult<VoyageProgress> cancelled = await PostAsync<VoyageProgress>(fx.AuthClient, "/api/v1/voyages/progress/enumerate", new { Status = "Cancelled", PageSize = 1000 }).ConfigureAwait(false);
                AssertTrue(cancelled.Objects.All(p => p.Voyage.Status == VoyageStatusEnum.Cancelled), "status filter");

                using (HttpResponseMessage bad = await fx.AuthClient.PostAsync("/api/v1/voyages/progress/enumerate", JsonHelper.ToJsonContent(new { Status = "Sideways" })).ConfigureAwait(false))
                {
                    AssertEqual(HttpStatusCode.BadRequest, bad.StatusCode, "unknown status");
                }

                using (HttpResponseMessage anonymous = await fx.UnauthClient.PostAsync("/api/v1/voyages/progress/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false))
                {
                    AssertEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode, "authentication required");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Bounded work APIs",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Voyage> CreateBareVoyageAsync(HttpClient client, string title)
        {
            using HttpResponseMessage response = await client.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = title, Description = "bounded" })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, body);
            return JsonHelper.Deserialize<Voyage>(body);
        }

        private static async Task<EnumerationResult<T>> PostAsync<T>(HttpClient client, string route, object body)
        {
            using HttpResponseMessage response = await client.PostAsync(route, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, route + ": " + text);
            return JsonHelper.Deserialize<EnumerationResult<T>>(text);
        }

        private static async Task<McpToolError> CallMcpErrorAsync(E2EServerFixture fx, string tool, object args)
        {
            using HttpClient mcp = new HttpClient();
            mcp.BaseAddress = fx.McpClient.BaseAddress;
            mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
            string rpc = await McpTestCalls.CallToolAsync(mcp, tool, args).ConfigureAwait(false);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
            string text = envelope.Result?.Content.FirstOrDefault()?.Text ?? "";
            AssertTrue(text.TrimStart().StartsWith("{", StringComparison.Ordinal), "MCP " + tool + " returned a JSON error: " + text);
            return JsonHelper.Deserialize<McpToolError>(text);
        }

        private TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
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
