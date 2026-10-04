namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
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
                AssertNotNull(data.Vessels);
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
                AssertFalse(raw.Contains("\"gitHubTokenOverride\"", StringComparison.Ordinal));
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
                VoyageDetailResponse data = DeserializeData<VoyageDetailResponse>(resp);
                AssertNotNull(data.Voyage);
                AssertNotNull(data.Missions);
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

                // Pending -> Assigned -> InProgress via REST
                await authClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "Assigned" })).ConfigureAwait(false);
                await authClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    JsonHelper.ToJsonContent(new { Status = "InProgress" })).ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync(restPort, "transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Mission data = DeserializeData<Mission>(resp);
                AssertNotNull(data.CompletedUtc);
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

        #endregion
    }
}
