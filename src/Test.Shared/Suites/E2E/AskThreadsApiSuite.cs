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
                    AssertNotNull(await s.WaitForAsync(m => m.Contains("status.snapshot")).ConfigureAwait(false), "subscribed");
                }

                await adminSocket.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe", ["AllTenants"] = true }).ConfigureAwait(false);
                AssertNotNull(await adminSocket.WaitForAsync(m => m.Contains("status.snapshot")).ConfigureAwait(false), "admin subscribed to all tenants");

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
                AssertContains("ctive", proposal.ResultText ?? "", "real status handler result");

                AssertNotNull(await aliceSocket.WaitForAsync(m => m.Contains("\"ask.proposal\"") && m.Contains(proposal.Id)).ConfigureAwait(false), "owner gets ask.proposal");
                AssertNotNull(await aliceSocket.WaitForAsync(m => m.Contains("\"ask.message\"") && m.Contains(thread.Id)).ConfigureAwait(false), "owner gets ask.message");
                AssertNotNull(await aliceSocket.WaitForAsync(m => m.Contains("\"ask.thread\"") && m.Contains(thread.Id)).ConfigureAwait(false), "owner gets ask.thread");
                await Task.Delay(500).ConfigureAwait(false);
                AssertFalse(bobSocket.Received().Any(m => m.Contains("\"ask.")), "another user of the same tenant gets no ask.* events");
                AssertFalse(carolSocket.Received().Any(m => m.Contains("\"ask.")), "another tenant gets no ask.* events");
                AssertFalse(adminSocket.Received().Any(m => m.Contains("\"ask.")), "an all-tenants admin gets no ask.* events either");

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
                AskTrackedWork work = detail.TrackedWork.Single();
                AssertEqual(AskTrackedEntityTypeEnum.Voyage, work.EntityType);
                AssertNotNull(work.Snapshot, "snapshot embedded");
                AssertEqual("Ask dispatch", work.Snapshot!.Title);
                AssertEqual(1, work.Snapshot.TotalCount, "one mission");
                AssertEqual("Add a README line", work.Snapshot.Missions.Single().Title);

                AssertStatusCode(HttpStatusCode.OK, await a.GetAsync("/api/v1/voyages/" + work.EntityId).ConfigureAwait(false), "the voyage is visible in the caller's tenant");

                AssertNotNull(await socket.WaitForAsync(m => m.Contains("\"ask.work\"") && m.Contains(work.Id)).ConfigureAwait(false), "ask.work delivered");
                AskWorkSnapshot snapshot = await JsonHelper.DeserializeAsync<AskWorkSnapshot>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id + "/work/" + work.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(work.EntityId, snapshot.EntityId);

                // Cancel the voyage so this suite leaves nothing pending.
                await a.DeleteAsync("/api/v1/voyages/" + work.EntityId).ConfigureAwait(false);
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

                using (Armada.Runtimes.Mcp.McpToolClient mcp = new Armada.Runtimes.Mcp.McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", threadToken))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    string read = await mcp.CallToolAsync("enumerate", "{\"entityType\":\"vessel\",\"pageSize\":5}").ConfigureAwait(false);
                    AssertContains(vessel.Id, read, "read-only tool runs as the user");

                    string proposed = await mcp.CallToolAsync("dispatch", "{\"title\":\"Gated\",\"vesselId\":\"" + vessel.Id + "\",\"missions\":[{\"title\":\"m\",\"description\":\"d\"}]}").ConfigureAwait(false);
                    AssertContains("Proposed as aap_", proposed, "dispatch becomes a proposal");
                }

                AskThreadDetail detail = await JsonHelper.DeserializeAsync<AskThreadDetail>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AskActionProposal pending = detail.PendingProposals.Single();
                AssertEqual("dispatch", pending.ToolName);
                AssertNotNull(pending.ExpiresUtc, "ExpiresUtc");
                AssertEqual(0, detail.TrackedWork.Count, "nothing ran yet");

                HttpResponseMessage approved = await a.PostAsync("/api/v1/ask/threads/" + thread.Id + "/proposals/" + pending.Id + "/approve", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, approved);
                AskActionProposal executed = await JsonHelper.DeserializeAsync<AskActionProposal>(approved).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, executed.Status, executed.ErrorText ?? "");
                AskThreadDetail after = await JsonHelper.DeserializeAsync<AskThreadDetail>(await a.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(AskTrackedEntityTypeEnum.Voyage, after.TrackedWork.Single().EntityType, "voyage tracked after approval");
                await a.DeleteAsync("/api/v1/voyages/" + after.TrackedWork.Single().EntityId).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("quick_action_catalog", "GET quick-actions returns the catalog", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<AskQuickAction> actions = await JsonHelper.DeserializeAsync<List<AskQuickAction>>(await fx.AuthClient.GetAsync("/api/v1/ask/quick-actions").ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual("dispatch,fleet-action,status,health,import", String.Join(",", actions.Select(a => a.Name)));
                AssertTrue(actions.All(a => a.ArgumentsSchema != null), "schemas");
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
