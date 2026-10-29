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
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage of the Ask Armada thread REST API through the real server: owner scoping (another user's or
    /// tenant's thread is 404), update semantics, messages, quick actions executed through the real MCP tool handlers,
    /// work tracking with embedded snapshots, and owner-only delivery of ask.* WebSocket events.
    /// </summary>
    public sealed class AskThreadsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.AskThreads";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("crud_and_owner_scoping", "Threads are private: another user's or tenant's thread is 404", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "alice").ConfigureAwait(false);
                E2ETenantUser bob = await E2ETenantUser.CreateAsync(fx.AuthClient, "bob", true, alice.TenantId).ConfigureAwait(false);
                E2ETenantUser carol = await E2ETenantUser.CreateAsync(fx.AuthClient, "carol").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                using HttpClient b = bob.CreateClient(fx.BaseUrl);
                using HttpClient c = carol.CreateClient(fx.BaseUrl);

                HttpResponseMessage created = await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(created).ConfigureAwait(false);
                AssertEqual("New conversation", thread.Title);

                AssertStatusCode(HttpStatusCode.OK, await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false));
                foreach (HttpClient stranger in new[] { b, c, fx.AuthClient })
                {
                    AssertStatusCode(HttpStatusCode.NotFound, await stranger.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.NotFound, await stranger.PutAsync("/api/v1/ask/threads/" + thread.Id, JsonHelper.ToJsonContent(new { Title = "x" })).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.NotFound, await stranger.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.NotFound, await stranger.PostAsync("/api/v1/ask/threads/" + thread.Id + "/actions", JsonHelper.ToJsonContent(new { ToolName = "status" })).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.NotFound, await stranger.DeleteAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false));
                    EnumerationResult<AskThread> theirs = await JsonHelper.DeserializeAsync<EnumerationResult<AskThread>>(await stranger.PostAsync("/api/v1/ask/threads/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                    AssertFalse(theirs.Objects.Any(t => t.Id == thread.Id), "not listed for others");
                }

                EnumerationResult<AskThread> mine = await JsonHelper.DeserializeAsync<EnumerationResult<AskThread>>(await a.PostAsync("/api/v1/ask/threads/enumerate", JsonHelper.ToJsonContent(new { PageSize = 10 })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertTrue(mine.Objects.Any(t => t.Id == thread.Id), "listed for the owner");

                AssertStatusCode(HttpStatusCode.Unauthorized, await new HttpClient { BaseAddress = new Uri(fx.BaseUrl) }.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.NoContent, await a.DeleteAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("bulk_delete_owner_scoped", "POST /api/v1/ask/threads/delete/multiple deletes the caller's threads and skips another user's or an unknown ID with a reason", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "bulkdel").ConfigureAwait(false);
                E2ETenantUser bob = await E2ETenantUser.CreateAsync(fx.AuthClient, "bulkdelb", true, alice.TenantId).ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                using HttpClient b = bob.CreateClient(fx.BaseUrl);

                AskThread first = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { Title = "bulk one" })).ConfigureAwait(false)).ConfigureAwait(false);
                AskThread second = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { Title = "bulk two" })).ConfigureAwait(false)).ConfigureAwait(false);
                AskThread theirs = await JsonHelper.DeserializeAsync<AskThread>(await b.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { Title = "bob private" })).ConfigureAwait(false)).ConfigureAwait(false);

                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/ask/threads/delete/multiple", JsonHelper.ToJsonContent(new { Ids = Array.Empty<string>() })).ConfigureAwait(false));
                AskThreadDeleteMultipleResult empty = await JsonHelper.DeserializeAsync<AskThreadDeleteMultipleResult>(
                    await a.PostAsync("/api/v1/ask/threads/delete/multiple", JsonHelper.ToJsonContent(new { Ids = new[] { "" } })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(0, empty.Deleted, "an empty ID deletes nothing");
                AssertTrue(empty.Skipped.Any(s => s.Id == "" && s.Reason == "Empty ID"), "an empty ID is skipped with its reason");
                AssertTrue(empty.Status == DeleteMultipleStatusEnum.NoneDeleted, "nothing deleted status: " + empty.Status);

                HttpResponseMessage response = await a.PostAsync(
                    "/api/v1/ask/threads/delete/multiple",
                    JsonHelper.ToJsonContent(new { Ids = new[] { first.Id, theirs.Id, "ath_missing" } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                AskThreadDeleteMultipleResult result = await JsonHelper.DeserializeAsync<AskThreadDeleteMultipleResult>(response).ConfigureAwait(false);
                AssertEqual(1, result.Deleted, "one of the caller's threads deleted");
                AssertTrue(result.DeletedIds.SequenceEqual(new[] { first.Id }), "the response names the deleted ID: " + String.Join(",", result.DeletedIds));
                AssertEqual(2, result.Skipped.Count, "the stranger's and the unknown ID are skipped");
                AssertTrue(result.Skipped.Any(s => s.Id == theirs.Id && s.Reason == "Not found"), "another user's thread is skipped as not found");
                AssertTrue(result.Skipped.Any(s => s.Id == "ath_missing" && s.Reason == "Not found"), "the unknown ID is skipped as not found");
                AssertTrue(result.Status == DeleteMultipleStatusEnum.PartiallyDeleted, "partial delete status: " + result.Status);

                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/ask/threads/" + first.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.OK, await b.GetAsync("/api/v1/ask/threads/" + theirs.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.OK, await a.GetAsync("/api/v1/ask/threads/" + second.Id).ConfigureAwait(false));

                AskThreadDeleteMultipleResult all = await JsonHelper.DeserializeAsync<AskThreadDeleteMultipleResult>(
                    await a.PostAsync("/api/v1/ask/threads/delete/multiple", JsonHelper.ToJsonContent(new { Ids = new[] { second.Id } })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1, all.Deleted, "the second thread deleted");
                AssertEqual(0, all.Skipped.Count, "nothing skipped");
                AssertTrue(all.Status == DeleteMultipleStatusEnum.Deleted, "clean delete status: " + all.Status);
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/ask/threads/" + second.Id).ConfigureAwait(false));

                AssertStatusCode(HttpStatusCode.Unauthorized, await new HttpClient { BaseAddress = new Uri(fx.BaseUrl) }.PostAsync("/api/v1/ask/threads/delete/multiple", JsonHelper.ToJsonContent(new { Ids = new[] { theirs.Id } })).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.OK, await b.GetAsync("/api/v1/ask/threads/" + theirs.Id).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("update_semantics", "PUT changes only sent fields; CaptainId null clears the captain", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "upd").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                HttpResponseMessage captainResp = await a.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = "ask-e2e-captain", Runtime = "ClaudeCode" })).ConfigureAwait(false);
                captainResp.EnsureSuccessStatusCode();
                Captain captain = await JsonHelper.DeserializeAsync<Captain>(captainResp).ConfigureAwait(false);

                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { CaptainId = captain.Id })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(captain.Id, thread.CaptainId);

                AskThread renamed = await JsonHelper.DeserializeAsync<AskThread>(await a.PutAsync("/api/v1/ask/threads/" + thread.Id, JsonHelper.ToJsonContent(new { Title = "Renamed", Pinned = true })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual("Renamed", renamed.Title);
                AssertTrue(renamed.Pinned, "pinned");
                AssertEqual(captain.Id, renamed.CaptainId, "absent captain kept");

                HttpResponseMessage cleared = await a.PutAsync("/api/v1/ask/threads/" + thread.Id, new StringContent("{\"CaptainId\":null}", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AskThread clearedThread = await JsonHelper.DeserializeAsync<AskThread>(cleared).ConfigureAwait(false);
                AssertNull(clearedThread.CaptainId, "explicit null clears");
                AssertEqual("Renamed", clearedThread.Title, "title kept");

                AssertStatusCode(HttpStatusCode.BadRequest, await a.PutAsync("/api/v1/ask/threads/" + thread.Id, JsonHelper.ToJsonContent(new { CaptainId = "cpt_missing" })).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("messages_and_quick_action_events_owner_only", "Messages and quick actions work, and ask.* events reach only the owner's sockets", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "evta").ConfigureAwait(false);
                E2ETenantUser bob = await E2ETenantUser.CreateAsync(fx.AuthClient, "evtb", true, alice.TenantId).ConfigureAwait(false);
                E2ETenantUser carol = await E2ETenantUser.CreateAsync(fx.AuthClient, "evtc").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);

                using WebSocketTestClient aliceSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, alice.BearerToken).ConfigureAwait(false);
                using WebSocketTestClient bobSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, bob.BearerToken).ConfigureAwait(false);
                using WebSocketTestClient carolSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, carol.BearerToken).ConfigureAwait(false);
                using WebSocketTestClient adminSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }).ConfigureAwait(false);
                foreach (WebSocketTestClient s in new[] { aliceSocket, bobSocket, carolSocket })
                {
                    await s.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
                    AssertNotNull(await s.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "subscribed");
                }

                await adminSocket.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe", ["AllTenants"] = true }).ConfigureAwait(false);
                AssertNotNull(await adminSocket.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "admin subscribed to all tenants");

                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);

                HttpResponseMessage sent = await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages", JsonHelper.ToJsonContent(new { Content = "What is the status of the fleet today?" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Accepted, sent);
                AskMessageSendResponse accepted = await JsonHelper.DeserializeAsync<AskMessageSendResponse>(sent).ConfigureAwait(false);
                AssertNotNull(accepted.MessageId, "message id");
                AssertNull(accepted.TurnId, "no captain, no turn");

                HttpResponseMessage action = await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/actions", JsonHelper.ToJsonContent(new { ToolName = "status", Arguments = new { } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, action);
                AskActionProposal proposal = await JsonHelper.DeserializeAsync<AskActionProposal>(action).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, proposal.Status);
                AssertEqual(AskProposalSourceEnum.QuickAction, proposal.Source);
                AssertEqual(System.Text.Json.JsonTokenType.Number, JsonShape.TopLevelProperty(proposal.ResultText ?? "", "ActiveVoyages")?.ValueToken, "real status handler result (ArmadaStatus.ActiveVoyages)");
                AssertEqual(System.Text.Json.JsonTokenType.Number, JsonShape.TopLevelProperty(proposal.ResultText ?? "", "TotalCaptains")?.ValueToken, "real status handler result (ArmadaStatus.TotalCaptains)");

                AssertNotNull(await aliceSocket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "ask.proposal")?.Data?.Proposal?.Id == proposal.Id).ConfigureAwait(false), "owner gets ask.proposal");
                AssertNotNull(await aliceSocket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "ask.message")?.Data?.ThreadId == thread.Id).ConfigureAwait(false), "owner gets ask.message");
                AssertNotNull(await aliceSocket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "ask.thread")?.Data?.ThreadId == thread.Id).ConfigureAwait(false), "owner gets ask.thread");
                await Task.Delay(500).ConfigureAwait(false);
                // Leak check: an ask.* frame of any kind, or any frame that carries the thread id.
                Func<string, bool> leaked = m => (E2eWebSocketFrame.Parse(m)?.Type ?? "").StartsWith("ask.", StringComparison.Ordinal) || m.Contains(thread.Id, StringComparison.Ordinal);
                AssertFalse(bobSocket.Received().Any(leaked), "another user of the same tenant gets no ask.* events");
                AssertFalse(carolSocket.Received().Any(leaked), "another tenant gets no ask.* events");
                AssertFalse(adminSocket.Received().Any(leaked), "an all-tenants admin gets no ask.* events either");

                AskMessagePage page = await JsonHelper.DeserializeAsync<AskMessagePage>(await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages/enumerate", JsonHelper.ToJsonContent(new { PageSize = 1 })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1, page.Messages.Count, "page size");
                AssertTrue(page.HasMore, "older message exists");
                AssertEqual(AskMessageKindEnum.ActionResult, page.Messages[0].Kind, "newest is the action result");
                AssertEqual(proposal.Id, page.Messages[0].Proposal!.Id, "embedded proposal");

                AskThreadDetail detail = await JsonHelper.DeserializeAsync<AskThreadDetail>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual("What is the status of the fleet today?", detail.Thread.Title, "auto title");
                AssertEqual(1, detail.Thread.UnreadCount, "action result unread");
                AskThread read = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/read", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(0, read.UnreadCount, "marked read");

                AssertStatusCode(HttpStatusCode.NotFound, await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/proposals/aap_missing/approve", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.Conflict, await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/proposals/" + proposal.Id + "/approve", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.Conflict, await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/cancel", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/actions", JsonHelper.ToJsonContent(new { ToolName = "no_such_tool" })).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("dispatch_quick_action_tracks_voyage", "/dispatch runs the real dispatch handler and the voyage is tracked with a live snapshot", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "disp").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                using WebSocketTestClient socket = await WebSocketTestClient.ConnectAsync(fx.RestPort, alice.BearerToken).ConfigureAwait(false);

                HttpResponseMessage vesselResp = await a.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = "ask-dispatch-" + Guid.NewGuid().ToString("N").Substring(0, 6), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() })).ConfigureAwait(false);
                vesselResp.EnsureSuccessStatusCode();
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResp).ConfigureAwait(false);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);

                HttpResponseMessage action = await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/actions", JsonHelper.ToJsonContent(new
                {
                    ToolName = "dispatch",
                    Arguments = new { title = "Ask dispatch", vesselId = vessel.Id, missions = new[] { new { title = "Add a README line", description = "Append one line." } } }
                })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, action);
                AskActionProposal proposal = await JsonHelper.DeserializeAsync<AskActionProposal>(action).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, proposal.Status, proposal.ErrorText ?? "");

                AskThreadDetail detail = await JsonHelper.DeserializeAsync<AskThreadDetail>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1L, detail.TrackedWorkCount, "one tracked item counted");
                EnumerationResult<AskTrackedWork> workPage = await JsonHelper.DeserializeAsync<EnumerationResult<AskTrackedWork>>(await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/work/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                AskTrackedWork work = workPage.Objects.Single();
                AssertEqual(AskTrackedEntityTypeEnum.Voyage, work.EntityType);
                AssertNotNull(work.Snapshot, "snapshot embedded");
                AssertEqual("Ask dispatch", work.Snapshot!.Title);
                AssertEqual(1, work.Snapshot.TotalCount, "one mission");
                AssertEqual(1, work.Snapshot.MissionCount, "one mission row");
                EnumerationResult<AskWorkMissionSnapshot> missionRows = await JsonHelper.DeserializeAsync<EnumerationResult<AskWorkMissionSnapshot>>(await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/work/" + work.Id + "/missions/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual("Add a README line", missionRows.Objects.Single().Title);

                AssertStatusCode(HttpStatusCode.OK, await a.GetAsync("/api/v1/voyages/" + work.EntityId).ConfigureAwait(false), "the voyage is visible in the caller's tenant");

                AssertNotNull(await socket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "ask.work")?.Data?.TrackedWorkId == work.Id).ConfigureAwait(false), "ask.work delivered");
                AskWorkSnapshot snapshot = await JsonHelper.DeserializeAsync<AskWorkSnapshot>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id + "/work/" + work.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(work.EntityId, snapshot.EntityId);

                // Cancel the voyage so this suite leaves nothing pending.
                await a.DeleteAsync("/api/v1/voyages/" + work.EntityId).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("mcp_tools_manage_threads", "MCP tools list, read, create, update, message, and delete the caller's own Ask conversations, and never another user's", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "askmcp").ConfigureAwait(false);
                E2ETenantUser bob = await E2ETenantUser.CreateAsync(fx.AuthClient, "askmcpbob", true, alice.TenantId).ConfigureAwait(false);
                Armada.Core.Services.SessionTokenService tokens = new Armada.Core.Services.SessionTokenService(fx.SessionTokenEncryptionKey);
                string aliceToken = tokens.CreateToken(alice.TenantId, alice.UserId).Token!;
                string bobToken = tokens.CreateToken(bob.TenantId, bob.UserId).Token!;

                using (Armada.Runtimes.Mcp.McpToolClient mcp = new Armada.Runtimes.Mcp.McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", aliceToken))
                using (Armada.Runtimes.Mcp.McpToolClient other = new Armada.Runtimes.Mcp.McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", bobToken))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    await other.InitializeAsync().ConfigureAwait(false);

                    AskThread first = JsonHelper.Deserialize<AskThread>(await mcp.CallToolAsync("create_ask_thread", "{\"title\":\"Over MCP\"}").ConfigureAwait(false));
                    AskThread second = JsonHelper.Deserialize<AskThread>(await mcp.CallToolAsync("create_ask_thread", "{}").ConfigureAwait(false));
                    AssertEqual("Over MCP", first.Title);

                    EnumerationResult<AskThread> listed = JsonHelper.Deserialize<EnumerationResult<AskThread>>(await mcp.CallToolAsync("list_ask_threads", "{\"pageSize\":50}").ConfigureAwait(false));
                    AssertTrue(listed.Objects.Any(t => t.Id == first.Id) && listed.Objects.Any(t => t.Id == second.Id), "both listed for the owner");

                    AskThread renamed = JsonHelper.Deserialize<AskThread>(await mcp.CallToolAsync("update_ask_thread", "{\"threadId\":\"" + first.Id + "\",\"title\":\"Renamed\",\"pinned\":true}").ConfigureAwait(false));
                    AssertEqual("Renamed", renamed.Title);
                    AssertTrue(renamed.Pinned, "pinned");

                    string detail = await mcp.CallToolAsync("get_ask_thread", "{\"threadId\":\"" + first.Id + "\"}").ConfigureAwait(false);
                    AssertTrue(detail.Contains(first.Id, StringComparison.Ordinal), "detail for the owner");
                    string messages = await mcp.CallToolAsync("list_ask_messages", "{\"threadId\":\"" + first.Id + "\"}").ConfigureAwait(false);
                    AssertFalse(messages.Contains("NotFound", StringComparison.Ordinal), "messages readable: " + messages);
                    string actions = await mcp.CallToolAsync("list_ask_quick_actions", "{}").ConfigureAwait(false);
                    AssertTrue(actions.TrimStart().StartsWith("[", StringComparison.Ordinal), "quick actions listed");

                    string stranger = await other.CallToolAsync("get_ask_thread", "{\"threadId\":\"" + first.Id + "\"}").ConfigureAwait(false);
                    AssertTrue(stranger.Contains("NotFound", StringComparison.Ordinal), "another user cannot read it: " + stranger);
                    string strangerDelete = await other.CallToolAsync("delete_ask_threads", "{\"threadIds\":[\"" + first.Id + "\"]}").ConfigureAwait(false);
                    AssertTrue(strangerDelete.Contains("\"deletedCount\":0", StringComparison.Ordinal), "another user cannot delete it: " + strangerDelete);

                    string deleted = await mcp.CallToolAsync("delete_ask_threads", "{\"threadIds\":[\"" + first.Id + "\",\"" + second.Id + "\"]}").ConfigureAwait(false);
                    AssertTrue(deleted.Contains("\"deletedCount\":2", StringComparison.Ordinal), "both deleted: " + deleted);
                    string gone = await mcp.CallToolAsync("get_ask_thread", "{\"threadId\":\"" + first.Id + "\"}").ConfigureAwait(false);
                    AssertTrue(gone.Contains("NotFound", StringComparison.Ordinal), "gone after delete");
                }
            }));

            cases.Add(CaseAsync("thread_scoped_mcp_token_gates_tools", "A thread-scoped token is MCP-only and turns state-changing calls into proposals", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser alice = await E2ETenantUser.CreateAsync(fx.AuthClient, "mcpgate").ConfigureAwait(false);
                using HttpClient a = alice.CreateClient(fx.BaseUrl);
                HttpResponseMessage vesselResp = await a.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = "ask-gate-" + Guid.NewGuid().ToString("N").Substring(0, 6), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() })).ConfigureAwait(false);
                vesselResp.EnsureSuccessStatusCode();
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResp).ConfigureAwait(false);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await a.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);

                Armada.Core.Services.SessionTokenService tokens = new Armada.Core.Services.SessionTokenService(fx.SessionTokenEncryptionKey);
                string threadToken = tokens.CreateThreadScopedToken(alice.TenantId, alice.UserId, thread.Id, TimeSpan.FromMinutes(10)).Token!;
                string plainToken = tokens.CreateToken(alice.TenantId, alice.UserId).Token!;

                using (HttpClient plain = new HttpClient { BaseAddress = new Uri(fx.BaseUrl) })
                using (HttpClient scoped = new HttpClient { BaseAddress = new Uri(fx.BaseUrl) })
                {
                    plain.DefaultRequestHeaders.Add("X-Token", plainToken);
                    scoped.DefaultRequestHeaders.Add("X-Token", threadToken);
                    AssertStatusCode(HttpStatusCode.OK, await plain.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false), "a normal session token works on REST");
                    AssertStatusCode(HttpStatusCode.Unauthorized, await scoped.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false), "a thread-scoped token is refused on REST");
                }

                bool wsAccepted;
                try
                {
                    using (WebSocketTestClient ws = await WebSocketTestClient.ConnectAsync(fx.RestPort, threadToken).ConfigureAwait(false)) { wsAccepted = true; }
                }
                catch (System.Net.WebSockets.WebSocketException) { wsAccepted = false; }
                AssertFalse(wsAccepted, "a thread-scoped token is refused on /ws");

                string proposed;
                using (Armada.Runtimes.Mcp.McpToolClient mcp = new Armada.Runtimes.Mcp.McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", threadToken))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    string read = await mcp.CallToolAsync("enumerate", "{\"entityType\":\"vessel\",\"pageSize\":5}").ConfigureAwait(false);
                    EnumerationResult<Vessel> listed = JsonHelper.Deserialize<EnumerationResult<Vessel>>(read);
                    AssertTrue(listed.Objects.Any(v => v.Id == vessel.Id), "read-only tool runs as the user: " + read);

                    proposed = await mcp.CallToolAsync("dispatch", "{\"title\":\"Gated\",\"vesselId\":\"" + vessel.Id + "\",\"missions\":[{\"title\":\"m\",\"description\":\"d\"}]}").ConfigureAwait(false);
                }

                AskThreadDetail detail = await JsonHelper.DeserializeAsync<AskThreadDetail>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1L, detail.PendingProposalCount, "one pending proposal counted");
                EnumerationResult<AskActionProposal> pendingPage = await JsonHelper.DeserializeAsync<EnumerationResult<AskActionProposal>>(await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/proposals/enumerate", JsonHelper.ToJsonContent(new { Status = "Pending" })).ConfigureAwait(false)).ConfigureAwait(false);
                AskActionProposal pending = pendingPage.Objects.Single();
                AssertEqual(String.Format(Armada.Server.Ask.AskActionService.ProposedResultFormat, pending.Id), proposed, "dispatch becomes this proposal (the tool replies with the proposal's own id)");
                AssertEqual("dispatch", pending.ToolName);
                AssertNotNull(pending.ExpiresUtc, "ExpiresUtc");
                AssertEqual(0L, detail.TrackedWorkCount, "nothing ran yet");

                HttpResponseMessage approved = await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/proposals/" + pending.Id + "/approve", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, approved);
                AskActionProposal executed = await JsonHelper.DeserializeAsync<AskActionProposal>(approved).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, executed.Status, executed.ErrorText ?? "");
                AskThreadDetail after = await JsonHelper.DeserializeAsync<AskThreadDetail>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1L, after.TrackedWorkCount, "one item tracked after approval");
                EnumerationResult<AskTrackedWork> afterWork = await JsonHelper.DeserializeAsync<EnumerationResult<AskTrackedWork>>(await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/work/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(AskTrackedEntityTypeEnum.Voyage, afterWork.Objects.Single().EntityType, "voyage tracked after approval");
                await a.DeleteAsync("/api/v1/voyages/" + afterWork.Objects.Single().EntityId).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("quick_action_catalog", "GET quick-actions returns the catalog", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<AskQuickAction> actions = await JsonHelper.DeserializeAsync<List<AskQuickAction>>(await fx.AuthClient.GetAsync("/api/v1/ask/quick-actions").ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual("dispatch,fleet-action,status,health,import,blackbox", String.Join(",", actions.Select(a => a.Name)));
                AssertTrue(actions.All(a => a.ArgumentsSchema != null), "schemas");
                AskQuickAction blackBox = actions.Single(a => a.Name == "blackbox");
                AssertEqual("get_blackbox", blackBox.ToolName);
                AssertTrue(blackBox.ReadOnly, "/blackbox reads only");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Threads API", cases: cases);
        }

        #endregion

        #region Private-Methods

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
