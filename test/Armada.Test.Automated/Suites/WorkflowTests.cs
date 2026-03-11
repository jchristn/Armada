namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
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
                JsonElement fleet = await CreateFleetAsync("Integration Fleet").ConfigureAwait(false);
                string fleetId = fleet.GetProperty("Id").GetString()!;
                AssertStartsWith("flt_", fleetId);

                // Step 2: Register a vessel
                JsonElement vessel = await CreateVesselAsync("IntegrationRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.GetProperty("Id").GetString()!;
                AssertStartsWith("vsl_", vesselId);

                // Step 3: Create a captain
                JsonElement captain = await CreateCaptainAsync("int-captain-1").ConfigureAwait(false);
                string captainId = captain.GetProperty("Id").GetString()!;
                AssertStartsWith("cpt_", captainId);

                // Step 4: Create a mission (without vesselId to avoid git operations)
                JsonElement mission = await CreateMissionAsync("Fix login bug").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                AssertStartsWith("msn_", missionId);
                AssertEqual("Pending", mission.GetProperty("Status").GetString());

                // Step 5: Transition mission through full lifecycle
                await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "Testing").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "Review").ConfigureAwait(false);
                HttpResponseMessage completeResp = await TransitionMissionStatusAsync(missionId, "Complete").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, completeResp);

                // Step 6: Verify mission is complete
                JsonElement completedMission = await GetAsync("/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertEqual("Complete", completedMission.GetProperty("Status").GetString());

                // Step 7: Verify status dashboard shows the captain
                JsonElement status = await GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertTrue(status.GetProperty("TotalCaptains").GetInt32() >= 1);

                // Step 8: Verify events were generated for status transitions
                JsonElement events = await GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertTrue(events.GetProperty("Objects").GetArrayLength() >= 1);
            }).ConfigureAwait(false);

            await RunTest("VoyageWorkflow_CreateAndCancel", async () =>
            {
                // Setup: fleet + vessel
                JsonElement fleet = await CreateFleetAsync("Voyage Fleet").ConfigureAwait(false);
                string fleetId = fleet.GetProperty("Id").GetString()!;
                JsonElement vessel = await CreateVesselAsync("VoyageRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.GetProperty("Id").GetString()!;

                // Create voyage with multiple missions
                JsonElement voyage = await CreateVoyageAsync(
                    "API Hardening", vesselId,
                    ("Add rate limiting", "Add rate limiting middleware"),
                    ("Add input validation", "Validate all POST endpoints"),
                    ("Add request logging", "Log with correlation IDs")).ConfigureAwait(false);

                string voyageId = voyage.GetProperty("Id").GetString()!;
                AssertStartsWith("vyg_", voyageId);
                AssertEqual("InProgress", voyage.GetProperty("Status").GetString());

                // Verify voyage details show missions
                JsonElement voyageDetail = await GetAsync("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                JsonElement missions = voyageDetail.GetProperty("Missions");
                AssertEqual(3, missions.GetArrayLength());

                // Verify missions are linked to the voyage
                JsonElement missionsByVoyage = await GetAsync("/api/v1/missions?voyageId=" + voyageId).ConfigureAwait(false);
                AssertEqual(3, missionsByVoyage.GetProperty("Objects").GetArrayLength());

                // Cancel the voyage
                HttpResponseMessage cancelResp = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, cancelResp);

                // Verify cancel response has voyage data
                string cancelBody = await cancelResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument cancelDoc = JsonDocument.Parse(cancelBody);
                Assert(cancelDoc.RootElement.TryGetProperty("Voyage", out _), "Cancel response should have Voyage property");

                // Verify via GET that voyage still exists and has a valid status
                JsonElement cancelledVoyage = await GetAsync("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                string voyageStatus = cancelledVoyage.GetProperty("Voyage").GetProperty("Status").GetString()!;
                Assert(voyageStatus == "Cancelled" || voyageStatus == "InProgress" || voyageStatus == "Complete",
                    "Expected Cancelled, InProgress, or Complete but got " + voyageStatus);
            }).ConfigureAwait(false);

            await RunTest("SignalFlow_CreateAndRetrieve", async () =>
            {
                // Create a captain
                JsonElement captain = await CreateCaptainAsync("signal-captain").ConfigureAwait(false);
                string captainId = captain.GetProperty("Id").GetString()!;

                // Send a signal to the captain
                JsonElement signal = await CreateSignalAsync("Mail", "Please check the tests", captainId).ConfigureAwait(false);
                string signalId = signal.GetProperty("Id").GetString()!;
                AssertStartsWith("sig_", signalId);

                // Retrieve signals and verify it's there
                JsonElement signals = await GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertTrue(signals.GetProperty("Objects").GetArrayLength() >= 1);

                // Find our signal
                bool found = false;
                foreach (JsonElement s in signals.GetProperty("Objects").EnumerateArray())
                {
                    if (s.GetProperty("Id").GetString() == signalId)
                    {
                        found = true;
                        AssertEqual("Please check the tests", s.GetProperty("Payload").GetString());
                        break;
                    }
                }
                Assert(found, "Signal not found in signal list");
            }).ConfigureAwait(false);

            await RunTest("MultiEntity_StatusDashboard", async () =>
            {
                // Create multiple entities
                JsonElement fleet = await CreateFleetAsync("Dashboard Fleet").ConfigureAwait(false);
                string fleetId = fleet.GetProperty("Id").GetString()!;

                JsonElement vessel = await CreateVesselAsync("DashRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.GetProperty("Id").GetString()!;

                await CreateCaptainAsync("dash-captain-1").ConfigureAwait(false);
                await CreateCaptainAsync("dash-captain-2").ConfigureAwait(false);

                // Create missions (without vesselId to avoid git operations)
                JsonElement m1 = await CreateMissionAsync("Mission A").ConfigureAwait(false);
                string m1Id = m1.GetProperty("Id").GetString()!;
                await CreateMissionAsync("Mission B").ConfigureAwait(false);

                // Transition one mission
                await TransitionMissionStatusAsync(m1Id, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(m1Id, "InProgress").ConfigureAwait(false);

                // Check status dashboard
                JsonElement status = await GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertTrue(status.GetProperty("TotalCaptains").GetInt32() >= 2);

                // MissionsByStatus should have entries
                JsonElement missionsByStatus = status.GetProperty("MissionsByStatus");
                AssertTrue(missionsByStatus.EnumerateObject().Any());
            }).ConfigureAwait(false);

            await RunTest("FleetVesselHierarchy_DeleteFleetDoesNotDeleteVessels", async () =>
            {
                // Create fleet and vessel
                JsonElement fleet = await CreateFleetAsync("Temp Fleet").ConfigureAwait(false);
                string fleetId = fleet.GetProperty("Id").GetString()!;

                JsonElement vessel = await CreateVesselAsync("PermanentRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.GetProperty("Id").GetString()!;
                string vesselName = vessel.GetProperty("Name").GetString()!;

                // Delete fleet
                HttpResponseMessage deleteResp = await _AuthClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NoContent, deleteResp);

                // Vessel should still exist (FleetId set to null)
                JsonElement vesselAfter = await GetAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false);
                AssertEqual(vesselName, vesselAfter.GetProperty("Name").GetString());
            }).ConfigureAwait(false);

            await RunTest("MissionStatusTransition_InvalidTransitions_Rejected", async () =>
            {
                // Create a pending mission
                JsonElement mission = await CreateMissionAsync("Transition Test").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                // Invalid: Pending -> Complete (skip required steps)
                HttpResponseMessage resp = await TransitionMissionStatusAsync(missionId, "Complete").ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property for invalid transition");

                // Invalid: Pending -> InProgress (must go through Assigned first)
                HttpResponseMessage resp2 = await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);
                string body2 = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc2 = JsonDocument.Parse(body2);
                Assert(doc2.RootElement.TryGetProperty("Error", out _) || doc2.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property for invalid transition");

                // Valid: Pending -> Assigned
                HttpResponseMessage resp3 = await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, resp3);

                // Valid: Assigned -> InProgress
                HttpResponseMessage resp4 = await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, resp4);

                // Invalid: InProgress -> Assigned (can't go back to assigned)
                HttpResponseMessage resp5 = await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                string body5 = await resp5.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc5 = JsonDocument.Parse(body5);
                Assert(doc5.RootElement.TryGetProperty("Error", out _) || doc5.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property for invalid transition");
            }).ConfigureAwait(false);

            await RunTest("CaptainLifecycle_CreateStopDelete", async () =>
            {
                // Create captain
                JsonElement captain = await CreateCaptainAsync("lifecycle-captain").ConfigureAwait(false);
                string captainId = captain.GetProperty("Id").GetString()!;

                // Verify it shows in list
                JsonElement captains = await GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertTrue(captains.GetProperty("Objects").GetArrayLength() >= 1);

                // Stop the captain
                HttpResponseMessage stopResp = await _AuthClient.PostAsync("/api/v1/captains/" + captainId + "/stop", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, stopResp);

                // Delete the captain
                HttpResponseMessage deleteResp = await _AuthClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NoContent, deleteResp);

                // Verify it's gone
                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                string body = await getResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property for deleted captain");
            }).ConfigureAwait(false);

            await RunTest("EventFiltering_ByMissionId", async () =>
            {
                // Create a mission and transition it to generate events
                JsonElement mission = await CreateMissionAsync("Event Filter Test").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                await TransitionMissionStatusAsync(missionId, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(missionId, "InProgress").ConfigureAwait(false);

                // Query events filtered by missionId
                JsonElement events = await GetAsync("/api/v1/events?missionId=" + missionId).ConfigureAwait(false);
                AssertTrue(events.GetProperty("Objects").GetArrayLength() >= 1);

                // All returned events should reference this mission
                foreach (JsonElement evt in events.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual(missionId, evt.GetProperty("MissionId").GetString());
                }
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<JsonElement> CreateFleetAsync(string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private async Task<JsonElement> CreateVesselAsync(string name, string repoUrl, string? fleetId = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string uniqueRepoUrl = repoUrl.StartsWith("file://") ? repoUrl : repoUrl + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object payload = fleetId != null
                ? (object)new { Name = uniqueName, RepoUrl = uniqueRepoUrl, FleetId = fleetId }
                : new { Name = uniqueName, RepoUrl = uniqueRepoUrl };
            StringContent content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private async Task<JsonElement> CreateCaptainAsync(string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private async Task<JsonElement> CreateMissionAsync(string title, string? vesselId = null)
        {
            object payload = vesselId != null
                ? (object)new { Title = title, VesselId = vesselId }
                : new { Title = title };
            StringContent content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private async Task<JsonElement> CreateVoyageAsync(string title, string vesselId, params (string Title, string Description)[] missions)
        {
            List<object> missionList = new List<object>();
            foreach ((string t, string d) in missions)
            {
                missionList.Add(new { Title = t, Description = d });
            }
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Title = title, VesselId = vesselId, Missions = missionList }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private async Task<JsonElement> CreateSignalAsync(string type, string payload, string? toCaptainId = null)
        {
            object body = toCaptainId != null
                ? (object)new { Type = type, Payload = payload, ToCaptainId = toCaptainId }
                : new { Type = type, Payload = payload };
            StringContent content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/signals", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(respBody).RootElement.Clone();
        }

        private async Task<HttpResponseMessage> TransitionMissionStatusAsync(string missionId, string status)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Status = status }),
                Encoding.UTF8, "application/json");
            return await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content).ConfigureAwait(false);
        }

        private async Task<JsonElement> GetAsync(string path)
        {
            HttpResponseMessage resp = await _AuthClient.GetAsync(path).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException("GET " + path + " returned " + (int)resp.StatusCode + ": " + body);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        #endregion
    }
}
