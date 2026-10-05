namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The same question answered over REST, MCP, and WebSocket gets the same, caller-scoped answer. A dedicated server
    /// gets two tenants; tenant B's captain, mission, and signal are seeded directly in the database, tenant A gets one
    /// captain and one signal. Covers O-03 (aggregate status is scoped to the caller's tenant for everyone but a global
    /// administrator, on GET /api/v1/status, the MCP status tool, and the /ws status.snapshot) and the inbox (REST and
    /// MCP apply the same Ask proposal expiry).
    /// </summary>
    public sealed class SurfaceScopingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.SurfaceScoping";

        private SecurityTestServer? _Server;
        private E2ETenantUser? _TenantA;
        private E2ETenantUser? _TenantAUser;
        private E2ETenantUser? _TenantB;
        private string _TenantACaptainId = String.Empty;
        private string _TenantBCaptainId = String.Empty;
        private string _TenantBSignalId = String.Empty;
        private string _FreshProposalId = String.Empty;
        private string _ExpiredProposalId = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_two_tenants", "Two tenants seeded with captains, a mission, signals, and Ask proposals", TestTags.Positive, async () =>
            {
                _Server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                _Server.Settings.Mcp.ToolCallsPerSecond = 0;
                _Server.Settings.Ask.ProposalExpiryMinutes = 60;
                await _Server.StartAsync().ConfigureAwait(false);
                using (HttpClient admin = _Server.CreateRestClient(true))
                {
                    _TenantA = await E2ETenantUser.CreateAsync(admin, "scope-a", true).ConfigureAwait(false);
                    _TenantAUser = await E2ETenantUser.CreateAsync(admin, "scope-a-user", false, _TenantA.TenantId).ConfigureAwait(false);
                    _TenantB = await E2ETenantUser.CreateAsync(admin, "scope-b", true).ConfigureAwait(false);
                }

                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    Captain captainA = new Captain("scope-a-captain");
                    captainA.TenantId = _TenantA.TenantId;
                    captainA.UserId = _TenantA.UserId;
                    _TenantACaptainId = (await db.Captains.CreateAsync(captainA).ConfigureAwait(false)).Id;

                    Captain captainB = new Captain("scope-b-captain");
                    captainB.TenantId = _TenantB.TenantId;
                    captainB.UserId = _TenantB.UserId;
                    _TenantBCaptainId = (await db.Captains.CreateAsync(captainB).ConfigureAwait(false)).Id;

                    Mission missionB = new Mission("scope-b-mission");
                    missionB.TenantId = _TenantB.TenantId;
                    missionB.UserId = _TenantB.UserId;
                    missionB.Status = MissionStatusEnum.Failed;
                    await db.Missions.CreateAsync(missionB).ConfigureAwait(false);

                    Signal signalA = new Signal(SignalTypeEnum.Progress, "scope-a-signal");
                    signalA.TenantId = _TenantA.TenantId;
                    signalA.UserId = _TenantA.UserId;
                    await db.Signals.CreateAsync(signalA).ConfigureAwait(false);

                    Signal signalB = new Signal(SignalTypeEnum.Progress, "scope-b-signal");
                    signalB.TenantId = _TenantB.TenantId;
                    signalB.UserId = _TenantB.UserId;
                    _TenantBSignalId = (await db.Signals.CreateAsync(signalB).ConfigureAwait(false)).Id;

                    AskThread thread = await db.AskThreads.CreateAsync(new AskThread { TenantId = _TenantAUser.TenantId, UserId = _TenantAUser.UserId }).ConfigureAwait(false);
                    _FreshProposalId = (await db.AskActionProposals.CreateAsync(NewProposal(thread, "dispatch", "Fresh proposal")).ConfigureAwait(false)).Id;
                    AskActionProposal expired = NewProposal(thread, "cancel_voyage", "Expired proposal");
                    expired.CreatedUtc = DateTime.UtcNow.AddMinutes(-90);
                    _ExpiredProposalId = (await db.AskActionProposals.CreateAsync(expired).ConfigureAwait(false)).Id;
                }
            }));

            cases.Add(CaseAsync("rest_status_is_tenant_scoped", "GET /api/v1/status shows a tenant admin only its own tenant; a global admin sees every tenant", TestTags.Negative, async () =>
            {
                RequireSetup();
                ArmadaStatus tenantStatus;
                using (HttpClient client = _TenantA!.CreateClient(_Server!.BaseUrl))
                {
                    HttpResponseMessage resp = await client.GetAsync("/api/v1/status").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, resp.StatusCode);
                    tenantStatus = await JsonHelper.DeserializeAsync<ArmadaStatus>(resp).ConfigureAwait(false);
                }

                AssertTenantAStatus(tenantStatus, "REST");

                using (HttpClient admin = _Server!.CreateRestClient(true))
                {
                    HttpResponseMessage resp = await admin.GetAsync("/api/v1/status").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, resp.StatusCode);
                    ArmadaStatus adminStatus = await JsonHelper.DeserializeAsync<ArmadaStatus>(resp).ConfigureAwait(false);
                    AssertTrue(adminStatus.TotalCaptains >= 2, "a global admin sees both tenants' captains, got " + adminStatus.TotalCaptains);
                    AssertTrue(adminStatus.MissionsByStatus.ContainsKey("Failed"), "a global admin sees tenant B's failed mission");
                    AssertTrue(adminStatus.RecentSignals.Any(s => s.Id == _TenantBSignalId), "a global admin sees tenant B's signal");
                }
            }));

            cases.Add(CaseAsync("mcp_status_is_tenant_scoped", "The MCP status tool shows a tenant admin only its own tenant", TestTags.Negative, async () =>
            {
                RequireSetup();
                using (McpToolClient client = CreateMcpClient(_TenantA!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("status", "{}").ConfigureAwait(false);
                    AssertFalse(result.IsError, "status should succeed: " + result.Text);
                    ArmadaStatus status = JsonHelper.Deserialize<ArmadaStatus>(result.Text);
                    AssertTenantAStatus(status, "MCP");
                }
            }));

            cases.Add(CaseAsync("websocket_snapshot_is_tenant_scoped", "The /ws status.snapshot shows a tenant admin only its own tenant", TestTags.Negative, async () =>
            {
                RequireSetup();
                int restPort = new Uri(_Server!.BaseUrl).Port;
                using (WebSocketTestClient client = await WebSocketTestClient.ConnectAsync(restPort, _TenantA!.BearerToken).ConfigureAwait(false))
                {
                    await client.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
                    string? frameText = await client.WaitForAsync(text => E2eWebSocketFrame.IsType(text, "status.snapshot")).ConfigureAwait(false);
                    AssertNotNull(frameText, "the subscribe should be answered with a status.snapshot");
                    E2eStatusSnapshotFrame frame = E2eStatusSnapshotFrame.Parse(frameText)!;
                    AssertNotNull(frame.Data, "the snapshot carries a status");
                    AssertTenantAStatus(frame.Data!, "WebSocket");
                }
            }));

            cases.Add(CaseAsync("inbox_rest_and_mcp_omit_expired_ask_proposals", "REST and MCP inbox both leave out Ask proposals older than Ask.ProposalExpiryMinutes", TestTags.Negative, async () =>
            {
                RequireSetup();
                List<InboxItem> restItems;
                using (HttpClient client = _TenantAUser!.CreateClient(_Server!.BaseUrl))
                {
                    HttpResponseMessage resp = await client.GetAsync("/api/v1/inbox").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, resp.StatusCode);
                    restItems = await JsonHelper.DeserializeAsync<List<InboxItem>>(resp).ConfigureAwait(false);
                }

                List<InboxItem> mcpItems;
                using (McpToolClient client = CreateMcpClient(_TenantAUser!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("inbox", "{}").ConfigureAwait(false);
                    AssertFalse(result.IsError, "inbox should succeed: " + result.Text);
                    mcpItems = JsonHelper.Deserialize<E2eMcpInboxResult>(result.Text).Items;
                }

                List<string> restAsks = restItems.Where(i => i.Kind == InboxItemKinds.AskProposal).Select(i => i.EntityId ?? "").ToList();
                List<string> mcpAsks = mcpItems.Where(i => i.Kind == InboxItemKinds.AskProposal).Select(i => i.EntityId ?? "").ToList();
                AssertEqual(1, restAsks.Count, "REST lists only the unexpired proposal");
                AssertEqual(_FreshProposalId, restAsks[0]);
                AssertEqual(1, mcpAsks.Count, "MCP lists only the unexpired proposal");
                AssertEqual(_FreshProposalId, mcpAsks[0]);
                AssertFalse(mcpAsks.Contains(_ExpiredProposalId), "MCP must not list the expired proposal");
            }));

            cases.Add(CaseAsync("teardown", "Stop the server", TestTags.Positive, () =>
            {
                _Server?.Dispose();
                _Server = null;
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Surface scoping (status, inbox)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private void RequireSetup()
        {
            if (_Server == null || _TenantA == null || _TenantAUser == null || _TenantB == null) throw new InvalidOperationException("setup case did not run");
        }

        private void AssertTenantAStatus(ArmadaStatus status, string surface)
        {
            AssertEqual(1, status.TotalCaptains, surface + ": tenant A has exactly one captain; other tenants' captains must not be counted");
            AssertFalse(status.MissionsByStatus.ContainsKey("Failed"), surface + ": tenant B's failed mission must not be counted");
            AssertFalse(status.RecentSignals.Any(s => s.Id == _TenantBSignalId), surface + ": tenant B's signal must not be listed");
            AssertTrue(status.RecentSignals.All(s => String.Equals(s.TenantId, _TenantA!.TenantId, StringComparison.Ordinal)), surface + ": every listed signal belongs to tenant A");
        }

        private McpToolClient CreateMcpClient(E2ETenantUser user)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + user.BearerToken
            };
            return new McpToolClient(_Server!.McpUrl + "/mcp", null, headers, null, 60);
        }

        private async Task<DatabaseDriver> OpenDatabaseAsync()
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseDriver db = DatabaseDriverFactory.Create(_Server!.Settings.Database, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private static AskActionProposal NewProposal(AskThread thread, string toolName, string summary)
        {
            AskActionProposal proposal = new AskActionProposal();
            proposal.TenantId = thread.TenantId;
            proposal.UserId = thread.UserId;
            proposal.ThreadId = thread.Id;
            proposal.ToolName = toolName;
            proposal.SummaryText = summary;
            return proposal;
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
