namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Ask Armada, planning, refinement, objective, and memory surfaces over a live server (docs/ENUMERATION.md):
    /// details carry counts instead of embedded collections, the new paged enumerations page in the database with exact
    /// totals and normalized page sizes, they are owner-scoped like the parent read, and lists over their limit are a
    /// typed CollectionLimitExceeded error on REST and MCP instead of being stored or truncated.
    /// </summary>
    public sealed class BoundedAskPlanningApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.BoundedAskPlanning";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("ask_collections_paged_and_owner_scoped", "Thread detail, messages, and snapshots carry counts; tracked work, proposals, tool calls, and work rows page with exact totals and are 404 to another user", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndask").ConfigureAwait(false);
                E2ETenantUser bob = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndaskb", true, alice.TenantId).ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                using HttpClient b = bob.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);

                string messageId;
                string voyageWorkId;
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        AskTrackedWork work = new AskTrackedWork { TenantId = alice.TenantId, UserId = alice.UserId, ThreadId = thread.Id, EntityType = AskTrackedEntityTypeEnum.Mission, EntityId = "msn_bnd_missing" + i, Title = "missing " + i };
                        await db.AskTrackedWork.CreateOrGetAsync(work).ConfigureAwait(false);
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        AskActionProposal proposal = new AskActionProposal { TenantId = alice.TenantId, UserId = alice.UserId, ThreadId = thread.Id, ToolName = "status", SummaryText = "p" + i };
                        proposal = await db.AskActionProposals.CreateAsync(proposal).ConfigureAwait(false);
                        if (i == 0) await db.AskActionProposals.TryTransitionAsync(alice.TenantId, proposal.Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Rejected, alice.UserId).ConfigureAwait(false);
                    }

                    AskMessage message = new AskMessage { TenantId = alice.TenantId, UserId = alice.UserId, ThreadId = thread.Id, Role = AskMessageRoleEnum.Assistant, ContentText = "Used tools." };
                    message = await db.AskMessages.CreateAsync(message, false).ConfigureAwait(false);
                    messageId = message.Id;
                    List<AskMessageToolCall> calls = Enumerable.Range(0, 5).Select(i => new AskMessageToolCall { TenantId = alice.TenantId, UserId = alice.UserId, ThreadId = thread.Id, MessageId = message.Id, CallId = "call_" + i, ToolName = "status" }).ToList();
                    await db.AskMessageToolCalls.CreateManyAsync(calls).ConfigureAwait(false);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("bounded-ask", "https://example.invalid/bounded-ask.git") { TenantId = alice.TenantId, UserId = alice.UserId }).ConfigureAwait(false);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("Bounded voyage") { TenantId = alice.TenantId, UserId = alice.UserId }).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                        await db.Missions.CreateAsync(new Mission("Bounded mission " + i) { TenantId = alice.TenantId, UserId = alice.UserId, VesselId = vessel.Id, VoyageId = voyage.Id }).ConfigureAwait(false);
                    AskTrackedWork voyageWork = await db.AskTrackedWork.CreateOrGetAsync(new AskTrackedWork { TenantId = alice.TenantId, UserId = alice.UserId, ThreadId = thread.Id, EntityType = AskTrackedEntityTypeEnum.Voyage, EntityId = voyage.Id, Title = "Bounded voyage" }).ConfigureAwait(false);
                    voyageWorkId = voyageWork.Id;
                }

                string root = "/api/v1/ask/threads/" + thread.Id;
                string detailJson = await (await a.GetAsync(root).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
                AskThreadDetail detail = JsonHelper.Deserialize<AskThreadDetail>(detailJson);
                AssertEqual(6L, detail.TrackedWorkCount, "tracked work counted");
                AssertEqual(3L, detail.PendingProposalCount, "pending proposals counted");
                foreach (string embedded in new[] { "\"TrackedWork\"", "\"PendingProposals\"", "\"PendingCliPermissions\"" })
                    AssertFalse(detailJson.Contains(embedded, StringComparison.Ordinal), "the detail embeds no " + embedded);

                List<string> seen = new List<string>();
                for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
                {
                    EnumerationResult<AskTrackedWork> page = await PostPageAsync<AskTrackedWork>(a, root + "/work/enumerate", new { PageNumber = pageNumber, PageSize = 2 }).ConfigureAwait(false);
                    AssertEqual(6L, page.TotalRecords, "work total on page " + pageNumber);
                    AssertEqual(3, page.TotalPages, "work pages");
                    seen.AddRange(page.Objects.Select(w => w.Id));
                }

                AssertEqual(6, seen.Distinct().Count(), "pages cover every tracked item once");
                EnumerationResult<AskTrackedWork> past = await PostPageAsync<AskTrackedWork>(a, root + "/work/enumerate", new { PageNumber = 9, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "a page past the end is empty");
                AssertEqual(6L, past.TotalRecords, "with exact totals");
                EnumerationResult<AskTrackedWork> huge = await PostPageAsync<AskTrackedWork>(a, root + "/work/enumerate?pageSize=50000", new { }).ConfigureAwait(false);
                AssertEqual(CollectionLimits.MaxPageSize, huge.PageSize, "an oversized page size is normalized to 1000");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync(root + "/work/enumerate", JsonHelper.ToJsonContent(new { Status = "Bogus" })).ConfigureAwait(false));

                EnumerationResult<AskActionProposal> pending = await PostPageAsync<AskActionProposal>(a, root + "/proposals/enumerate", new { Status = "Pending", PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, pending.TotalRecords, "pending filter counted before paging");
                AssertEqual(2, pending.Objects.Count, "first page");

                string pageJson = await (await a.PostAsync(root + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
                AskMessagePage messages = JsonHelper.Deserialize<AskMessagePage>(pageJson);
                AssertEqual(5, messages.Messages.Single(m => m.Id == messageId).ToolCallCount, "the message counts its tool calls");
                AssertFalse(pageJson.Contains("\"ToolCalls\"", StringComparison.Ordinal), "messages embed no tool calls");
                EnumerationResult<AskMessageToolCall> callPage = await PostPageAsync<AskMessageToolCall>(a, root + "/messages/" + messageId + "/tool-calls/enumerate", new { PageSize = 2, Order = "CreatedAscending" }).ConfigureAwait(false);
                AssertEqual(5L, callPage.TotalRecords, "tool calls paged");
                AssertEqual(2, callPage.Objects.Count, "page of two");
                AssertEqual(0L, (await PostPageAsync<AskMessageToolCall>(a, root + "/turn/tool-calls/enumerate", new { }).ConfigureAwait(false)).TotalRecords, "an idle thread has no running-turn tool calls");

                string snapshotJson = await (await a.GetAsync(root + "/work/" + voyageWorkId).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
                AskWorkSnapshot snapshot = JsonHelper.Deserialize<AskWorkSnapshot>(snapshotJson);
                AssertEqual(3, snapshot.MissionCount, "the snapshot counts its mission rows");
                AssertFalse(snapshotJson.Contains("\"Missions\"", StringComparison.Ordinal) || snapshotJson.Contains("\"Targets\"", StringComparison.Ordinal), "the snapshot embeds no rows");
                EnumerationResult<AskWorkMissionSnapshot> rows = await PostPageAsync<AskWorkMissionSnapshot>(a, root + "/work/" + voyageWorkId + "/missions/enumerate", new { PageSize = 2, Order = "CreatedAscending" }).ConfigureAwait(false);
                AssertEqual(3L, rows.TotalRecords, "mission rows paged in the database");
                AssertEqual(2, rows.Objects.Count, "page of two");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync(root + "/work/" + voyageWorkId + "/targets/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));

                foreach (string path in new[] { "/work/enumerate", "/proposals/enumerate", "/messages/" + messageId + "/tool-calls/enumerate", "/turn/tool-calls/enumerate", "/work/" + voyageWorkId + "/missions/enumerate" })
                    AssertStatusCode(HttpStatusCode.NotFound, await b.PostAsync(root + path, JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));

                using HttpClient mcpA = McpAs(fx, alice);
                using HttpClient mcpB = McpAs(fx, bob);
                EnumerationResult<AskTrackedWork> mcpWork = JsonHelper.Deserialize<EnumerationResult<AskTrackedWork>>(await ToolTextAsync(mcpA, "list_ask_work", new { threadId = thread.Id, pageSize = 2 }).ConfigureAwait(false));
                AssertEqual(6L, mcpWork.TotalRecords, "MCP list_ask_work pages with the same totals");
                AssertEqual(2, mcpWork.Objects.Count, "MCP page size");
                EnumerationResult<AskMessageToolCall> mcpCalls = JsonHelper.Deserialize<EnumerationResult<AskMessageToolCall>>(await ToolTextAsync(mcpA, "list_ask_tool_calls", new { threadId = thread.Id, messageId = messageId, pageSize = 10 }).ConfigureAwait(false));
                AssertEqual(5, mcpCalls.Objects.Count, "MCP list_ask_tool_calls");
                EnumerationResult<AskWorkMissionSnapshot> mcpRows = JsonHelper.Deserialize<EnumerationResult<AskWorkMissionSnapshot>>(await ToolTextAsync(mcpA, "list_ask_work_missions", new { threadId = thread.Id, workId = voyageWorkId }).ConfigureAwait(false));
                AssertEqual(3L, mcpRows.TotalRecords, "MCP list_ask_work_missions");
                string mcpDetail = await ToolTextAsync(mcpA, "get_ask_thread", new { threadId = thread.Id }).ConfigureAwait(false);
                AssertFalse(mcpDetail.Contains("\"trackedWork\"", StringComparison.OrdinalIgnoreCase), "MCP get_ask_thread embeds no tracked work: " + mcpDetail);
                McpToolError notFound = JsonHelper.Deserialize<McpToolError>(await ToolTextAsync(mcpB, "list_ask_proposals", new { threadId = thread.Id }).ConfigureAwait(false));
                AssertEqual(McpToolErrorCodeEnum.NotFound, notFound.ErrorCode, "another user's thread is not found over MCP");
            }));

            cases.Add(CaseAsync("ask_bulk_delete_limit", "Deleting more than 1000 threads at once is a typed limit error on REST and MCP and deletes nothing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndaskdel").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                List<string> ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "ath_none" + i).ToList();
                ids.Add(thread.Id);
                await AssertRestLimitAsync(await a.PostAsync("/api/v1/ask/threads/delete/multiple", JsonHelper.ToJsonContent(new { Ids = ids })).ConfigureAwait(false), "Ids", CollectionLimits.MaxRequestItems).ConfigureAwait(false);
                using HttpClient mcp = McpAs(fx, alice);
                AssertMcpLimit(await ToolTextAsync(mcp, "delete_ask_threads", new { threadIds = ids }).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.OK, await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false), "nothing was deleted");
            }));

            cases.Add(CaseAsync("objective_and_memory_list_limits", "Objective config lists stop at 256, link id lists at 1000, reorder at 1000 items, and memory tags at 256, on REST and MCP", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpClient admin = fx.AuthClient;
                List<string> atLimit = Enumerable.Range(0, CollectionLimits.MaxConfigItems).Select(i => "tag" + i).ToList();
                List<string> overLimit = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => "tag" + i).ToList();

                HttpResponseMessage ok = await admin.PostAsync("/api/v1/objectives", JsonHelper.ToJsonContent(new { Title = "Bounded tags", Tags = atLimit })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, ok, "exactly MaxConfigItems tags are accepted");
                Objective objective = await JsonHelper.DeserializeAsync<Objective>(ok).ConfigureAwait(false);
                AssertEqual(CollectionLimits.MaxConfigItems, objective.Tags.Count, "every tag stored");

                await AssertRestLimitAsync(await admin.PostAsync("/api/v1/objectives", JsonHelper.ToJsonContent(new { Title = "Too many tags", Tags = overLimit })).ConfigureAwait(false), "Tags", CollectionLimits.MaxConfigItems).ConfigureAwait(false);
                await AssertRestLimitAsync(await admin.PostAsync("/api/v1/backlog", JsonHelper.ToJsonContent(new { Title = "Too many criteria", AcceptanceCriteria = overLimit })).ConfigureAwait(false), "AcceptanceCriteria", CollectionLimits.MaxConfigItems).ConfigureAwait(false);
                List<string> vessels = Enumerable.Range(0, CollectionLimits.MaxLinkedIds + 1).Select(i => "vsl_none" + i).ToList();
                await AssertRestLimitAsync(await admin.PutAsync("/api/v1/objectives/" + objective.Id, JsonHelper.ToJsonContent(new { VesselIds = vessels })).ConfigureAwait(false), "VesselIds", CollectionLimits.MaxLinkedIds).ConfigureAwait(false);
                List<object> items = Enumerable.Range(0, CollectionLimits.MaxRequestItems + 1).Select(i => (object)new { ObjectiveId = "obj_none" + i, Rank = i }).ToList();
                await AssertRestLimitAsync(await admin.PostAsync("/api/v1/backlog/reorder", JsonHelper.ToJsonContent(new { Items = items })).ConfigureAwait(false), "Items", CollectionLimits.MaxRequestItems).ConfigureAwait(false);

                ObjectiveReorderResult reordered = await JsonHelper.DeserializeAsync<ObjectiveReorderResult>(await admin.PostAsync("/api/v1/objectives/reorder", JsonHelper.ToJsonContent(new { Items = new[] { new { ObjectiveId = objective.Id, Rank = 42 } } })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1, reordered.Updated, "reorder counts what it changed");
                AssertEqual(42, reordered.Items.Single().Rank, "and returns the applied ranks, not the objectives");

                await AssertRestLimitAsync(await admin.PostAsync("/api/v1/memories", JsonHelper.ToJsonContent(new { Content = "Too many tags", Tags = overLimit })).ConfigureAwait(false), "Tags", CollectionLimits.MaxConfigItems).ConfigureAwait(false);

                using HttpClient mcp = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
                mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                AssertMcpLimit(await ToolTextAsync(mcp, "create_backlog_item", new { title = "Too many tags", tags = overLimit }).ConfigureAwait(false));
                AssertMcpLimit(await ToolTextAsync(mcp, "update_objective", new { objectiveId = objective.Id, vesselIds = vessels }).ConfigureAwait(false));
                AssertMcpLimit(await ToolTextAsync(mcp, "reorder_objectives", new { items = Enumerable.Range(0, CollectionLimits.MaxRequestItems + 1).Select(i => new { objectiveId = "obj_none" + i, rank = i }).ToList() }).ConfigureAwait(false));
                AssertMcpLimit(await ToolTextAsync(mcp, "create_memory", new { content = "Too many tags", tags = overLimit }).ConfigureAwait(false));

                Objective stored = await JsonHelper.DeserializeAsync<Objective>(await admin.GetAsync("/api/v1/objectives/" + objective.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(0, stored.VesselIds.Count, "a rejected update changes nothing");
                await admin.DeleteAsync("/api/v1/objectives/" + objective.Id).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("planning_and_refinement_transcripts_paged", "Planning and refinement details count their transcript; the transcript pages newest first on REST and MCP and is 404 to another tenant", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndplan").ConfigureAwait(false);
                E2ETenantUser carol = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndplanc").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                using HttpClient c = carol.CreateClient(fx.BaseUrl);

                string planningId;
                string refinementId;
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    Captain captain = await db.Captains.CreateAsync(new Captain("bounded-planner") { TenantId = alice.TenantId, UserId = alice.UserId }).ConfigureAwait(false);
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("bounded-plan", "https://example.invalid/bounded-plan.git") { TenantId = alice.TenantId, UserId = alice.UserId }).ConfigureAwait(false);
                    PlanningSession planning = new PlanningSession { TenantId = alice.TenantId, UserId = alice.UserId, CaptainId = captain.Id, VesselId = vessel.Id, Title = "Bounded plan", Status = PlanningSessionStatusEnum.Stopped };
                    planning = await db.PlanningSessions.CreateAsync(planning).ConfigureAwait(false);
                    planningId = planning.Id;
                    for (int i = 1; i <= 15; i++)
                        await db.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage { PlanningSessionId = planning.Id, TenantId = alice.TenantId, UserId = alice.UserId, Role = i % 2 == 1 ? "User" : "Assistant", Sequence = i, Content = "plan " + i }).ConfigureAwait(false);

                    Objective objective = await db.Objectives.CreateAsync(new Objective { Title = "Bounded refine", TenantId = alice.TenantId, UserId = alice.UserId }).ConfigureAwait(false);
                    ObjectiveRefinementSession refinement = new ObjectiveRefinementSession { ObjectiveId = objective.Id, TenantId = alice.TenantId, UserId = alice.UserId, CaptainId = captain.Id, Title = "Bounded refinement", Status = ObjectiveRefinementSessionStatusEnum.Stopped };
                    refinement = await db.ObjectiveRefinementSessions.CreateAsync(refinement).ConfigureAwait(false);
                    refinementId = refinement.Id;
                    for (int i = 1; i <= 12; i++)
                        await db.ObjectiveRefinementMessages.CreateAsync(new ObjectiveRefinementMessage { ObjectiveRefinementSessionId = refinement.Id, ObjectiveId = objective.Id, TenantId = alice.TenantId, UserId = alice.UserId, Role = "User", Sequence = i, Content = "refine " + i }).ConfigureAwait(false);
                }

                string planningJson = await (await a.GetAsync("/api/v1/planning-sessions/" + planningId).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertEqual(15L, JsonHelper.Deserialize<PlanningSessionDetail>(planningJson).MessageCount, "planning detail counts the transcript");
                AssertFalse(planningJson.Contains("\"Messages\"", StringComparison.Ordinal), "planning detail embeds no transcript");
                EnumerationResult<PlanningSessionMessage> newest = await PostPageAsync<PlanningSessionMessage>(a, "/api/v1/planning-sessions/" + planningId + "/messages/enumerate", new { PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(15L, newest.TotalRecords, "transcript length");
                AssertEqual("plan 15", newest.Objects.First().Content, "newest first");
                EnumerationResult<PlanningSessionMessage> older = await PostPageAsync<PlanningSessionMessage>(a, "/api/v1/planning-sessions/" + planningId + "/messages/enumerate", new { PageNumber = 2, PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(5, older.Objects.Count, "final page");
                AssertEqual(15, newest.Objects.Concat(older.Objects).Select(m => m.Id).Distinct().Count(), "every message once");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/planning-sessions/" + planningId + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));
                EnumerationResult<PlanningSession> sessions = await PostPageAsync<PlanningSession>(a, "/api/v1/planning-sessions/enumerate", new { Status = "Stopped" }).ConfigureAwait(false);
                AssertTrue(sessions.Objects.Any(s => s.Id == planningId), "planning sessions page in the database with the status filter");
                AssertFalse((await PostPageAsync<PlanningSession>(c, "/api/v1/planning-sessions/enumerate", new { PageSize = 1000 }).ConfigureAwait(false)).Objects.Any(s => s.Id == planningId), "another tenant does not see it");

                string refinementJson = await (await a.GetAsync("/api/v1/objective-refinement-sessions/" + refinementId).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertEqual(12L, JsonHelper.Deserialize<ObjectiveRefinementSessionDetail>(refinementJson).MessageCount, "refinement detail counts the transcript");
                AssertFalse(refinementJson.Contains("\"Messages\"", StringComparison.Ordinal), "refinement detail embeds no transcript");
                EnumerationResult<ObjectiveRefinementMessage> oldest = await PostPageAsync<ObjectiveRefinementMessage>(a, "/api/v1/objective-refinement-sessions/" + refinementId + "/messages/enumerate", new { PageSize = 5, Order = "CreatedAscending" }).ConfigureAwait(false);
                AssertEqual(12L, oldest.TotalRecords, "refinement transcript length");
                AssertEqual("refine 1", oldest.Objects.First().Content, "oldest first on request");
                AssertStatusCode(HttpStatusCode.NotFound, await c.PostAsync("/api/v1/objective-refinement-sessions/" + refinementId + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));

                using HttpClient mcpA = McpAs(fx, alice);
                EnumerationResult<PlanningSessionMessage> mcpPlanning = JsonHelper.Deserialize<EnumerationResult<PlanningSessionMessage>>(await ToolTextAsync(mcpA, "list_backlog_planning_session_messages", new { sessionId = planningId, pageSize = 4 }).ConfigureAwait(false));
                AssertEqual(15L, mcpPlanning.TotalRecords, "MCP planning transcript total");
                AssertEqual(4, mcpPlanning.Objects.Count, "MCP planning page size");
                EnumerationResult<ObjectiveRefinementMessage> mcpRefinement = JsonHelper.Deserialize<EnumerationResult<ObjectiveRefinementMessage>>(await ToolTextAsync(mcpA, "list_backlog_refinement_session_messages", new { sessionId = refinementId, pageSize = 4 }).ConfigureAwait(false));
                AssertEqual(12L, mcpRefinement.TotalRecords, "MCP refinement transcript total");
                string mcpPlanningDetail = await ToolTextAsync(mcpA, "get_backlog_planning_session", new { sessionId = planningId }).ConfigureAwait(false);
                AssertFalse(mcpPlanningDetail.Contains("\"messages\"", StringComparison.OrdinalIgnoreCase), "MCP planning detail embeds no transcript: " + mcpPlanningDetail);
                AssertTrue(mcpPlanningDetail.Contains("linkedObjectiveCount", StringComparison.OrdinalIgnoreCase), "MCP planning detail counts its linked objectives");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Bounded Ask, planning, refinement, objective, and memory collections", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<EnumerationResult<T>> PostPageAsync<T>(HttpClient client, string path, object body)
        {
            HttpResponseMessage response = await client.PostAsync(path, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, path + ": " + text);
            return JsonHelper.Deserialize<EnumerationResult<T>>(text);
        }

        private static async Task AssertRestLimitAsync(HttpResponseMessage response, string field, int limit)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, body);
            AssertTrue(error.Data!.Field.EndsWith(field, StringComparison.OrdinalIgnoreCase), "field " + error.Data.Field + " names " + field);
            AssertEqual(limit, error.Data.Limit, "limit");
        }

        private static void AssertMcpLimit(string text)
        {
            AssertTrue(text.TrimStart().StartsWith("{", StringComparison.Ordinal), "a typed MCP error: " + text);
            McpToolError error = JsonHelper.Deserialize<McpToolError>(text);
            AssertEqual(McpToolErrorCodeEnum.InvalidArgument, error.ErrorCode, text);
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Code, text);
        }

        private static HttpClient McpAs(E2EServerFixture fx, E2ETenantUser user)
        {
            HttpClient client = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.BearerToken);
            return client;
        }

        private static async Task<string> ToolTextAsync(HttpClient mcp, string tool, object arguments)
        {
            string rpc = await McpTestCalls.CallToolAsync(mcp, tool, arguments).ConfigureAwait(false);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
            return envelope.Result?.Content.FirstOrDefault()?.Text ?? rpc;
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseDriver db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") }, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
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
