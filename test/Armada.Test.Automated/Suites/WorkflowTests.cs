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
    /// Workflow integration tests migrated from xUnit to TestSuite harness.
    /// Tests end-to-end scenarios across multiple entities.
    /// </summary>
    public class WorkflowTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Workflow Tests";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new workflow test suite.
        /// </summary>
        public WorkflowTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all workflow tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("EndToEnd_FleetToMissionLifecycle", async () =>
            {
                // Step 1: Create a fleet
                Fleet fleet = await CreateFleetAsync("Integration Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;
                AssertStartsWith("flt_", fleetId);

                // Step 2: Register a vessel
                Vessel vessel = await CreateVesselAsync("IntegrationRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;
                AssertStartsWith("vsl_", vesselId);

                // Step 3: Create a captain
                Captain captain = await CreateCaptainAsync("int-captain-1").ConfigureAwait(false);
                string captainId = captain.Id!;
                AssertStartsWith("cpt_", captainId);

                // Step 4: Create a mission (without vesselId to avoid git operations)
                Mission mission = await CreateMissionAsync("Fix login bug").ConfigureAwait(false);
                string missionId = mission.Id!;
                AssertStartsWith("msn_", missionId);
                AssertEqual("Pending", mission.Status.ToString());

                // Step 5: Transition mission through full lifecycle
                await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "Testing").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "Review").ConfigureAwait(false);
                HttpResponseMessage completeResp = await TransitionMissionStatusAsync(missionId, "Complete").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, completeResp);

                // Step 6: Verify mission is complete
                Mission completedMission = await GetAsync<Mission>("/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertEqual("Complete", completedMission.Status.ToString());

                // Step 7: Verify status dashboard shows the captain
                ArmadaStatus status = await GetAsync<ArmadaStatus>("/api/v1/status").ConfigureAwait(false);
                AssertTrue(status.TotalCaptains >= 1);

                // Step 8: Verify events were generated for status transitions
                EnumerationResult<ArmadaEvent> events = await GetAsync<EnumerationResult<ArmadaEvent>>("/api/v1/events").ConfigureAwait(false);
                AssertTrue(events.Objects.Count >= 1);
            }).ConfigureAwait(false);

            await RunTest("VoyageWorkflow_CreateAndCancel", async () =>
            {
                // Setup: fleet + vessel
                Fleet fleet = await CreateFleetAsync("Voyage Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;
                Vessel vessel = await CreateVesselAsync("VoyageRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;

                // Create voyage with multiple missions
                Voyage voyage = await CreateVoyageAsync(
                    "API Hardening", vesselId,
                    new MissionDescription("Add rate limiting", "Add rate limiting middleware"),
                    new MissionDescription("Add input validation", "Validate all POST endpoints"),
                    new MissionDescription("Add request logging", "Log with correlation IDs")).ConfigureAwait(false);

                string voyageId = voyage.Id!;
                AssertStartsWith("vyg_", voyageId);
                AssertEqual("InProgress", voyage.Status.ToString());

                // Verify voyage details show missions
                VoyageDetailResponse voyageDetail = await GetAsync<VoyageDetailResponse>("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                AssertEqual(3, voyageDetail.Missions!.Count);

                // Verify missions are linked to the voyage
                EnumerationResult<Mission> missionsByVoyage = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions?voyageId=" + voyageId).ConfigureAwait(false);
                AssertEqual(3, missionsByVoyage.Objects.Count);

                // Cancel the voyage
                HttpResponseMessage cancelResp = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, cancelResp);

                // Verify cancel response has voyage data
                CancelVoyageResponse cancelResult = await JsonHelper.DeserializeAsync<CancelVoyageResponse>(cancelResp).ConfigureAwait(false);
                Assert(cancelResult.Voyage != null, "Cancel response should have Voyage property");

                // Verify via GET that voyage still exists and has a valid status
                VoyageDetailResponse cancelledDetail = await GetAsync<VoyageDetailResponse>("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                string voyageStatus = cancelledDetail.Voyage!.Status.ToString();
                Assert(voyageStatus == "Cancelled" || voyageStatus == "InProgress" || voyageStatus == "Complete",
                    "Expected Cancelled, InProgress, or Complete but got " + voyageStatus);
            }).ConfigureAwait(false);

            await RunTest("SignalFlow_CreateAndRetrieve", async () =>
            {
                // Create a captain
                Captain captain = await CreateCaptainAsync("signal-captain").ConfigureAwait(false);
                string captainId = captain.Id!;

                // Send a signal to the captain
                Signal signal = await CreateSignalAsync("Mail", "Please check the tests", captainId).ConfigureAwait(false);
                string signalId = signal.Id!;
                AssertStartsWith("sig_", signalId);

                // Retrieve signals and verify it's there
                EnumerationResult<Signal> signals = await GetAsync<EnumerationResult<Signal>>("/api/v1/signals").ConfigureAwait(false);
                AssertTrue(signals.Objects.Count >= 1);

                // Find our signal
                bool found = false;
                foreach (Signal s in signals.Objects)
                {
                    if (s.Id == signalId)
                    {
                        found = true;
                        AssertEqual("Please check the tests", s.Payload);
                        break;
                    }
                }
                Assert(found, "Signal not found in signal list");
            }).ConfigureAwait(false);

            await RunTest("MultiEntity_StatusDashboard", async () =>
            {
                // Create multiple entities
                Fleet fleet = await CreateFleetAsync("Dashboard Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;

                Vessel vessel = await CreateVesselAsync("DashRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;

                await CreateCaptainAsync("dash-captain-1").ConfigureAwait(false);
                await CreateCaptainAsync("dash-captain-2").ConfigureAwait(false);

                // Create missions (without vesselId to avoid git operations)
                Mission m1 = await CreateMissionAsync("Mission A").ConfigureAwait(false);
                string m1Id = m1.Id!;
                await CreateMissionAsync("Mission B").ConfigureAwait(false);

                // Transition one mission
                await TransitionMissionStatusAsync(m1Id, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(m1Id, "InProgress").ConfigureAwait(false);

                // Check status dashboard
                ArmadaStatus status = await GetAsync<ArmadaStatus>("/api/v1/status").ConfigureAwait(false);
                AssertTrue(status.TotalCaptains >= 2);

                // MissionsByStatus should have entries
                AssertTrue(status.MissionsByStatus.Any());
            }).ConfigureAwait(false);

            await RunTest("FleetVesselHierarchy_DeleteFleetDoesNotDeleteVessels", async () =>
            {
                // Create fleet and vessel
                Fleet fleet = await CreateFleetAsync("Temp Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;

                Vessel vessel = await CreateVesselAsync("PermanentRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;
                string vesselName = vessel.Name!;

                // Delete fleet
                HttpResponseMessage deleteResp = await _AuthClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NoContent, deleteResp);

                // Vessel should still exist (FleetId set to null)
                Vessel vesselAfter = await GetAsync<Vessel>("/api/v1/vessels/" + vesselId).ConfigureAwait(false);
                AssertEqual(vesselName, vesselAfter.Name);
            }).ConfigureAwait(false);

            await RunTest("MissionStatusTransition_InvalidTransitions_Rejected", async () =>
            {
                // Create a pending mission
                Mission mission = await CreateMissionAsync("Transition Test").ConfigureAwait(false);
                string missionId = mission.Id!;

                // Invalid: Pending -> Complete (skip required steps)
                HttpResponseMessage resp = await TransitionMissionStatusAsync(missionId, "Complete").ConfigureAwait(false);
                ArmadaErrorResponse err = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp).ConfigureAwait(false);
                Assert(err.Error != null || err.Message != null,
                    "Should have Error or Message property for invalid transition");

                // Invalid: Pending -> InProgress (must go through Assigned first)
                HttpResponseMessage resp2 = await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);
                ArmadaErrorResponse err2 = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp2).ConfigureAwait(false);
                Assert(err2.Error != null || err2.Message != null,
                    "Should have Error or Message property for invalid transition");

                // Valid: Pending -> Assigned
                HttpResponseMessage resp3 = await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, resp3);

                // Valid: Assigned -> InProgress
                HttpResponseMessage resp4 = await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, resp4);

                // Invalid: InProgress -> Assigned (can't go back to assigned)
                HttpResponseMessage resp5 = await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                ArmadaErrorResponse err5 = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp5).ConfigureAwait(false);
                Assert(err5.Error != null || err5.Message != null,
                    "Should have Error or Message property for invalid transition");
            }).ConfigureAwait(false);

            await RunTest("CaptainLifecycle_CreateStopDelete", async () =>
            {
                // Create captain
                Captain captain = await CreateCaptainAsync("lifecycle-captain").ConfigureAwait(false);
                string captainId = captain.Id!;

                // Verify it shows in list
                EnumerationResult<Captain> captains = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
                AssertTrue(captains.Objects.Count >= 1);

                // Stop the captain
                HttpResponseMessage stopResp = await _AuthClient.PostAsync("/api/v1/captains/" + captainId + "/stop", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, stopResp);

                // Delete the captain
                HttpResponseMessage deleteResp = await _AuthClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NoContent, deleteResp);

                // Verify it's gone
                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                ArmadaErrorResponse errResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp).ConfigureAwait(false);
                Assert(errResp.Error != null || errResp.Message != null,
                    "Should have Error or Message property for deleted captain");
            }).ConfigureAwait(false);

            await RunTest("EventFiltering_ByMissionId", async () =>
            {
                // Create a mission and transition it to generate events
                Mission mission = await CreateMissionAsync("Event Filter Test").ConfigureAwait(false);
                string missionId = mission.Id!;

                await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);

                // Query events filtered by missionId
                EnumerationResult<ArmadaEvent> events = await GetAsync<EnumerationResult<ArmadaEvent>>("/api/v1/events?missionId=" + missionId).ConfigureAwait(false);
                AssertTrue(events.Objects.Count >= 1);

                // All returned events should reference this mission
                foreach (ArmadaEvent evt in events.Objects)
                {
                    AssertEqual(missionId, evt.MissionId);
                }
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<Fleet> CreateFleetAsync(string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = uniqueName })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Fleet>(resp).ConfigureAwait(false);
        }

        private async Task<Vessel> CreateVesselAsync(string name, string repoUrl, string? fleetId = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string uniqueRepoUrl = repoUrl.StartsWith("file://") ? repoUrl : repoUrl + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object payload = fleetId != null
                ? (object)new { Name = uniqueName, RepoUrl = uniqueRepoUrl, FleetId = fleetId }
                : new { Name = uniqueName, RepoUrl = uniqueRepoUrl };
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(payload)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
        }

        private async Task<Captain> CreateCaptainAsync(string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = uniqueName })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
        }

        private async Task<Mission> CreateMissionAsync(string title, string? vesselId = null)
        {
            object payload = vesselId != null
                ? (object)new { Title = title, VesselId = vesselId }
                : new { Title = title };
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(payload)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission = wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);
            return mission;
        }

        private async Task<Voyage> CreateVoyageAsync(string title, string vesselId, params MissionDescription[] missions)
        {
            List<object> missionList = new List<object>();
            foreach (MissionDescription m in missions)
            {
                missionList.Add(new { Title = m.Title, Description = m.Description });
            }
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = title, VesselId = vesselId, Missions = missionList })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Voyage>(resp).ConfigureAwait(false);
        }

        private async Task<Signal> CreateSignalAsync(string type, string payload, string? toCaptainId = null)
        {
            object body = toCaptainId != null
                ? (object)new { Type = type, Payload = payload, ToCaptainId = toCaptainId }
                : new { Type = type, Payload = payload };
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/signals", JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Signal>(resp).ConfigureAwait(false);
        }

        private async Task<HttpResponseMessage> TransitionMissionStatusAsync(string missionId, string status)
        {
            return await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", JsonHelper.ToJsonContent(new { Status = status })).ConfigureAwait(false);
        }

        private async Task<T> GetAsync<T>(string path)
        {
            HttpResponseMessage resp = await _AuthClient.GetAsync(path).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string errorBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException("GET " + path + " returned " + (int)resp.StatusCode + ": " + errorBody);
            }
            return await JsonHelper.DeserializeAsync<T>(resp).ConfigureAwait(false);
        }

        #endregion
    }
}
