namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes.Mcp;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end CLI tool permissions through the real server: a captain's thread- or mission-scoped session calls
    /// cli_permission_prompt over MCP and waits; an admin decides over REST and the waiting call returns Claude Code's
    /// {"behavior":"allow","updatedInput":...} or {"behavior":"deny","message":...}; non-admins, captain sessions, and
    /// unscoped callers are refused on REST, MCP, and WebSocket; rules, policies, the inbox kind, the
    /// cli_permission.* WebSocket events, and a cancelled prompt call resolving its request at once.
    /// </summary>
    public sealed class CliPermissionsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.CliPermissions";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("thread_prompt_allowed_by_admin", "A thread captain's prompt waits over MCP until an admin allows it, then returns allow with the original input", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpe-admin", true).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpe-owner", false, admin.TenantId).ConfigureAwait(false);
                using HttpClient a = admin.CreateClient(fx.BaseUrl);
                using HttpClient o = owner.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await o.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                string threadToken = ThreadToken(fx, owner, thread.Id);

                using (WebSocketTestClient ws = await WebSocketTestClient.ConnectAsync(fx.RestPort, admin.BearerToken).ConfigureAwait(false))
                using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", threadToken))
                {
                    await ws.SendAsync(new { route = "subscribe" }).ConfigureAwait(false);
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    Task<McpToolCallResult> call = mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"git push origin main\",\"description\":\"Push\"},\"tool_use_id\":\"toolu_1\"}");

                    CliPermissionRequest pending = await WaitForPendingAsync(fx, a, thread, call).ConfigureAwait(false);
                    AssertFalse(call.IsCompleted, "the captain's call is waiting");
                    AssertEqual("Bash", pending.ToolName);
                    AssertEqual("git push origin main", pending.SummaryText);
                    AssertTrue(pending.CanDecide && pending.CanRemember, "tenant admin can decide and remember");
                    string? requested = await ws.WaitForAsync(frame => IsCliPermissionEvent(frame, "cli_permission.requested", pending.Id)).ConfigureAwait(false);
                    AssertNotNull(requested, "cli_permission.requested reached the admin socket");
                    AssertTrue(JsonHelper.Deserialize<CliPermissionEventFrame>(requested!).Data!.Request!.CanDecide, "the admin's copy is decidable");

                    HttpResponseMessage ownerDecision = await o.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "AllowOnce" })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.Forbidden, ownerDecision, "the owner cannot approve by default");
                    using (HttpClient scoped = new HttpClient { BaseAddress = new Uri(fx.BaseUrl) })
                    {
                        scoped.DefaultRequestHeaders.Add("X-Token", threadToken);
                        AssertStatusCode(HttpStatusCode.Unauthorized, await scoped.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "AllowOnce" })).ConfigureAwait(false), "the captain's token is refused on REST");
                    }

                    McpToolCallResult selfApproval = await mcp.CallToolResultAsync("decide_cli_permission_request", JsonHelper.Serialize(new { requestId = pending.Id, decision = "AllowOnce" })).ConfigureAwait(false);
                    AssertEqual(McpToolErrorCodeEnum.Forbidden, JsonHelper.Deserialize<McpToolError>(selfApproval.Text).ErrorCode, "the captain cannot approve its own prompt over MCP: " + selfApproval.Text);

                    HttpResponseMessage allowed = await a.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "AllowOnce" })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, allowed);
                    AssertEqual(CliPermissionRequestStatusEnum.Allowed, (await JsonHelper.DeserializeAsync<CliPermissionRequest>(allowed).ConfigureAwait(false)).Status);

                    McpToolCallResult result = await call.ConfigureAwait(false);
                    AssertFalse(result.IsError, result.Text);
                    PromptAnswer answer = JsonHelper.Deserialize<PromptAnswer>(result.Text);
                    AssertEqual("allow", answer.Behavior, result.Text);
                    AssertEqual("git push origin main", answer.UpdatedInput!.Command, "the original input is returned unchanged");
                    AssertNull(answer.Message, "no message on allow");
                    string? resolved = await ws.WaitForAsync(frame => IsCliPermissionEvent(frame, "cli_permission.resolved", pending.Id)).ConfigureAwait(false);
                    AssertNotNull(resolved, "cli_permission.resolved reached the admin socket");
                    AssertEqual(CliPermissionRequestStatusEnum.Allowed, JsonHelper.Deserialize<CliPermissionEventFrame>(resolved!).Data!.Status);

                    AssertStatusCode(HttpStatusCode.Conflict, await a.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "Deny" })).ConfigureAwait(false), "decided twice");
                }
            }));

            cases.Add(CaseAsync("thread_prompt_denied_and_card", "A denial returns deny with the reason; the thread shows the CliPermission card and pending list", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpd-admin", true).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpd-owner", false, admin.TenantId).ConfigureAwait(false);
                using HttpClient a = admin.CreateClient(fx.BaseUrl);
                using HttpClient o = owner.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await o.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);

                using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", ThreadToken(fx, owner, thread.Id)))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    Task<McpToolCallResult> call = mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"WebFetch\",\"input\":{\"url\":\"https://example.com/x\",\"prompt\":\"read\"}}");
                    CliPermissionRequest pending = await WaitForPendingAsync(fx, a, thread, call).ConfigureAwait(false);

                    AskThreadDetail detail = await JsonHelper.DeserializeAsync<AskThreadDetail>(await o.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
                    AssertEqual(pending.Id, detail.PendingCliPermissions.Single().Id, "pending list on the thread");
                    AskMessagePage page = await JsonHelper.DeserializeAsync<AskMessagePage>(await o.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                    AskMessage card = page.Messages.Single(m => m.Kind == AskMessageKindEnum.CliPermission);
                    AssertEqual(pending.Id, card.CliPermissionRequest!.Id, "card carries its request");

                    List<InboxItem> inbox = await JsonHelper.DeserializeAsync<List<InboxItem>>(await a.GetAsync("/api/v1/inbox").ConfigureAwait(false)).ConfigureAwait(false);
                    InboxItem item = inbox.Single(i => i.Kind == InboxItemKinds.CliPermission && i.EntityId == pending.Id);
                    AssertEqual(InboxSeverityEnum.Warning, item.Severity);
                    AssertTrue(item.CliPermission!.CanDecide, "admin's inbox item is decidable");

                    HttpResponseMessage denied = await a.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "Deny", Message = "not that site" })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, denied);
                    McpToolCallResult result = await call.ConfigureAwait(false);
                    PromptAnswer answer = JsonHelper.Deserialize<PromptAnswer>(result.Text);
                    AssertEqual("deny", answer.Behavior, result.Text);
                    AssertContains("not that site", answer.Message ?? "");
                    AssertNull(answer.UpdatedInput, "no input on deny");
                }
            }));

            cases.Add(CaseAsync("mission_prompt_and_rules", "A mission captain's prompt is decided by rules (allow and deny) without waiting and records the mission and vessel", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpm-admin", true).ConfigureAwait(false);
                using HttpClient a = admin.CreateClient(fx.BaseUrl);
                string missionId;
                string vesselId;
                string captainId;
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                    Vessel vessel = new Vessel("cpm-vessel-" + suffix, "https://example.invalid/cpm-" + suffix + ".git") { TenantId = admin.TenantId, UserId = admin.UserId };
                    vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    Captain captain = new Captain("cpm-captain-" + suffix) { TenantId = admin.TenantId, UserId = admin.UserId, State = CaptainStateEnum.Working };
                    captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                    Mission mission = new Mission("cpm-mission-" + suffix, "cli permission test") { TenantId = admin.TenantId, UserId = admin.UserId, VesselId = vessel.Id, CaptainId = captain.Id, Status = MissionStatusEnum.InProgress };
                    mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                    missionId = mission.Id;
                    vesselId = vessel.Id;
                    captainId = captain.Id;
                }

                HttpResponseMessage allowRule = await a.PostAsync("/api/v1/cli-permissions/rules", JsonHelper.ToJsonContent(new { Pattern = "Bash(dotnet build:*)", Action = "Allow", Scope = "Vessel", VesselId = vesselId })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, allowRule);
                CliPermissionRule rule = await JsonHelper.DeserializeAsync<CliPermissionRule>(allowRule).ConfigureAwait(false);
                AssertEqual(admin.TenantId, rule.TenantId);
                HttpResponseMessage denyRule = await a.PostAsync("/api/v1/cli-permissions/rules", JsonHelper.ToJsonContent(new { Pattern = "Bash(curl:*)", Action = "Deny", Scope = "Captain", CaptainId = captainId })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, denyRule);
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/cli-permissions/rules", JsonHelper.ToJsonContent(new { Pattern = "Bash(", Action = "Allow" })).ConfigureAwait(false), "invalid pattern");

                SessionTokenService tokens = new SessionTokenService(fx.SessionTokenEncryptionKey);
                string missionToken = tokens.CreateMissionScopedToken(admin.TenantId, admin.UserId, missionId, captainId, TimeSpan.FromMinutes(10)).Token!;
                using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", missionToken))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    McpToolCallResult allowed = await mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"dotnet build src/x.sln\"}}").ConfigureAwait(false);
                    AssertEqual("allow", JsonHelper.Deserialize<PromptAnswer>(allowed.Text).Behavior, allowed.Text);
                    McpToolCallResult denied = await mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"curl https://evil.test\"}}").ConfigureAwait(false);
                    PromptAnswer deniedAnswer = JsonHelper.Deserialize<PromptAnswer>(denied.Text);
                    AssertEqual("deny", deniedAnswer.Behavior, denied.Text);
                    AssertContains("Bash(curl:*)", deniedAnswer.Message ?? "");
                }

                List<CliPermissionRequest> recorded = await JsonHelper.DeserializeAsync<List<CliPermissionRequest>>(await a.GetAsync("/api/v1/cli-permissions/requests?missionId=" + missionId).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(2, recorded.Count, "both prompts recorded");
                AssertTrue(recorded.All(r => r.VesselId == vesselId && r.CaptainId == captainId && r.Status != CliPermissionRequestStatusEnum.Pending), "mission context recorded");
                AssertEqual(1, recorded.Count(r => r.DecisionSource == CliPermissionDecisionSourceEnum.AllowRule && r.RuleId == rule.Id));

                AssertStatusCode(HttpStatusCode.NoContent, await a.DeleteAsync("/api/v1/cli-permissions/rules/" + rule.Id).ConfigureAwait(false));
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/cli-permissions/rules/" + rule.Id).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("prompt_requires_a_captain_session", "cli_permission_prompt refuses a caller without a mission- or thread-scoped session", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpn-user", true).ConfigureAwait(false);
                string plain = new SessionTokenService(fx.SessionTokenEncryptionKey).CreateToken(user.TenantId, user.UserId).Token!;
                using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", plain))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    McpToolCallResult result = await mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"ls\"}}").ConfigureAwait(false);
                    AssertEqual(McpToolErrorCodeEnum.Forbidden, JsonHelper.Deserialize<McpToolError>(result.Text).ErrorCode, "an unscoped caller is refused: " + result.Text);
                }

                List<McpRemoteTool> listed;
                using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", plain))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    listed = await mcp.ListToolsAsync().ConfigureAwait(false);
                }

                foreach (string tool in new[] { "cli_permission_prompt", "list_cli_permission_requests", "get_cli_permission_request", "decide_cli_permission_request", "list_cli_permission_rules", "create_cli_permission_rule", "update_cli_permission_rule", "delete_cli_permission_rule", "set_captain_cli_permission_policy" })
                    AssertTrue(listed.Any(t => t.Name == tool), "MCP lists " + tool);
            }));

            cases.Add(CaseAsync("owner_approval_setting_and_policies", "Owner approval follows Permissions.AllowOwnerApproval; Bypass needs an admin on threads and captains", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpo-admin", true).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpo-owner", false, admin.TenantId).ConfigureAwait(false);
                using HttpClient a = admin.CreateClient(fx.BaseUrl);
                using HttpClient o = owner.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await o.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);

                AssertStatusCode(HttpStatusCode.Forbidden, await o.PutAsync("/api/v1/ask/threads/" + thread.Id + "/cli-permission-policy", JsonHelper.ToJsonContent(new { Policy = "Bypass" })).ConfigureAwait(false), "a user cannot set Bypass");
                HttpResponseMessage refuse = await o.PutAsync("/api/v1/ask/threads/" + thread.Id + "/cli-permission-policy", JsonHelper.ToJsonContent(new { Policy = "Refuse" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, refuse);
                AskThread refused = await JsonHelper.DeserializeAsync<AskThread>(refuse).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.Refuse, refused.CliPermissionPolicy);
                AssertStatusCode(HttpStatusCode.NotFound, await a.PutAsync("/api/v1/ask/threads/" + thread.Id + "/cli-permission-policy", JsonHelper.ToJsonContent(new { Policy = "Refuse" })).ConfigureAwait(false), "only the owner changes a thread");

                HttpResponseMessage createdCaptain = await a.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = "cpo-captain-" + Guid.NewGuid().ToString("N").Substring(0, 6), Runtime = "ClaudeCode" })).ConfigureAwait(false);
                createdCaptain.EnsureSuccessStatusCode();
                Captain captain = await JsonHelper.DeserializeAsync<Captain>(createdCaptain).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Forbidden, await o.PutAsync("/api/v1/captains/" + captain.Id + "/cli-permission-policy", JsonHelper.ToJsonContent(new { Policy = "Bypass" })).ConfigureAwait(false), "a user cannot change a captain's policy");
                HttpResponseMessage set = await a.PutAsync("/api/v1/captains/" + captain.Id + "/cli-permission-policy", JsonHelper.ToJsonContent(new { Policy = "ApproveInArmada" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, set);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, (await JsonHelper.DeserializeAsync<Captain>(set).ConfigureAwait(false)).CliPermissionPolicy);
                captain.Name = captain.Name + "-renamed";
                captain.CliPermissionPolicy = CliPermissionPolicyEnum.Bypass;
                HttpResponseMessage put = await a.PutAsync("/api/v1/captains/" + captain.Id, JsonHelper.ToJsonContent(captain)).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, put);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, (await JsonHelper.DeserializeAsync<Captain>(put).ConfigureAwait(false)).CliPermissionPolicy, "a captain update cannot change the policy");

                CliPermissionSettings original = (await JsonHelper.DeserializeAsync<SettingsProbe>(await fx.AuthClient.GetAsync("/api/v1/settings").ConfigureAwait(false)).ConfigureAwait(false)).Permissions!;
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, original.AskDefaultPolicy, "settings expose Permissions");
                AssertStatusCode(HttpStatusCode.Forbidden, await a.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Permissions = new { AllowOwnerApproval = true } })).ConfigureAwait(false), "a tenant admin cannot change server settings");
                try
                {
                    AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Permissions = new { AllowOwnerApproval = true, AskDefaultPolicy = "ApproveInArmada", MissionDefaultPolicy = "Bypass", PromptTimeoutSeconds = 600 } })).ConfigureAwait(false));
                    await o.PutAsync("/api/v1/ask/threads/" + thread.Id + "/cli-permission-policy", JsonHelper.ToJsonContent(new { Policy = (string?)null })).ConfigureAwait(false);
                    using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", ThreadToken(fx, owner, thread.Id)))
                    {
                        await mcp.InitializeAsync().ConfigureAwait(false);
                        Task<McpToolCallResult> call = mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"npm test\"}}");
                        CliPermissionRequest pending = await WaitForPendingAsync(fx, o, thread, call).ConfigureAwait(false);
                        AssertTrue(pending.CanDecide, "the owner can decide with the setting on");
                        AssertFalse(pending.CanRemember, "but not remember");
                        AssertStatusCode(HttpStatusCode.Forbidden, await o.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "AllowAndRemember" })).ConfigureAwait(false), "remember needs an admin");
                        AssertStatusCode(HttpStatusCode.OK, await o.PostAsync("/api/v1/cli-permissions/requests/" + pending.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "AllowOnce" })).ConfigureAwait(false));
                        AssertEqual("allow", JsonHelper.Deserialize<PromptAnswer>((await call.ConfigureAwait(false)).Text).Behavior);
                    }
                }
                finally
                {
                    await fx.AuthClient.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Permissions = original })).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("cancelled_prompt_call_resolves_at_once", "When the captain cancels its MCP prompt call (notifications/cancelled, as Claude Code sends on an interrupt), the pending request is resolved at once, not after the prompt timeout", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpd-owner", true).ConfigureAwait(false);
                using HttpClient o = owner.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await o.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                string endpoint = "http://127.0.0.1:" + fx.McpPort + "/mcp";
                using (HttpClient raw = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    raw.DefaultRequestHeaders.TryAddWithoutValidation("X-Token", ThreadToken(fx, owner, thread.Id));
                    HttpResponseMessage init = await raw.SendAsync(McpPost(endpoint, null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"claude-code\",\"version\":\"test\"}}}")).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, init, "initialize");
                    string session = init.Headers.GetValues("Mcp-Session-Id").First();
                    await raw.SendAsync(McpPost(endpoint, session, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")).ConfigureAwait(false);

                    Task<HttpResponseMessage> call = raw.SendAsync(McpPost(endpoint, session, "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"tools/call\",\"params\":{\"name\":\"cli_permission_prompt\",\"arguments\":{\"tool_name\":\"Bash\",\"input\":{\"command\":\"terraform apply\"}}}}"));
                    CliPermissionRequest pending = await WaitForPendingAsync(fx, o, thread, call).ConfigureAwait(false);
                    AssertTrue(pending.ExpiresUtc > DateTime.UtcNow.AddMinutes(5), "the prompt timeout is minutes away");
                    AssertFalse(call.IsCompleted, "the call waits for a decision");

                    await raw.SendAsync(McpPost(endpoint, session, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":42,\"reason\":\"user interrupted\"}}")).ConfigureAwait(false);

                    CliPermissionRequest? resolved = null;
                    bool done = await AskTestHarness.WaitUntilAsync(async () =>
                    {
                        HttpResponseMessage resp = await o.GetAsync("/api/v1/cli-permissions/requests/" + pending.Id).ConfigureAwait(false);
                        if (resp.StatusCode != HttpStatusCode.OK) return false;
                        resolved = await JsonHelper.DeserializeAsync<CliPermissionRequest>(resp).ConfigureAwait(false);
                        return resolved.Status != CliPermissionRequestStatusEnum.Pending;
                    }, 15000).ConfigureAwait(false);
                    AssertTrue(done, "the request left Pending soon after the call was cancelled (status " + resolved?.Status + ")");
                    AssertEqual(CliPermissionRequestStatusEnum.Cancelled, resolved!.Status, "resolved as cancelled");
                    AssertEqual(CliPermissionDecisionSourceEnum.Cancelled, resolved.DecisionSource, "by the cancelled call");
                    try { (await call.ConfigureAwait(false)).Dispose(); }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                }
            }));

            cases.Add(CaseAsync("websocket_decide_command", "The WebSocket decide command acts as the caller (global admins only, as for every WebSocket command)", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "cpw-owner", true).ConfigureAwait(false);
                using HttpClient o = owner.CreateClient(fx.BaseUrl);
                AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await o.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
                using (McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", ThreadToken(fx, owner, thread.Id)))
                {
                    await mcp.InitializeAsync().ConfigureAwait(false);
                    Task<McpToolCallResult> call = mcp.CallToolResultAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"make\"}}");
                    CliPermissionRequest pending = await WaitForPendingAsync(fx, o, thread, call).ConfigureAwait(false);

                    using (WebSocketTestClient tenantAdminSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, owner.BearerToken).ConfigureAwait(false))
                    {
                        await tenantAdminSocket.SendAsync(new { route = "command", action = "decide_cli_permission_request", id = pending.Id, data = new { Decision = "AllowOnce" } }).ConfigureAwait(false);
                        string? refused = await tenantAdminSocket.WaitForAsync(frame => E2eWebSocketFrame.Parse(frame)?.Type == "command.error").ConfigureAwait(false);
                        AssertNotNull(refused, "WebSocket commands need a global admin");
                        AssertEqual(WebSocketCommandErrorCodeEnum.Forbidden, E2eWebSocketFrame.Parse(refused)!.Code);
                    }

                    using (System.Net.WebSockets.ClientWebSocket adminSocket = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(fx.RestPort).ConfigureAwait(false))
                    {
                        E2eWebSocketFrame reply = await SendCommandAsync(adminSocket, new { route = "command", action = "decide_cli_permission_request", id = pending.Id, data = new { Decision = "Deny", Message = "ws says no" } }).ConfigureAwait(false);
                        AssertEqual("command.result", reply.Type, "decided over WebSocket: " + reply.Error);
                    }

                    PromptAnswer answer = JsonHelper.Deserialize<PromptAnswer>((await call.ConfigureAwait(false)).Text);
                    AssertEqual("deny", answer.Behavior);
                    AssertContains("ws says no", answer.Message ?? "");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "CLI permissions API", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string ThreadToken(E2EServerFixture fx, E2ETenantUser owner, string threadId)
        {
            SessionTokenService tokens = new SessionTokenService(fx.SessionTokenEncryptionKey);
            return tokens.CreateThreadScopedToken(owner.TenantId, owner.UserId, threadId, TimeSpan.FromMinutes(10)).Token!;
        }

        private static async Task<CliPermissionRequest> WaitForPendingAsync(E2EServerFixture fx, HttpClient client, AskThread thread, Task? call = null)
        {
            CliPermissionRequest? found = null;
            CliPermissionWaitTrace trace = new CliPermissionWaitTrace();
            bool ok = await AskTestHarness.WaitUntilAsync(async () =>
            {
                // The prompt call only returns once decided, so a call that has already finished failed: stop waiting
                // and report why instead of timing out with no detail.
                if (call != null && call.IsCompleted) return true;
                System.Diagnostics.Stopwatch poll = System.Diagnostics.Stopwatch.StartNew();
                HttpResponseMessage resp;
                try
                {
                    resp = await client.GetAsync("/api/v1/cli-permissions/requests?status=Pending&threadId=" + thread.Id).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    trace.RecordPollError(poll.ElapsedMilliseconds, ex);
                    return false;
                }

                if (resp.StatusCode != HttpStatusCode.OK)
                {
                    trace.RecordPoll(resp.StatusCode, poll.ElapsedMilliseconds, null);
                    return false;
                }

                List<CliPermissionRequest> rows = await JsonHelper.DeserializeAsync<List<CliPermissionRequest>>(resp).ConfigureAwait(false);
                trace.RecordPoll(resp.StatusCode, poll.ElapsedMilliseconds, rows);
                // Not the first Pending row: the request is listed before its Ask card is posted, and the callers read
                // the card right after this returns (thread_prompt_denied_and_card failed with "Sequence contains no
                // matching element" when it enumerated the messages in that gap).
                found = CliPermissionPendingWait.FirstWithCard(rows);
                return found != null;
            }, 15000).ConfigureAwait(false);
            if (call != null && call.IsCompleted && found == null)
            {
                string detail = call.Exception != null ? call.Exception.GetBaseException().ToString() : call.Status.ToString();
                AssertTrue(false, "the prompt call ended before its request was pending: " + detail);
            }

            if (!ok || found == null)
            {
                // Name the step that stalled: the polls, the server's prompts in flight, the stored rows, the thread pool,
                // and the fixture's warnings.
                string state = await trace.DescribeAsync(fx, thread, call).ConfigureAwait(false);
                AssertTrue(false, "a pending request with its Ask card appeared within 15 s; state:" + Environment.NewLine + state);
            }

            return found!;
        }

        private static HttpRequestMessage McpPost(string endpoint, string? session, string json)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (session != null)
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
            }

            return request;
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

        private static bool IsCliPermissionEvent(string frame, string type, string requestId)
        {
            CliPermissionEventFrame? parsed;
            try { parsed = JsonHelper.Deserialize<CliPermissionEventFrame>(frame); }
            catch (System.Text.Json.JsonException) { return false; }
            return parsed != null && parsed.Type == type && parsed.Data != null && parsed.Data.RequestId == requestId;
        }

        private static async Task<E2eWebSocketFrame> SendCommandAsync(System.Net.WebSockets.ClientWebSocket socket, object message)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonHelper.Serialize(message));
            await socket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                while (true)
                {
                    byte[] buffer = new byte[65536];
                    System.Text.StringBuilder text = new System.Text.StringBuilder();
                    System.Net.WebSockets.WebSocketReceiveResult received;
                    do
                    {
                        received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                        text.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count));
                    }
                    while (!received.EndOfMessage);
                    E2eWebSocketFrame? frame = E2eWebSocketFrame.Parse(text.ToString());
                    if (frame != null && (frame.Type == "command.result" || frame.Type == "command.error")) return frame;
                }
            }
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

        #region Nested-Types

        private sealed class PromptAnswer
        {
            [JsonPropertyName("behavior")]
            public string? Behavior { get; set; } = null;

            [JsonPropertyName("updatedInput")]
            public CliToolInput? UpdatedInput { get; set; } = null;

            [JsonPropertyName("message")]
            public string? Message { get; set; } = null;
        }

        private sealed class CliPermissionEventFrame
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; } = null;

            [JsonPropertyName("data")]
            public CliPermissionEventData? Data { get; set; } = null;
        }

        private sealed class CliPermissionEventData
        {
            [JsonPropertyName("requestId")]
            public string? RequestId { get; set; } = null;

            [JsonPropertyName("status")]
            public CliPermissionRequestStatusEnum? Status { get; set; } = null;

            [JsonPropertyName("request")]
            public CliPermissionRequest? Request { get; set; } = null;
        }

        private sealed class SettingsProbe
        {
            public CliPermissionSettings? Permissions { get; set; } = null;
        }

        #endregion
    }
}
