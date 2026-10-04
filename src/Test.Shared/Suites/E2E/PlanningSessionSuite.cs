namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end REST coverage for planning session create, get, list, and the full send/summarize/
    /// dispatch/stop/delete lifecycle. Ported from the retired automated <c>PlanningSessionTests</c>
    /// suite; every case drives the real REST API exposed by the shared in-process server obtained
    /// through <see cref="E2EServerFixture"/> and cleans up its own created entities.
    /// </summary>
    public sealed class PlanningSessionSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end Planning Session API suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_get_and_list_planning_session_returns_detail", "CreateGetAndListPlanningSession_ReturnsDetail", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                List<string> createdCaptainIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdFleetIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();
                List<string> createdPlanningSessionIds = new List<string>();

                try
                {
                    string fleetId = await CreateFleetAsync(authClient, createdFleetIds).ConfigureAwait(false);
                    string vesselId = await CreateVesselAsync(authClient, fleetId, createdVesselIds).ConfigureAwait(false);
                    Captain captain = await CreateCaptainAsync(authClient, "planning-api-create", createdCaptainIds).ConfigureAwait(false);

                    HttpResponseMessage createResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions",
                        JsonHelper.ToJsonContent(new
                        {
                            Title = "API planning session",
                            CaptainId = captain.Id,
                            VesselId = vesselId
                        })).ConfigureAwait(false);

                    AssertEqual(HttpStatusCode.Created, createResp.StatusCode);
                    PlanningSessionDetailResponse created = await JsonHelper.DeserializeAsync<PlanningSessionDetailResponse>(createResp).ConfigureAwait(false);
                    AssertNotNull(created.Session);
                    AssertEqual("API planning session", created.Session!.Title);
                    AssertEqual(captain.Id, created.Session.CaptainId);
                    AssertEqual(vesselId, created.Session.VesselId);
                    AssertNotNull(created.Captain);
                    AssertNotNull(created.Vessel);
                    AssertTrue(created.Messages.Count == 0);
                    createdPlanningSessionIds.Add(created.Session.Id);

                    HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/planning-sessions/" + created.Session.Id).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, getResp.StatusCode);
                    PlanningSessionDetailResponse fetched = await JsonHelper.DeserializeAsync<PlanningSessionDetailResponse>(getResp).ConfigureAwait(false);
                    AssertNotNull(fetched.Session);
                    AssertEqual(created.Session.Id, fetched.Session!.Id);

                    HttpResponseMessage listResp = await authClient.GetAsync("/api/v1/planning-sessions").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, listResp.StatusCode);
                    List<PlanningSession> sessions = await JsonHelper.DeserializeAsync<List<PlanningSession>>(listResp).ConfigureAwait(false);
                    AssertTrue(sessions.Any(session => session.Id == created.Session.Id), "Created planning session should appear in the list");
                }
                finally
                {
                    await CleanupAsync(authClient, createdPlanningSessionIds, createdVoyageIds, createdCaptainIds, createdVesselIds, createdFleetIds).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("planning_session_lifecycle_send_summarize_dispatch_stop_delete_works", "PlanningSessionLifecycle_SendSummarizeDispatchStopDelete_Works", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                List<string> createdCaptainIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdFleetIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();
                List<string> createdPlanningSessionIds = new List<string>();

                try
                {
                    string fleetId = await CreateFleetAsync(authClient, createdFleetIds).ConfigureAwait(false);
                    string vesselId = await CreateVesselAsync(authClient, fleetId, createdVesselIds).ConfigureAwait(false);
                    Captain captain = await CreateCaptainAsync(authClient, "planning-api-lifecycle", createdCaptainIds).ConfigureAwait(false);

                    PlanningSessionDetailResponse created = await CreatePlanningSessionAsync(authClient, captain.Id, vesselId, "Lifecycle session").ConfigureAwait(false);
                    createdPlanningSessionIds.Add(created.Session!.Id);

                    // Force a deterministic assistant failure path so this test does not depend on any external CLI.
                    await UpdateCaptainRuntimeAsync(authClient, captain.Id, "Custom").ConfigureAwait(false);

                    HttpResponseMessage sendResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/messages",
                        JsonHelper.ToJsonContent(new
                        {
                            Content = "Produce a release plan for the local dashboard."
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, sendResp.StatusCode);

                    PlanningSessionDetailResponse withAssistant = await WaitForAssistantOutputAsync(authClient, created.Session.Id).ConfigureAwait(false);
                    PlanningSessionMessage? assistant = withAssistant.Messages
                        .Where(message => String.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(message => message.Sequence)
                        .FirstOrDefault(message => !String.IsNullOrWhiteSpace(message.Content));

                    AssertNotNull(assistant);
                    AssertTrue(!String.IsNullOrWhiteSpace(assistant!.Content), "Assistant output should be available for summarize and dispatch");

                    HttpResponseMessage summarizeResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/summarize",
                        JsonHelper.ToJsonContent(new
                        {
                            MessageId = assistant.Id,
                            Title = "Lifecycle dispatch draft"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, summarizeResp.StatusCode);
                    PlanningSessionSummaryResponse summary = await JsonHelper.DeserializeAsync<PlanningSessionSummaryResponse>(summarizeResp).ConfigureAwait(false);
                    AssertEqual(created.Session.Id, summary.SessionId);
                    AssertEqual(assistant.Id, summary.MessageId);
                    AssertEqual("assistant-fallback", summary.Method);
                    AssertTrue(!String.IsNullOrWhiteSpace(summary.Title), "Summary title should not be empty");
                    AssertTrue(!String.IsNullOrWhiteSpace(summary.Description), "Summary description should not be empty");

                    HttpResponseMessage dispatchResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/dispatch",
                        JsonHelper.ToJsonContent(new
                        {
                            MessageId = assistant.Id,
                            Title = summary.Title,
                            Description = summary.Description
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, dispatchResp.StatusCode);
                    Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(dispatchResp).ConfigureAwait(false);
                    createdVoyageIds.Add(voyage.Id);
                    AssertEqual(created.Session.Id, voyage.SourcePlanningSessionId);
                    AssertEqual(assistant.Id, voyage.SourcePlanningMessageId);

                    HttpResponseMessage stopResp = await authClient.PostAsync(
                        "/api/v1/planning-sessions/" + created.Session.Id + "/stop",
                        JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, stopResp.StatusCode);
                    PlanningSessionDetailResponse stopping = await JsonHelper.DeserializeAsync<PlanningSessionDetailResponse>(stopResp).ConfigureAwait(false);
                    AssertNotNull(stopping.Session);
                    AssertTrue(
                        stopping.Session!.Status.ToString() == "Stopping" || stopping.Session.Status.ToString() == "Stopped",
                        "Stop should return a stopping or stopped session state");

                    PlanningSessionDetailResponse stopped = await WaitForSessionStatusAsync(authClient, created.Session.Id, "Stopped").ConfigureAwait(false);
                    AssertEqual("Stopped", stopped.Session!.Status.ToString());

                    HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/planning-sessions/" + created.Session.Id).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                    createdPlanningSessionIds.Remove(created.Session.Id);

                    HttpResponseMessage getDeletedResp = await authClient.GetAsync("/api/v1/planning-sessions/" + created.Session.Id).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NotFound, getDeletedResp.StatusCode);
                }
                finally
                {
                    await CleanupAsync(authClient, createdPlanningSessionIds, createdVoyageIds, createdCaptainIds, createdVesselIds, createdFleetIds).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Planning Session API Tests",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.PlanningSession";

        #endregion

        #region Private-Methods

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

        private static async Task<PlanningSessionDetailResponse> WaitForAssistantOutputAsync(HttpClient authClient, string sessionId, int timeoutMs = 15000)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
            while (!deadline.Passed)
            {
                HttpResponseMessage resp = await authClient.GetAsync("/api/v1/planning-sessions/" + sessionId).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                PlanningSessionDetailResponse detail = await JsonHelper.DeserializeAsync<PlanningSessionDetailResponse>(resp).ConfigureAwait(false);
                if (detail.Messages.Any(message =>
                    String.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase) &&
                    !String.IsNullOrWhiteSpace(message.Content)))
                {
                    return detail;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            throw new Exception("Timed out waiting for assistant planning output for session " + sessionId);
        }

        private static async Task<PlanningSessionDetailResponse> WaitForSessionStatusAsync(HttpClient authClient, string sessionId, string expectedStatus, int timeoutMs = 15000)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
            while (!deadline.Passed)
            {
                HttpResponseMessage resp = await authClient.GetAsync("/api/v1/planning-sessions/" + sessionId).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                PlanningSessionDetailResponse detail = await JsonHelper.DeserializeAsync<PlanningSessionDetailResponse>(resp).ConfigureAwait(false);
                if (String.Equals(detail.Session?.Status.ToString(), expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return detail;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            throw new Exception("Timed out waiting for planning session " + sessionId + " to reach status " + expectedStatus);
        }

        private static async Task<string> CreateFleetAsync(HttpClient authClient, List<string> createdFleetIds)
        {
            HttpResponseMessage resp = await authClient.PostAsync(
                "/api/v1/fleets",
                JsonHelper.ToJsonContent(new
                {
                    Name = "PlanningFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8)
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
                    Name = "PlanningVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8),
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
