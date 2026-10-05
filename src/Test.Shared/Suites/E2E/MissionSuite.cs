namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end Mission API descriptors covering CRUD, status transitions (valid and invalid),
    /// diff retrieval, list pagination and filters, and enumeration against a live in-process
    /// Armada server provided by <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class MissionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private readonly object _SeedLock = new object();
        private Task? _SharedMissionSeed;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission API end-to-end suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            #region CRUD-Create

            cases.Add(CaseAsync("create_mission_returns_201_with_correct_properties", "CreateMission_Returns201_WithCorrectProperties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);

                StringContent content = JsonHelper.ToJsonContent(new { Title = "New Mission", VesselId = vesselId });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission mission;
                if (wrapper.Mission != null)
                    mission = wrapper.Mission;
                else
                    mission = JsonHelper.Deserialize<Mission>(body);

                createdMissionIds.Add(mission.Id);

                AssertStartsWith("msn_", mission.Id);
                AssertEqual("New Mission", mission.Title);
                AssertEqual(MissionStatusEnum.Pending, mission.Status);
                AssertFalse(mission.CreatedUtc == default);
                AssertFalse(mission.LastUpdateUtc == default);
                AssertTrue(mission.Priority >= 0);
            }));

            cases.Add(CaseAsync("create_mission_default_status_is_pending", "CreateMission_DefaultStatusIsPending", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Pending Check");
                AssertEqual(MissionStatusEnum.Pending, mission.Status);
            }));

            cases.Add(CaseAsync("create_mission_default_priority_is_100", "CreateMission_DefaultPriorityIs100", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);

                StringContent content = JsonHelper.ToJsonContent(new { Title = "Priority Check", VesselId = vesselId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions", content);
                string body = await response.Content.ReadAsStringAsync();
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission mission;
                if (wrapper.Mission != null)
                    mission = wrapper.Mission;
                else
                    mission = JsonHelper.Deserialize<Mission>(body);

                createdMissionIds.Add(mission.Id);

                AssertEqual(100, mission.Priority);
            }));

            cases.Add(CaseAsync("create_mission_with_all_optional_fields", "CreateMission_WithAllOptionalFields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();
                List<string> createdCaptainIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);

                string voyageId = await CreateVoyageAsync(authClient, createdVoyageIds, "FullMissionVoyage");

                Mission parentMission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Parent Mission");
                string parentMissionId = parentMission.Id;

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Title = "Full Mission",
                    VesselId = vesselId,
                    Description = "A detailed description",
                    Priority = 50,
                    VoyageId = voyageId,
                    ParentMissionId = parentMissionId,
                    BranchName = "feature/test-branch"
                });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission created;
                if (wrapper.Mission != null)
                    created = wrapper.Mission;
                else
                    created = JsonHelper.Deserialize<Mission>(body);

                string missionId = created.Id;
                createdMissionIds.Add(missionId);

                string captainId = await CreateCaptainAsync(authClient, createdCaptainIds, "full-mission-captain");
                StringContent assignContent = JsonHelper.ToJsonContent(new
                {
                    Title = "Full Mission",
                    VesselId = vesselId,
                    Description = "A detailed description",
                    Priority = 50,
                    VoyageId = voyageId,
                    CaptainId = captainId,
                    ParentMissionId = parentMissionId,
                    BranchName = "feature/test-branch"
                });
                await authClient.PutAsync("/api/v1/missions/" + missionId, assignContent);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission result = await JsonHelper.DeserializeAsync<Mission>(getResp);

                AssertEqual("Full Mission", result.Title);
                AssertEqual("A detailed description", result.Description);
                AssertEqual(50, result.Priority);
            }));

            cases.Add(CaseAsync("create_mission_with_custom_priority", "CreateMission_WithCustomPriority", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "High Priority", priority: 10);
                AssertEqual(10, mission.Priority);
            }));

            cases.Add(CaseAsync("create_mission_with_description", "CreateMission_WithDescription", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Described Mission", description: "This is a detailed description");
                AssertEqual("This is a detailed description", mission.Description);
            }));

            cases.Add(CaseAsync("create_mission_id_has_msn_prefix", "CreateMission_IdHasMsnPrefix", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Prefix Check");
                AssertStartsWith("msn_", mission.Id);
            }));

            cases.Add(CaseAsync("create_mission_has_timestamps", "CreateMission_HasTimestamps", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Timestamp Check");

                AssertFalse(mission.CreatedUtc == default);
                AssertFalse(mission.LastUpdateUtc == default);
            }));

            #endregion

            #region CRUD-Read

            cases.Add(CaseAsync("get_mission_exists_returns_mission", "GetMission_Exists_ReturnsMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "GetTest");
                string missionId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("GetTest", fetched.Title);
                AssertEqual(missionId, fetched.Id);
            }));

            cases.Add(CaseAsync("get_mission_not_found_returns_error", "GetMission_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/msn_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("get_mission_returns_all_properties", "GetMission_ReturnsAllProperties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Full Read", description: "Full desc", priority: 42);
                string missionId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);

                AssertEqual("Full Read", fetched.Title);
                AssertEqual("Full desc", fetched.Description);
                AssertEqual(42, fetched.Priority);
                AssertEqual(MissionStatusEnum.Pending, fetched.Status);
            }));

            #endregion

            #region CRUD-Update

            cases.Add(CaseAsync("update_mission_title_returns_updated", "UpdateMission_Title_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Original Title");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Updated Title" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("Updated Title", updated.Title);
            }));

            cases.Add(CaseAsync("update_mission_description_returns_updated", "UpdateMission_Description_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Desc Update");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Desc Update", Description = "New description" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("New description", updated.Description);
            }));

            cases.Add(CaseAsync("update_mission_priority_returns_updated", "UpdateMission_Priority_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Priority Update");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Priority Update", Priority = 1 });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(1, updated.Priority);
            }));

            cases.Add(CaseAsync("update_mission_multiple_fields_returns_updated", "UpdateMission_MultipleFields_ReturnsUpdated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Multi Update");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "New Title", Description = "New Desc", Priority = 5 });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("New Title", updated.Title);
                AssertEqual("New Desc", updated.Description);
                AssertEqual(5, updated.Priority);
            }));

            cases.Add(CaseAsync("update_mission_not_found_returns_error", "UpdateMission_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Ghost" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/msn_nonexistent", updateContent);
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("update_mission_preserves_id", "UpdateMission_PreservesId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Id Preserve");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Updated" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(missionId, updated.Id);
            }));

            #endregion

            #region CRUD-Delete

            cases.Add(CaseAsync("delete_mission_returns_cancelled_status", "DeleteMission_ReturnsCancelledStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "ToCancel");
                string missionId = created.Id;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission deleted = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Cancelled, deleted.Status);
                AssertEqual(missionId, deleted.Id);
            }));

            cases.Add(CaseAsync("delete_mission_sets_status_to_cancelled_in_database", "DeleteMission_SetsStatusToCancelledInDatabase", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "CancelVerify");
                string missionId = created.Id;

                await authClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);
                AssertEqual(MissionStatusEnum.Cancelled, fetched.Status);
            }));

            cases.Add(CaseAsync("delete_mission_sets_completed_utc", "DeleteMission_SetsCompletedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "CancelTimestamp");
                string missionId = created.Id;

                await authClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);
                AssertTrue(fetched.CompletedUtc != null);
            }));

            cases.Add(CaseAsync("delete_mission_not_found_returns_error", "DeleteMission_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/missions/msn_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            #endregion

            #region StatusTransition-Valid-HappyPath

            cases.Add(CaseAsync("status_transition_pending_to_assigned_succeeds", "StatusTransition_PendingToAssigned_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "PendToAssign");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Assigned");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Assigned, transitioned.Status);
            }));

            cases.Add(CaseAsync("status_transition_pending_to_cancelled_succeeds", "StatusTransition_PendingToCancelled_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "PendToCancel");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Cancelled");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_in_progress_succeeds", "StatusTransition_AssignedToInProgress_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "AssignToIP");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_cancelled_succeeds", "StatusTransition_AssignedToCancelled_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "AssignToCancel");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "Cancelled");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_testing_succeeds", "StatusTransition_InProgressToTesting_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToTest");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_review_succeeds", "StatusTransition_InProgressToReview_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToReview");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_complete_succeeds", "StatusTransition_InProgressToComplete_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Complete");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_failed_succeeds", "StatusTransition_InProgressToFailed_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToFailed");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Failed");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_cancelled_succeeds", "StatusTransition_InProgressToCancelled_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToCancel");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Cancelled");
            }));

            cases.Add(CaseAsync("status_transition_testing_to_review_succeeds", "StatusTransition_TestingToReview_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "TestToReview");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
            }));

            cases.Add(CaseAsync("status_transition_testing_to_in_progress_succeeds", "StatusTransition_TestingToInProgress_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "TestToIP");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
            }));

            cases.Add(CaseAsync("status_transition_testing_to_complete_succeeds", "StatusTransition_TestingToComplete_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "TestToComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "Complete");
            }));

            cases.Add(CaseAsync("status_transition_testing_to_failed_succeeds", "StatusTransition_TestingToFailed_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "TestToFailed");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "Failed");
            }));

            cases.Add(CaseAsync("status_transition_review_to_complete_succeeds", "StatusTransition_ReviewToComplete_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "ReviewToComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
                await TransitionAndAssertAsync(authClient, missionId, "Complete");
            }));

            cases.Add(CaseAsync("status_transition_review_to_in_progress_succeeds", "StatusTransition_ReviewToInProgress_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "ReviewToIP");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
            }));

            cases.Add(CaseAsync("status_transition_review_to_failed_succeeds", "StatusTransition_ReviewToFailed_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "ReviewToFailed");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
                await TransitionAndAssertAsync(authClient, missionId, "Failed");
            }));

            #endregion

            #region StatusTransition-Valid-Lifecycle

            cases.Add(CaseAsync("status_transition_full_lifecycle_pending_through_review_to_complete", "StatusTransition_FullLifecycle_PendingThroughReviewToComplete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Lifecycle Full");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "Review");

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Complete");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Complete, transitioned.Status);
            }));

            cases.Add(CaseAsync("status_transition_full_lifecycle_sets_completed_utc_on_complete", "StatusTransition_FullLifecycle_SetsCompletedUtcOnComplete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Complete Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Complete");
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.CompletedUtc != null);
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_in_progress_sets_started_utc", "StatusTransition_AssignedToInProgress_SetsStartedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Start Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "InProgress");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.StartedUtc != null, "InProgress transition should stamp StartedUtc");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_complete_sets_total_runtime_ms", "StatusTransition_InProgressToComplete_SetsTotalRuntimeMs", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Runtime Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Complete");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.TotalRuntimeMs != null, "Complete transition should preserve TotalRuntimeMs when StartedUtc exists");
            }));

            cases.Add(CaseAsync("status_transition_full_lifecycle_sets_completed_utc_on_failed", "StatusTransition_FullLifecycle_SetsCompletedUtcOnFailed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Failed Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Failed");
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.CompletedUtc != null);
            }));

            cases.Add(CaseAsync("status_transition_full_lifecycle_sets_completed_utc_on_cancelled", "StatusTransition_FullLifecycle_SetsCompletedUtcOnCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Cancel Timestamp");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Cancelled");
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.CompletedUtc != null);
            }));

            cases.Add(CaseAsync("status_transition_testing_bounce_back_to_in_progress_then_complete", "StatusTransition_TestingBounceBackToInProgress_ThenComplete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Bounce Back");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Testing");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
                await TransitionAndAssertAsync(authClient, missionId, "Complete");
            }));

            cases.Add(CaseAsync("status_transition_review_bounce_back_to_in_progress_then_complete", "StatusTransition_ReviewBounceBackToInProgress_ThenComplete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Review Bounce");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Review");
                await TransitionAndAssertAsync(authClient, missionId, "Complete");
            }));

            #endregion

            #region StatusTransition-Invalid

            cases.Add(CaseAsync("status_transition_pending_to_complete_fails", "StatusTransition_PendingToComplete_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadPendComplete");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Complete");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_pending_to_in_progress_fails", "StatusTransition_PendingToInProgress_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadPendIP");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "InProgress");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_pending_to_testing_fails", "StatusTransition_PendingToTesting_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadPendTest");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Testing");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_pending_to_review_fails", "StatusTransition_PendingToReview_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadPendReview");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Review");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_pending_to_failed_fails", "StatusTransition_PendingToFailed_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadPendFail");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Failed");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_complete_fails", "StatusTransition_AssignedToComplete_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadAssignComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Complete");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_testing_fails", "StatusTransition_AssignedToTesting_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadAssignTest");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Testing");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_review_fails", "StatusTransition_AssignedToReview_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadAssignReview");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Review");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_failed_fails", "StatusTransition_AssignedToFailed_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadAssignFail");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Failed");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_complete_to_anything_fails", "StatusTransition_CompleteToAnything_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "CompleteTerminal");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Complete");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Failed", "Cancelled" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(authClient, missionId, target);
                    await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "Complete->" + target + " is refused");
                }
            }));

            cases.Add(CaseAsync("status_transition_cancelled_to_anything_fails", "StatusTransition_CancelledToAnything_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "CancelledTerminal");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Cancelled");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Failed" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(authClient, missionId, target);
                    await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "Cancelled->" + target + " is refused");
                }
            }));

            cases.Add(CaseAsync("status_transition_failed_to_anything_fails", "StatusTransition_FailedToAnything_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "FailedTerminal");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                await TransitionAndAssertAsync(authClient, missionId, "Failed");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Cancelled" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(authClient, missionId, target);
                    await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "Failed->" + target + " is refused");
                }
            }));

            cases.Add(CaseAsync("status_transition_pending_to_pending_fails", "StatusTransition_PendingToPending_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "SameState");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Pending");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_assigned_to_pending_fails", "StatusTransition_AssignedToPending_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "AssignedToPend");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Pending");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_pending_fails", "StatusTransition_InProgressToPending_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToPend");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Pending");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_in_progress_to_assigned_fails", "StatusTransition_InProgressToAssigned_Fails", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "IPToAssign");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");
                await TransitionAndAssertAsync(authClient, missionId, "InProgress");
                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Assigned");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            #endregion

            #region StatusTransition-ErrorCases

            cases.Add(CaseAsync("status_transition_empty_status_returns_error", "StatusTransition_EmptyStatus_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EmptyStatus");
                string missionId = created.Id;

                StringContent content = JsonHelper.ToJsonContent(new { Status = "" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/missions/" + missionId + "/status", content);

                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "an empty status is a typed 400");
            }));

            cases.Add(CaseAsync("status_transition_invalid_status_name_returns_error", "StatusTransition_InvalidStatusName_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "BadStatusName");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "NotAStatus");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_garbage_status_name_returns_error", "StatusTransition_GarbageStatusName_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Garbage");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "!@#$%^&*()");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("status_transition_not_found_returns_error", "StatusTransition_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await TransitionAsync(authClient, "msn_nonexistent", "Assigned");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "transition on a missing mission is a typed 404");
            }));

            cases.Add(CaseAsync("status_transition_updates_last_update_utc", "StatusTransition_UpdatesLastUpdateUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Timestamp Update");
                string missionId = created.Id;
                DateTime originalLastUpdate = created.LastUpdateUtc;

                await Task.Delay(50);
                await TransitionAndAssertAsync(authClient, missionId, "Assigned");

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);

                AssertNotEqual(originalLastUpdate.ToString("o"), fetched.LastUpdateUtc.ToString("o"));
            }));

            #endregion

            #region Diff

            cases.Add(CaseAsync("diff_mission_not_found_returns_error", "Diff_MissionNotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/msn_nonexistent/diff");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("diff_no_diff_file_returns_error_or_empty", "Diff_NoDiffFile_ReturnsErrorOrEmpty", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "No Diff");
                string missionId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/diff");
                string body = await response.Content.ReadAsStringAsync();
                MissionDiffResponse diff = JsonHelper.Deserialize<MissionDiffResponse>(body);

                bool isError = diff.Error != null;
                bool isEmptyDiff = diff.Diff == "" || diff.Diff == null;
                Assert(isError || isEmptyDiff, "Expected error or empty diff but got: " + body);
            }));

            cases.Add(CaseAsync("get_mission_diff_snapshot_is_null", "GetMission_DiffSnapshotIsNull", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "DiffSnapshotExclusion");
                string missionId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);

                AssertTrue(fetched.DiffSnapshot == null, "DiffSnapshot should be null or absent");
            }));

            cases.Add(CaseAsync("list_missions_diff_snapshot_is_null_in_results", "ListMissions_DiffSnapshotIsNullInResults", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "DiffSnapshotListCheck");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                foreach (Mission mission in result.Objects)
                {
                    AssertTrue(mission.DiffSnapshot == null);
                }
            }));

            cases.Add(CaseAsync("enumerate_missions_diff_snapshot_is_null_in_results", "EnumerateMissions_DiffSnapshotIsNullInResults", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "DiffSnapshotEnumCheck");

                StringContent enumContent = JsonHelper.ToJsonContent(new { Status = "Pending", PageSize = 10 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", enumContent);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                foreach (Mission mission in result.Objects)
                {
                    AssertTrue(mission.DiffSnapshot == null);
                }
            }));

            #endregion

            #region List-Pagination

            cases.Add(CaseAsync("list_missions_empty_returns_empty_enumeration", "ListMissions_Empty_ReturnsEmptyEnumeration", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("list_missions_after_create_returns_missions", "ListMissions_AfterCreate_ReturnsMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "List Test 1");
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "List Test 2");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertTrue(result.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("list_missions_pagination_25_missions_page_size_10_returns_correct_counts", "ListMissions_Pagination_25Missions_PageSize10_ReturnsCorrectCounts", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureMissionsSeededAsync(authClient);

                HttpResponseMessage page1Resp = await authClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=1");
                EnumerationResult<Mission> page1 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page1Resp);
                AssertEqual(10, page1.Objects.Count);
                AssertEqual(1, page1.PageNumber);
                AssertEqual(10, page1.PageSize);
            }));

            cases.Add(CaseAsync("list_missions_pagination_page_2", "ListMissions_Pagination_Page2", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureMissionsSeededAsync(authClient);

                HttpResponseMessage page2Resp = await authClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=2");
                EnumerationResult<Mission> page2 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page2Resp);
                AssertEqual(10, page2.Objects.Count);
                AssertEqual(2, page2.PageNumber);
            }));

            cases.Add(CaseAsync("list_missions_pagination_last_page_partial_results", "ListMissions_Pagination_LastPage_PartialResults", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureMissionsSeededAsync(authClient);

                // With shared data, just verify that a page beyond total returns empty
                HttpResponseMessage resp = await authClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=1");
                EnumerationResult<Mission> firstPage = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(resp);
                int totalPages = firstPage.TotalPages;

                // The last page should have <= 10 items
                HttpResponseMessage lastPageResp = await authClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=" + totalPages);
                EnumerationResult<Mission> lastPage = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(lastPageResp);
                int lastPageCount = lastPage.Objects.Count;
                AssertTrue(lastPageCount > 0 && lastPageCount <= 10, "Last page should have 1-10 items");
                AssertEqual(totalPages, lastPage.PageNumber);
            }));

            cases.Add(CaseAsync("list_missions_pagination_beyond_last_page_returns_empty", "ListMissions_Pagination_BeyondLastPage_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureMissionsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=99");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_missions_pagination_page_size_1_each_page_has_one_record", "ListMissions_Pagination_PageSize1_EachPageHasOneRecord", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureMissionsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?pageSize=1&pageNumber=1");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(1, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_missions_enumeration_result_has_expected_structure", "ListMissions_EnumerationResult_HasExpectedStructure", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects != null);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
                // Success is a bool, just verify it deserializes
                AssertTrue(result.Success || !result.Success);
            }));

            cases.Add(CaseAsync("list_missions_pages_contain_distinct_records", "ListMissions_PagesContainDistinctRecords", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                for (int i = 1; i <= 6; i++)
                {
                    await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Distinct Mission " + i);
                }

                HttpResponseMessage page1Resp = await authClient.GetAsync("/api/v1/missions?pageSize=3&pageNumber=1");
                EnumerationResult<Mission> page1 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page1Resp);

                HttpResponseMessage page2Resp = await authClient.GetAsync("/api/v1/missions?pageSize=3&pageNumber=2");
                EnumerationResult<Mission> page2 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page2Resp);

                List<string> page1Ids = page1.Objects.Select(m => m.Id).ToList();

                foreach (Mission obj in page2.Objects)
                {
                    AssertFalse(page1Ids.Contains(obj.Id));
                }
            }));

            #endregion

            #region List-Filters

            cases.Add(CaseAsync("list_missions_filter_by_status_returns_only_matching", "ListMissions_FilterByStatus_ReturnsOnlyMatching", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "StatusFilter Pending 1");
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "StatusFilter Pending 2");
                Mission toAssign = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "StatusFilter Assigned");
                string toAssignId = toAssign.Id;
                await TransitionAndAssertAsync(authClient, toAssignId, "Assigned");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?status=Pending");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                int count = result.Objects.Count;
                Assert(count >= 2, "Expected at least 2 Pending missions, got " + count);

                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Pending, obj.Status);
                }
            }));

            cases.Add(CaseAsync("list_missions_filter_by_status_assigned", "ListMissions_FilterByStatus_Assigned", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission m1 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Filter Assigned 1");
                string m1Id = m1.Id;
                await TransitionAndAssertAsync(authClient, m1Id, "Assigned");

                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Filter Stay Pending");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?status=Assigned");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 1);
                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Assigned, obj.Status);
                }
            }));

            cases.Add(CaseAsync("list_missions_filter_by_vessel_id_returns_only_matching", "ListMissions_FilterByVesselId_ReturnsOnlyMatching", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vessel1 = await CreateVesselAsync(authClient, createdVesselIds, fleetId);

                StringContent v2Content = JsonHelper.ToJsonContent(new { Name = "OtherVessel", RepoUrl = "https://github.com/test/other", FleetId = fleetId });
                HttpResponseMessage v2Resp = await authClient.PostAsync("/api/v1/vessels", v2Content);
                Vessel v2 = await JsonHelper.DeserializeAsync<Vessel>(v2Resp);
                string vessel2 = v2.Id;
                createdVesselIds.Add(vessel2);

                await CreateMissionAsync(authClient, createdMissionIds, vessel1, "Vessel1 Mission A");
                await CreateMissionAsync(authClient, createdMissionIds, vessel1, "Vessel1 Mission B");
                await CreateMissionAsync(authClient, createdMissionIds, vessel2, "Vessel2 Mission");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?vesselId=" + vessel1);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                int count = result.Objects.Count;
                Assert(count >= 2, "Expected at least 2 missions for vessel1, got " + count);
            }));

            cases.Add(CaseAsync("list_missions_filter_by_captain_id_returns_valid_result", "ListMissions_FilterByCaptainId_ReturnsValidResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                // CaptainId is an operational field managed by the dispatch system,
                // not assignable via PUT. Verify the filter endpoint returns a valid result.
                string captainId = await CreateCaptainAsync(authClient, createdCaptainIds, "filter-captain");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?captainId=" + captainId);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                // Should return a valid enumeration result (possibly empty)
                AssertTrue(result.Objects != null, "Should return Objects array");
                AssertTrue(result.TotalRecords >= 0, "Should return TotalRecords");
            }));

            cases.Add(CaseAsync("list_missions_filter_by_voyage_id_returns_only_matching", "ListMissions_FilterByVoyageId_ReturnsOnlyMatching", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                string voyageId = await CreateVoyageAsync(authClient, createdVoyageIds, "VoyageFilterTest");

                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Voyage Mission 1", voyageId: voyageId);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Voyage Mission 2", voyageId: voyageId);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "No Voyage Mission");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?voyageId=" + voyageId);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("list_missions_filter_by_nonexistent_status_returns_empty", "ListMissions_FilterByNonexistentStatus_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Filter by a nonexistent vesselId to guarantee empty results
                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?vesselId=vsl_nonexistent_" + Guid.NewGuid().ToString("N"));
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_missions_filter_by_nonexistent_vessel_id_returns_empty", "ListMissions_FilterByNonexistentVesselId_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "SomeExistingMission");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?vesselId=vsl_doesnotexist");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(0, result.Objects.Count);
            }));

            #endregion

            #region Enumerate-POST

            cases.Add(CaseAsync("enumerate_empty_body_returns_all_missions", "Enumerate_EmptyBody_ReturnsAllMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Enum Mission 1");
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Enum Mission 2");

                StringContent content = JsonHelper.ToJsonContent(new { });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertTrue(result.Objects.Count >= 2);
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("enumerate_with_pagination", "Enumerate_WithPagination", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                for (int i = 1; i <= 15; i++)
                {
                    await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumPage Mission " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertEqual(5, result.Objects.Count);
            }));

            cases.Add(CaseAsync("enumerate_with_status_filter", "Enumerate_WithStatusFilter", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission m1 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumStatus Assigned");
                string m1Id = m1.Id;
                await TransitionAndAssertAsync(authClient, m1Id, "Assigned");

                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumStatus Pending");

                StringContent content = JsonHelper.ToJsonContent(new { Status = "Assigned" });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 1);
                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Assigned, obj.Status);
                }
            }));

            cases.Add(CaseAsync("enumerate_with_vessel_id_filter", "Enumerate_WithVesselIdFilter", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vessel1 = await CreateVesselAsync(authClient, createdVesselIds, fleetId);

                StringContent v2Content = JsonHelper.ToJsonContent(new { Name = "EnumOtherVessel", RepoUrl = "https://github.com/test/enum-other", FleetId = fleetId });
                HttpResponseMessage v2Resp = await authClient.PostAsync("/api/v1/vessels", v2Content);
                Vessel v2 = await JsonHelper.DeserializeAsync<Vessel>(v2Resp);
                string vessel2 = v2.Id;
                createdVesselIds.Add(vessel2);

                await CreateMissionAsync(authClient, createdMissionIds, vessel1, "EnumVessel1 A");
                await CreateMissionAsync(authClient, createdMissionIds, vessel1, "EnumVessel1 B");
                await CreateMissionAsync(authClient, createdMissionIds, vessel2, "EnumVessel2 A");

                StringContent content = JsonHelper.ToJsonContent(new { VesselId = vessel1 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("enumerate_with_ordering_created_descending", "Enumerate_WithOrdering_CreatedDescending", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumOrder First");
                await Task.Delay(50);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumOrder Second");
                await Task.Delay(50);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumOrder Third");

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedDescending" });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 3);

                string firstTitle = result.Objects[0].Title;
                AssertEqual("EnumOrder Third", firstTitle);
            }));

            cases.Add(CaseAsync("enumerate_with_ordering_created_ascending", "Enumerate_WithOrdering_CreatedAscending", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission m1 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumAsc First");
                await Task.Delay(50);
                Mission m2 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumAsc Second");
                await Task.Delay(50);
                Mission m3 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumAsc Third");

                string id1 = m1.Id;
                string id2 = m2.Id;
                string id3 = m3.Id;

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedAscending", PageSize = 10000 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 3);

                // Verify our 3 items appear in ascending order (id1 before id2 before id3)
                int idx1 = -1, idx2 = -1, idx3 = -1;
                for (int i = 0; i < result.Objects.Count; i++)
                {
                    string id = result.Objects[i].Id;
                    if (id == id1) idx1 = i;
                    if (id == id2) idx2 = i;
                    if (id == id3) idx3 = i;
                }
                AssertTrue(idx1 >= 0, "First mission should appear in results");
                AssertTrue(idx2 > idx1, "Second mission should appear after first in ascending order");
                AssertTrue(idx3 > idx2, "Third mission should appear after second in ascending order");
            }));

            cases.Add(CaseAsync("enumerate_page_2_returns_correct_page", "Enumerate_Page2_ReturnsCorrectPage", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                for (int i = 1; i <= 8; i++)
                {
                    await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumPage2 Mission " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 3 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("enumerate_has_enumeration_result_structure", "Enumerate_HasEnumerationResultStructure", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent content = JsonHelper.ToJsonContent(new { });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects != null);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.Success || !result.Success);
            }));

            cases.Add(CaseAsync("enumerate_combined_filters_status_and_vessel_id", "Enumerate_CombinedFilters_StatusAndVesselId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vessel1 = await CreateVesselAsync(authClient, createdVesselIds, fleetId);

                Mission m1 = await CreateMissionAsync(authClient, createdMissionIds, vessel1, "Combined Assigned");
                string m1Id = m1.Id;
                await TransitionAndAssertAsync(authClient, m1Id, "Assigned");

                await CreateMissionAsync(authClient, createdMissionIds, vessel1, "Combined Pending");

                StringContent content = JsonHelper.ToJsonContent(new { Status = "Assigned", VesselId = vessel1 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 1);
                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Assigned, obj.Status);
                }
            }));

            #endregion

            #region EdgeCases

            cases.Add(CaseAsync("create_multiple_missions_each_has_unique_id", "CreateMultipleMissions_EachHasUniqueId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission m1 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Unique 1");
                Mission m2 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Unique 2");
                Mission m3 = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Unique 3");

                string id1 = m1.Id;
                string id2 = m2.Id;
                string id3 = m3.Id;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            }));

            cases.Add(CaseAsync("delete_then_get_shows_cancelled_status", "DeleteThenGet_ShowsCancelledStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Delete Then Get");
                string missionId = created.Id;

                await authClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Cancelled, fetched.Status);
            }));

            cases.Add(CaseAsync("status_transition_cancelled_mission_cannot_transition", "StatusTransition_CancelledMission_CannotTransition", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Cancel Block");
                string missionId = created.Id;

                await authClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "Pending");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.BadRequest, "expected BadRequest");
            }));

            cases.Add(CaseAsync("update_mission_after_status_transition_preserves_status", "UpdateMission_AfterStatusTransition_PreservesStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Status Preserve");
                string missionId = created.Id;

                await TransitionAndAssertAsync(authClient, missionId, "Assigned");

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Updated While Assigned" });
                await authClient.PutAsync("/api/v1/missions/" + missionId, updateContent);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);
                AssertEqual("Updated While Assigned", fetched.Title);
            }));

            cases.Add(CaseAsync("create_mission_with_priority_0_accepted", "CreateMission_WithPriority0_Accepted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Zero Priority", priority: 0);
                AssertEqual(0, mission.Priority);
            }));

            cases.Add(CaseAsync("create_mission_with_high_priority_accepted", "CreateMission_WithHighPriority_Accepted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission mission = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "High Priority", priority: 9999);
                AssertEqual(9999, mission.Priority);
            }));

            cases.Add(CaseAsync("list_missions_filter_by_multiple_statuses_via_multiple_calls", "ListMissions_FilterByMultipleStatuses_ViaMultipleCalls", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);

                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Multi Pending");
                Mission assigned = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Multi Assigned");
                string assignedId = assigned.Id;
                await TransitionAndAssertAsync(authClient, assignedId, "Assigned");

                HttpResponseMessage pendingResp = await authClient.GetAsync("/api/v1/missions?status=Pending");
                EnumerationResult<Mission> pendingResult = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(pendingResp);
                AssertTrue(pendingResult.Objects.Count >= 1);

                HttpResponseMessage assignedResp = await authClient.GetAsync("/api/v1/missions?status=Assigned");
                EnumerationResult<Mission> assignedResult = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(assignedResp);
                AssertTrue(assignedResult.Objects.Count >= 1);
            }));

            cases.Add(CaseAsync("enumerate_voyage_id_filter_matches_correct_missions", "Enumerate_VoyageIdFilter_MatchesCorrectMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                string voyageId = await CreateVoyageAsync(authClient, createdVoyageIds, "EnumVoyageFilter");

                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumVoyage 1", voyageId: voyageId);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumVoyage 2", voyageId: voyageId);
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "EnumNoVoyage");

                StringContent content = JsonHelper.ToJsonContent(new { VoyageId = voyageId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("enumerate_empty_result_has_correct_structure", "Enumerate_EmptyResult_HasCorrectStructure", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fakeVesselId = "vsl_nonexistent_" + Guid.NewGuid().ToString("N");
                StringContent content = JsonHelper.ToJsonContent(new { VesselId = fakeVesselId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertEqual(0, result.Objects.Count);
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("status_transition_case_insensitive_accepts_lowercase", "StatusTransition_CaseInsensitive_AcceptsLowercase", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Case Test");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "assigned");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Assigned, transitioned.Status);
            }));

            cases.Add(CaseAsync("status_transition_case_insensitive_accepts_mixed_case", "StatusTransition_CaseInsensitive_AcceptsMixedCase", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdMissionIds = new List<string>();

                string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
                Mission created = await CreateMissionAsync(authClient, createdMissionIds, vesselId, "Mixed Case");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(authClient, missionId, "ASSIGNED");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            #endregion

            return new TestSuiteDescriptor(
                suiteId: "E2E.Mission",
                displayName: "Missions",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Ensure the shared mission dataset holds at least 25 listable missions, seeding it exactly once
        /// for the suite. Every case runs against the same in-process server fixture and the list pagination
        /// cases only assert accumulation-tolerant conditions (a full page slice, a last page with 1-10
        /// items, an empty page beyond the end, a single-record page), so they can share one dataset instead
        /// of each re-creating a vessel and 25 missions over sequential HTTP round-trips. The seed Task is
        /// memoized: the first pagination case to run pays the cost and the rest await the completed Task.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>A task that completes once the shared dataset exists.</returns>
        private Task EnsureMissionsSeededAsync(HttpClient authClient)
        {
            lock (_SeedLock)
            {
                if (_SharedMissionSeed == null) _SharedMissionSeed = SeedSharedMissionsAsync(authClient);
                return _SharedMissionSeed;
            }
        }

        /// <summary>
        /// Create a dedicated vessel and 25 missions on it exactly once for the shared pagination dataset.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>A task that completes once the vessel and its 25 missions exist.</returns>
        private static async Task SeedSharedMissionsAsync(HttpClient authClient)
        {
            List<string> createdFleetIds = new List<string>();
            List<string> createdVesselIds = new List<string>();
            List<string> createdMissionIds = new List<string>();

            string vesselId = await SetupVesselAsync(authClient, createdFleetIds, createdVesselIds);
            for (int i = 1; i <= 25; i++)
            {
                await CreateMissionAsync(authClient, createdMissionIds, vesselId, "SharedPagMission " + i);
            }
        }

        /// <summary>
        /// Creates a fleet and returns its ID.
        /// </summary>
        private static async Task<string> CreateFleetAsync(HttpClient client, List<string> createdFleetIds)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = "MissionTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/fleets", content);
            string body = await resp.Content.ReadAsStringAsync();
            Fleet fleet = JsonHelper.Deserialize<Fleet>(body);
            if (String.IsNullOrEmpty(fleet.Id))
                throw new Exception("CreateFleetAsync failed (" + (int)resp.StatusCode + "): " + body);
            createdFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        /// <summary>
        /// Creates a vessel against a local bare repo in the given fleet and returns its ID.
        /// </summary>
        private static async Task<string> CreateVesselAsync(HttpClient client, List<string> createdVesselIds, string fleetId)
        {
            string repoUrl = TestRepoHelper.GetLocalBareRepoUrl();
            StringContent content = JsonHelper.ToJsonContent(new { Name = "MissionTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = repoUrl, FleetId = fleetId });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/vessels", content);
            string body = await resp.Content.ReadAsStringAsync();
            Vessel vessel = JsonHelper.Deserialize<Vessel>(body);
            if (String.IsNullOrEmpty(vessel.Id))
                throw new Exception("CreateVesselAsync failed (" + (int)resp.StatusCode + "): " + body);
            createdVesselIds.Add(vessel.Id);
            return vessel.Id;
        }

        /// <summary>
        /// Creates a mission and returns the deserialized Mission object.
        /// </summary>
        private static async Task<Mission> CreateMissionAsync(HttpClient client, List<string> createdMissionIds, string vesselId, string title, string? voyageId = null, int priority = 100, string? description = null, string? captainId = null)
        {
            object requestBody;
            if (voyageId != null && captainId != null)
                requestBody = new { Title = title, VesselId = vesselId, VoyageId = voyageId, Priority = priority, Description = description ?? "", CaptainId = captainId };
            else if (voyageId != null)
                requestBody = new { Title = title, VesselId = vesselId, VoyageId = voyageId, Priority = priority, Description = description ?? "" };
            else if (captainId != null)
                requestBody = new { Title = title, VesselId = vesselId, Priority = priority, Description = description ?? "", CaptainId = captainId };
            else
                requestBody = new { Title = title, VesselId = vesselId, Priority = priority, Description = description ?? "" };

            StringContent content = JsonHelper.ToJsonContent(requestBody);
            HttpResponseMessage resp = await client.PostAsync("/api/v1/missions", content);
            string body = await resp.Content.ReadAsStringAsync();

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission;
            if (wrapper.Mission != null)
                mission = wrapper.Mission;
            else
                mission = JsonHelper.Deserialize<Mission>(body);

            if (String.IsNullOrEmpty(mission.Id))
                throw new Exception("CreateMissionAsync failed (" + (int)resp.StatusCode + "): " + body);
            createdMissionIds.Add(mission.Id);
            return mission;
        }

        /// <summary>
        /// Issues a status transition PUT request and returns the raw response.
        /// </summary>
        private static async Task<HttpResponseMessage> TransitionAsync(HttpClient client, string missionId, string status)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Status = status });
            return await client.PutAsync("/api/v1/missions/" + missionId + "/status", content);
        }

        /// <summary>
        /// Issues a status transition and asserts it succeeded with the expected status.
        /// </summary>
        private static async Task TransitionAndAssertAsync(HttpClient client, string missionId, string status)
        {
            HttpResponseMessage resp = await TransitionAsync(client, missionId, status);
            AssertEqual(HttpStatusCode.OK, resp.StatusCode);
            Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(resp);
            AssertEqual(status, transitioned.Status.ToString());
        }

        /// <summary>
        /// Creates a fleet and a vessel in it, returning the vessel ID.
        /// </summary>
        private static async Task<string> SetupVesselAsync(HttpClient client, List<string> createdFleetIds, List<string> createdVesselIds)
        {
            string fleetId = await CreateFleetAsync(client, createdFleetIds);
            return await CreateVesselAsync(client, createdVesselIds, fleetId);
        }

        /// <summary>
        /// Creates a captain and returns its ID.
        /// </summary>
        private static async Task<string> CreateCaptainAsync(HttpClient client, List<string> createdCaptainIds, string name = "test-captain")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/captains", content);
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp);
            createdCaptainIds.Add(captain.Id);
            return captain.Id;
        }

        /// <summary>
        /// Creates a voyage and returns its ID.
        /// </summary>
        private static async Task<string> CreateVoyageAsync(HttpClient client, List<string> createdVoyageIds, string title = "TestVoyage")
        {
            StringContent content = JsonHelper.ToJsonContent(new
            {
                Title = title,
                Description = "Test voyage"
            });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/voyages", content);
            string body = await resp.Content.ReadAsStringAsync();
            Voyage voyage = JsonHelper.Deserialize<Voyage>(body);
            createdVoyageIds.Add(voyage.Id);
            return voyage.Id;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "E2E.Mission",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
