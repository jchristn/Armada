namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage for planning session WebSocket broadcasts (changed, message lifecycle,
    /// summary, dispatch, delete). Ported from the retired automated <c>PlanningWebSocketTests</c> suite;
    /// each case opens a raw WebSocket to the shared server obtained through <see cref="E2EServerFixture"/>
    /// and drives the REST API to trigger broadcasts, cleaning up its own created entities.
    /// </summary>
    public sealed class PlanningWebSocketSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end Planning WebSocket suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_planning_session_broadcasts_changed_payload", "CreatePlanningSession_BroadcastsChangedPayload", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                List<string> createdCaptainIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdFleetIds = new List<string>();
                List<string> createdPlanningSessionIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                try
                {
                    using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);
                    await SubscribeAsync(ws).ConfigureAwait(false);

                    string fleetId = await CreateFleetAsync(authClient, createdFleetIds).ConfigureAwait(false);
                    string vesselId = await CreateVesselAsync(authClient, fleetId, createdVesselIds).ConfigureAwait(false);
                    Captain captain = await CreateCaptainAsync(authClient, "planning-ws-create", createdCaptainIds).ConfigureAwait(false);

                    PlanningSessionDetailResponse created = await CreatePlanningSessionAsync(authClient, captain.Id, vesselId, "WebSocket planning session").ConfigureAwait(false);
                    createdPlanningSessionIds.Add(created.Session!.Id);

                    JsonElement evt = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.changed"
                            && root.GetProperty("data").GetProperty("session").GetProperty("id").GetString() == created.Session.Id;
                    }).ConfigureAwait(false);

                    AssertEqual("planning-session.changed", evt.GetProperty("type").GetString());
                    AssertTrue(evt.TryGetProperty("timestamp", out _), "Planning session changed payload should contain timestamp");
                    JsonElement session = evt.GetProperty("data").GetProperty("session");
                    AssertEqual(created.Session.Id, session.GetProperty("id").GetString());
                    AssertEqual("WebSocket planning session", session.GetProperty("title").GetString());
                }
                finally
                {
                    await CleanupAsync(authClient, createdPlanningSessionIds, createdVoyageIds, createdCaptainIds, createdVesselIds, createdFleetIds).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("planning_session_lifecycle_broadcasts_transcript_summary_dispatch_and_delete_events", "PlanningSessionLifecycle_BroadcastsTranscriptSummaryDispatchAndDeleteEvents", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                int restPort = fx.RestPort;

                List<string> createdCaptainIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdFleetIds = new List<string>();
                List<string> createdPlanningSessionIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                try
                {
                    using ClientWebSocket ws = await ConnectAsync(restPort).ConfigureAwait(false);
                    await SubscribeAsync(ws).ConfigureAwait(false);

                    string fleetId = await CreateFleetAsync(authClient, createdFleetIds).ConfigureAwait(false);
                    string vesselId = await CreateVesselAsync(authClient, fleetId, createdVesselIds).ConfigureAwait(false);
                    Captain captain = await CreateCaptainAsync(authClient, "planning-ws-lifecycle", createdCaptainIds).ConfigureAwait(false);

                    PlanningSessionDetailResponse created = await CreatePlanningSessionAsync(authClient, captain.Id, vesselId, "Planning WebSocket lifecycle").ConfigureAwait(false);
                    createdPlanningSessionIds.Add(created.Session!.Id);

                    await UpdateCaptainRuntimeAsync(authClient, captain.Id, "Custom").ConfigureAwait(false);

                    await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/messages",
                        JsonHelper.ToJsonContent(new
                        {
                            Content = "Lay out a migration plan and convert it into a dispatch draft."
                        })).ConfigureAwait(false);

                    JsonElement createdUser = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.message.created"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == created.Session.Id
                            && root.GetProperty("data").GetProperty("message").GetProperty("role").GetString() == "User";
                    }).ConfigureAwait(false);
                    AssertEqual(created.Session.Id, createdUser.GetProperty("data").GetProperty("sessionId").GetString());

                    JsonElement createdAssistant = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.message.created"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == created.Session.Id
                            && root.GetProperty("data").GetProperty("message").GetProperty("role").GetString() == "Assistant";
                    }).ConfigureAwait(false);
                    string assistantMessageId = createdAssistant.GetProperty("data").GetProperty("message").GetProperty("id").GetString()
                        ?? throw new Exception("Assistant planning message id not found in WebSocket payload");

                    JsonElement updatedAssistant = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.message.updated"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == created.Session.Id
                            && root.GetProperty("data").GetProperty("message").GetProperty("id").GetString() == assistantMessageId
                            && !String.IsNullOrWhiteSpace(root.GetProperty("data").GetProperty("message").GetProperty("content").GetString());
                    }).ConfigureAwait(false);
                    AssertTrue(updatedAssistant.TryGetProperty("timestamp", out _), "Planning session message update should contain timestamp");

                    HttpResponseMessage summarizeResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/summarize",
                        JsonHelper.ToJsonContent(new
                        {
                            MessageId = assistantMessageId,
                            Title = "WebSocket summary draft"
                        })).ConfigureAwait(false);
                    summarizeResp.EnsureSuccessStatusCode();

                    JsonElement summaryEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.summary.created"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == created.Session.Id;
                    }).ConfigureAwait(false);
                    JsonElement draft = summaryEvent.GetProperty("data").GetProperty("draft");
                    AssertEqual("assistant-fallback", draft.GetProperty("method").GetString());
                    AssertTrue(!String.IsNullOrWhiteSpace(draft.GetProperty("title").GetString()), "Summary event should contain a title");
                    AssertTrue(!String.IsNullOrWhiteSpace(draft.GetProperty("description").GetString()), "Summary event should contain a description");

                    HttpResponseMessage dispatchResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/dispatch",
                        JsonHelper.ToJsonContent(new
                        {
                            MessageId = assistantMessageId,
                            Description = draft.GetProperty("description").GetString()
                        })).ConfigureAwait(false);
                    dispatchResp.EnsureSuccessStatusCode();
                    Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(dispatchResp).ConfigureAwait(false);
                    createdVoyageIds.Add(voyage.Id);

                    JsonElement dispatchEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.dispatch.created"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == created.Session.Id;
                    }).ConfigureAwait(false);
                    AssertEqual(voyage.Id, dispatchEvent.GetProperty("data").GetProperty("voyageId").GetString());
                    AssertEqual(assistantMessageId, dispatchEvent.GetProperty("data").GetProperty("messageId").GetString());

                    HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/planning-sessions/" + created.Session.Id).ConfigureAwait(false);
                    AssertEqual(System.Net.HttpStatusCode.NoContent, deleteResp.StatusCode);
                    createdPlanningSessionIds.Remove(created.Session.Id);

                    JsonElement deletedEvent = await WaitForEventAsync(ws, root =>
                    {
                        return root.GetProperty("type").GetString() == "planning-session.deleted"
                            && root.GetProperty("data").GetProperty("sessionId").GetString() == created.Session.Id;
                    }).ConfigureAwait(false);
                    AssertEqual(created.Session.Id, deletedEvent.GetProperty("data").GetProperty("sessionId").GetString());
                }
                finally
                {
                    await CleanupAsync(authClient, createdPlanningSessionIds, createdVoyageIds, createdCaptainIds, createdVesselIds, createdFleetIds).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Planning WebSocket Tests",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.PlanningWebSocket";

        #endregion

        #region Private-Methods

        private static async Task<ClientWebSocket> ConnectAsync(int restPort)
        {
            return await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(restPort).ConfigureAwait(false);
        }

        private static async Task SubscribeAsync(ClientWebSocket ws)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonHelper.Serialize(new { Route = "subscribe" }));
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

        private static async Task<PlanningSessionDetailResponse> CreatePlanningSessionAsync(HttpClient authClient, string captainId, string vesselId, string title)
        {
            HttpResponseMessage createResp = await authClient.PostAsync(
                "/api/v1/planning-sessions",
                JsonHelper.ToJsonContent(new
                {
                    Title = title,
                    CaptainId = captainId,
                    VesselId = vesselId
                })).ConfigureAwait(false);
            createResp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<PlanningSessionDetailResponse>(createResp).ConfigureAwait(false);
        }

        private static async Task<string> CreateFleetAsync(HttpClient authClient, List<string> createdFleetIds)
        {
            HttpResponseMessage resp = await authClient.PostAsync(
                "/api/v1/fleets",
                JsonHelper.ToJsonContent(new
                {
                    Name = "PlanningWsFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp).ConfigureAwait(false);
            createdFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        private static async Task<string> CreateVesselAsync(HttpClient authClient, string fleetId, List<string> createdVesselIds)
        {
            HttpResponseMessage resp = await authClient.PostAsync(
                "/api/v1/vessels",
                JsonHelper.ToJsonContent(new
                {
                    Name = "PlanningWsVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    FleetId = fleetId
                })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
            createdVesselIds.Add(vessel.Id);
            return vessel.Id;
        }

        private static async Task<Captain> CreateCaptainAsync(HttpClient authClient, string prefix, List<string> createdCaptainIds, string runtime = "ClaudeCode")
        {
            HttpResponseMessage resp = await authClient.PostAsync(
                "/api/v1/captains",
                JsonHelper.ToJsonContent(new
                {
                    Name = prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Runtime = runtime
                })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
            createdCaptainIds.Add(captain.Id);
            return captain;
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

        private static async Task CleanupAsync(
            HttpClient authClient,
            List<string> createdPlanningSessionIds,
            List<string> createdVoyageIds,
            List<string> createdCaptainIds,
            List<string> createdVesselIds,
            List<string> createdFleetIds)
        {
            foreach (string planningSessionId in createdPlanningSessionIds.ToArray())
            {
                try
                {
                    await authClient.PostAsync("/api/v1/planning-sessions/" + planningSessionId + "/stop", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await authClient.DeleteAsync("/api/v1/planning-sessions/" + planningSessionId).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            foreach (string voyageId in createdVoyageIds)
            {
                try
                {
                    await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge").ConfigureAwait(false);
                }
                catch
                {
                }
            }

            foreach (string captainId in createdCaptainIds)
            {
                try
                {
                    await authClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            foreach (string vesselId in createdVesselIds)
            {
                try
                {
                    await authClient.DeleteAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            foreach (string fleetId in createdFleetIds)
            {
                try
                {
                    await authClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                }
                catch
                {
                }
            }
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

        #region Private-Types

        private sealed class PlanningSessionDetailResponse
        {
            public PlanningSession? Session { get; set; }
            public List<PlanningSessionMessage> Messages { get; set; } = new List<PlanningSessionMessage>();
            public Captain? Captain { get; set; }
            public Vessel? Vessel { get; set; }
        }

        #endregion
    }
}
