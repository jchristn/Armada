namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server.WebSocket;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using WatsonWebserver.Core.WebSockets;
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

            cases.Add(CaseAsync("slow_client_does_not_block_tenant_broadcast", "SlowClient_DoesNotBlockTenantBroadcast", TestTags.Positive, () =>
                AssertSlowClientDoesNotBlockAsync(
                    "tenant-a",
                    "user-a",
                    "tenant-b",
                    "user-b",
                    "wave-",
                    (ArmadaWebSocketHub hub, string marker) => hub.BroadcastToTenant("tenant-a", "mission.changed", new { marker }),
                    true)));

            cases.Add(CaseAsync("slow_client_does_not_block_cli_permission_broadcast", "SlowClient_DoesNotBlockCliPermissionBroadcast", TestTags.Positive, () =>
                AssertSlowClientDoesNotBlockAsync(
                    "tenant-a",
                    "user-owner",
                    "tenant-a",
                    "user-other",
                    "perm-",
                    (ArmadaWebSocketHub hub, string marker) =>
                    {
                        CliPermissionRequest request = new CliPermissionRequest();
                        request.Id = marker;
                        request.TenantId = "tenant-a";
                        request.UserId = "user-owner";
                        request.ToolName = "Bash";
                        hub.BroadcastCliPermission("cli_permission.requested", request);
                    },
                    false)));

            cases.Add(CaseAsync("slow_client_does_not_block_command_reply", "SlowClient_DoesNotBlockCommandReply", TestTags.Positive, () =>
                AssertSlowCommandSendDoesNotBlockAsync("{\"route\":\"command\",\"action\":\"list_fleets\"}", "Forbidden", false)));

            cases.Add(CaseAsync("slow_client_does_not_block_unknown_route_error", "SlowClient_DoesNotBlockUnknownRouteError", TestTags.Positive, () =>
                AssertSlowCommandSendDoesNotBlockAsync("{\"route\":\"other\"}", "Unknown route", false)));

            cases.Add(CaseAsync("slow_client_does_not_block_handler_error", "SlowClient_DoesNotBlockHandlerError", TestTags.Positive, () =>
                AssertSlowCommandSendDoesNotBlockAsync("{\"route\":\"subscribe\"}", "status unavailable", true)));

            cases.Add(CaseAsync("outbound_queue_keeps_order_and_rejects_overflow_without_waiting", "OutboundQueue_KeepsOrderAndRejectsOverflowWithoutWaiting", TestTags.Positive, AssertOutputQueueIsolatesAHeldSendAsync));

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

        private static async Task AssertSlowClientDoesNotBlockAsync(
            string recipientTenantId,
            string recipientUserId,
            string otherTenantId,
            string otherUserId,
            string markerPrefix,
            Action<ArmadaWebSocketHub, string> broadcast,
            bool floodUntilDisconnected)
        {
            using (DeliveryHarness harness = DeliveryHarness.Create(false))
            {
                ScriptedSocket slow = harness.AddClient(recipientTenantId, recipientUserId, true);
                ScriptedSocket fast = harness.AddClient(recipientTenantId, recipientUserId, false);
                ScriptedSocket other = harness.AddClient(otherTenantId, otherUserId, false);
                try
                {
                    await harness.BroadcastWhileHeldAsync(slow, fast, other, markerPrefix + "000", marker => broadcast(harness.Hub, marker)).ConfigureAwait(false);
                    if (!floodUntilDisconnected) return;

                    bool disconnected = false;
                    for (int i = 1; i <= 128 && !disconnected; i++)
                    {
                        string marker = markerPrefix + i.ToString("D3");
                        await harness.BroadcastWhileHeldAsync(slow, fast, other, marker, markerValue => broadcast(harness.Hub, markerValue)).ConfigureAwait(false);
                        disconnected = slow.DisposeCount > 0;
                    }

                    Assert(slow.DisposeCount > 0, "A full outbound backlog must abort only the slow client while its send is still held.");
                    await harness.BroadcastWhileHeldAsync(slow, fast, other, markerPrefix + "999", marker => broadcast(harness.Hub, marker)).ConfigureAwait(false);
                    Assert(slow.ReleaseCompleted == false, "The held send must still be outstanding when the slow client is aborted.");
                    AssertEqual(1, slow.SendCount, "Only the first frame may enter the held send.");
                    AssertEqual(2, harness.Hub.ConnectionCount, "The fast client and the filtered client stay connected.");
                    Assert(other.Contains(markerPrefix) == false, "A client outside the recipient filter must not receive the event.");
                }
                finally
                {
                    slow.Release();
                    await harness.DrainAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task AssertSlowCommandSendDoesNotBlockAsync(string body, string replyMarker, bool failStatus)
        {
            using (DeliveryHarness harness = DeliveryHarness.Create(failStatus))
            {
                ScriptedSocket slow = harness.AddClient("tenant-a", "user-a", true);
                ScriptedSocket fast = harness.AddClient("tenant-a", "user-a", false);
                ScriptedSocket other = harness.AddClient("tenant-b", "user-b", false);
                Task? handler = null;
                try
                {
                    handler = Task.Run(() => harness.HandleAsync(slow, body));
                    await slow.WaitUntilSendStartedAsync().ConfigureAwait(false);
                    await harness.BroadcastWhileHeldAsync(
                        slow,
                        fast,
                        other,
                        "delivery-marker",
                        marker => harness.Hub.BroadcastToTenant("tenant-a", "mission.changed", new { marker })).ConfigureAwait(false);
                    await harness.WaitForCompletionAsync(handler, "The command handler must return while its send is still held.").ConfigureAwait(false);
                    await slow.WaitForTextAsync(replyMarker).ConfigureAwait(false);
                }
                finally
                {
                    slow.Release();
                    if (handler != null)
                    {
                        try { await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                        catch (Exception ex) { _ = ex.Message; }
                    }

                    await harness.DrainAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task AssertOutputQueueIsolatesAHeldSendAsync()
        {
            TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> fastSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<string> heldFrames = new List<string>();
            int disconnects = 0;

            using (WebSocketClientOutputQueue held = new WebSocketClientOutputQueue(
                async frame =>
                {
                    lock (heldFrames)
                    {
                        heldFrames.Add(frame);
                        if (heldFrames.Count >= 2) drained.TrySetResult(true);
                    }

                    started.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                },
                error =>
                {
                    Interlocked.Increment(ref disconnects);
                    disconnected.TrySetResult(true);
                },
                1))
            using (WebSocketClientOutputQueue fast = new WebSocketClientOutputQueue(
                frame =>
                {
                    fastSent.TrySetResult(frame == "event");
                    return Task.CompletedTask;
                },
                error => throw new InvalidOperationException("The other queue must stay open."),
                1))
            {
                try
                {
                    Assert(held.TryEnqueue("first"), "The first frame must be accepted.");
                    using (CancellationTokenSource startedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        await started.Task.WaitAsync(startedTimeout.Token).ConfigureAwait(false);
                    }

                    Assert(held.TryEnqueue("second"), "One waiting frame must fit behind the held send.");
                    Assert(held.TryEnqueue("third") == false, "A full queue must reject the next frame without waiting.");
                    using (CancellationTokenSource disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    {
                        await disconnected.Task.WaitAsync(disconnectTimeout.Token).ConfigureAwait(false);
                    }

                    AssertEqual(1, Volatile.Read(ref disconnects));
                    Assert(release.Task.IsCompleted == false, "Queue overflow must be reported while the send is still held.");
                    Assert(fast.TryEnqueue("event"), "The other client must accept a frame while the first send is held.");
                    using (CancellationTokenSource fastTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    {
                        bool sent = await fastSent.Task.WaitAsync(fastTimeout.Token).ConfigureAwait(false);
                        Assert(sent, "The other client must receive its frame while the first send is held.");
                    }

                    lock (heldFrames) AssertEqual("first", heldFrames[0]);
                }
                finally
                {
                    release.TrySetResult(true);
                }

                using (CancellationTokenSource orderTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    await drained.Task.WaitAsync(orderTimeout.Token).ConfigureAwait(false);
                }

                lock (heldFrames)
                {
                    AssertEqual(2, heldFrames.Count);
                    AssertEqual("first", heldFrames[0]);
                    AssertEqual("second", heldFrames[1]);
                }
            }
        }

        #endregion

        #region Private-Types

        private sealed class DeliveryHarness : IDisposable
        {
            private readonly ConcurrentDictionary<Guid, WebSocketClientState> _Clients;
            private readonly List<ScriptedSocket> _Sockets = new List<ScriptedSocket>();
            private readonly List<Task> _Operations = new List<Task>();

            private DeliveryHarness(ArmadaWebSocketHub hub, ConcurrentDictionary<Guid, WebSocketClientState> clients)
            {
                Hub = hub;
                _Clients = clients;
            }

            public ArmadaWebSocketHub Hub { get; }

            public static DeliveryHarness Create(bool failStatus)
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                IdleDatabaseDriver database = new IdleDatabaseDriver();
                ArmadaWebSocketHub hub = new ArmadaWebSocketHub(
                    logging,
                    SlowClientAdmiralProxy.Create(failStatus),
                    database,
                    new RecordingMergeQueueService());
                FieldInfo? clientsField = typeof(ArmadaWebSocketHub).GetField("_Clients", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(clientsField != null, "The hub client map must be present.");
                ConcurrentDictionary<Guid, WebSocketClientState> clients = (ConcurrentDictionary<Guid, WebSocketClientState>)clientsField.GetValue(hub)!;
                return new DeliveryHarness(hub, clients);
            }

            public ScriptedSocket AddClient(string tenantId, string userId, bool holdSend)
            {
                ScriptedSocket socket = new ScriptedSocket(holdSend);
                WebSocketRequestDescriptor descriptor = new WebSocketRequestDescriptor(
                    "/ws",
                    new NameValueCollection(),
                    new Dictionary<string, string>(),
                    "13",
                    new List<string>(),
                    "127.0.0.1",
                    9);
                WebSocketSession session = new WebSocketSession(socket, descriptor, Guid.NewGuid(), 8192, 1048576, 1000);
                socket.Session = session;
                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "Bearer");
                _Clients[session.Id] = new WebSocketClientState(session, auth);
                _Sockets.Add(socket);
                return socket;
            }

            public Task HandleAsync(ScriptedSocket socket, string body)
            {
                WebSocketClientState? client = null;
                foreach (WebSocketClientState candidate in _Clients.Values)
                {
                    if (ReferenceEquals(candidate.Session, socket.Session))
                    {
                        client = candidate;
                        break;
                    }
                }

                Assert(client != null, "The command client must be registered.");
                MethodInfo? method = typeof(ArmadaWebSocketHub).GetMethod(
                    "HandleMessageAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(WebSocketClientState), typeof(string) },
                    null);
                Assert(method != null, "HandleMessageAsync must be present.");
                object? result = method.Invoke(Hub, new object[] { client, body });
                Task handler = (Task)result!;
                _Operations.Add(handler);
                return handler;
            }

            public async Task BroadcastWhileHeldAsync(ScriptedSocket slow, ScriptedSocket fast, ScriptedSocket other, string marker, Action<string> broadcast)
            {
                Task operation = Task.Run(() => broadcast(marker));
                _Operations.Add(operation);
                await slow.WaitUntilSendStartedAsync().ConfigureAwait(false);
                await WaitForCompletionAsync(operation, "The broadcast must return while a client send is still held.").ConfigureAwait(false);
                await fast.WaitForTextAsync(marker).ConfigureAwait(false);
                Assert(other.Contains(marker) == false, "A filtered client must not receive the frame.");
                Assert(slow.ReleaseCompleted == false, "The assertion must run before the held send is released.");
            }

            public async Task WaitForCompletionAsync(Task operation, string message)
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                {
                    try
                    {
                        await operation.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Assert(false, message);
                    }
                }
            }

            public async Task DrainAsync()
            {
                foreach (ScriptedSocket socket in _Sockets) socket.Release();
                foreach (Task operation in _Operations)
                {
                    try { await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                    catch (Exception ex) { _ = ex.Message; }
                }

                FieldInfo? queuesField = typeof(ArmadaWebSocketHub).GetField("_OutputQueues", BindingFlags.Instance | BindingFlags.NonPublic);
                if (queuesField != null)
                {
                    object? value = queuesField.GetValue(Hub);
                    if (value is System.Collections.IEnumerable queues)
                    {
                        foreach (object entry in queues)
                        {
                            PropertyInfo? valueProperty = entry.GetType().GetProperty("Value");
                            if (valueProperty == null) continue;
                            IDisposable? queue = valueProperty.GetValue(entry) as IDisposable;
                            if (queue != null) queue.Dispose();
                        }
                    }
                }
            }

            public void Dispose()
            {
                foreach (ScriptedSocket socket in _Sockets) socket.Shutdown();
            }
        }

        private sealed class ScriptedSocket : WebSocket
        {
            private readonly bool _Hold;
            private readonly object _Gate = new object();
            private readonly List<string> _Frames = new List<string>();
            private readonly List<FrameWait> _Waiters = new List<FrameWait>();
            private readonly TaskCompletionSource<bool> _SendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly SemaphoreSlim _Transport = new SemaphoreSlim(1, 1);
            private volatile WebSocketState _State = WebSocketState.Open;
            private int _SendCount;
            private int _DisposeCount;

            public ScriptedSocket(bool holdSend)
            {
                _Hold = holdSend;
            }

            public WebSocketSession? Session { get; set; }

            public int SendCount => Volatile.Read(ref _SendCount);

            public int DisposeCount => Volatile.Read(ref _DisposeCount);

            public bool ReleaseCompleted => _Release.Task.IsCompleted;

            public async Task WaitUntilSendStartedAsync()
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await _SendStarted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }

            public Task WaitForTextAsync(string marker)
            {
                TaskCompletionSource<bool> waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_Gate)
                {
                    if (HasMarker(marker)) return Task.CompletedTask;
                    _Waiters.Add(new FrameWait(marker, waiter));
                }

                return WaitForMarkerAsync(waiter);
            }

            public bool Contains(string marker)
            {
                lock (_Gate) return HasMarker(marker);
            }

            public void Release()
            {
                _Release.TrySetResult(true);
            }

            public void Shutdown()
            {
                Release();
                _Transport.Dispose();
            }

            public override void Abort()
            {
                _State = WebSocketState.Aborted;
            }

            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                return CloseAfterTransportAsync(cancellationToken);
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public override void Dispose()
            {
                Interlocked.Increment(ref _DisposeCount);
                _State = WebSocketState.Aborted;
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                string text = Encoding.UTF8.GetString(buffer.Array ?? Array.Empty<byte>(), buffer.Array == null ? 0 : buffer.Offset, buffer.Array == null ? 0 : buffer.Count);
                AddFrame(text);
                if (!_Hold)
                {
                    Interlocked.Increment(ref _SendCount);
                    return;
                }

                await _Transport.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Interlocked.Increment(ref _SendCount);
                    _SendStarted.TrySetResult(true);
                    await _Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _Transport.Release();
                }
            }

            public override WebSocketCloseStatus? CloseStatus => null;

            public override string? CloseStatusDescription => null;

            public override WebSocketState State => _State;

            public override string? SubProtocol => null;

            private async Task WaitForMarkerAsync(TaskCompletionSource<bool> waiter)
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    await waiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }

            private async Task CloseAfterTransportAsync(CancellationToken cancellationToken)
            {
                await _Transport.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    _State = WebSocketState.Closed;
                }
                finally
                {
                    _Transport.Release();
                }
            }

            private void AddFrame(string text)
            {
                List<FrameWait> ready = new List<FrameWait>();
                lock (_Gate)
                {
                    _Frames.Add(text);
                    for (int i = 0; i < _Waiters.Count; i++)
                    {
                        if (text.Contains(_Waiters[i].Marker, StringComparison.Ordinal)) ready.Add(_Waiters[i]);
                    }

                    for (int i = 0; i < ready.Count; i++) _Waiters.Remove(ready[i]);
                }

                for (int i = 0; i < ready.Count; i++) ready[i].Done.TrySetResult(true);
            }

            private bool HasMarker(string marker)
            {
                for (int i = 0; i < _Frames.Count; i++)
                {
                    if (_Frames[i].Contains(marker, StringComparison.Ordinal)) return true;
                }

                return false;
            }
        }

        private sealed class FrameWait
        {
            public FrameWait(string marker, TaskCompletionSource<bool> done)
            {
                Marker = marker;
                Done = done;
            }

            public string Marker { get; }

            public TaskCompletionSource<bool> Done { get; }
        }

        private sealed class IdleDatabaseDriver : DatabaseDriver
        {
            public override Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;

            public override Task<int> GetSchemaVersionAsync(CancellationToken token = default) => Task.FromResult(0);

            public override int GetLatestSchemaVersion() => 0;

            public override void Dispose()
            {
            }

            internal override IReadOnlyList<SchemaMigration> GetMigrationsForVerification() => new List<SchemaMigration>();

            internal override Task ReplayMigrationsAsync(CancellationToken token = default) => Task.CompletedTask;
        }

        /// <summary>
        /// Admiral stand-in for the slow-client WebSocket cases. Status reads can fail on request.
        /// Every other call returns a completed task.
        /// </summary>
        public class SlowClientAdmiralProxy : DispatchProxy
        {
            /// <summary>
            /// When true, GetStatusAsync throws.
            /// </summary>
            public bool FailStatus { get; set; }

            /// <summary>
            /// Create the proxy.
            /// </summary>
            /// <param name="failStatus">Whether status reads throw.</param>
            /// <returns>The admiral proxy.</returns>
            public static IAdmiralService Create(bool failStatus)
            {
                IAdmiralService proxy = DispatchProxy.Create<IAdmiralService, SlowClientAdmiralProxy>();
                ((SlowClientAdmiralProxy)(object)proxy).FailStatus = failStatus;
                return proxy;
            }

            /// <inheritdoc />
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));
                if (FailStatus && targetMethod.Name == "GetStatusAsync")
                    throw new InvalidOperationException("status unavailable");

                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(Task)) return Task.CompletedTask;
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    Type resultType = returnType.GetGenericArguments()[0];
                    object? result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                    MethodInfo fromResult = typeof(Task).GetMethod(nameof(Task.FromResult), BindingFlags.Public | BindingFlags.Static)!;
                    return fromResult.MakeGenericMethod(resultType).Invoke(null, new object?[] { result });
                }

                if (returnType == typeof(void)) return null;
                if (returnType.IsValueType) return Activator.CreateInstance(returnType);
                return null;
            }
        }

        #endregion
    }
}
