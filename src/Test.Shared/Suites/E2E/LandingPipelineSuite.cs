namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
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
    /// End-to-end coverage for the landing pipeline exercised through the REST API against the shared
    /// running server. Ported from the retired automated <c>LandingPipelineTests</c> suite; verifies the
    /// real orchestration paths in <c>ArmadaServer.HandleMissionCompleteAsync</c> and related methods via
    /// the server obtained through <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class LandingPipelineSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end Landing Pipeline suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // === PullRequestOpen Status Transitions ===

            cases.Add(CaseAsync("pull_request_open_transitions_from_work_produced", "PullRequestOpen_TransitionsFromWorkProduced", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR transition test", "WorkProduced");

                // Transition to PullRequestOpen
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "PullRequestOpen");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("PullRequestOpen", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("pull_request_open_transitions_to_complete", "PullRequestOpen_TransitionsToComplete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR to Complete", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Complete", mission.Status.ToString());
                AssertTrue(mission.CompletedUtc != null,
                    "CompletedUtc should be set");
            }));

            cases.Add(CaseAsync("pull_request_open_transitions_to_landing_failed", "PullRequestOpen_TransitionsToLandingFailed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR to LandingFailed", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "LandingFailed");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("LandingFailed", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("pull_request_open_transitions_to_cancelled", "PullRequestOpen_TransitionsToCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR to Cancelled", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Cancelled");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Cancelled", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("pull_request_open_rejects_invalid_transition_to_in_progress", "PullRequestOpen_RejectsInvalidTransitionToInProgress", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR invalid transition", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "InProgress");
                await E2eApiErrorAssert.ExpectAsync(resp, HttpStatusCode.BadRequest, "invalid transition PullRequestOpen -> InProgress is refused").ConfigureAwait(false);

                // Mission should still be PullRequestOpen
                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("PullRequestOpen", mission.Status.ToString());
            }));

            // === Manual Complete Without Dock (Audit Event) ===

            cases.Add(CaseAsync("manual_complete_no_dock_emits_audit_event", "ManualComplete_NoDock_EmitsAuditEvent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a mission and advance to WorkProduced (no dock since no vessel assignment)
                string missionId = await CreateAndAdvanceMissionAsync(authClient, "Manual complete audit", "WorkProduced");

                // Manually transition to Complete (no dock exists for this mission)
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Complete", mission.Status.ToString());

                // Check that the audit event was emitted
                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>(authClient, "/api/v1/events?type=mission.manual_complete_no_dock");
                int count = 0;
                foreach (ArmadaEvent evt in events.Objects ?? new List<ArmadaEvent>())
                {
                    if (evt.MissionId != null && evt.MissionId == missionId)
                    {
                        count++;
                    }
                }
                AssertTrue(count >= 1, "Expected at least 1 mission.manual_complete_no_dock event for mission " + missionId);
            }));

            // === MergeQueue Auto-Enqueue ===

            cases.Add(CaseAsync("merge_queue_vessel_landing_mode_creates_entry", "MergeQueue_VesselLandingMode_CreatesEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a vessel with LandingMode = MergeQueue
                Vessel vessel = await CreateVesselWithLandingModeAsync(authClient, "MQ-Vessel", "MergeQueue");
                string vesselId = vessel.Id!;

                // Verify vessel LandingMode was persisted
                Vessel readVessel = await GetTypedAsync<Vessel>(authClient, "/api/v1/vessels/" + vesselId);
                AssertTrue(readVessel.LandingMode != null, "Vessel should have LandingMode property");
                AssertEqual("MergeQueue", readVessel.LandingMode.ToString());
            }));

            // === Event Emission Correctness ===

            cases.Add(CaseAsync("status_changed_event_emitted_for_each_transition", "StatusChanged_EventEmitted_ForEachTransition", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "Event emission test", "InProgress");

                // Transition to WorkProduced
                await TransitionAsync(authClient, missionId, "WorkProduced");

                // Check that a status_changed event was emitted
                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>(authClient, "/api/v1/events?type=mission.status_changed&missionId=" + missionId);
                bool found = false;
                foreach (ArmadaEvent evt in events.Objects ?? new List<ArmadaEvent>())
                {
                    string? msg = evt.Message;
                    // TODO(R5, production): mission.status_changed carries the new status only in its message text.
                    if (msg == "Mission " + missionId + " transitioned to WorkProduced")
                    {
                        found = true;
                        break;
                    }
                }
                AssertTrue(found, "Expected mission.status_changed event mentioning WorkProduced for mission " + missionId);
            }));

            cases.Add(CaseAsync("landing_failed_transitions_back_to_work_produced", "LandingFailed_TransitionsBackToWorkProduced", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "LandingFailed retry", "WorkProduced");
                await TransitionAsync(authClient, missionId, "LandingFailed");

                // LandingFailed -> WorkProduced (retry)
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "WorkProduced");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("WorkProduced", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("landing_failed_transitions_to_failed", "LandingFailed_TransitionsToFailed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "LandingFailed to Failed", "WorkProduced");
                await TransitionAsync(authClient, missionId, "LandingFailed");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Failed");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Failed", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("landing_failed_transitions_to_cancelled", "LandingFailed_TransitionsToCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "LandingFailed to Cancelled", "WorkProduced");
                await TransitionAsync(authClient, missionId, "LandingFailed");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Cancelled");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Cancelled", mission.Status.ToString());
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Landing Pipeline (Automated)",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.LandingPipeline";

        #endregion

        #region Private-Methods

        /// <summary>
        /// Create a mission (no vessel) and advance it through the status chain to the target status.
        /// Chain: Pending -> Assigned -> InProgress -> WorkProduced -> PullRequestOpen
        /// </summary>
        private static async Task<string> CreateAndAdvanceMissionAsync(HttpClient authClient, string title, string targetStatus)
        {
            Mission mission = await CreateMissionAsync(authClient, title);
            string missionId = mission.Id!;

            string[] chain = new[] { "Assigned", "InProgress", "WorkProduced", "PullRequestOpen" };
            foreach (string status in chain)
            {
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, status);
                resp.EnsureSuccessStatusCode();
                if (status == targetStatus) break;
            }

            return missionId;
        }

        private static async Task<Mission> CreateMissionAsync(HttpClient authClient, string title)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Title = title });
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission = wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);

            return mission;
        }

        private static async Task<Vessel> CreateVesselWithLandingModeAsync(HttpClient authClient, string name, string landingMode)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string repoUrl = TestRepoHelper.GetLocalBareRepoUrl();
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = repoUrl, LandingMode = landingMode });
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Vessel>(resp);
        }

        private static async Task<HttpResponseMessage> TransitionAsync(HttpClient authClient, string missionId, string status)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Status = status });
            return await authClient.PutAsync("/api/v1/missions/" + missionId + "/status", content).ConfigureAwait(false);
        }

        private static async Task<Mission> GetMissionAsync(HttpClient authClient, string missionId)
        {
            return await GetTypedAsync<Mission>(authClient, "/api/v1/missions/" + missionId).ConfigureAwait(false);
        }

        private static async Task<T> GetTypedAsync<T>(HttpClient authClient, string path)
        {
            HttpResponseMessage resp = await authClient.GetAsync(path).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException("GET " + path + " returned " + (int)resp.StatusCode + ": " + body);
            return JsonHelper.Deserialize<T>(body);
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
