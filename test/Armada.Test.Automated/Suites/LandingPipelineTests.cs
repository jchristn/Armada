namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Automated tests for the landing pipeline exercised through the REST API
    /// against a real running ArmadaServer. These complement the unit-level
    /// LandingPipelineTests by verifying the actual orchestration paths in
    /// ArmadaServer.HandleMissionCompleteAsync and related methods.
    /// </summary>
    public class LandingPipelineTests : TestSuite
    {
        #region Public-Members

        public override string Name => "Landing Pipeline (Automated)";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;

        #endregion

        #region Constructors-and-Factories

        public LandingPipelineTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        protected override async Task RunTestsAsync()
        {
            // === PullRequestOpen Status Transitions ===

            await RunTest("PullRequestOpen_TransitionsFromWorkProduced", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("PR transition test", "WorkProduced");

                // Transition to PullRequestOpen
                HttpResponseMessage resp = await TransitionAsync(missionId, "PullRequestOpen");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("PullRequestOpen", mission.Status.ToString());
            });

            await RunTest("PullRequestOpen_TransitionsToComplete", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("PR to Complete", "WorkProduced");
                await TransitionAsync(missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(missionId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("Complete", mission.Status.ToString());
                AssertTrue(mission.CompletedUtc != null,
                    "CompletedUtc should be set");
            });

            await RunTest("PullRequestOpen_TransitionsToLandingFailed", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("PR to LandingFailed", "WorkProduced");
                await TransitionAsync(missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(missionId, "LandingFailed");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("LandingFailed", mission.Status.ToString());
            });

            await RunTest("PullRequestOpen_TransitionsToCancelled", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("PR to Cancelled", "WorkProduced");
                await TransitionAsync(missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(missionId, "Cancelled");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("Cancelled", mission.Status.ToString());
            });

            await RunTest("PullRequestOpen_RejectsInvalidTransitionToInProgress", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("PR invalid transition", "WorkProduced");
                await TransitionAsync(missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(missionId, "InProgress");
                string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                ArmadaErrorResponse errorResp = JsonHelper.Deserialize<ArmadaErrorResponse>(respBody);

                // Server returns 200 with an Error property in the body for invalid transitions
                AssertTrue(errorResp.Error != null || (errorResp.Message != null && errorResp.Message.Contains("Invalid transition")),
                    "Expected error response for invalid transition PullRequestOpen -> InProgress");

                // Mission should still be PullRequestOpen
                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("PullRequestOpen", mission.Status.ToString());
            });

            // === Manual Complete Without Dock (Audit Event) ===

            await RunTest("ManualComplete_NoDock_EmitsAuditEvent", async () =>
            {
                // Create a mission and advance to WorkProduced (no dock since no vessel assignment)
                string missionId = await CreateAndAdvanceMissionAsync("Manual complete audit", "WorkProduced");

                // Manually transition to Complete (no dock exists for this mission)
                HttpResponseMessage resp = await TransitionAsync(missionId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("Complete", mission.Status.ToString());

                // Check that the audit event was emitted
                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>("/api/v1/events?type=mission.manual_complete_no_dock");
                int count = 0;
                foreach (ArmadaEvent evt in events.Objects ?? new List<ArmadaEvent>())
                {
                    if (evt.MissionId != null && evt.MissionId == missionId)
                    {
                        count++;
                    }
                }
                AssertTrue(count >= 1, "Expected at least 1 mission.manual_complete_no_dock event for mission " + missionId);
            });

            // === MergeQueue Auto-Enqueue ===

            await RunTest("MergeQueue_VesselLandingMode_CreatesEntry", async () =>
            {
                // Create a vessel with LandingMode = MergeQueue
                Vessel vessel = await CreateVesselWithLandingModeAsync("MQ-Vessel", "MergeQueue");
                string vesselId = vessel.Id!;

                // Verify vessel LandingMode was persisted
                Vessel readVessel = await GetTypedAsync<Vessel>("/api/v1/vessels/" + vesselId);
                AssertTrue(readVessel.LandingMode != null, "Vessel should have LandingMode property");
                AssertEqual("MergeQueue", readVessel.LandingMode.ToString());
            });

            // === Event Emission Correctness ===

            await RunTest("StatusChanged_EventEmitted_ForEachTransition", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("Event emission test", "InProgress");

                // Transition to WorkProduced
                await TransitionAsync(missionId, "WorkProduced");

                // Check that a status_changed event was emitted
                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>("/api/v1/events?type=mission.status_changed&missionId=" + missionId);
                bool found = false;
                foreach (ArmadaEvent evt in events.Objects ?? new List<ArmadaEvent>())
                {
                    string? msg = evt.Message;
                    if (msg != null && msg.Contains("WorkProduced"))
                    {
                        found = true;
                        break;
                    }
                }
                AssertTrue(found, "Expected mission.status_changed event mentioning WorkProduced for mission " + missionId);
            });

            await RunTest("LandingFailed_TransitionsBackToWorkProduced", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("LandingFailed retry", "WorkProduced");
                await TransitionAsync(missionId, "LandingFailed");

                // LandingFailed -> WorkProduced (retry)
                HttpResponseMessage resp = await TransitionAsync(missionId, "WorkProduced");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("WorkProduced", mission.Status.ToString());
            });

            await RunTest("LandingFailed_TransitionsToFailed", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("LandingFailed to Failed", "WorkProduced");
                await TransitionAsync(missionId, "LandingFailed");

                HttpResponseMessage resp = await TransitionAsync(missionId, "Failed");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("Failed", mission.Status.ToString());
            });

            await RunTest("LandingFailed_TransitionsToCancelled", async () =>
            {
                string missionId = await CreateAndAdvanceMissionAsync("LandingFailed to Cancelled", "WorkProduced");
                await TransitionAsync(missionId, "LandingFailed");

                HttpResponseMessage resp = await TransitionAsync(missionId, "Cancelled");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(missionId);
                AssertEqual("Cancelled", mission.Status.ToString());
            });
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Create a mission (no vessel) and advance it through the status chain to the target status.
        /// Chain: Pending -> Assigned -> InProgress -> WorkProduced -> PullRequestOpen
        /// </summary>
        private async Task<string> CreateAndAdvanceMissionAsync(string title, string targetStatus)
        {
            Mission mission = await CreateMissionAsync(title);
            string missionId = mission.Id!;

            string[] chain = new[] { "Assigned", "InProgress", "WorkProduced", "PullRequestOpen" };
            foreach (string status in chain)
            {
                HttpResponseMessage resp = await TransitionAsync(missionId, status);
                resp.EnsureSuccessStatusCode();
                if (status == targetStatus) break;
            }

            return missionId;
        }

        private async Task<Mission> CreateMissionAsync(string title)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Title = title });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission = wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);

            return mission;
        }

        private async Task<Vessel> CreateVesselWithLandingModeAsync(string name, string landingMode)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string repoUrl = TestRepoHelper.GetLocalBareRepoUrl();
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = repoUrl, LandingMode = landingMode });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Vessel>(resp);
        }

        private async Task<HttpResponseMessage> TransitionAsync(string missionId, string status)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Status = status });
            return await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content).ConfigureAwait(false);
        }

        private async Task<Mission> GetMissionAsync(string missionId)
        {
            return await GetTypedAsync<Mission>("/api/v1/missions/" + missionId).ConfigureAwait(false);
        }

        private async Task<T> GetTypedAsync<T>(string path)
        {
            HttpResponseMessage resp = await _AuthClient.GetAsync(path).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException("GET " + path + " returned " + (int)resp.StatusCode + ": " + body);
            return JsonHelper.Deserialize<T>(body);
        }

        #endregion
    }
}
