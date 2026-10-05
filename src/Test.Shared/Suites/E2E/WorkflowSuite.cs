namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// End-to-end workflow integration coverage exercising multi-entity scenarios across fleets,
    /// vessels, captains, missions, voyages, signals, and events. Ported from the retired automated
    /// <c>WorkflowTests</c> suite; every case drives the real REST API exposed by the shared
    /// in-process server obtained through <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class WorkflowSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end Workflow suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("end_to_end_fleet_to_mission_lifecycle", "EndToEnd_FleetToMissionLifecycle", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Step 1: Create a fleet
                Fleet fleet = await CreateFleetAsync(authClient, "Integration Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;
                AssertStartsWith("flt_", fleetId);

                // Step 2: Register a vessel
                Vessel vessel = await CreateVesselAsync(authClient, "IntegrationRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;
                AssertStartsWith("vsl_", vesselId);

                // Step 3: Create a captain
                Captain captain = await CreateCaptainAsync(authClient, "int-captain-1").ConfigureAwait(false);
                string captainId = captain.Id!;
                AssertStartsWith("cpt_", captainId);

                // Step 4: Create a mission (without vesselId to avoid git operations)
                Mission mission = await CreateMissionAsync(authClient, "Fix login bug").ConfigureAwait(false);
                string missionId = mission.Id!;
                AssertStartsWith("msn_", missionId);
                AssertEqual("Pending", mission.Status.ToString());

                // Step 5: Transition mission through full lifecycle
                await TransitionMissionStatusAsync(authClient, missionId, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(authClient, missionId, "InProgress").ConfigureAwait(false);
                await TransitionMissionStatusAsync(authClient, missionId, "Testing").ConfigureAwait(false);
                await TransitionMissionStatusAsync(authClient, missionId, "Review").ConfigureAwait(false);
                HttpResponseMessage completeResp = await TransitionMissionStatusAsync(authClient, missionId, "Complete").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, completeResp);

                // Step 6: Verify mission is complete
                Mission completedMission = await GetAsync<Mission>(authClient, "/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertEqual("Complete", completedMission.Status.ToString());

                // Step 7: Verify status dashboard shows the captain
                ArmadaStatus status = await GetAsync<ArmadaStatus>(authClient, "/api/v1/status").ConfigureAwait(false);
                AssertTrue(status.TotalCaptains >= 1);

                // Step 8: Verify events were generated for status transitions
                EnumerationResult<ArmadaEvent> events = await GetAsync<EnumerationResult<ArmadaEvent>>(authClient, "/api/v1/events").ConfigureAwait(false);
                AssertTrue(events.Objects.Count >= 1);
            }));

            cases.Add(CaseAsync("voyage_workflow_create_and_cancel", "VoyageWorkflow_CreateAndCancel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // A voyage only becomes InProgress when a mission is assigned and its agent launches. Captains run the
                // Claude Code runtime by default, so this case used to depend on the host: it passed where the claude CLI
                // is installed (and then launched a real agent) and failed everywhere else (CI) with the launch rolled
                // back and the voyage left Open. The fixture's stub runtime launches a process the test owns instead.
                StubAgentProcesses agents = fx.StubAgents;
                int launchedBefore = agents.StartedProcessIds.Count;
                try
                {
                    // Setup: fleet + vessel + an idle captain of its own (the case must not depend on earlier cases)
                    Fleet fleet = await CreateFleetAsync(authClient, "Voyage Fleet").ConfigureAwait(false);
                    string fleetId = fleet.Id!;
                    Vessel vessel = await CreateVesselAsync(authClient, "VoyageRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                    string vesselId = vessel.Id!;
                    await CreateCaptainAsync(authClient, "voyage-captain").ConfigureAwait(false);

                    // Create voyage with multiple missions
                    Voyage voyage = await CreateVoyageAsync(
                        authClient, "API Hardening", vesselId,
                        new MissionDescription("Add rate limiting", "Add rate limiting middleware"),
                        new MissionDescription("Add input validation", "Validate all POST endpoints"),
                        new MissionDescription("Add request logging", "Log with correlation IDs")).ConfigureAwait(false);

                    string voyageId = voyage.Id!;
                    AssertStartsWith("vyg_", voyageId);
                    AssertEqual("InProgress", voyage.Status.ToString());
                    AssertTrue(agents.StartedProcessIds.Count > launchedBefore, "a mission was launched on the stub runtime");

                    // Verify voyage details show missions
                    VoyageDetailResponse voyageDetail = await GetAsync<VoyageDetailResponse>(authClient, "/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                    AssertEqual(3, voyageDetail.Missions!.Count);
                    AssertTrue(voyageDetail.Missions.Any(m => m.Status == MissionStatusEnum.InProgress), "one mission is InProgress");

                    // Verify missions are linked to the voyage
                    EnumerationResult<Mission> missionsByVoyage = await GetAsync<EnumerationResult<Mission>>(authClient, "/api/v1/missions?voyageId=" + voyageId).ConfigureAwait(false);
                    AssertEqual(3, missionsByVoyage.Objects.Count);

                    // Cancel the voyage
                    HttpResponseMessage cancelResp = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, cancelResp);

                    // Verify cancel response has voyage data
                    CancelVoyageResponse cancelResult = await JsonHelper.DeserializeAsync<CancelVoyageResponse>(cancelResp).ConfigureAwait(false);
                    Assert(cancelResult.Voyage != null, "Cancel response should have Voyage property");

                    // Verify via GET that the voyage is cancelled and its waiting missions with it
                    VoyageDetailResponse cancelledDetail = await GetAsync<VoyageDetailResponse>(authClient, "/api/v1/voyages/" + voyageId).ConfigureAwait(false);
                    AssertEqual(VoyageStatusEnum.Cancelled, cancelledDetail.Voyage!.Status, "voyage status after cancel");
                    AssertFalse(cancelledDetail.Missions!.Any(m => m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Assigned),
                        "no mission of a cancelled voyage is still waiting for a captain");
                }
                finally
                {
                    // Cancelling a voyage leaves an InProgress mission's agent running; stop the stub processes now.
                    agents.StopAll();
                }
            }));

            cases.Add(CaseAsync("signal_flow_create_and_retrieve", "SignalFlow_CreateAndRetrieve", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a captain
                Captain captain = await CreateCaptainAsync(authClient, "signal-captain").ConfigureAwait(false);
                string captainId = captain.Id!;

                // Send a signal to the captain
                Signal signal = await CreateSignalAsync(authClient, "Mail", "Please check the tests", captainId).ConfigureAwait(false);
                string signalId = signal.Id!;
                AssertStartsWith("sig_", signalId);

                // Retrieve signals and verify it's there
                EnumerationResult<Signal> signals = await GetAsync<EnumerationResult<Signal>>(authClient, "/api/v1/signals").ConfigureAwait(false);
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
            }));

            cases.Add(CaseAsync("multi_entity_status_dashboard", "MultiEntity_StatusDashboard", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create multiple entities
                Fleet fleet = await CreateFleetAsync(authClient, "Dashboard Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;

                Vessel vessel = await CreateVesselAsync(authClient, "DashRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;

                await CreateCaptainAsync(authClient, "dash-captain-1").ConfigureAwait(false);
                await CreateCaptainAsync(authClient, "dash-captain-2").ConfigureAwait(false);

                // Create missions (without vesselId to avoid git operations)
                Mission m1 = await CreateMissionAsync(authClient, "Mission A").ConfigureAwait(false);
                string m1Id = m1.Id!;
                await CreateMissionAsync(authClient, "Mission B").ConfigureAwait(false);

                // Transition one mission
                await TransitionMissionStatusAsync(authClient, m1Id, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(authClient, m1Id, "InProgress").ConfigureAwait(false);

                // Check status dashboard
                ArmadaStatus status = await GetAsync<ArmadaStatus>(authClient, "/api/v1/status").ConfigureAwait(false);
                AssertTrue(status.TotalCaptains >= 2);

                // MissionsByStatus should have entries
                AssertTrue(status.MissionsByStatus.Any());
            }));

            cases.Add(CaseAsync("fleet_vessel_hierarchy_delete_fleet_does_not_delete_vessels", "FleetVesselHierarchy_DeleteFleetDoesNotDeleteVessels", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create fleet and vessel
                Fleet fleet = await CreateFleetAsync(authClient, "Temp Fleet").ConfigureAwait(false);
                string fleetId = fleet.Id!;

                Vessel vessel = await CreateVesselAsync(authClient, "PermanentRepo", TestRepoHelper.GetLocalBareRepoUrl(), fleetId).ConfigureAwait(false);
                string vesselId = vessel.Id!;
                string vesselName = vessel.Name!;

                // Delete fleet
                HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NoContent, deleteResp);

                // Vessel should still exist (FleetId set to null)
                Vessel vesselAfter = await GetAsync<Vessel>(authClient, "/api/v1/vessels/" + vesselId).ConfigureAwait(false);
                AssertEqual(vesselName, vesselAfter.Name);
            }));

            cases.Add(CaseAsync("mission_status_transition_invalid_transitions_rejected", "MissionStatusTransition_InvalidTransitions_Rejected", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a pending mission
                Mission mission = await CreateMissionAsync(authClient, "Transition Test").ConfigureAwait(false);
                string missionId = mission.Id!;

                // Invalid: Pending -> Complete (skip required steps)
                HttpResponseMessage resp = await TransitionMissionStatusAsync(authClient, missionId, "Complete").ConfigureAwait(false);
                ArmadaErrorResponse err = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp).ConfigureAwait(false);
                Assert(err.Error != null || err.Message != null,
                    "Should have Error or Message property for invalid transition");

                // Invalid: Pending -> InProgress (must go through Assigned first)
                HttpResponseMessage resp2 = await TransitionMissionStatusAsync(authClient, missionId, "InProgress").ConfigureAwait(false);
                ArmadaErrorResponse err2 = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp2).ConfigureAwait(false);
                Assert(err2.Error != null || err2.Message != null,
                    "Should have Error or Message property for invalid transition");

                // Valid: Pending -> Assigned
                HttpResponseMessage resp3 = await TransitionMissionStatusAsync(authClient, missionId, "Assigned").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, resp3);

                // Valid: Assigned -> InProgress
                HttpResponseMessage resp4 = await TransitionMissionStatusAsync(authClient, missionId, "InProgress").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, resp4);

                // Invalid: InProgress -> Assigned (can't go back to assigned)
                HttpResponseMessage resp5 = await TransitionMissionStatusAsync(authClient, missionId, "Assigned").ConfigureAwait(false);
                ArmadaErrorResponse err5 = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp5).ConfigureAwait(false);
                Assert(err5.Error != null || err5.Message != null,
                    "Should have Error or Message property for invalid transition");
            }));

            cases.Add(CaseAsync("captain_lifecycle_create_stop_delete", "CaptainLifecycle_CreateStopDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create captain
                Captain captain = await CreateCaptainAsync(authClient, "lifecycle-captain").ConfigureAwait(false);
                string captainId = captain.Id!;

                // Verify it shows in list
                EnumerationResult<Captain> captains = await GetAsync<EnumerationResult<Captain>>(authClient, "/api/v1/captains").ConfigureAwait(false);
                AssertTrue(captains.Objects.Count >= 1);

                // Stop the captain
                HttpResponseMessage stopResp = await authClient.PostAsync("/api/v1/captains/" + captainId + "/stop", null).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, stopResp);

                // Delete the captain
                HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NoContent, deleteResp);

                // Verify it's gone
                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                ArmadaErrorResponse errResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp).ConfigureAwait(false);
                Assert(errResp.Error != null || errResp.Message != null,
                    "Should have Error or Message property for deleted captain");
            }));

            cases.Add(CaseAsync("event_filtering_by_mission_id", "EventFiltering_ByMissionId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a mission and transition it to generate events
                Mission mission = await CreateMissionAsync(authClient, "Event Filter Test").ConfigureAwait(false);
                string missionId = mission.Id!;

                await TransitionMissionStatusAsync(authClient, missionId, "Assigned").ConfigureAwait(false);
                await TransitionMissionStatusAsync(authClient, missionId, "InProgress").ConfigureAwait(false);

                // Query events filtered by missionId
                EnumerationResult<ArmadaEvent> events = await GetAsync<EnumerationResult<ArmadaEvent>>(authClient, "/api/v1/events?missionId=" + missionId).ConfigureAwait(false);
                AssertTrue(events.Objects.Count >= 1);

                // All returned events should reference this mission
                foreach (ArmadaEvent evt in events.Objects)
                {
                    AssertEqual(missionId, evt.MissionId);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Workflow Tests",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.Workflow";

        #endregion

        #region Private-Methods

        private static async Task<Fleet> CreateFleetAsync(HttpClient authClient, string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = uniqueName })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Fleet>(resp).ConfigureAwait(false);
        }

        private static async Task<Vessel> CreateVesselAsync(HttpClient authClient, string name, string repoUrl, string? fleetId = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string uniqueRepoUrl = repoUrl.StartsWith("file://") ? repoUrl : repoUrl + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object payload = fleetId != null
                ? (object)new { Name = uniqueName, RepoUrl = uniqueRepoUrl, FleetId = fleetId }
                : new { Name = uniqueName, RepoUrl = uniqueRepoUrl };
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(payload)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
        }

        private static async Task<Captain> CreateCaptainAsync(HttpClient authClient, string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = uniqueName })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateMissionAsync(HttpClient authClient, string title, string? vesselId = null)
        {
            object payload = vesselId != null
                ? (object)new { Title = title, VesselId = vesselId }
                : new { Title = title };
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(payload)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission = wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);
            return mission;
        }

        private static async Task<Voyage> CreateVoyageAsync(HttpClient authClient, string title, string vesselId, params MissionDescription[] missions)
        {
            List<object> missionList = new List<object>();
            foreach (MissionDescription m in missions)
            {
                missionList.Add(new { Title = m.Title, Description = m.Description });
            }
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = title, VesselId = vesselId, Missions = missionList })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Voyage>(resp).ConfigureAwait(false);
        }

        private static async Task<Signal> CreateSignalAsync(HttpClient authClient, string type, string payload, string? toCaptainId = null)
        {
            object body = toCaptainId != null
                ? (object)new { Type = type, Payload = payload, ToCaptainId = toCaptainId }
                : new { Type = type, Payload = payload };
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/signals", JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Signal>(resp).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> TransitionMissionStatusAsync(HttpClient authClient, string missionId, string status)
        {
            return await authClient.PutAsync("/api/v1/missions/" + missionId + "/status", JsonHelper.ToJsonContent(new { Status = status })).ConfigureAwait(false);
        }

        private static async Task<T> GetAsync<T>(HttpClient authClient, string path)
        {
            HttpResponseMessage resp = await authClient.GetAsync(path).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string errorBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException("GET " + path + " returned " + (int)resp.StatusCode + ": " + errorBody);
            }
            return await JsonHelper.DeserializeAsync<T>(resp).ConfigureAwait(false);
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
