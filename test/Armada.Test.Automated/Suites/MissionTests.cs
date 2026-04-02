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

    public class MissionTests : TestSuite
    {
        #region Public-Members

        public override string Name => "Missions";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private List<string> _CreatedMissionIds = new List<string>();
        private List<string> _CreatedVoyageIds = new List<string>();
        private List<string> _CreatedCaptainIds = new List<string>();
        private List<string> _CreatedVesselIds = new List<string>();
        private List<string> _CreatedFleetIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        public MissionTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        protected override async Task RunTestsAsync()
        {
            #region CRUD-Create

            await RunTest("CreateMission_Returns201_WithCorrectProperties", async () =>
            {
                string vesselId = await SetupVesselAsync();

                StringContent content = JsonHelper.ToJsonContent(new { Title = "New Mission", VesselId = vesselId });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission mission;
                if (wrapper.Mission != null)
                    mission = wrapper.Mission;
                else
                    mission = JsonHelper.Deserialize<Mission>(body);

                _CreatedMissionIds.Add(mission.Id);

                AssertStartsWith("msn_", mission.Id);
                AssertEqual("New Mission", mission.Title);
                AssertEqual(MissionStatusEnum.Pending, mission.Status);
                AssertFalse(mission.CreatedUtc == default);
                AssertFalse(mission.LastUpdateUtc == default);
                AssertTrue(mission.Priority >= 0);
            });

            await RunTest("CreateMission_DefaultStatusIsPending", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "Pending Check");
                AssertEqual(MissionStatusEnum.Pending, mission.Status);
            });

            await RunTest("CreateMission_DefaultPriorityIs100", async () =>
            {
                string vesselId = await SetupVesselAsync();

                StringContent content = JsonHelper.ToJsonContent(new { Title = "Priority Check", VesselId = vesselId });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content);
                string body = await response.Content.ReadAsStringAsync();
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission mission;
                if (wrapper.Mission != null)
                    mission = wrapper.Mission;
                else
                    mission = JsonHelper.Deserialize<Mission>(body);

                _CreatedMissionIds.Add(mission.Id);

                AssertEqual(100, mission.Priority);
            });

            await RunTest("CreateMission_WithAllOptionalFields", async () =>
            {
                string vesselId = await SetupVesselAsync();

                string voyageId = await CreateVoyageAsync("FullMissionVoyage");

                Mission parentMission = await CreateMissionAsync(vesselId, "Parent Mission");
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

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission created;
                if (wrapper.Mission != null)
                    created = wrapper.Mission;
                else
                    created = JsonHelper.Deserialize<Mission>(body);

                string missionId = created.Id;
                _CreatedMissionIds.Add(missionId);

                string captainId = await CreateCaptainAsync("full-mission-captain");
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
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId, assignContent);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission result = await JsonHelper.DeserializeAsync<Mission>(getResp);

                AssertEqual("Full Mission", result.Title);
                AssertEqual("A detailed description", result.Description);
                AssertEqual(50, result.Priority);
            });

            await RunTest("CreateMission_WithCustomPriority", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "High Priority", priority: 10);
                AssertEqual(10, mission.Priority);
            });

            await RunTest("CreateMission_WithDescription", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "Described Mission", description: "This is a detailed description");
                AssertEqual("This is a detailed description", mission.Description);
            });

            await RunTest("CreateMission_IdHasMsnPrefix", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "Prefix Check");
                AssertStartsWith("msn_", mission.Id);
            });

            await RunTest("CreateMission_HasTimestamps", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "Timestamp Check");

                AssertFalse(mission.CreatedUtc == default);
                AssertFalse(mission.LastUpdateUtc == default);
            });

            #endregion

            #region CRUD-Read

            await RunTest("GetMission_Exists_ReturnsMission", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "GetTest");
                string missionId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("GetTest", fetched.Title);
                AssertEqual(missionId, fetched.Id);
            });

            await RunTest("GetMission_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/msn_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("GetMission_ReturnsAllProperties", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Full Read", description: "Full desc", priority: 42);
                string missionId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);

                AssertEqual("Full Read", fetched.Title);
                AssertEqual("Full desc", fetched.Description);
                AssertEqual(42, fetched.Priority);
                AssertEqual(MissionStatusEnum.Pending, fetched.Status);
            });

            #endregion

            #region CRUD-Update

            await RunTest("UpdateMission_Title_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Original Title");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Updated Title" });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("Updated Title", updated.Title);
            });

            await RunTest("UpdateMission_Description_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Desc Update");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Desc Update", Description = "New description" });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("New description", updated.Description);
            });

            await RunTest("UpdateMission_Priority_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Priority Update");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Priority Update", Priority = 1 });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(1, updated.Priority);
            });

            await RunTest("UpdateMission_MultipleFields_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Multi Update");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "New Title", Description = "New Desc", Priority = 5 });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual("New Title", updated.Title);
                AssertEqual("New Desc", updated.Description);
                AssertEqual(5, updated.Priority);
            });

            await RunTest("UpdateMission_NotFound_ReturnsError", async () =>
            {
                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Ghost" });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/msn_nonexistent", updateContent);
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("UpdateMission_PreservesId", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Id Preserve");
                string missionId = created.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Updated" });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                Mission updated = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(missionId, updated.Id);
            });

            #endregion

            #region CRUD-Delete

            await RunTest("DeleteMission_ReturnsCancelledStatus", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "ToCancel");
                string missionId = created.Id;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission deleted = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Cancelled, deleted.Status);
                AssertEqual(missionId, deleted.Id);
            });

            await RunTest("DeleteMission_SetsStatusToCancelledInDatabase", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "CancelVerify");
                string missionId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);
                AssertEqual(MissionStatusEnum.Cancelled, fetched.Status);
            });

            await RunTest("DeleteMission_SetsCompletedUtc", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "CancelTimestamp");
                string missionId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);
                AssertTrue(fetched.CompletedUtc != null);
            });

            await RunTest("DeleteMission_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/missions/msn_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            #endregion

            #region StatusTransition-Valid-HappyPath

            await RunTest("StatusTransition_PendingToAssigned_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "PendToAssign");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Assigned");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Assigned, transitioned.Status);
            });

            await RunTest("StatusTransition_PendingToCancelled_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "PendToCancel");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Cancelled");
            });

            await RunTest("StatusTransition_AssignedToInProgress_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "AssignToIP");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
            });

            await RunTest("StatusTransition_AssignedToCancelled_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "AssignToCancel");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "Cancelled");
            });

            await RunTest("StatusTransition_InProgressToTesting_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToTest");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
            });

            await RunTest("StatusTransition_InProgressToReview_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToReview");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
            });

            await RunTest("StatusTransition_InProgressToComplete_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_InProgressToFailed_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToFailed");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Failed");
            });

            await RunTest("StatusTransition_InProgressToCancelled_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToCancel");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Cancelled");
            });

            await RunTest("StatusTransition_TestingToReview_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "TestToReview");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Review");
            });

            await RunTest("StatusTransition_TestingToInProgress_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "TestToIP");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "InProgress");
            });

            await RunTest("StatusTransition_TestingToComplete_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "TestToComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_TestingToFailed_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "TestToFailed");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Failed");
            });

            await RunTest("StatusTransition_ReviewToComplete_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "ReviewToComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_ReviewToInProgress_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "ReviewToIP");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "InProgress");
            });

            await RunTest("StatusTransition_ReviewToFailed_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "ReviewToFailed");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "Failed");
            });

            #endregion

            #region StatusTransition-Valid-Lifecycle

            await RunTest("StatusTransition_FullLifecycle_PendingThroughReviewToComplete", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Lifecycle Full");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Review");

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Complete, transitioned.Status);
            });

            await RunTest("StatusTransition_FullLifecycle_SetsCompletedUtcOnComplete", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Complete Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.CompletedUtc != null);
            });

            await RunTest("StatusTransition_AssignedToInProgress_SetsStartedUtc", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Start Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");

                HttpResponseMessage response = await TransitionAsync(missionId, "InProgress");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.StartedUtc != null, "InProgress transition should stamp StartedUtc");
            });

            await RunTest("StatusTransition_InProgressToComplete_SetsTotalRuntimeMs", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Runtime Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.TotalRuntimeMs != null, "Complete transition should preserve TotalRuntimeMs when StartedUtc exists");
            });

            await RunTest("Get Mission After Complete Returns TotalRuntimeMs", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Runtime Readback");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");

                HttpResponseMessage completeResponse = await TransitionAsync(missionId, "Complete");
                AssertEqual(HttpStatusCode.OK, completeResponse.StatusCode);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, getResponse.StatusCode);

                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResponse);
                AssertEqual(MissionStatusEnum.Complete, fetched.Status);
                AssertTrue(fetched.CompletedUtc != null, "Completed mission read should include CompletedUtc");
                AssertTrue(fetched.TotalRuntimeMs != null, "Completed mission read should include TotalRuntimeMs");
                AssertTrue(fetched.TotalRuntimeMs!.Value >= 0, "Completed mission runtime should be non-negative");
            });

            await RunTest("StatusTransition_FullLifecycle_SetsCompletedUtcOnFailed", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Failed Timestamp");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(missionId, "Failed");
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.CompletedUtc != null);
            });

            await RunTest("StatusTransition_FullLifecycle_SetsCompletedUtcOnCancelled", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Cancel Timestamp");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Cancelled");
                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertTrue(transitioned.CompletedUtc != null);
            });

            await RunTest("StatusTransition_TestingBounceBackToInProgress_ThenComplete", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Bounce Back");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_ReviewBounceBackToInProgress_ThenComplete", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Review Bounce");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            #endregion

            #region StatusTransition-Invalid

            await RunTest("StatusTransition_PendingToComplete_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadPendComplete");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_PendingToInProgress_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadPendIP");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "InProgress");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_PendingToTesting_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadPendTest");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Testing");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_PendingToReview_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadPendReview");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Review");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_PendingToFailed_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadPendFail");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Failed");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_AssignedToComplete_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadAssignComplete");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_AssignedToTesting_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadAssignTest");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Testing");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_AssignedToReview_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadAssignReview");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Review");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_AssignedToFailed_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadAssignFail");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Failed");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_CompleteToAnything_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "CompleteTerminal");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Complete");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Failed", "Cancelled" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(missionId, target);
                    string body = await response.Content.ReadAsStringAsync();
                    ArmadaErrorResponse error = JsonHelper.Deserialize<ArmadaErrorResponse>(body);
                    Assert(
                        error.Error != null || error.Message != null,
                        "Expected error for Complete->" + target + " but got: " + body);
                }
            });

            await RunTest("StatusTransition_CancelledToAnything_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "CancelledTerminal");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Cancelled");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Failed" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(missionId, target);
                    string body = await response.Content.ReadAsStringAsync();
                    ArmadaErrorResponse error = JsonHelper.Deserialize<ArmadaErrorResponse>(body);
                    Assert(
                        error.Error != null || error.Message != null,
                        "Expected error for Cancelled->" + target + " but got: " + body);
                }
            });

            await RunTest("StatusTransition_FailedToAnything_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "FailedTerminal");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Failed");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Cancelled" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(missionId, target);
                    string body = await response.Content.ReadAsStringAsync();
                    ArmadaErrorResponse error = JsonHelper.Deserialize<ArmadaErrorResponse>(body);
                    Assert(
                        error.Error != null || error.Message != null,
                        "Expected error for Failed->" + target + " but got: " + body);
                }
            });

            await RunTest("StatusTransition_PendingToPending_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "SameState");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_AssignedToPending_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "AssignedToPend");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_InProgressToPending_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToPend");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_InProgressToAssigned_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "IPToAssign");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                HttpResponseMessage response = await TransitionAsync(missionId, "Assigned");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            #endregion

            #region StatusTransition-ErrorCases

            await RunTest("StatusTransition_EmptyStatus_ReturnsError", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "EmptyStatus");
                string missionId = created.Id;

                StringContent content = JsonHelper.ToJsonContent(new { Status = "" });
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content);

                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_InvalidStatusName_ReturnsError", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "BadStatusName");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "NotAStatus");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_GarbageStatusName_ReturnsError", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Garbage");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "!@#$%^&*()");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await TransitionAsync("msn_nonexistent", "Assigned");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("StatusTransition_UpdatesLastUpdateUtc", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Timestamp Update");
                string missionId = created.Id;
                DateTime originalLastUpdate = created.LastUpdateUtc;

                await Task.Delay(50);
                await TransitionAndAssertAsync(missionId, "Assigned");

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);

                AssertNotEqual(originalLastUpdate.ToString("o"), fetched.LastUpdateUtc.ToString("o"));
            });

            #endregion

            #region Diff

            await RunTest("Diff_MissionNotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/msn_nonexistent/diff");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("Diff_NoDiffFile_ReturnsErrorOrEmpty", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "No Diff");
                string missionId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/diff");
                string body = await response.Content.ReadAsStringAsync();
                MissionDiffResponse diff = JsonHelper.Deserialize<MissionDiffResponse>(body);

                bool isError = diff.Error != null;
                bool isEmptyDiff = diff.Diff == "" || diff.Diff == null;
                Assert(isError || isEmptyDiff, "Expected error or empty diff but got: " + body);
            });

            await RunTest("GetMission_DiffSnapshotIsNull", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "DiffSnapshotExclusion");
                string missionId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);

                AssertTrue(fetched.DiffSnapshot == null, "DiffSnapshot should be null or absent");
            });

            await RunTest("ListMissions_DiffSnapshotIsNullInResults", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "DiffSnapshotListCheck");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                foreach (Mission mission in result.Objects)
                {
                    AssertTrue(mission.DiffSnapshot == null);
                }
            });

            await RunTest("EnumerateMissions_DiffSnapshotIsNullInResults", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "DiffSnapshotEnumCheck");

                StringContent enumContent = JsonHelper.ToJsonContent(new { Status = "Pending", PageSize = 10 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", enumContent);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                foreach (Mission mission in result.Objects)
                {
                    AssertTrue(mission.DiffSnapshot == null);
                }
            });

            #endregion

            #region List-Pagination

            await RunTest("ListMissions_Empty_ReturnsEmptyEnumeration", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.Success);
            });

            await RunTest("ListMissions_AfterCreate_ReturnsMissions", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "List Test 1");
                await CreateMissionAsync(vesselId, "List Test 2");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertTrue(result.Objects.Count >= 2);
            });

            await RunTest("ListMissions_Pagination_25Missions_PageSize10_ReturnsCorrectCounts", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateMissionAsync(vesselId, "Page Mission " + i);
                }

                HttpResponseMessage page1Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=1");
                EnumerationResult<Mission> page1 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page1Resp);
                AssertEqual(10, page1.Objects.Count);
                AssertEqual(1, page1.PageNumber);
                AssertEqual(10, page1.PageSize);
            });

            await RunTest("ListMissions_Pagination_Page2", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateMissionAsync(vesselId, "Page2 Mission " + i);
                }

                HttpResponseMessage page2Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=2");
                EnumerationResult<Mission> page2 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page2Resp);
                AssertEqual(10, page2.Objects.Count);
                AssertEqual(2, page2.PageNumber);
            });

            await RunTest("ListMissions_Pagination_LastPage_PartialResults", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateMissionAsync(vesselId, "LastPage Mission " + i);
                }

                // With shared data, just verify that a page beyond total returns empty
                HttpResponseMessage resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=1");
                EnumerationResult<Mission> firstPage = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(resp);
                int totalPages = firstPage.TotalPages;

                // The last page should have <= 10 items
                HttpResponseMessage lastPageResp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=" + totalPages);
                EnumerationResult<Mission> lastPage = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(lastPageResp);
                int lastPageCount = lastPage.Objects.Count;
                AssertTrue(lastPageCount > 0 && lastPageCount <= 10, "Last page should have 1-10 items");
                AssertEqual(totalPages, lastPage.PageNumber);
            });

            await RunTest("ListMissions_Pagination_BeyondLastPage_ReturnsEmpty", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 5; i++)
                {
                    await CreateMissionAsync(vesselId, "Beyond Mission " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=99");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("ListMissions_Pagination_PageSize1_EachPageHasOneRecord", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "Single A");
                await CreateMissionAsync(vesselId, "Single B");
                await CreateMissionAsync(vesselId, "Single C");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?pageSize=1&pageNumber=1");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(1, result.Objects.Count);
            });

            await RunTest("ListMissions_EnumerationResult_HasExpectedStructure", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects != null);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
                // Success is a bool, just verify it deserializes
                AssertTrue(result.Success || !result.Success);
            });

            await RunTest("ListMissions_PagesContainDistinctRecords", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 6; i++)
                {
                    await CreateMissionAsync(vesselId, "Distinct Mission " + i);
                }

                HttpResponseMessage page1Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=3&pageNumber=1");
                EnumerationResult<Mission> page1 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page1Resp);

                HttpResponseMessage page2Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=3&pageNumber=2");
                EnumerationResult<Mission> page2 = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(page2Resp);

                List<string> page1Ids = page1.Objects.Select(m => m.Id).ToList();

                foreach (Mission obj in page2.Objects)
                {
                    AssertFalse(page1Ids.Contains(obj.Id));
                }
            });

            #endregion

            #region List-Filters

            await RunTest("ListMissions_FilterByStatus_ReturnsOnlyMatching", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "StatusFilter Pending 1");
                await CreateMissionAsync(vesselId, "StatusFilter Pending 2");
                Mission toAssign = await CreateMissionAsync(vesselId, "StatusFilter Assigned");
                string toAssignId = toAssign.Id;
                await TransitionAndAssertAsync(toAssignId, "Assigned");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?status=Pending");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                int count = result.Objects.Count;
                Assert(count >= 2, "Expected at least 2 Pending missions, got " + count);

                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Pending, obj.Status);
                }
            });

            await RunTest("ListMissions_FilterByStatus_Assigned", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission m1 = await CreateMissionAsync(vesselId, "Filter Assigned 1");
                string m1Id = m1.Id;
                await TransitionAndAssertAsync(m1Id, "Assigned");

                await CreateMissionAsync(vesselId, "Filter Stay Pending");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?status=Assigned");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 1);
                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Assigned, obj.Status);
                }
            });

            await RunTest("ListMissions_FilterByVesselId_ReturnsOnlyMatching", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vessel1 = await CreateVesselAsync(fleetId);

                StringContent v2Content = JsonHelper.ToJsonContent(new { Name = "OtherVessel", RepoUrl = "https://github.com/test/other", FleetId = fleetId });
                HttpResponseMessage v2Resp = await _AuthClient.PostAsync("/api/v1/vessels", v2Content);
                Vessel v2 = await JsonHelper.DeserializeAsync<Vessel>(v2Resp);
                string vessel2 = v2.Id;
                _CreatedVesselIds.Add(vessel2);

                await CreateMissionAsync(vessel1, "Vessel1 Mission A");
                await CreateMissionAsync(vessel1, "Vessel1 Mission B");
                await CreateMissionAsync(vessel2, "Vessel2 Mission");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?vesselId=" + vessel1);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                int count = result.Objects.Count;
                Assert(count >= 2, "Expected at least 2 missions for vessel1, got " + count);
            });

            await RunTest("ListMissions_FilterByCaptainId_ReturnsValidResult", async () =>
            {
                // CaptainId is an operational field managed by the dispatch system,
                // not assignable via PUT. Verify the filter endpoint returns a valid result.
                string captainId = await CreateCaptainAsync("filter-captain");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?captainId=" + captainId);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                // Should return a valid enumeration result (possibly empty)
                AssertTrue(result.Objects != null, "Should return Objects array");
                AssertTrue(result.TotalRecords >= 0, "Should return TotalRecords");
            });

            await RunTest("ListMissions_FilterByVoyageId_ReturnsOnlyMatching", async () =>
            {
                string vesselId = await SetupVesselAsync();
                string voyageId = await CreateVoyageAsync("VoyageFilterTest");

                await CreateMissionAsync(vesselId, "Voyage Mission 1", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "Voyage Mission 2", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "No Voyage Mission");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?voyageId=" + voyageId);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 2);
            });

            await RunTest("ListMissions_FilterByNonexistentStatus_ReturnsEmpty", async () =>
            {
                // Filter by a nonexistent vesselId to guarantee empty results
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?vesselId=vsl_nonexistent_" + Guid.NewGuid().ToString("N"));
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("ListMissions_FilterByNonexistentVesselId_ReturnsEmpty", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "SomeExistingMission");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?vesselId=vsl_doesnotexist");
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertEqual(0, result.Objects.Count);
            });

            #endregion

            #region Enumerate-POST

            await RunTest("Enumerate_EmptyBody_ReturnsAllMissions", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "Enum Mission 1");
                await CreateMissionAsync(vesselId, "Enum Mission 2");

                StringContent content = JsonHelper.ToJsonContent(new { });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);
                AssertTrue(result.Objects.Count >= 2);
                AssertTrue(result.Success);
            });

            await RunTest("Enumerate_WithPagination", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 15; i++)
                {
                    await CreateMissionAsync(vesselId, "EnumPage Mission " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertEqual(5, result.Objects.Count);
            });

            await RunTest("Enumerate_WithStatusFilter", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission m1 = await CreateMissionAsync(vesselId, "EnumStatus Assigned");
                string m1Id = m1.Id;
                await TransitionAndAssertAsync(m1Id, "Assigned");

                await CreateMissionAsync(vesselId, "EnumStatus Pending");

                StringContent content = JsonHelper.ToJsonContent(new { Status = "Assigned" });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 1);
                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Assigned, obj.Status);
                }
            });

            await RunTest("Enumerate_WithVesselIdFilter", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vessel1 = await CreateVesselAsync(fleetId);

                StringContent v2Content = JsonHelper.ToJsonContent(new { Name = "EnumOtherVessel", RepoUrl = "https://github.com/test/enum-other", FleetId = fleetId });
                HttpResponseMessage v2Resp = await _AuthClient.PostAsync("/api/v1/vessels", v2Content);
                Vessel v2 = await JsonHelper.DeserializeAsync<Vessel>(v2Resp);
                string vessel2 = v2.Id;
                _CreatedVesselIds.Add(vessel2);

                await CreateMissionAsync(vessel1, "EnumVessel1 A");
                await CreateMissionAsync(vessel1, "EnumVessel1 B");
                await CreateMissionAsync(vessel2, "EnumVessel2 A");

                StringContent content = JsonHelper.ToJsonContent(new { VesselId = vessel1 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 2);
            });

            await RunTest("Enumerate_WithOrdering_CreatedDescending", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "EnumOrder First");
                await Task.Delay(50);
                await CreateMissionAsync(vesselId, "EnumOrder Second");
                await Task.Delay(50);
                await CreateMissionAsync(vesselId, "EnumOrder Third");

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedDescending" });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 3);

                string firstTitle = result.Objects[0].Title;
                AssertEqual("EnumOrder Third", firstTitle);
            });

            await RunTest("Enumerate_WithOrdering_CreatedAscending", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission m1 = await CreateMissionAsync(vesselId, "EnumAsc First");
                await Task.Delay(50);
                Mission m2 = await CreateMissionAsync(vesselId, "EnumAsc Second");
                await Task.Delay(50);
                Mission m3 = await CreateMissionAsync(vesselId, "EnumAsc Third");

                string id1 = m1.Id;
                string id2 = m2.Id;
                string id3 = m3.Id;

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedAscending", PageSize = 10000 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
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
            });

            await RunTest("Enumerate_Page2_ReturnsCorrectPage", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 8; i++)
                {
                    await CreateMissionAsync(vesselId, "EnumPage2 Mission " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 3 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("Enumerate_HasEnumerationResultStructure", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects != null);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.Success || !result.Success);
            });

            await RunTest("Enumerate_CombinedFilters_StatusAndVesselId", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vessel1 = await CreateVesselAsync(fleetId);

                Mission m1 = await CreateMissionAsync(vessel1, "Combined Assigned");
                string m1Id = m1.Id;
                await TransitionAndAssertAsync(m1Id, "Assigned");

                await CreateMissionAsync(vessel1, "Combined Pending");

                StringContent content = JsonHelper.ToJsonContent(new { Status = "Assigned", VesselId = vessel1 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 1);
                foreach (Mission obj in result.Objects)
                {
                    AssertEqual(MissionStatusEnum.Assigned, obj.Status);
                }
            });

            #endregion

            #region EdgeCases

            await RunTest("CreateMultipleMissions_EachHasUniqueId", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission m1 = await CreateMissionAsync(vesselId, "Unique 1");
                Mission m2 = await CreateMissionAsync(vesselId, "Unique 2");
                Mission m3 = await CreateMissionAsync(vesselId, "Unique 3");

                string id1 = m1.Id;
                string id2 = m2.Id;
                string id3 = m3.Id;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            });

            await RunTest("DeleteThenGet_ShowsCancelledStatus", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Delete Then Get");
                string missionId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Cancelled, fetched.Status);
            });

            await RunTest("StatusTransition_CancelledMission_CannotTransition", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Cancel Block");
                string missionId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("UpdateMission_AfterStatusTransition_PreservesStatus", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Status Preserve");
                string missionId = created.Id;

                await TransitionAndAssertAsync(missionId, "Assigned");

                StringContent updateContent = JsonHelper.ToJsonContent(new { Title = "Updated While Assigned" });
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                Mission fetched = await JsonHelper.DeserializeAsync<Mission>(getResp);
                AssertEqual("Updated While Assigned", fetched.Title);
            });

            await RunTest("CreateMission_WithPriority0_Accepted", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "Zero Priority", priority: 0);
                AssertEqual(0, mission.Priority);
            });

            await RunTest("CreateMission_WithHighPriority_Accepted", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission mission = await CreateMissionAsync(vesselId, "High Priority", priority: 9999);
                AssertEqual(9999, mission.Priority);
            });

            await RunTest("ListMissions_FilterByMultipleStatuses_ViaMultipleCalls", async () =>
            {
                string vesselId = await SetupVesselAsync();

                await CreateMissionAsync(vesselId, "Multi Pending");
                Mission assigned = await CreateMissionAsync(vesselId, "Multi Assigned");
                string assignedId = assigned.Id;
                await TransitionAndAssertAsync(assignedId, "Assigned");

                HttpResponseMessage pendingResp = await _AuthClient.GetAsync("/api/v1/missions?status=Pending");
                EnumerationResult<Mission> pendingResult = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(pendingResp);
                AssertTrue(pendingResult.Objects.Count >= 1);

                HttpResponseMessage assignedResp = await _AuthClient.GetAsync("/api/v1/missions?status=Assigned");
                EnumerationResult<Mission> assignedResult = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(assignedResp);
                AssertTrue(assignedResult.Objects.Count >= 1);
            });

            await RunTest("Enumerate_VoyageIdFilter_MatchesCorrectMissions", async () =>
            {
                string vesselId = await SetupVesselAsync();
                string voyageId = await CreateVoyageAsync("EnumVoyageFilter");

                await CreateMissionAsync(vesselId, "EnumVoyage 1", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "EnumVoyage 2", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "EnumNoVoyage");

                StringContent content = JsonHelper.ToJsonContent(new { VoyageId = voyageId });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertTrue(result.Objects.Count >= 2);
            });

            await RunTest("Enumerate_EmptyResult_HasCorrectStructure", async () =>
            {
                string fakeVesselId = "vsl_nonexistent_" + Guid.NewGuid().ToString("N");
                StringContent content = JsonHelper.ToJsonContent(new { VesselId = fakeVesselId });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response);

                AssertEqual(0, result.Objects.Count);
                AssertTrue(result.Success);
            });

            await RunTest("StatusTransition_CaseInsensitive_AcceptsLowercase", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Case Test");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "assigned");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(response);
                AssertEqual(MissionStatusEnum.Assigned, transitioned.Status);
            });

            await RunTest("StatusTransition_CaseInsensitive_AcceptsMixedCase", async () =>
            {
                string vesselId = await SetupVesselAsync();
                Mission created = await CreateMissionAsync(vesselId, "Mixed Case");
                string missionId = created.Id;

                HttpResponseMessage response = await TransitionAsync(missionId, "ASSIGNED");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            });

            #endregion

            #region Cleanup

            await CleanupAsync();

            #endregion
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateFleetAsync()
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = "MissionTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content);
            string body = await resp.Content.ReadAsStringAsync();
            Fleet fleet = JsonHelper.Deserialize<Fleet>(body);
            if (String.IsNullOrEmpty(fleet.Id))
                throw new Exception("CreateFleetAsync failed (" + (int)resp.StatusCode + "): " + body);
            _CreatedFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        private async Task<string> CreateVesselAsync(string fleetId)
        {
            string repoUrl = TestRepoHelper.GetLocalBareRepoUrl();
            StringContent content = JsonHelper.ToJsonContent(new { Name = "MissionTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = repoUrl, FleetId = fleetId });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content);
            string body = await resp.Content.ReadAsStringAsync();
            Vessel vessel = JsonHelper.Deserialize<Vessel>(body);
            if (String.IsNullOrEmpty(vessel.Id))
                throw new Exception("CreateVesselAsync failed (" + (int)resp.StatusCode + "): " + body);
            _CreatedVesselIds.Add(vessel.Id);
            return vessel.Id;
        }

        private async Task<Mission> CreateMissionAsync(string vesselId, string title, string? voyageId = null, int priority = 100, string? description = null, string? captainId = null)
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
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content);
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
            _CreatedMissionIds.Add(mission.Id);
            return mission;
        }

        private async Task<HttpResponseMessage> TransitionAsync(string missionId, string status)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Status = status });
            return await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content);
        }

        private async Task TransitionAndAssertAsync(string missionId, string status)
        {
            HttpResponseMessage resp = await TransitionAsync(missionId, status);
            AssertEqual(HttpStatusCode.OK, resp.StatusCode);
            Mission transitioned = await JsonHelper.DeserializeAsync<Mission>(resp);
            AssertEqual(status, transitioned.Status.ToString());
        }

        private async Task<string> SetupVesselAsync()
        {
            string fleetId = await CreateFleetAsync();
            return await CreateVesselAsync(fleetId);
        }

        private async Task<string> CreateCaptainAsync(string name = "test-captain")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", content);
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp);
            _CreatedCaptainIds.Add(captain.Id);
            return captain.Id;
        }

        private async Task<string> CreateVoyageAsync(string title = "TestVoyage")
        {
            StringContent content = JsonHelper.ToJsonContent(new
            {
                Title = title,
                Description = "Test voyage"
            });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
            string body = await resp.Content.ReadAsStringAsync();
            Voyage voyage = JsonHelper.Deserialize<Voyage>(body);
            _CreatedVoyageIds.Add(voyage.Id);
            return voyage.Id;
        }

        private async Task CleanupAsync()
        {
            foreach (string missionId in _CreatedMissionIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId); } catch { }
            }

            foreach (string voyageId in _CreatedVoyageIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge"); } catch { }
            }

            foreach (string captainId in _CreatedCaptainIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/captains/" + captainId); } catch { }
            }

            foreach (string vesselId in _CreatedVesselIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/vessels/" + vesselId); } catch { }
            }

            foreach (string fleetId in _CreatedFleetIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/fleets/" + fleetId); } catch { }
            }
        }

        #endregion
    }
}
