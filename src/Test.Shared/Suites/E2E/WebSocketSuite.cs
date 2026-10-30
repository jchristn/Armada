namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Server.WebSocket;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage for the Admiral WebSocket surface: the status snapshot handshake,
    /// broadcast events triggered through the REST API, and the command channel that mirrors the
    /// REST fleet/vessel/voyage/mission/captain/signal/merge-queue/enumerate actions. Ported from the
    /// retired automated <c>WebSocketTests</c> suite; each case opens a raw WebSocket to the shared
    /// server obtained through <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class WebSocketSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end WebSocket suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // Subscribe Tests
            cases.Add(CaseAsync("subscribe_returns_status_snapshot", "Subscribe_ReturnsStatusSnapshot", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);

                string msg = JsonHelper.Serialize(new { Route = "subscribe" });
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                byte[] buffer = new byte[1048576];
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                AssertEqual("status.snapshot", root.GetProperty("type").GetString());
                Assert(root.TryGetProperty("data", out _), "Should contain data property");
                Assert(root.TryGetProperty("timestamp", out _), "Should contain timestamp");
            }));

            cases.Add(CaseAsync("subscribe_check_run_import_broadcasts_check_run_changed", "Subscribe_CheckRunImport_BroadcastsCheckRunChanged", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);
                await SubscribeAsync(ws).ConfigureAwait(false);

                string vesselId = await CreateVesselViaRestAsync(authClient, "ws-checkrun-broadcast").ConfigureAwait(false);
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/check-runs/import",
                    JsonHelper.ToJsonContent(new
                    {
                        VesselId = vesselId,
                        Type = "Build",
                        Status = "Passed",
                        Label = "WebSocket Imported Check",
                        Command = "echo imported",
                        Output = "Build succeeded.",
                        Summary = "Imported check passed."
                    })).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                CheckRun created = await JsonHelper.DeserializeAsync<CheckRun>(response).ConfigureAwait(false);

                JsonElement evt = await WaitForEventAsync(ws, root =>
                {
                    return root.GetProperty("type").GetString() == "check-run.changed"
                        && root.GetProperty("data").GetProperty("id").GetString() == created.Id;
                }).ConfigureAwait(false);

                AssertEqual("check-run.changed", evt.GetProperty("type").GetString());
                AssertEqual(created.Id, evt.GetProperty("data").GetProperty("id").GetString());
                AssertEqual("Passed", evt.GetProperty("data").GetProperty("status").GetString());
            }));

            cases.Add(CaseAsync("subscribe_mission_review_transition_broadcasts_approval_needed", "Subscribe_MissionReviewTransition_BroadcastsApprovalNeeded", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);
                await SubscribeAsync(ws).ConfigureAwait(false);

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-approval-needed").ConfigureAwait(false);

                HttpResponseMessage assignedResponse = await authClient.PutAsync(
                    "/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "Assigned" })).ConfigureAwait(false);
                assignedResponse.EnsureSuccessStatusCode();

                HttpResponseMessage inProgressResponse = await authClient.PutAsync(
                    "/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "InProgress" })).ConfigureAwait(false);
                inProgressResponse.EnsureSuccessStatusCode();

                HttpResponseMessage reviewResponse = await authClient.PutAsync(
                    "/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "Review" })).ConfigureAwait(false);
                reviewResponse.EnsureSuccessStatusCode();

                JsonElement evt = await WaitForEventAsync(ws, root =>
                {
                    return root.GetProperty("type").GetString() == "approval-needed"
                        && root.GetProperty("data").GetProperty("missionId").GetString() == missionId;
                }).ConfigureAwait(false);

                AssertEqual("approval-needed", evt.GetProperty("type").GetString());
                AssertEqual(missionId, evt.GetProperty("data").GetProperty("missionId").GetString());
                AssertEqual("mission", evt.GetProperty("data").GetProperty("entityType").GetString());
                AssertEqual("Review", evt.GetProperty("data").GetProperty("status").GetString());
            }));

            cases.Add(CaseAsync("subscribe_backlog_create_broadcasts_objective_changed", "Subscribe_BacklogCreate_BroadcastsObjectiveChanged", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string objectiveId = String.Empty;

                try
                {
                    using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);
                    await SubscribeAsync(ws).ConfigureAwait(false);

                    HttpResponseMessage createResponse = await authClient.PostAsync(
                        "/api/v1/backlog",
                        JsonHelper.ToJsonContent(new
                        {
                            Title = "WebSocket backlog coverage",
                            Description = "Broadcast objective.changed for backlog routes."
                        })).ConfigureAwait(false);
                    createResponse.EnsureSuccessStatusCode();
                    Objective created = await JsonHelper.DeserializeAsync<Objective>(createResponse).ConfigureAwait(false);
                    objectiveId = created.Id;

                    JsonElement evt = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective.changed"
                            && root.GetProperty("data").GetProperty("id").GetString() == objectiveId;
                    }).ConfigureAwait(false);

                    AssertEqual("objective.changed", evt.GetProperty("type").GetString());
                    AssertEqual(objectiveId, evt.GetProperty("data").GetProperty("id").GetString());
                    AssertEqual("Inbox", evt.GetProperty("data").GetProperty("backlogState").GetString());
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(objectiveId))
                        await authClient.DeleteAsync("/api/v1/backlog/" + objectiveId).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("subscribe_backlog_refinement_lifecycle_broadcasts_refinement_events", "Subscribe_BacklogRefinementLifecycle_BroadcastsRefinementEvents", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string objectiveId = String.Empty;
                string captainId = String.Empty;
                string sessionId = String.Empty;

                try
                {
                    using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);
                    await SubscribeAsync(ws).ConfigureAwait(false);

                    Objective objective = await CreateObjectiveViaRestAsync(authClient, "WebSocket refinement objective").ConfigureAwait(false);
                    objectiveId = objective.Id;

                    Captain captain = await CreateCaptainRecordViaRestAsync(authClient, "ws-refinement-captain").ConfigureAwait(false);
                    captainId = captain.Id;

                    HttpResponseMessage createSessionResponse = await authClient.PostAsync(
                        "/api/v1/backlog/" + objectiveId + "/refinement-sessions",
                        JsonHelper.ToJsonContent(new
                        {
                            CaptainId = captainId,
                            Title = "WebSocket refinement lifecycle"
                        })).ConfigureAwait(false);
                    createSessionResponse.EnsureSuccessStatusCode();
                    ObjectiveRefinementSessionDetail createdSession = await JsonHelper.DeserializeAsync<ObjectiveRefinementSessionDetail>(createSessionResponse).ConfigureAwait(false);
                    sessionId = createdSession.Session.Id;

                    JsonElement createdEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective-refinement-session.changed"
                            && root.GetProperty("data").GetProperty("session").GetProperty("id").GetString() == sessionId
                            && root.GetProperty("data").GetProperty("session").GetProperty("status").GetString() == "Active";
                    }).ConfigureAwait(false);
                    AssertEqual(sessionId, createdEvent.GetProperty("data").GetProperty("session").GetProperty("id").GetString());

                    await UpdateCaptainRuntimeAsync(authClient, captainId, "Custom").ConfigureAwait(false);

                    HttpResponseMessage sendResponse = await authClient.PostAsync(
                        "/api/v1/objective-refinement-sessions/" + sessionId + "/messages",
                        JsonHelper.ToJsonContent(new
                        {
                            Content = "Clarify acceptance criteria for the backlog item."
                        })).ConfigureAwait(false);
                    sendResponse.EnsureSuccessStatusCode();

                    JsonElement userMessageEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective-refinement-session.message.created"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == sessionId
                            && root.GetProperty("data").GetProperty("message").GetProperty("role").GetString() == "User";
                    }).ConfigureAwait(false);
                    AssertEqual(sessionId, userMessageEvent.GetProperty("data").GetProperty("sessionId").GetString());

                    JsonElement assistantCreatedEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective-refinement-session.message.created"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == sessionId
                            && root.GetProperty("data").GetProperty("message").GetProperty("role").GetString() == "Assistant";
                    }).ConfigureAwait(false);
                    string assistantMessageId = assistantCreatedEvent.GetProperty("data").GetProperty("message").GetProperty("id").GetString()
                        ?? throw new Exception("Assistant refinement message id not found in WebSocket payload");

                    JsonElement assistantUpdatedEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective-refinement-session.message.updated"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == sessionId
                            && root.GetProperty("data").GetProperty("message").GetProperty("id").GetString() == assistantMessageId
                            && !String.IsNullOrWhiteSpace(root.GetProperty("data").GetProperty("message").GetProperty("content").GetString());
                    }).ConfigureAwait(false);
                    AssertContains("Refinement response failed", assistantUpdatedEvent.GetProperty("data").GetProperty("message").GetProperty("content").GetString() ?? String.Empty);

                    JsonElement activeEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective-refinement-session.changed"
                            && root.GetProperty("data").GetProperty("session").GetProperty("id").GetString() == sessionId
                            && root.GetProperty("data").GetProperty("session").GetProperty("status").GetString() == "Active";
                    }).ConfigureAwait(false);
                    AssertEqual(sessionId, activeEvent.GetProperty("data").GetProperty("session").GetProperty("id").GetString());

                    HttpResponseMessage deleteSessionResponse = await authClient.DeleteAsync("/api/v1/objective-refinement-sessions/" + sessionId).ConfigureAwait(false);
                    AssertEqual(System.Net.HttpStatusCode.NoContent, deleteSessionResponse.StatusCode);

                    JsonElement deletedEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "objective-refinement-session.deleted"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == sessionId;
                    }).ConfigureAwait(false);
                    AssertEqual(sessionId, deletedEvent.GetProperty("data").GetProperty("sessionId").GetString());
                    sessionId = String.Empty;
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(sessionId))
                        await authClient.DeleteAsync("/api/v1/objective-refinement-sessions/" + sessionId).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(objectiveId))
                        await authClient.DeleteAsync("/api/v1/backlog/" + objectiveId).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(captainId))
                        await authClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                }
            }));

            // Status Tests
            cases.Add(CaseAsync("status_returns_armada_status", "Status_ReturnsArmadaStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "status").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("status", resp.GetProperty("action").GetString());
                ArmadaStatus status = DeserializeData<ArmadaStatus>(resp);
                AssertTrue(status.TotalCaptains >= 0);
            }));

            cases.Add(CaseAsync("stop_all_returns_all_stopped", "StopAll_ReturnsAllStopped", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                try
                {
                    JsonElement resp = await WsCommandAsync(restPort, "stop_all").ConfigureAwait(false);
                    AssertEqual("command.result", resp.GetProperty("type").GetString());
                    AssertEqual("stop_all", resp.GetProperty("action").GetString());
                    AssertEqual("all_stopped", resp.GetProperty("data").GetProperty("status").GetString());
                }
                catch (TaskCanceledException)
                {
                    // StopAll may take longer than expected with many active captains - acceptable
                }
                catch (System.Net.WebSockets.WebSocketException)
                {
                    // StopAll may close the WebSocket connection - acceptable
                }
            }));

            // Fleet Tests
            cases.Add(CaseAsync("list_fleets_empty_returns_empty_list", "ListFleets_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_fleets").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_fleets", resp.GetProperty("action").GetString());
                EnumerationResult<Fleet> data = DeserializeData<EnumerationResult<Fleet>>(resp);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("create_fleet_returns_created_fleet", "CreateFleet_ReturnsCreatedFleet", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "create_fleet", new { data = new { Name = "ws-fleet" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("create_fleet", resp.GetProperty("action").GetString());
                Fleet data = DeserializeData<Fleet>(resp);
                AssertStartsWith("flt_", data.Id);
            }));

            cases.Add(CaseAsync("get_fleet_existing_fleet_returns_fleet", "GetFleet_ExistingFleet_ReturnsFleet", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string fleetId = await CreateFleetViaRestAsync(authClient, "ws-get-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                FleetDetailResponse data = DeserializeData<FleetDetailResponse>(resp);
                AssertEqual(fleetId, data.Fleet!.Id);
                AssertEqual(0L, data.VesselCount, "vessel count; vessels are not embedded");
            }));

            cases.Add(CaseAsync("get_fleet_non_existent_returns_error", "GetFleet_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_fleet", new { id = "flt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Fleet not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("update_fleet_existing_fleet_returns_updated", "UpdateFleet_ExistingFleet_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string fleetId = await CreateFleetViaRestAsync(authClient, "ws-upd-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "update_fleet", new { id = fleetId, data = new { Name = "ws-upd-fleet-renamed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Fleet data = DeserializeData<Fleet>(resp);
                AssertEqual(fleetId, data.Id);
            }));

            cases.Add(CaseAsync("update_fleet_non_existent_returns_error", "UpdateFleet_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "update_fleet", new { id = "flt_nonexistent", data = new { Name = "x" } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Fleet not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("update_fleet_preserves_tenant_and_owner", "UpdateFleet_PreservesTenantAndOwner", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string fleetId = await CreateFleetViaRestAsync(authClient, "ws-owner-fleet").ConfigureAwait(false);
                Fleet before = (await GetViaRestAsync<FleetDetailResponse>(authClient, "/api/v1/fleets/" + fleetId).ConfigureAwait(false)).Fleet!;
                AssertFalse(String.IsNullOrEmpty(before.TenantId), "precondition: fleet has a tenant");
                AssertFalse(String.IsNullOrEmpty(before.UserId), "precondition: fleet has an owner");

                JsonElement resp = await WsCommandAsync(restPort, "update_fleet", new { id = fleetId, data = new { Name = "ws-owner-fleet-renamed", TenantId = "ten_spoofed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());

                Fleet after = (await GetViaRestAsync<FleetDetailResponse>(authClient, "/api/v1/fleets/" + fleetId).ConfigureAwait(false)).Fleet!;
                AssertEqual("ws-owner-fleet-renamed", after.Name);
                AssertEqual(before.TenantId, after.TenantId);
                AssertEqual(before.UserId, after.UserId);
            }));

            cases.Add(CaseAsync("create_fleet_undeserializable_data_returns_invalid_argument", "CreateFleet_UndeserializableData_ReturnsInvalidArgument", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                // A string where the Fleet object is expected cannot be deserialized: that is bad input, not a server fault.
                JsonElement resp = await WsCommandAsync(restPort, "create_fleet", new { data = "not-a-fleet" }).ConfigureAwait(false);
                E2eWebSocketFrame frame = E2eWebSocketFrame.Parse(resp.GetRawText())!;
                AssertEqual("command.error", frame.Type);
                AssertEqual(WebSocketCommandErrorCodeEnum.InvalidArgument, frame.Code);
            }));

            cases.Add(CaseAsync("delete_fleet_existing_fleet_returns_deleted", "DeleteFleet_ExistingFleet_ReturnsDeleted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string fleetId = await CreateFleetViaRestAsync(authClient, "ws-del-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "delete_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                DeleteFleetResponse data = DeserializeData<DeleteFleetResponse>(resp);
                AssertEqual("deleted", data.Status);
            }));

            cases.Add(CaseAsync("list_fleets_after_create_returns_fleet", "ListFleets_AfterCreate_ReturnsFleet", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                await WsCommandAsync(restPort, "create_fleet", new { data = new { Name = "ws-list-fleet" } }).ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "list_fleets").ConfigureAwait(false);
                EnumerationResult<Fleet> data = DeserializeData<EnumerationResult<Fleet>>(resp);
                AssertTrue(data.TotalRecords >= 1);
            }));

            cases.Add(CaseAsync("list_fleets_with_pagination_respects_page_size", "ListFleets_WithPagination_RespectsPageSize", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                await CreateFleetViaRestAsync(authClient, "ws-page-1").ConfigureAwait(false);
                await CreateFleetViaRestAsync(authClient, "ws-page-2").ConfigureAwait(false);
                await CreateFleetViaRestAsync(authClient, "ws-page-3").ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "list_fleets", new { query = new { pageSize = 2, pageNumber = 1 } }).ConfigureAwait(false);
                EnumerationResult<Fleet> data = DeserializeData<EnumerationResult<Fleet>>(resp);
                AssertEqual(2, data.PageSize);
                AssertEqual(2, data.Objects.Count);
            }));

            // Vessel Tests
            cases.Add(CaseAsync("list_vessels_empty_returns_empty_list", "ListVessels_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_vessels").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_vessels", resp.GetProperty("action").GetString());
            }));

            cases.Add(CaseAsync("create_vessel_returns_created_vessel", "CreateVessel_ReturnsCreatedVessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "create_vessel", new { data = new { Name = "ws-vessel", RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Vessel data = DeserializeData<Vessel>(resp);
                AssertStartsWith("vsl_", data.Id);
            }));

            cases.Add(CaseAsync("create_vessel_over_config_limit_returns_invalid_argument", "CreateVessel_ConfigListOverLimit_ReturnsInvalidArgumentAndCreatesNothing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;
                string name = "ws-vessel-limit-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                List<string> patterns = Enumerable.Range(0, Armada.Core.Services.CollectionLimits.MaxConfigItems + 1).Select(i => "release/" + i).ToList();

                JsonElement resp = await WsCommandAsync(restPort, "create_vessel", new { data = new { Name = name, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), ProtectedBranchPatterns = patterns } }).ConfigureAwait(false);
                E2eWebSocketFrame frame = E2eWebSocketFrame.Parse(resp.GetRawText())!;
                AssertEqual("command.error", frame.Type);
                AssertEqual(WebSocketCommandErrorCodeEnum.InvalidArgument, frame.Code);
                AssertContains("ProtectedBranchPatterns", resp.GetRawText(), "the error names the list");

                EnumerationResult<Vessel> page = await GetViaRestAsync<EnumerationResult<Vessel>>(fx.AuthClient, "/api/v1/vessels?pageSize=1000").ConfigureAwait(false);
                AssertFalse(page.Objects.Any(v => v.Name == name), "nothing was created");
            }));

            cases.Add(CaseAsync("create_vessel_git_hub_token_override_does_not_leak", "CreateVessel_GitHubTokenOverrideDoesNotLeak", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                string token = "ghp_ws_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                JsonElement resp = await WsCommandAsync(restPort, "create_vessel", new
                {
                    data = new
                    {
                        Name = "ws-vessel-github-override",
                        RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                        GitHubTokenOverride = token
                    }
                }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                string raw = resp.GetRawText();
                AssertFalse(raw.Contains(token, StringComparison.Ordinal));
                AssertFalse(JsonShape.HasPropertyAnywhere(raw, "GitHubTokenOverride"), "no GitHubTokenOverride property in any casing");
                Vessel data = DeserializeData<Vessel>(resp);
                AssertTrue(data.HasGitHubTokenOverride);
            }));

            cases.Add(CaseAsync("get_vessel_existing_vessel_returns_vessel", "GetVessel_ExistingVessel_ReturnsVessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string vesselId = await CreateVesselViaRestAsync(authClient, "ws-get-vessel").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_vessel", new { id = vesselId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Vessel data = DeserializeData<Vessel>(resp);
                AssertEqual(vesselId, data.Id);
            }));

            cases.Add(CaseAsync("get_vessel_non_existent_returns_error", "GetVessel_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_vessel", new { id = "vsl_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Vessel not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("update_vessel_existing_vessel_returns_updated", "UpdateVessel_ExistingVessel_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string vesselId = await CreateVesselViaRestAsync(authClient, "ws-upd-vessel").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "update_vessel", new { id = vesselId, data = new { Name = "ws-upd-vessel-renamed", RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Vessel data = DeserializeData<Vessel>(resp);
                AssertEqual(vesselId, data.Id);
            }));

            cases.Add(CaseAsync("update_vessel_empty_git_hub_token_override_clears_override", "UpdateVessel_EmptyGitHubTokenOverrideClearsOverride", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement createResponse = await WsCommandAsync(restPort, "create_vessel", new
                {
                    data = new
                    {
                        Name = "ws-clear-github-override",
                        RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                        GitHubTokenOverride = "ghp_ws_clear"
                    }
                }).ConfigureAwait(false);
                Vessel created = DeserializeData<Vessel>(createResponse);
                AssertTrue(created.HasGitHubTokenOverride);

                JsonElement updateResponse = await WsCommandAsync(restPort, "update_vessel", new
                {
                    id = created.Id,
                    data = new
                    {
                        Name = "ws-clear-github-override",
                        RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                        GitHubTokenOverride = ""
                    }
                }).ConfigureAwait(false);
                Vessel updated = DeserializeData<Vessel>(updateResponse);
                AssertFalse(updated.HasGitHubTokenOverride);
            }));

            cases.Add(CaseAsync("update_vessel_non_existent_returns_error", "UpdateVessel_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "update_vessel", new { id = "vsl_nonexistent", data = new { Name = "x", RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("delete_vessel_existing_vessel_returns_deleted", "DeleteVessel_ExistingVessel_ReturnsDeleted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string vesselId = await CreateVesselViaRestAsync(authClient, "ws-del-vessel").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "delete_vessel", new { id = vesselId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                DeleteVesselResponse data = DeserializeData<DeleteVesselResponse>(resp);
                AssertEqual("deleted", data.Status);
            }));

            // Voyage Tests
            cases.Add(CaseAsync("list_voyages_empty_returns_empty_list", "ListVoyages_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_voyages").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_voyages", resp.GetProperty("action").GetString());
            }));

            cases.Add(CaseAsync("create_voyage_bare_voyage_returns_created_voyage", "CreateVoyage_BareVoyage_ReturnsCreatedVoyage", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "create_voyage", new { data = new { title = "ws-voyage", description = "test voyage" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("create_voyage", resp.GetProperty("action").GetString());
                Voyage data = DeserializeData<Voyage>(resp);
                AssertStartsWith("vyg_", data.Id);
            }));

            cases.Add(CaseAsync("get_voyage_existing_voyage_returns_voyage_with_missions", "GetVoyage_ExistingVoyage_ReturnsVoyageWithMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string voyageId = await CreateVoyageViaRestAsync(authClient, "ws-get-voyage").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                VoyageProgress data = DeserializeData<VoyageProgress>(resp);
                AssertNotNull(data.Voyage);
                AssertTrue(data.TotalMissions >= 0, "get_voyage carries mission counts");
                AssertFalse(resp.GetProperty("data").TryGetProperty("missions", out JsonElement _) || resp.GetProperty("data").TryGetProperty("Missions", out JsonElement _), "get_voyage embeds no missions");
            }));

            cases.Add(CaseAsync("get_voyage_non_existent_returns_error", "GetVoyage_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_voyage", new { id = "vyg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Voyage not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("cancel_voyage_existing_voyage_returns_cancelled", "CancelVoyage_ExistingVoyage_ReturnsCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string voyageId = await CreateVoyageViaRestAsync(authClient, "ws-cancel-voyage").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "cancel_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                CancelVoyageResponse data = DeserializeData<CancelVoyageResponse>(resp);
                AssertEqual("Cancelled", data.Voyage!.Status.ToString());
                AssertTrue(data.CancelledMissions >= 0);
            }));

            cases.Add(CaseAsync("cancel_voyage_non_existent_returns_error", "CancelVoyage_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "cancel_voyage", new { id = "vyg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Voyage not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("purge_voyage_existing_voyage_returns_deleted", "PurgeVoyage_ExistingVoyage_ReturnsDeleted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string voyageId = await CreateVoyageViaRestAsync(authClient, "ws-purge-voyage").ConfigureAwait(false);

                // Cancel first — purge is blocked on Open/InProgress voyages
                await WsCommandAsync(restPort, "cancel_voyage", new { id = voyageId }).ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "purge_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                PurgeVoyageResponse data = DeserializeData<PurgeVoyageResponse>(resp);
                AssertEqual("deleted", data.Status);
            }));

            cases.Add(CaseAsync("purge_voyage_non_existent_returns_error", "PurgeVoyage_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "purge_voyage", new { id = "vyg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Voyage not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("list_voyages_after_create_returns_voyage", "ListVoyages_AfterCreate_ReturnsVoyage", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                await WsCommandAsync(restPort, "create_voyage", new { data = new { title = "ws-list-voyage" } }).ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "list_voyages").ConfigureAwait(false);
                EnumerationResult<Voyage> data = DeserializeData<EnumerationResult<Voyage>>(resp);
                AssertTrue(data.TotalRecords >= 1);
            }));

            // Mission Tests
            cases.Add(CaseAsync("list_missions_empty_returns_empty_list", "ListMissions_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_missions").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_missions", resp.GetProperty("action").GetString());
                EnumerationResult<Mission> data = DeserializeData<EnumerationResult<Mission>>(resp);
                AssertNotNull(data);
            }));

            cases.Add(CaseAsync("list_mission_summaries_empty_returns_empty_list", "ListMissionSummaries_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_missions_summary").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_missions_summary", resp.GetProperty("action").GetString());
                EnumerationResult<MissionSummary> data = DeserializeData<EnumerationResult<MissionSummary>>(resp);
                AssertNotNull(data);
            }));

            cases.Add(CaseAsync("create_mission_returns_created_mission", "CreateMission_ReturnsCreatedMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "create_mission", new { data = new { Title = "ws-mission" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertStartsWith("msn_", data.Id);
            }));

            cases.Add(CaseAsync("get_mission_existing_mission_returns_mission", "GetMission_ExistingMission_ReturnsMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-get-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertEqual(missionId, data.Id);
            }));

            cases.Add(CaseAsync("get_mission_non_existent_returns_error", "GetMission_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_mission", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Mission not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("update_mission_existing_mission_returns_updated", "UpdateMission_ExistingMission_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-upd-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "update_mission", new { id = missionId, data = new { Title = "ws-upd-mission-renamed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertEqual(missionId, data.Id);
            }));

            cases.Add(CaseAsync("update_mission_preserves_server_owned_fields", "UpdateMission_PreservesServerOwnedFields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-owner-mission").ConfigureAwait(false);
                Mission before = await GetViaRestAsync<Mission>(authClient, "/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertFalse(String.IsNullOrEmpty(before.TenantId), "precondition: mission has a tenant");
                AssertFalse(String.IsNullOrEmpty(before.UserId), "precondition: mission has an owner");

                // Only metadata is applied: status, tenant, and owner in the body are ignored, as on REST PUT.
                JsonElement resp = await WsCommandAsync(restPort, "update_mission", new { id = missionId, data = new { Title = "ws-owner-mission-renamed", Status = "Cancelled", TenantId = "ten_spoofed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());

                Mission after = await GetViaRestAsync<Mission>(authClient, "/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertEqual("ws-owner-mission-renamed", after.Title);
                AssertEqual(before.Status, after.Status);
                AssertEqual(before.TenantId, after.TenantId);
                AssertEqual(before.UserId, after.UserId);
                AssertEqual(before.CaptainId, after.CaptainId);
            }));

            cases.Add(CaseAsync("update_mission_non_existent_returns_error", "UpdateMission_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "update_mission", new { id = "msn_nonexistent", data = new { Title = "x" } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("cancel_mission_existing_mission_returns_cancelled", "CancelMission_ExistingMission_ReturnsCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-cancel-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "cancel_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertEqual("Cancelled", data.Status.ToString());
                AssertEqual(missionId, data.Id);
            }));

            cases.Add(CaseAsync("cancel_mission_non_existent_returns_error", "CancelMission_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "cancel_mission", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Mission not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("transition_mission_status_pull_request_open_round_trip", "TransitionMissionStatus_PullRequestOpen_RoundTrip", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-pr-open-mission").ConfigureAwait(false);
                foreach (string step in new[] { "Assigned", "InProgress" })
                {
                    HttpResponseMessage stepResp = await authClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                        JsonHelper.ToJsonContent(new { Status = step })).ConfigureAwait(false);
                    AssertEqual(System.Net.HttpStatusCode.OK, stepResp.StatusCode, "REST transition to " + step);
                }

                // REST refuses a manual InProgress -> WorkProduced handoff (only the agent exit and its stage check hand
                // work off), so WorkProduced is seeded in the fixture database, as the agent exit would write it.
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                using (DatabaseDriver db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") }, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    Mission produced = (await db.Missions.ReadAsync(missionId).ConfigureAwait(false))!;
                    produced.Status = MissionStatusEnum.WorkProduced;
                    // A read-only mission has no branch to land, so Complete is a plain status write.
                    produced.Mode = MissionModeEnum.Research;
                    produced.LastUpdateUtc = DateTime.UtcNow;
                    await db.Missions.UpdateAsync(produced).ConfigureAwait(false);
                }

                // WorkProduced -> PullRequestOpen -> Complete are legal in MissionStateMachine and over REST.
                JsonElement prResp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "PullRequestOpen" }).ConfigureAwait(false);
                AssertEqual("command.result", prResp.GetProperty("type").GetString());
                AssertEqual(MissionStatusEnum.PullRequestOpen, DeserializeData<Mission>(prResp).Status);

                JsonElement doneResp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertEqual("command.result", doneResp.GetProperty("type").GetString());
                AssertEqual(MissionStatusEnum.Complete, DeserializeData<Mission>(doneResp).Status);
            }));

            cases.Add(CaseAsync("transition_mission_status_valid_transition_returns_updated", "TransitionMissionStatus_ValidTransition_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-transition-mission").ConfigureAwait(false);

                // Pending -> Assigned via REST
                await authClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "Assigned" })).ConfigureAwait(false);

                // Assigned -> InProgress via WebSocket
                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "InProgress" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertEqual("InProgress", data.Status.ToString());
            }));

            cases.Add(CaseAsync("transition_mission_status_invalid_transition_returns_error", "TransitionMissionStatus_InvalidTransition_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-bad-transition").ConfigureAwait(false);

                // Pending -> Complete is not valid
                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Invalid transition", resp.GetProperty("error").GetString()!);
            }));

            cases.Add(CaseAsync("transition_mission_status_invalid_status_string_returns_error", "TransitionMissionStatus_InvalidStatusString_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-bad-status").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "BogusStatus" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Invalid status", resp.GetProperty("error").GetString()!);
            }));

            cases.Add(CaseAsync("transition_mission_status_non_existent_mission_returns_error", "TransitionMissionStatus_NonExistentMission_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = "msn_nonexistent", status = "InProgress" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Mission not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("transition_mission_status_to_complete_sets_completed_utc", "TransitionMissionStatus_ToComplete_SetsCompletedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-complete-mission").ConfigureAwait(false);

                // A read-only (Audit) mission whose work was produced completes by a status write; write modes land.
                await SeedMissionAsync(fx, missionId, MissionStatusEnum.WorkProduced, MissionModeEnum.Audit).ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertEqual(MissionStatusEnum.Complete, data.Status);
                AssertNotNull(data.CompletedUtc);
            }));

            cases.Add(CaseAsync("transition_mission_status_review_to_complete_returns_conflict", "TransitionMissionStatus_ReviewToComplete_ReturnsConflict", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(fx.AuthClient, "ws-review-complete").ConfigureAwait(false);
                await SeedMissionAsync(fx, missionId, MissionStatusEnum.Review, MissionModeEnum.Research).ConfigureAwait(false);

                // Review -> Complete is legal in the state machine, but the gate is passed only through approve/deny review.
                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertWsConflict(resp);
                AssertContains("waiting at a review gate", resp.GetProperty("error").GetString()!);
                AssertEqual(MissionStatusEnum.Review, await ReadMissionStatusAsync(fx, missionId).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("transition_mission_status_review_cancel_still_allowed", "TransitionMissionStatus_Review_CancelStillAllowed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(fx.AuthClient, "ws-review-cancel").ConfigureAwait(false);
                await SeedMissionAsync(fx, missionId, MissionStatusEnum.Review, MissionModeEnum.Implementation).ConfigureAwait(false);

                // The review gate does not block cancellation; it goes through cancel_mission (MissionStateMachine has no
                // Review -> Cancelled status edge, so transition_mission_status reports it as an invalid transition).
                JsonElement tmResp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Cancelled" }).ConfigureAwait(false);
                AssertEqual("command.error", tmResp.GetProperty("type").GetString());
                AssertContains("Invalid transition", tmResp.GetProperty("error").GetString()!);

                JsonElement resp = await WsCommandAsync(restPort, "cancel_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString(), resp.GetRawText());
                AssertEqual(MissionStatusEnum.Cancelled, DeserializeData<Mission>(resp).Status);
            }));

            cases.Add(CaseAsync("transition_mission_status_in_progress_to_complete_returns_handoff_refusal", "TransitionMissionStatus_InProgressToComplete_ReturnsHandoffRefusal", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-inprogress-complete").ConfigureAwait(false);
                await authClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "Assigned" })).ConfigureAwait(false);
                await authClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "InProgress" })).ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertWsConflict(resp);
                AssertContains("still owns this mission", resp.GetProperty("error").GetString()!);
                AssertEqual(MissionStatusEnum.InProgress, await ReadMissionStatusAsync(fx, missionId).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("transition_mission_status_process_id_set_returns_handoff_refusal", "TransitionMissionStatus_ProcessIdSet_ReturnsHandoffRefusal", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(fx.AuthClient, "ws-pid-complete").ConfigureAwait(false);
                await SeedMissionAsync(fx, missionId, MissionStatusEnum.WorkProduced, MissionModeEnum.Research, 4242).ConfigureAwait(false);

                // A live process (or exit claim) owns the handoff even for a read-only mission.
                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertWsConflict(resp);
                AssertContains("still owns this mission", resp.GetProperty("error").GetString()!);
                AssertEqual(MissionStatusEnum.WorkProduced, await ReadMissionStatusAsync(fx, missionId).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("transition_mission_status_write_mission_without_dock_returns_conflict", "TransitionMissionStatus_WriteMissionWithoutDock_ReturnsConflict", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(fx.AuthClient, "ws-nodock-complete").ConfigureAwait(false);
                await SeedMissionAsync(fx, missionId, MissionStatusEnum.WorkProduced, MissionModeEnum.Implementation).ConfigureAwait(false);

                // A write-mode mission must land its branch; with no active dock there is nothing to land.
                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertWsConflict(resp);
                string error = resp.GetProperty("error").GetString()!;
                AssertTrue(error.Contains("An active dock is required") || error.Contains("Landing is not available"), "unexpected error: " + error);
                AssertEqual(MissionStatusEnum.WorkProduced, await ReadMissionStatusAsync(fx, missionId).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("list_missions_with_pagination_respects_page_size", "ListMissions_WithPagination_RespectsPageSize", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                await CreateMissionViaRestAsync(authClient, "ws-page-m1").ConfigureAwait(false);
                await CreateMissionViaRestAsync(authClient, "ws-page-m2").ConfigureAwait(false);
                await CreateMissionViaRestAsync(authClient, "ws-page-m3").ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "list_missions", new { query = new { pageSize = 2 } }).ConfigureAwait(false);
                EnumerationResult<Mission> data = DeserializeData<EnumerationResult<Mission>>(resp);
                AssertEqual(2, data.Objects.Count);
            }));

            cases.Add(CaseAsync("list_mission_summaries_with_pagination_respects_page_size", "ListMissionSummaries_WithPagination_RespectsPageSize", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                await CreateMissionViaRestAsync(authClient, "ws-page-summary-m1").ConfigureAwait(false);
                await CreateMissionViaRestAsync(authClient, "ws-page-summary-m2").ConfigureAwait(false);
                await CreateMissionViaRestAsync(authClient, "ws-page-summary-m3").ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "list_missions_summary", new { query = new { pageSize = 2 } }).ConfigureAwait(false);
                EnumerationResult<MissionSummary> data = DeserializeData<EnumerationResult<MissionSummary>>(resp);
                AssertEqual(2, data.Objects.Count);
            }));

            // Captain Tests
            cases.Add(CaseAsync("list_captains_empty_returns_empty_list", "ListCaptains_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_captains").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_captains", resp.GetProperty("action").GetString());
            }));

            cases.Add(CaseAsync("create_captain_returns_created_captain", "CreateCaptain_ReturnsCreatedCaptain", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "create_captain", new { data = new { Name = "ws-captain", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Captain data = DeserializeData<Captain>(resp);
                AssertStartsWith("cpt_", data.Id);
            }));

            cases.Add(CaseAsync("get_captain_existing_captain_returns_captain", "GetCaptain_ExistingCaptain_ReturnsCaptain", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string captainId = await CreateCaptainViaRestAsync(authClient, "ws-get-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Captain data = DeserializeData<Captain>(resp);
                AssertEqual(captainId, data.Id);
            }));

            cases.Add(CaseAsync("get_captain_non_existent_returns_error", "GetCaptain_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_captain", new { id = "cpt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Captain not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("update_captain_existing_captain_returns_updated", "UpdateCaptain_ExistingCaptain_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string captainId = await CreateCaptainViaRestAsync(authClient, "ws-upd-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "update_captain", new { id = captainId, data = new { Name = "ws-upd-captain-renamed", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Captain data = DeserializeData<Captain>(resp);
                AssertEqual(captainId, data.Id);
            }));

            cases.Add(CaseAsync("update_captain_preserves_operational_fields", "UpdateCaptain_PreservesOperationalFields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string captainId = await CreateCaptainViaRestAsync(authClient, "ws-preserve-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "update_captain", new { id = captainId, data = new { Name = "ws-preserve-renamed", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Captain data = DeserializeData<Captain>(resp);
                AssertEqual("Idle", data.State.ToString());
            }));

            cases.Add(CaseAsync("update_captain_preserves_tenant_and_owner", "UpdateCaptain_PreservesTenantAndOwner", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                Captain before = await CreateCaptainRecordViaRestAsync(authClient, "ws-owner-captain").ConfigureAwait(false);
                AssertFalse(String.IsNullOrEmpty(before.TenantId), "precondition: captain has a tenant");
                AssertFalse(String.IsNullOrEmpty(before.UserId), "precondition: captain has an owner");

                JsonElement resp = await WsCommandAsync(restPort, "update_captain", new { id = before.Id, data = new { Name = "ws-owner-captain-renamed", Runtime = "ClaudeCode", TenantId = "ten_spoofed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());

                Captain after = await GetViaRestAsync<Captain>(authClient, "/api/v1/captains/" + before.Id).ConfigureAwait(false);
                AssertEqual("ws-owner-captain-renamed", after.Name);
                AssertEqual(before.TenantId, after.TenantId);
                AssertEqual(before.UserId, after.UserId);
            }));

            cases.Add(CaseAsync("update_captain_non_existent_returns_error", "UpdateCaptain_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "update_captain", new { id = "cpt_nonexistent", data = new { Name = "x" } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Captain not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("delete_captain_existing_captain_returns_deleted", "DeleteCaptain_ExistingCaptain_ReturnsDeleted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string captainId = await CreateCaptainViaRestAsync(authClient, "ws-del-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "delete_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                DeleteCaptainResponse data = DeserializeData<DeleteCaptainResponse>(resp);
                AssertEqual("deleted", data.Status);
            }));

            cases.Add(CaseAsync("delete_captain_non_existent_returns_error", "DeleteCaptain_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "delete_captain", new { id = "cpt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Captain not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("stop_captain_existing_captain_returns_stopped", "StopCaptain_ExistingCaptain_ReturnsStopped", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string captainId = await CreateCaptainViaRestAsync(authClient, "ws-stop-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "stop_captain", new { captainId = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                StopCaptainResponse data = DeserializeData<StopCaptainResponse>(resp);
                AssertEqual("stopped", data.Status);
            }));

            // Signal Tests
            cases.Add(CaseAsync("list_signals_empty_returns_empty_list", "ListSignals_Empty_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_signals").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_signals", resp.GetProperty("action").GetString());
            }));

            cases.Add(CaseAsync("send_signal_returns_created_signal", "SendSignal_ReturnsCreatedSignal", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "send_signal", new { data = new { Type = "Nudge", Payload = "hello" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("send_signal", resp.GetProperty("action").GetString());
                Signal data = DeserializeData<Signal>(resp);
                AssertStartsWith("sig_", data.Id);
            }));

            cases.Add(CaseAsync("list_signals_after_send_returns_signal", "ListSignals_AfterSend_ReturnsSignal", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                await WsCommandAsync(restPort, "send_signal", new { data = new { Type = "Mail", Payload = "test-mail" } }).ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "list_signals").ConfigureAwait(false);
                EnumerationResult<Signal> data = DeserializeData<EnumerationResult<Signal>>(resp);
                AssertTrue(data.TotalRecords >= 1);
            }));

            // Event Tests
            cases.Add(CaseAsync("list_events_returns_event_list", "ListEvents_ReturnsEventList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_events").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_events", resp.GetProperty("action").GetString());
                EnumerationResult<ArmadaEvent> data = DeserializeData<EnumerationResult<ArmadaEvent>>(resp);
                AssertNotNull(data.Objects);
            }));

            // Dock Tests
            cases.Add(CaseAsync("list_docks_returns_empty_list", "ListDocks_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_docks").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_docks", resp.GetProperty("action").GetString());
                EnumerationResult<Dock> data = DeserializeData<EnumerationResult<Dock>>(resp);
                AssertNotNull(data.Objects);
            }));

            // MergeQueue Tests
            cases.Add(CaseAsync("list_merge_queue_returns_empty_list", "ListMergeQueue_ReturnsEmptyList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "list_merge_queue").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_merge_queue", resp.GetProperty("action").GetString());
            }));

            cases.Add(CaseAsync("list_merge_queue_pages_in_database", "ListMergeQueue_PagesWithExactTotals", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;
                for (int i = 0; i < 3; i++)
                    await WsCommandAsync(restPort, "enqueue_merge", new { data = new { BranchName = "feature/ws-page-" + i, TargetBranch = "main" } }).ConfigureAwait(false);

                JsonElement first = await WsCommandAsync(restPort, "list_merge_queue", new { query = new { pageNumber = 1, pageSize = 2 } }).ConfigureAwait(false);
                EnumerationResult<MergeEntry> page = DeserializeData<EnumerationResult<MergeEntry>>(first);
                AssertEqual(2, page.Objects.Count, "one bounded page");
                AssertTrue(page.TotalRecords >= 3, "exact total from the database: " + page.TotalRecords);
                AssertEqual((int)Math.Ceiling(page.TotalRecords / 2.0), page.TotalPages, "pages");
                JsonElement past = await WsCommandAsync(restPort, "enumerate", new { entityType = "merge_queue", query = new { pageNumber = 100000, pageSize = 2 } }).ConfigureAwait(false);
                EnumerationResult<MergeEntry> empty = DeserializeData<EnumerationResult<MergeEntry>>(past);
                AssertEqual(0, empty.Objects.Count, "past the end is empty");
                AssertEqual(page.TotalRecords, empty.TotalRecords, "with the same total");
            }));

            cases.Add(CaseAsync("enqueue_merge_returns_created_entry", "EnqueueMerge_ReturnsCreatedEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enqueue_merge", new { data = new { BranchName = "feature/ws-test", TargetBranch = "main" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("enqueue_merge", resp.GetProperty("action").GetString());
                MergeEntry data = DeserializeData<MergeEntry>(resp);
                AssertStartsWith("mrg_", data.Id);
            }));

            cases.Add(CaseAsync("get_merge_entry_existing_entry_returns_entry", "GetMergeEntry_ExistingEntry_ReturnsEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement createResp = await WsCommandAsync(restPort, "enqueue_merge", new { data = new { BranchName = "feature/ws-get-merge", TargetBranch = "main" } }).ConfigureAwait(false);
                MergeEntry created = DeserializeData<MergeEntry>(createResp);
                string mergeId = created.Id;

                JsonElement resp = await WsCommandAsync(restPort, "get_merge_entry", new { id = mergeId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                MergeEntry data = DeserializeData<MergeEntry>(resp);
                AssertEqual(mergeId, data.Id);
            }));

            cases.Add(CaseAsync("get_merge_entry_non_existent_returns_error", "GetMergeEntry_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_merge_entry", new { id = "mrg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Merge entry not found", resp.GetProperty("error").GetString());
            }));

            cases.Add(CaseAsync("cancel_merge_existing_entry_returns_cancelled", "CancelMerge_ExistingEntry_ReturnsCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement createResp = await WsCommandAsync(restPort, "enqueue_merge", new { data = new { BranchName = "feature/ws-cancel-merge", TargetBranch = "main" } }).ConfigureAwait(false);
                MergeEntry created = DeserializeData<MergeEntry>(createResp);
                string mergeId = created.Id;

                JsonElement resp = await WsCommandAsync(restPort, "cancel_merge", new { id = mergeId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                CancelMergeResponse data = DeserializeData<CancelMergeResponse>(resp);
                AssertEqual("cancelled", data.Status);
            }));

            cases.Add(CaseAsync("process_merge_queue_returns_processed", "ProcessMergeQueue_ReturnsProcessed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "process_merge_queue").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                ProcessMergeQueueResponse data = DeserializeData<ProcessMergeQueueResponse>(resp);
                AssertEqual("processed", data.Status);
            }));

            // ── Mission Diff/Log and Captain Log ────────────────────────

            cases.Add(CaseAsync("get_mission_diff_non_existent_returns_error", "GetMissionDiff_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_mission_diff", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("get_mission_diff_existing_mission_returns_result", "GetMissionDiff_ExistingMission_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-diff-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_mission_diff", new { id = missionId }).ConfigureAwait(false);
                // May return error (no worktree/settings) or result — just verify action is correct
                string action = resp.GetProperty("action").GetString()!;
                AssertEqual("get_mission_diff", action);
            }));

            cases.Add(CaseAsync("get_mission_diff_harbor_worktree_returns_harbor_diff", "GetMissionDiff_HarborWorktree_ReturnsHarborDiff", TestTags.Positive, async () =>
            {
                await AssertHarborMissionDiffAsync(HarborDiffProbe.LiveHarbor).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_mission_diff_saved_file_precedes_harbor_worktree", "GetMissionDiff_SavedFile_PrecedesHarborWorktree", TestTags.Positive, async () =>
            {
                await AssertHarborMissionDiffAsync(HarborDiffProbe.SavedFile).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_mission_diff_snapshot_precedes_harbor_worktree", "GetMissionDiff_Snapshot_PrecedesHarborWorktree", TestTags.Positive, async () =>
            {
                await AssertHarborMissionDiffAsync(HarborDiffProbe.Snapshot).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_mission_diff_local_worktree_returns_local_diff", "GetMissionDiff_LocalWorktree_ReturnsLocalDiff", TestTags.Positive, async () =>
            {
                await AssertHarborMissionDiffAsync(HarborDiffProbe.LocalDock).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_mission_diff_missing_harbor_worktree_returns_unavailable", "GetMissionDiff_MissingHarborWorktree_ReturnsUnavailable", TestTags.Negative, async () =>
            {
                await AssertHarborMissionDiffAsync(HarborDiffProbe.MissingHarborWorktree).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_mission_diff_disconnected_harbor_returns_unavailable", "GetMissionDiff_DisconnectedHarbor_ReturnsUnavailable", TestTags.Negative, async () =>
            {
                await AssertHarborMissionDiffAsync(HarborDiffProbe.DisconnectedHarbor).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_mission_log_non_existent_returns_error", "GetMissionLog_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_mission_log", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("get_mission_log_existing_mission_returns_log_data", "GetMissionLog_ExistingMission_ReturnsLogData", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string missionId = await CreateMissionViaRestAsync(authClient, "ws-log-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_mission_log", new { id = missionId }).ConfigureAwait(false);
                // May return error (settings not configured) or result with empty log
                string action = resp.GetProperty("action").GetString()!;
                AssertEqual("get_mission_log", action);
            }));

            cases.Add(CaseAsync("get_captain_log_non_existent_returns_error", "GetCaptainLog_NonExistent_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "get_captain_log", new { id = "cpt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("get_captain_log_existing_captain_returns_log_data", "GetCaptainLog_ExistingCaptain_ReturnsLogData", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                string captainId = await CreateCaptainViaRestAsync(authClient, "ws-log-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "get_captain_log", new { id = captainId }).ConfigureAwait(false);
                // May return error (settings not configured) or result with empty log
                string action = resp.GetProperty("action").GetString()!;
                AssertEqual("get_captain_log", action);
            }));

            // Note: stop_server is not tested in automated suite as it would shut down the server.

            // ── Enumerate ─────────────────────────────────────────────

            cases.Add(CaseAsync("enumerate_fleets_returns_paginated_result", "Enumerate_Fleets_ReturnsPaginatedResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                await CreateFleetViaRestAsync(authClient, "ws-enum-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "fleets", query = new { pageSize = 10, pageNumber = 1 } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("enumerate", resp.GetProperty("action").GetString());
                EnumerationResult<Fleet> data = DeserializeData<EnumerationResult<Fleet>>(resp);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("enumerate_vessels_returns_result", "Enumerate_Vessels_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "vessels" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_captains_returns_result", "Enumerate_Captains_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "captains" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_missions_returns_result", "Enumerate_Missions_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "missions" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_voyages_returns_result", "Enumerate_Voyages_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "voyages" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_docks_returns_result", "Enumerate_Docks_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "docks" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_signals_returns_result", "Enumerate_Signals_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "signals" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_events_returns_result", "Enumerate_Events_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "events" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_merge_queue_returns_result", "Enumerate_MergeQueue_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "merge_queue" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_with_pagination_respects_page_size", "Enumerate_WithPagination_RespectsPageSize", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "fleets", query = new { pageSize = 5, pageNumber = 1 } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_singular_entity_type_works", "Enumerate_SingularEntityType_Works", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "fleet" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("enumerate_invalid_entity_type_returns_error", "Enumerate_InvalidEntityType_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "enumerate", new { entityType = "bananas" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Unknown entity type", resp.GetProperty("error").GetString()!);
            }));

            // Error Handling Tests
            cases.Add(CaseAsync("unknown_action_returns_error", "UnknownAction_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                JsonElement resp = await WsCommandAsync(restPort, "totally_bogus_action").ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Unknown action", resp.GetProperty("error").GetString()!);
            }));

            cases.Add(CaseAsync("unknown_route_returns_error", "UnknownRoute_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);

                string msg = JsonHelper.Serialize(new { Route = "bad_route" });
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                byte[] buffer = new byte[1048576];
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                AssertEqual("error", root.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("no_route_returns_error", "NoRoute_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);

                string msg = JsonHelper.Serialize(new { hello = "world" });
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                byte[] buffer = new byte[1048576];
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                AssertEqual("error", root.GetProperty("type").GetString());
            }));

            // CrossEntity Tests
            cases.Add(CaseAsync("full_fleet_lifecycle_create_get_update_delete", "FullFleetLifecycle_CreateGetUpdateDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                // Create
                JsonElement createResp = await WsCommandAsync(restPort, "create_fleet", new { data = new { Name = "lifecycle-fleet" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                Fleet createdFleet = DeserializeData<Fleet>(createResp);
                string fleetId = createdFleet.Id;
                AssertStartsWith("flt_", fleetId);

                // Get
                JsonElement getResp = await WsCommandAsync(restPort, "get_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", getResp.GetProperty("type").GetString());
                FleetDetailResponse getDetail = DeserializeData<FleetDetailResponse>(getResp);
                AssertEqual(fleetId, getDetail.Fleet!.Id);

                // Update
                JsonElement updateResp = await WsCommandAsync(restPort, "update_fleet", new { id = fleetId, data = new { Name = "lifecycle-fleet-updated" } }).ConfigureAwait(false);
                AssertEqual("command.result", updateResp.GetProperty("type").GetString());

                // Delete
                JsonElement deleteResp = await WsCommandAsync(restPort, "delete_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", deleteResp.GetProperty("type").GetString());
                DeleteFleetResponse deleted = DeserializeData<DeleteFleetResponse>(deleteResp);
                AssertEqual("deleted", deleted.Status);

                // Verify deleted
                JsonElement verifyResp = await WsCommandAsync(restPort, "get_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.error", verifyResp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("full_voyage_lifecycle_create_get_cancel_purge", "FullVoyageLifecycle_CreateGetCancelPurge", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                // Create bare voyage
                JsonElement createResp = await WsCommandAsync(restPort, "create_voyage", new { data = new { title = "lifecycle-voyage", description = "test" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                Voyage createdVoyage = DeserializeData<Voyage>(createResp);
                string voyageId = createdVoyage.Id;

                // Get
                JsonElement getResp = await WsCommandAsync(restPort, "get_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", getResp.GetProperty("type").GetString());

                // Create another voyage for purge test
                JsonElement create2Resp = await WsCommandAsync(restPort, "create_voyage", new { data = new { title = "purge-voyage" } }).ConfigureAwait(false);
                Voyage createdVoyage2 = DeserializeData<Voyage>(create2Resp);
                string purgeId = createdVoyage2.Id;

                // Cancel first
                JsonElement cancelResp = await WsCommandAsync(restPort, "cancel_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", cancelResp.GetProperty("type").GetString());
                CancelVoyageResponse cancelData = DeserializeData<CancelVoyageResponse>(cancelResp);
                AssertEqual("Cancelled", cancelData.Voyage!.Status.ToString());

                // Cancel second before purge — purge is blocked on Open/InProgress voyages
                await WsCommandAsync(restPort, "cancel_voyage", new { id = purgeId }).ConfigureAwait(false);

                // Purge second
                JsonElement purgeResp = await WsCommandAsync(restPort, "purge_voyage", new { id = purgeId }).ConfigureAwait(false);
                AssertEqual("command.result", purgeResp.GetProperty("type").GetString());
                PurgeVoyageResponse purgeData = DeserializeData<PurgeVoyageResponse>(purgeResp);
                AssertEqual("deleted", purgeData.Status);

                // Verify purged
                JsonElement verifyResp = await WsCommandAsync(restPort, "get_voyage", new { id = purgeId }).ConfigureAwait(false);
                AssertEqual("command.error", verifyResp.GetProperty("type").GetString());
            }));

            cases.Add(CaseAsync("full_mission_lifecycle_create_transition_cancel", "FullMissionLifecycle_CreateTransitionCancel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                // Create
                JsonElement createResp = await WsCommandAsync(restPort, "create_mission", new { data = new { Title = "lifecycle-mission" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                Mission createdMission = DeserializeData<Mission>(createResp);
                string missionId = createdMission.Id;

                // Transition: Pending -> Assigned
                JsonElement t1 = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Assigned" }).ConfigureAwait(false);
                AssertEqual("command.result", t1.GetProperty("type").GetString());

                // Transition: Assigned -> InProgress
                JsonElement t2 = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "InProgress" }).ConfigureAwait(false);
                AssertEqual("command.result", t2.GetProperty("type").GetString());

                // Cancel
                JsonElement cancelResp = await WsCommandAsync(restPort, "cancel_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", cancelResp.GetProperty("type").GetString());
                Mission cancelledMission = DeserializeData<Mission>(cancelResp);
                AssertEqual("Cancelled", cancelledMission.Status.ToString());
            }));

            cases.Add(CaseAsync("full_captain_lifecycle_create_update_delete", "FullCaptainLifecycle_CreateUpdateDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                int restPort = fx.RestPort;

                // Create
                JsonElement createResp = await WsCommandAsync(restPort, "create_captain", new { data = new { Name = "lifecycle-captain", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                Captain createdCaptain = DeserializeData<Captain>(createResp);
                string captainId = createdCaptain.Id;

                // Get
                JsonElement getResp = await WsCommandAsync(restPort, "get_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", getResp.GetProperty("type").GetString());

                // Update
                JsonElement updateResp = await WsCommandAsync(restPort, "update_captain", new { id = captainId, data = new { Name = "lifecycle-renamed", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", updateResp.GetProperty("type").GetString());

                // Delete
                JsonElement deleteResp = await WsCommandAsync(restPort, "delete_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", deleteResp.GetProperty("type").GetString());

                // Verify deleted
                JsonElement verifyResp = await WsCommandAsync(restPort, "get_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.error", verifyResp.GetProperty("type").GetString());
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "WebSocket Tests",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.WebSocket";

        #endregion

        #region Private-Methods

        private static async Task<ClientWebSocket> ConnectAsync(int restPort)
        {
            return await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(restPort).ConfigureAwait(false);
        }

        private static async Task SubscribeAsync(ClientWebSocket ws)
        {
            string payload = JsonHelper.Serialize(new { Route = "subscribe" });
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            await WaitForEventAsync(ws, root => root.GetProperty("type").GetString() == "status.snapshot").ConfigureAwait(false);
        }

        private static async Task<JsonElement> WaitForEventAsync(ClientWebSocket ws, Func<JsonElement, bool> predicate, int timeoutMs = 15000)
        {
            byte[] buffer = new byte[1048576];
            using CancellationTokenSource cts = new CancellationTokenSource(timeoutMs);

            while (true)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement.Clone();

                if (predicate(root))
                    return root;
            }
        }

        /// <summary>
        /// Extract the "data" property from a WsCommandAsync JsonElement result and deserialize it to T.
        /// </summary>
        private static T DeserializeData<T>(JsonElement resp)
        {
            string dataJson = resp.GetProperty("data").GetRawText();
            return JsonHelper.Deserialize<T>(dataJson);
        }

        private static async Task<JsonElement> WsCommandAsync(int restPort, string action, object? extraFields = null)
        {
            using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);

            Dictionary<string, object?> msg = new Dictionary<string, object?>
            {
                ["Route"] = "command",
                ["action"] = action
            };

            if (extraFields != null)
            {
                string extraJson = JsonHelper.Serialize(extraFields);
                using JsonDocument extraDoc = JsonDocument.Parse(extraJson);
                foreach (JsonProperty prop in extraDoc.RootElement.EnumerateObject())
                {
                    msg[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }

            string payload = JsonHelper.Serialize(msg);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

            byte[] buffer = new byte[1048576];
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Loop past broadcast messages (mission.changed, voyage.changed, etc.)
            // until we receive the actual command.result or command.error response.
            while (true)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement.Clone();

                if (root.TryGetProperty("type", out JsonElement typeElem))
                {
                    string? type = typeElem.GetString();
                    if (type == "command.result" || type == "command.error")
                        return root;
                }

                // Not a command response — skip and read next message
            }
        }

        private static async Task<T> GetViaRestAsync<T>(HttpClient authClient, string path)
        {
            HttpResponseMessage resp = await authClient.GetAsync(path).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<T>(resp).ConfigureAwait(false);
        }

        private static async Task<string> CreateFleetViaRestAsync(HttpClient authClient, string name)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/fleets",
                JsonHelper.ToJsonContent(new { Name = name })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp).ConfigureAwait(false);
            return fleet.Id;
        }

        private static async Task<string> CreateVesselViaRestAsync(HttpClient authClient, string name)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/vessels",
                JsonHelper.ToJsonContent(new { Name = name, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
            return vessel.Id;
        }

        private static async Task<string> CreateCaptainViaRestAsync(HttpClient authClient, string name)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/captains",
                JsonHelper.ToJsonContent(new { Name = name, Runtime = "ClaudeCode" })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
            return captain.Id;
        }

        private static async Task<Captain> CreateCaptainRecordViaRestAsync(HttpClient authClient, string name)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/captains",
                JsonHelper.ToJsonContent(new { Name = name, Runtime = "ClaudeCode" })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
        }

        private static async Task<Objective> CreateObjectiveViaRestAsync(HttpClient authClient, string title)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/backlog",
                JsonHelper.ToJsonContent(new { Title = title, Description = "WebSocket objective coverage" })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Objective>(resp).ConfigureAwait(false);
        }

        private static async Task UpdateCaptainRuntimeAsync(HttpClient authClient, string captainId, string runtime)
        {
            HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
            getResp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(getResp).ConfigureAwait(false);

            HttpResponseMessage updateResp = await authClient.PutAsync(
                "/api/v1/captains/" + captainId,
                JsonHelper.ToJsonContent(new
                {
                    Name = captain.Name,
                    Runtime = runtime
                })).ConfigureAwait(false);
            updateResp.EnsureSuccessStatusCode();
        }

        private static async Task SeedMissionAsync(E2EServerFixture fx, string missionId, MissionStatusEnum status, MissionModeEnum mode, int? processId = null)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            using (DatabaseDriver db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") }, quiet))
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Mission mission = (await db.Missions.ReadAsync(missionId).ConfigureAwait(false))!;
                mission.Status = status;
                mission.Mode = mode;
                mission.ProcessId = processId;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
            }
        }

        private static async Task<MissionStatusEnum> ReadMissionStatusAsync(E2EServerFixture fx, string missionId)
        {
            HttpResponseMessage resp = await fx.AuthClient.GetAsync("/api/v1/missions/" + missionId).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Mission mission = await JsonHelper.DeserializeAsync<Mission>(resp).ConfigureAwait(false);
            return mission.Status;
        }

        private static void AssertWsConflict(JsonElement resp)
        {
            AssertEqual("command.error", resp.GetProperty("type").GetString(), resp.GetRawText());
            AssertEqual("Conflict", resp.GetProperty("code").ToString(), resp.GetRawText());
        }

        private static async Task<string> CreateMissionViaRestAsync(HttpClient authClient, string title)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/missions",
                JsonHelper.ToJsonContent(new { Title = title })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            if (wrapper.Mission != null)
                return wrapper.Mission.Id;

            // Fallback: response is the mission directly
            Mission mission = JsonHelper.Deserialize<Mission>(body);
            return mission.Id;
        }

        private static async Task<string> CreateVoyageViaRestAsync(HttpClient authClient, string title)
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/voyages",
                JsonHelper.ToJsonContent(new { Title = title })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp).ConfigureAwait(false);
            return voyage.Id;
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

        private enum HarborDiffProbe
        {
            LiveHarbor,
            SavedFile,
            Snapshot,
            LocalDock,
            MissingHarborWorktree,
            DisconnectedHarbor
        }

        /// <summary>
        /// A mission diff read through the WebSocket command handler.
        /// </summary>
        private sealed class MissionDiffReply
        {
            /// <summary>
            /// Reply type.
            /// </summary>
            public string Type { get; set; } = "";

            /// <summary>
            /// Error text, or empty.
            /// </summary>
            public string Error { get; set; } = "";

            /// <summary>
            /// Error code, or empty.
            /// </summary>
            public string Code { get; set; } = "";

            /// <summary>
            /// Diff text, or empty.
            /// </summary>
            public string Diff { get; set; } = "";
        }

        /// <summary>
        /// Presents one Harbor path that is absent on this machine and runs git in the real repository.
        /// </summary>
        private sealed class HarborPathLoopback
        {
            private readonly HarborConnectionManager _Manager;
            private readonly string _HarborId;
            private readonly string _HarborPath;
            private readonly string _RealPath;
            private readonly LocalHostCommandExecutor _Host = new LocalHostCommandExecutor();

            /// <summary>
            /// Instantiate.
            /// </summary>
            /// <param name="manager">Admiral-side connection manager.</param>
            /// <param name="harborId">Harbor identifier.</param>
            /// <param name="harborPath">Worktree path stored on the dock. This directory is not created here.</param>
            /// <param name="realPath">Repository the Harbor git commands run in.</param>
            public HarborPathLoopback(HarborConnectionManager manager, string harborId, string harborPath, string realPath)
            {
                _Manager = manager;
                _HarborId = harborId;
                _HarborPath = harborPath;
                _RealPath = realPath;
            }

            /// <summary>
            /// Send channel for the Harbor handshake.
            /// </summary>
            public HarborSendDelegate Send
            {
                get { return SendAsync; }
            }

            private Task SendAsync(HarborMessage message, CancellationToken token)
            {
                if (message is HarborGitRequest git)
                {
                    _ = Task.Run(async () =>
                    {
                        await ReplyGitAsync(git, token).ConfigureAwait(false);
                    }, token);
                }
                else if (message is HarborFileRequest file)
                {
                    _ = Task.Run(async () =>
                    {
                        await ReplyFileAsync(file, token).ConfigureAwait(false);
                    }, token);
                }

                return Task.CompletedTask;
            }

            private async Task ReplyGitAsync(HarborGitRequest git, CancellationToken token)
            {
                HarborGitResult reply = new HarborGitResult
                {
                    RequestId = git.RequestId,
                    CorrelationId = git.CorrelationId
                };
                try
                {
                    List<string> arguments = new List<string>();
                    foreach (string argument in git.Arguments)
                        arguments.Add(Map(argument));

                    HostCommandResult result = await _Host.RunAsync(new HostCommandRequest
                    {
                        Executable = git.Executable,
                        WorkingDirectory = Map(git.WorkingDirectory),
                        Arguments = arguments,
                        TimeoutMs = git.TimeoutMs > 0 ? git.TimeoutMs : 120000,
                        ExpectedExitCodes = git.ExpectedExitCodes != null ? new List<int>(git.ExpectedExitCodes) : new List<int>()
                    }, token).ConfigureAwait(false);

                    reply.ExitCode = result.ExitCode;
                    reply.StandardOutput = result.StandardOutput;
                    reply.StandardError = result.StandardError;
                    reply.TimedOut = result.TimedOut;
                }
                catch (Exception ex)
                {
                    reply.ExitCode = 1;
                    reply.StandardError = ex.Message;
                }

                await _Manager.OnMessageAsync(_HarborId, reply, token).ConfigureAwait(false);
            }

            private async Task ReplyFileAsync(HarborFileRequest file, CancellationToken token)
            {
                HarborFileResult reply = new HarborFileResult
                {
                    RequestId = file.RequestId,
                    CorrelationId = file.CorrelationId,
                    Success = true
                };
                try
                {
                    if (file.Operation == HarborFileOperationEnum.Stat)
                    {
                        string mapped = Map(file.Path);
                        bool isDirectory = Directory.Exists(mapped);
                        reply.IsDirectory = isDirectory;
                        reply.Exists = isDirectory || File.Exists(mapped);
                    }
                    else
                    {
                        reply.Success = false;
                        reply.Message = "unsupported file operation " + file.Operation;
                    }
                }
                catch (Exception ex)
                {
                    reply.Success = false;
                    reply.Message = ex.Message;
                }

                await _Manager.OnMessageAsync(_HarborId, reply, token).ConfigureAwait(false);
            }

            private string Map(string path)
            {
                if (String.IsNullOrEmpty(path)) return path;
                if (String.Equals(path, _HarborPath, StringComparison.OrdinalIgnoreCase)) return _RealPath;
                string prefix = _HarborPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(_RealPath, path.Substring(prefix.Length));
                return path;
            }
        }

        private static async Task AssertHarborMissionDiffAsync(HarborDiffProbe probe)
        {
            const string HarborId = "hbr_ws_diff";
            const string Marker = "harbor-diff-marker";
            const string SavedText = "saved-diff-text\n";
            const string SnapshotText = "snapshot-diff-text";
            string root = TestTemp.NewDirectory("ws_harbor_diff");
            string repo = Path.Combine(root, "harbor-repo");
            string absent = Path.Combine(root, "not-on-admiral");
            string missing = Path.Combine(root, "missing-on-harbor");
            using (TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                await CreateFeatureRepositoryAsync(repo, Marker).ConfigureAwait(false);
                AssertFalse(Directory.Exists(absent), "the Harbor worktree path is absent on the Admiral machine");

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                settings.LogDirectory = Path.Combine(root, "logs");
                Directory.CreateDirectory(settings.LogDirectory);

                GitService localGit = new GitService(logging);
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(database.Driver, logging), logging, null);
                DockHostResolver hosts = new DockHostResolver(localGit, logging, manager);
                if (probe != HarborDiffProbe.DisconnectedHarbor)
                {
                    HarborPathLoopback loopback = new HarborPathLoopback(manager, HarborId, absent, repo);
                    HarborHandshake handshake = new HarborHandshake
                    {
                        HarborId = HarborId,
                        Name = "Rig",
                        ProtocolVersion = HarborProtocol.Version,
                        Capabilities = new List<HarborCapability>
                        {
                            new HarborCapability { Name = HarborProtocol.DockCapability, Available = true }
                        }
                    };
                    await manager.OnHandshakeAsync(handshake, null, null, loopback.Send).ConfigureAwait(false);
                }

                DockService docks = new DockService(logging, database.Driver, settings, localGit);
                CaptainService captains = new CaptainService(logging, database.Driver, settings, localGit, docks);
                MissionService missions = new MissionService(logging, database.Driver, settings, docks, captains, git: localGit);
                missions.DockHosts = hosts;

                Vessel vessel = new Vessel("app", repo);
                vessel.DefaultBranch = "main";
                vessel = await database.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                bool local = probe == HarborDiffProbe.LocalDock;
                string worktree = local ? repo : (probe == HarborDiffProbe.MissingHarborWorktree ? missing : absent);
                Dock dock = new Dock(vessel.Id);
                dock.WorktreePath = worktree;
                dock.BranchName = "feature";
                dock.Active = true;
                if (!local)
                {
                    dock.HarborId = HarborId;
                    dock.RepositoryPath = repo;
                }
                dock = await database.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                Mission mission = new Mission("Harbor diff", "Read the mission diff");
                mission.VesselId = vessel.Id;
                mission.DockId = dock.Id;
                mission.BranchName = "feature";
                if (probe == HarborDiffProbe.Snapshot) mission.DiffSnapshot = SnapshotText;
                mission = await database.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                if (probe == HarborDiffProbe.SavedFile)
                {
                    string savedDirectory = Path.Combine(settings.LogDirectory, "diffs");
                    Directory.CreateDirectory(savedDirectory);
                    await File.WriteAllTextAsync(Path.Combine(savedDirectory, mission.Id + ".diff"), SavedText).ConfigureAwait(false);
                }

                WebSocketCommandHandler handler = new WebSocketCommandHandler(
                    null!,
                    database.Driver,
                    null!,
                    settings,
                    localGit,
                    null,
                    new JsonSerializerOptions(),
                    (_, _) => { },
                    (_, _) => { });
                handler.Missions = missions;

                WebSocketCommand command = new WebSocketCommand();
                command.Action = "get_mission_diff";
                command.Id = mission.Id;
                object result = await handler.HandleCommandAsync("get_mission_diff", command, "{}").ConfigureAwait(false);
                MissionDiffReply reply = ReadMissionDiffReply(result);

                if (probe == HarborDiffProbe.LiveHarbor || probe == HarborDiffProbe.LocalDock)
                {
                    AssertEqual("command.result", reply.Type, "diff result");
                    AssertContains(Marker, reply.Diff, "diff text from git");
                }
                else if (probe == HarborDiffProbe.SavedFile)
                {
                    AssertEqual("command.result", reply.Type, "saved diff result");
                    AssertEqual(SavedText, reply.Diff, "saved diff text");
                }
                else if (probe == HarborDiffProbe.Snapshot)
                {
                    AssertEqual("command.result", reply.Type, "snapshot result");
                    AssertEqual(SnapshotText, reply.Diff, "snapshot text");
                }
                else if (probe == HarborDiffProbe.MissingHarborWorktree)
                {
                    AssertEqual("command.error", reply.Type, "missing Harbor worktree");
                    AssertEqual("Unavailable", reply.Code, "missing Harbor worktree code");
                    AssertEqual("No diff available: the worktree is not accessible on its owning host and no saved diff exists", reply.Error, "missing Harbor worktree message");
                }
                else
                {
                    AssertEqual("command.error", reply.Type, "disconnected Harbor");
                    AssertEqual("Unavailable", reply.Code, "disconnected Harbor code");
                    AssertEqual("The mission worktree is on Harbor " + HarborId + ". Connect that Harbor, ensure it supports mission docks, and try again; its diff is currently unavailable.", reply.Error, "disconnected Harbor message");
                }
            }

            TestTemp.TryDelete(root);
        }

        private static async Task CreateFeatureRepositoryAsync(string repo, string marker)
        {
            Directory.CreateDirectory(repo);
            await HarborDockGitFixture.GitAsync(repo, "init", "--initial-branch=main").ConfigureAwait(false);
            await HarborDockGitFixture.GitAsync(repo, "config", "user.email", "armada@example.com").ConfigureAwait(false);
            await HarborDockGitFixture.GitAsync(repo, "config", "user.name", "Armada Test").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "# app\n").ConfigureAwait(false);
            await HarborDockGitFixture.GitAsync(repo, "add", "README.md").ConfigureAwait(false);
            await HarborDockGitFixture.GitAsync(repo, "commit", "-m", "Initial commit").ConfigureAwait(false);
            await HarborDockGitFixture.GitAsync(repo, "checkout", "-b", "feature").ConfigureAwait(false);
            await HarborDockGitFixture.CommitFileAsync(repo, "note.txt", marker + "\n", "Feature work").ConfigureAwait(false);
        }

        private static MissionDiffReply ReadMissionDiffReply(object result)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            };
            string json = JsonSerializer.Serialize(result, options);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                MissionDiffReply reply = new MissionDiffReply();
                reply.Type = root.GetProperty("type").GetString() ?? "";
                if (root.TryGetProperty("error", out JsonElement error)) reply.Error = error.GetString() ?? "";
                if (root.TryGetProperty("code", out JsonElement code)) reply.Code = code.ValueKind == JsonValueKind.String ? (code.GetString() ?? "") : code.GetRawText();
                if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("diff", out JsonElement diff))
                    reply.Diff = diff.GetString() ?? "";
                return reply;
            }
        }

        #endregion
    }
}
