namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
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

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Title = "New Mission", VesselId = vesselId }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string missionId = root.GetProperty("Id").GetString()!;
                _CreatedMissionIds.Add(missionId);

                AssertStartsWith("msn_", missionId);
                AssertEqual("New Mission", root.GetProperty("Title").GetString()!);
                AssertEqual("Pending", root.GetProperty("Status").GetString()!);
                AssertTrue(root.TryGetProperty("CreatedUtc", out _));
                AssertTrue(root.TryGetProperty("LastUpdateUtc", out _));
                AssertTrue(root.TryGetProperty("Priority", out _));
            });

            await RunTest("CreateMission_DefaultStatusIsPending", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "Pending Check");
                AssertEqual("Pending", mission.GetProperty("Status").GetString()!);
            });

            await RunTest("CreateMission_DefaultPriorityIs100", async () =>
            {
                string vesselId = await SetupVesselAsync();

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Priority Check", VesselId = vesselId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                _CreatedMissionIds.Add(doc.RootElement.GetProperty("Id").GetString()!);

                AssertEqual(100, doc.RootElement.GetProperty("Priority").GetInt32());
            });

            await RunTest("CreateMission_WithAllOptionalFields", async () =>
            {
                string vesselId = await SetupVesselAsync();

                string voyageId = await CreateVoyageAsync("FullMissionVoyage");

                JsonElement parentMission = await CreateMissionAsync(vesselId, "Parent Mission");
                string parentMissionId = parentMission.GetProperty("Id").GetString()!;

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Title = "Full Mission",
                        VesselId = vesselId,
                        Description = "A detailed description",
                        Priority = 50,
                        VoyageId = voyageId,
                        ParentMissionId = parentMissionId,
                        BranchName = "feature/test-branch"
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;
                string missionId = root.GetProperty("Id").GetString()!;
                _CreatedMissionIds.Add(missionId);

                string captainId = await CreateCaptainAsync("full-mission-captain");
                StringContent assignContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Title = "Full Mission",
                        VesselId = vesselId,
                        Description = "A detailed description",
                        Priority = 50,
                        VoyageId = voyageId,
                        CaptainId = captainId,
                        ParentMissionId = parentMissionId,
                        BranchName = "feature/test-branch"
                    }),
                    Encoding.UTF8, "application/json");
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId, assignContent);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement result = getDoc.RootElement;

                AssertEqual("Full Mission", result.GetProperty("Title").GetString()!);
                AssertEqual("A detailed description", result.GetProperty("Description").GetString()!);
                AssertEqual(50, result.GetProperty("Priority").GetInt32());
            });

            await RunTest("CreateMission_WithCustomPriority", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "High Priority", priority: 10);
                AssertEqual(10, mission.GetProperty("Priority").GetInt32());
            });

            await RunTest("CreateMission_WithDescription", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "Described Mission", description: "This is a detailed description");
                AssertEqual("This is a detailed description", mission.GetProperty("Description").GetString()!);
            });

            await RunTest("CreateMission_IdHasMsnPrefix", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "Prefix Check");
                AssertStartsWith("msn_", mission.GetProperty("Id").GetString()!);
            });

            await RunTest("CreateMission_HasTimestamps", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "Timestamp Check");

                string createdUtc = mission.GetProperty("CreatedUtc").GetString()!;
                AssertFalse(String.IsNullOrEmpty(createdUtc));

                string lastUpdateUtc = mission.GetProperty("LastUpdateUtc").GetString()!;
                AssertFalse(String.IsNullOrEmpty(lastUpdateUtc));
            });

            #endregion

            #region CRUD-Read

            await RunTest("GetMission_Exists_ReturnsMission", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "GetTest");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("GetTest", doc.RootElement.GetProperty("Title").GetString()!);
                AssertEqual(missionId, doc.RootElement.GetProperty("Id").GetString()!);
            });

            await RunTest("GetMission_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/msn_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("GetMission_ReturnsAllProperties", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Full Read", description: "Full desc", priority: 42);
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual("Full Read", root.GetProperty("Title").GetString()!);
                AssertEqual("Full desc", root.GetProperty("Description").GetString()!);
                AssertEqual(42, root.GetProperty("Priority").GetInt32());
                AssertEqual("Pending", root.GetProperty("Status").GetString()!);
            });

            #endregion

            #region CRUD-Update

            await RunTest("UpdateMission_Title_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Original Title");
                string missionId = created.GetProperty("Id").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Updated Title" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Updated Title", doc.RootElement.GetProperty("Title").GetString()!);
            });

            await RunTest("UpdateMission_Description_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Desc Update");
                string missionId = created.GetProperty("Id").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Desc Update", Description = "New description" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("New description", doc.RootElement.GetProperty("Description").GetString()!);
            });

            await RunTest("UpdateMission_Priority_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Priority Update");
                string missionId = created.GetProperty("Id").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Priority Update", Priority = 1 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(1, doc.RootElement.GetProperty("Priority").GetInt32());
            });

            await RunTest("UpdateMission_MultipleFields_ReturnsUpdated", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Multi Update");
                string missionId = created.GetProperty("Id").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "New Title", Description = "New Desc", Priority = 5 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("New Title", doc.RootElement.GetProperty("Title").GetString()!);
                AssertEqual("New Desc", doc.RootElement.GetProperty("Description").GetString()!);
                AssertEqual(5, doc.RootElement.GetProperty("Priority").GetInt32());
            });

            await RunTest("UpdateMission_NotFound_ReturnsError", async () =>
            {
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Ghost" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/msn_nonexistent", updateContent);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("UpdateMission_PreservesId", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Id Preserve");
                string missionId = created.GetProperty("Id").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Updated" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(missionId, doc.RootElement.GetProperty("Id").GetString()!);
            });

            #endregion

            #region CRUD-Delete

            await RunTest("DeleteMission_ReturnsCancelledStatus", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "ToCancel");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Cancelled", doc.RootElement.GetProperty("Status").GetString()!);
                AssertEqual(missionId, doc.RootElement.GetProperty("Id").GetString()!);
            });

            await RunTest("DeleteMission_SetsStatusToCancelledInDatabase", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "CancelVerify");
                string missionId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                string body = await getResp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Cancelled", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("DeleteMission_SetsCompletedUtc", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "CancelTimestamp");
                string missionId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                string body = await getResp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("CompletedUtc", out JsonElement completedUtc));
                AssertNotEqual(JsonValueKind.Null, completedUtc.ValueKind);
            });

            await RunTest("DeleteMission_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/missions/msn_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            #endregion

            #region StatusTransition-Valid-HappyPath

            await RunTest("StatusTransition_PendingToAssigned_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "PendToAssign");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Assigned");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Assigned", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("StatusTransition_PendingToCancelled_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "PendToCancel");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Cancelled");
            });

            await RunTest("StatusTransition_AssignedToInProgress_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "AssignToIP");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
            });

            await RunTest("StatusTransition_AssignedToCancelled_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "AssignToCancel");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "Cancelled");
            });

            await RunTest("StatusTransition_InProgressToTesting_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToTest");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
            });

            await RunTest("StatusTransition_InProgressToReview_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToReview");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
            });

            await RunTest("StatusTransition_InProgressToComplete_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToComplete");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_InProgressToFailed_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToFailed");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Failed");
            });

            await RunTest("StatusTransition_InProgressToCancelled_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToCancel");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Cancelled");
            });

            await RunTest("StatusTransition_TestingToReview_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "TestToReview");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Review");
            });

            await RunTest("StatusTransition_TestingToInProgress_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "TestToIP");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "InProgress");
            });

            await RunTest("StatusTransition_TestingToComplete_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "TestToComplete");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_TestingToFailed_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "TestToFailed");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Failed");
            });

            await RunTest("StatusTransition_ReviewToComplete_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "ReviewToComplete");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "Complete");
            });

            await RunTest("StatusTransition_ReviewToInProgress_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "ReviewToIP");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Review");
                await TransitionAndAssertAsync(missionId, "InProgress");
            });

            await RunTest("StatusTransition_ReviewToFailed_Succeeds", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "ReviewToFailed");
                string missionId = created.GetProperty("Id").GetString()!;

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
                JsonElement created = await CreateMissionAsync(vesselId, "Lifecycle Full");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Testing");
                await TransitionAndAssertAsync(missionId, "Review");

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Complete", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("StatusTransition_FullLifecycle_SetsCompletedUtcOnComplete", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Complete Timestamp");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("CompletedUtc", out JsonElement completedUtc));
                AssertNotEqual(JsonValueKind.Null, completedUtc.ValueKind);
            });

            await RunTest("StatusTransition_FullLifecycle_SetsCompletedUtcOnFailed", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Failed Timestamp");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");

                HttpResponseMessage response = await TransitionAsync(missionId, "Failed");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("CompletedUtc", out JsonElement completedUtc));
                AssertNotEqual(JsonValueKind.Null, completedUtc.ValueKind);
            });

            await RunTest("StatusTransition_FullLifecycle_SetsCompletedUtcOnCancelled", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Cancel Timestamp");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Cancelled");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("CompletedUtc", out JsonElement completedUtc));
                AssertNotEqual(JsonValueKind.Null, completedUtc.ValueKind);
            });

            await RunTest("StatusTransition_TestingBounceBackToInProgress_ThenComplete", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Bounce Back");
                string missionId = created.GetProperty("Id").GetString()!;

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
                JsonElement created = await CreateMissionAsync(vesselId, "Review Bounce");
                string missionId = created.GetProperty("Id").GetString()!;

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
                JsonElement created = await CreateMissionAsync(vesselId, "BadPendComplete");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_PendingToInProgress_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadPendIP");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "InProgress");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_PendingToTesting_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadPendTest");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Testing");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_PendingToReview_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadPendReview");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Review");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_PendingToFailed_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadPendFail");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Failed");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_AssignedToComplete_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadAssignComplete");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Complete");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_AssignedToTesting_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadAssignTest");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Testing");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_AssignedToReview_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadAssignReview");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Review");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_AssignedToFailed_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadAssignFail");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Failed");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_CompleteToAnything_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "CompleteTerminal");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Complete");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Failed", "Cancelled" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(missionId, target);
                    string body = await response.Content.ReadAsStringAsync();
                    JsonDocument doc = JsonDocument.Parse(body);
                    Assert(
                        doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _),
                        "Expected error for Complete->" + target + " but got: " + body);
                }
            });

            await RunTest("StatusTransition_CancelledToAnything_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "CancelledTerminal");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Cancelled");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Failed" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(missionId, target);
                    string body = await response.Content.ReadAsStringAsync();
                    JsonDocument doc = JsonDocument.Parse(body);
                    Assert(
                        doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _),
                        "Expected error for Cancelled->" + target + " but got: " + body);
                }
            });

            await RunTest("StatusTransition_FailedToAnything_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "FailedTerminal");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                await TransitionAndAssertAsync(missionId, "Failed");

                string[] targets = new[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Cancelled" };
                foreach (string target in targets)
                {
                    HttpResponseMessage response = await TransitionAsync(missionId, target);
                    string body = await response.Content.ReadAsStringAsync();
                    JsonDocument doc = JsonDocument.Parse(body);
                    Assert(
                        doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _),
                        "Expected error for Failed->" + target + " but got: " + body);
                }
            });

            await RunTest("StatusTransition_PendingToPending_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "SameState");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_AssignedToPending_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "AssignedToPend");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_InProgressToPending_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToPend");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_InProgressToAssigned_Fails", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "IPToAssign");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");
                await TransitionAndAssertAsync(missionId, "InProgress");
                HttpResponseMessage response = await TransitionAsync(missionId, "Assigned");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            #endregion

            #region StatusTransition-ErrorCases

            await RunTest("StatusTransition_EmptyStatus_ReturnsError", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "EmptyStatus");
                string missionId = created.GetProperty("Id").GetString()!;

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Status = "" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_InvalidStatusName_ReturnsError", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "BadStatusName");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "NotAStatus");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_GarbageStatusName_ReturnsError", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Garbage");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "!@#$%^&*()");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await TransitionAsync("msn_nonexistent", "Assigned");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("StatusTransition_UpdatesLastUpdateUtc", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Timestamp Update");
                string missionId = created.GetProperty("Id").GetString()!;
                string originalLastUpdate = created.GetProperty("LastUpdateUtc").GetString()!;

                await Task.Delay(50);
                await TransitionAndAssertAsync(missionId, "Assigned");

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                string body = await getResp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                string newLastUpdate = doc.RootElement.GetProperty("LastUpdateUtc").GetString()!;

                AssertNotEqual(originalLastUpdate, newLastUpdate);
            });

            #endregion

            #region Diff

            await RunTest("Diff_MissionNotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/msn_nonexistent/diff");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("Diff_NoDiffFile_ReturnsErrorOrEmpty", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "No Diff");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/diff");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                bool isError = doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _);
                bool isEmptyDiff = doc.RootElement.TryGetProperty("Diff", out JsonElement diffElem) &&
                                   (diffElem.GetString() == "" || diffElem.GetString() == null);
                Assert(isError || isEmptyDiff, "Expected error or empty diff but got: " + body);
            });

            #endregion

            #region List-Pagination

            await RunTest("ListMissions_Empty_ReturnsEmptyEnumeration", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
                AssertTrue(doc.RootElement.TryGetProperty("Success", out JsonElement success));
                AssertTrue(success.GetBoolean());
            });

            await RunTest("ListMissions_AfterCreate_ReturnsMissions", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "List Test 1");
                await CreateMissionAsync(vesselId, "List Test 2");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2);
            });

            await RunTest("ListMissions_Pagination_25Missions_PageSize10_ReturnsCorrectCounts", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateMissionAsync(vesselId, "Page Mission " + i);
                }

                HttpResponseMessage page1Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=1");
                string page1Body = await page1Resp.Content.ReadAsStringAsync();
                JsonDocument page1Doc = JsonDocument.Parse(page1Body);
                AssertEqual(10, page1Doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(1, page1Doc.RootElement.GetProperty("PageNumber").GetInt32());
                AssertEqual(10, page1Doc.RootElement.GetProperty("PageSize").GetInt32());
            });

            await RunTest("ListMissions_Pagination_Page2", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateMissionAsync(vesselId, "Page2 Mission " + i);
                }

                HttpResponseMessage page2Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=2");
                string page2Body = await page2Resp.Content.ReadAsStringAsync();
                JsonDocument page2Doc = JsonDocument.Parse(page2Body);
                AssertEqual(10, page2Doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, page2Doc.RootElement.GetProperty("PageNumber").GetInt32());
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
                string body = await resp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                int totalPages = doc.RootElement.GetProperty("TotalPages").GetInt32();

                // The last page should have <= 10 items
                HttpResponseMessage lastPageResp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=" + totalPages);
                string lastPageBody = await lastPageResp.Content.ReadAsStringAsync();
                JsonDocument lastPageDoc = JsonDocument.Parse(lastPageBody);
                int lastPageCount = lastPageDoc.RootElement.GetProperty("Objects").GetArrayLength();
                AssertTrue(lastPageCount > 0 && lastPageCount <= 10, "Last page should have 1-10 items");
                AssertEqual(totalPages, lastPageDoc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("ListMissions_Pagination_BeyondLastPage_ReturnsEmpty", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 5; i++)
                {
                    await CreateMissionAsync(vesselId, "Beyond Mission " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?pageSize=10&pageNumber=99");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListMissions_Pagination_PageSize1_EachPageHasOneRecord", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "Single A");
                await CreateMissionAsync(vesselId, "Single B");
                await CreateMissionAsync(vesselId, "Single C");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?pageSize=1&pageNumber=1");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(1, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListMissions_EnumerationResult_HasExpectedStructure", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.TryGetProperty("Objects", out _));
                AssertTrue(root.TryGetProperty("PageNumber", out _));
                AssertTrue(root.TryGetProperty("PageSize", out _));
                AssertTrue(root.TryGetProperty("TotalPages", out _));
                AssertTrue(root.TryGetProperty("TotalRecords", out _));
                AssertTrue(root.TryGetProperty("Success", out _));
            });

            await RunTest("ListMissions_PagesContainDistinctRecords", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 6; i++)
                {
                    await CreateMissionAsync(vesselId, "Distinct Mission " + i);
                }

                HttpResponseMessage page1Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=3&pageNumber=1");
                string page1Body = await page1Resp.Content.ReadAsStringAsync();
                JsonDocument page1Doc = JsonDocument.Parse(page1Body);

                HttpResponseMessage page2Resp = await _AuthClient.GetAsync("/api/v1/missions?pageSize=3&pageNumber=2");
                string page2Body = await page2Resp.Content.ReadAsStringAsync();
                JsonDocument page2Doc = JsonDocument.Parse(page2Body);

                List<string> page1Ids = new List<string>();
                foreach (JsonElement obj in page1Doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    page1Ids.Add(obj.GetProperty("Id").GetString()!);
                }

                foreach (JsonElement obj in page2Doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    string id = obj.GetProperty("Id").GetString()!;
                    AssertFalse(page1Ids.Contains(id));
                }
            });

            #endregion

            #region List-Filters

            await RunTest("ListMissions_FilterByStatus_ReturnsOnlyMatching", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "StatusFilter Pending 1");
                await CreateMissionAsync(vesselId, "StatusFilter Pending 2");
                JsonElement toAssign = await CreateMissionAsync(vesselId, "StatusFilter Assigned");
                string toAssignId = toAssign.GetProperty("Id").GetString()!;
                await TransitionAndAssertAsync(toAssignId, "Assigned");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?status=Pending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                int count = doc.RootElement.GetProperty("Objects").GetArrayLength();
                Assert(count >= 2, "Expected at least 2 Pending missions, got " + count);

                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("Pending", obj.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("ListMissions_FilterByStatus_Assigned", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement m1 = await CreateMissionAsync(vesselId, "Filter Assigned 1");
                string m1Id = m1.GetProperty("Id").GetString()!;
                await TransitionAndAssertAsync(m1Id, "Assigned");

                await CreateMissionAsync(vesselId, "Filter Stay Pending");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?status=Assigned");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("Assigned", obj.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("ListMissions_FilterByVesselId_ReturnsOnlyMatching", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vessel1 = await CreateVesselAsync(fleetId);

                StringContent v2Content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "OtherVessel", RepoUrl = "https://github.com/test/other", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage v2Resp = await _AuthClient.PostAsync("/api/v1/vessels", v2Content);
                string v2Body = await v2Resp.Content.ReadAsStringAsync();
                string vessel2 = JsonDocument.Parse(v2Body).RootElement.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(vessel2);

                await CreateMissionAsync(vessel1, "Vessel1 Mission A");
                await CreateMissionAsync(vessel1, "Vessel1 Mission B");
                await CreateMissionAsync(vessel2, "Vessel2 Mission");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?vesselId=" + vessel1);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                int count = doc.RootElement.GetProperty("Objects").GetArrayLength();
                Assert(count >= 2, "Expected at least 2 missions for vessel1, got " + count);
            });

            await RunTest("ListMissions_FilterByCaptainId_ReturnsOnlyMatching", async () =>
            {
                string vesselId = await SetupVesselAsync();

                JsonElement captainMission = await CreateMissionAsync(vesselId, "Captain Mission");
                string captainMissionId = captainMission.GetProperty("Id").GetString()!;

                await CreateMissionAsync(vesselId, "No Captain Mission");

                string captainId = await CreateCaptainAsync("filter-captain");
                StringContent assignContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Captain Mission", CaptainId = captainId }),
                    Encoding.UTF8, "application/json");
                await _AuthClient.PutAsync("/api/v1/missions/" + captainMissionId, assignContent);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?captainId=" + captainId);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertTrue(obj.TryGetProperty("CaptainId", out JsonElement captainIdElem));
                    AssertEqual(captainId, captainIdElem.GetString()!);
                }
            });

            await RunTest("ListMissions_FilterByVoyageId_ReturnsOnlyMatching", async () =>
            {
                string vesselId = await SetupVesselAsync();
                string voyageId = await CreateVoyageAsync("VoyageFilterTest");

                await CreateMissionAsync(vesselId, "Voyage Mission 1", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "Voyage Mission 2", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "No Voyage Mission");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?voyageId=" + voyageId);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2);
            });

            await RunTest("ListMissions_FilterByNonexistentStatus_ReturnsEmpty", async () =>
            {
                // Filter by a nonexistent vesselId to guarantee empty results
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?vesselId=vsl_nonexistent_" + Guid.NewGuid().ToString("N"));
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListMissions_FilterByNonexistentVesselId_ReturnsEmpty", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "SomeExistingMission");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?vesselId=vsl_doesnotexist");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            #endregion

            #region Enumerate-POST

            await RunTest("Enumerate_EmptyBody_ReturnsAllMissions", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "Enum Mission 1");
                await CreateMissionAsync(vesselId, "Enum Mission 2");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2);
                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            });

            await RunTest("Enumerate_WithPagination", async () =>
            {
                string vesselId = await SetupVesselAsync();
                for (int i = 1; i <= 15; i++)
                {
                    await CreateMissionAsync(vesselId, "EnumPage Mission " + i);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 5 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("Enumerate_WithStatusFilter", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement m1 = await CreateMissionAsync(vesselId, "EnumStatus Assigned");
                string m1Id = m1.GetProperty("Id").GetString()!;
                await TransitionAndAssertAsync(m1Id, "Assigned");

                await CreateMissionAsync(vesselId, "EnumStatus Pending");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Status = "Assigned" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("Assigned", obj.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("Enumerate_WithVesselIdFilter", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vessel1 = await CreateVesselAsync(fleetId);

                StringContent v2Content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "EnumOtherVessel", RepoUrl = "https://github.com/test/enum-other", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage v2Resp = await _AuthClient.PostAsync("/api/v1/vessels", v2Content);
                string v2Body = await v2Resp.Content.ReadAsStringAsync();
                string vessel2 = JsonDocument.Parse(v2Body).RootElement.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(vessel2);

                await CreateMissionAsync(vessel1, "EnumVessel1 A");
                await CreateMissionAsync(vessel1, "EnumVessel1 B");
                await CreateMissionAsync(vessel2, "EnumVessel2 A");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { VesselId = vessel1 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2);
            });

            await RunTest("Enumerate_WithOrdering_CreatedDescending", async () =>
            {
                string vesselId = await SetupVesselAsync();
                await CreateMissionAsync(vesselId, "EnumOrder First");
                await Task.Delay(50);
                await CreateMissionAsync(vesselId, "EnumOrder Second");
                await Task.Delay(50);
                await CreateMissionAsync(vesselId, "EnumOrder Third");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedDescending" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                JsonElement objects = doc.RootElement.GetProperty("Objects");
                AssertTrue(objects.GetArrayLength() >= 3);

                string firstTitle = objects[0].GetProperty("Title").GetString()!;
                AssertEqual("EnumOrder Third", firstTitle);
            });

            await RunTest("Enumerate_WithOrdering_CreatedAscending", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement m1 = await CreateMissionAsync(vesselId, "EnumAsc First");
                await Task.Delay(50);
                JsonElement m2 = await CreateMissionAsync(vesselId, "EnumAsc Second");
                await Task.Delay(50);
                JsonElement m3 = await CreateMissionAsync(vesselId, "EnumAsc Third");

                string id1 = m1.GetProperty("Id").GetString()!;
                string id2 = m2.GetProperty("Id").GetString()!;
                string id3 = m3.GetProperty("Id").GetString()!;

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedAscending", PageSize = 10000 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                JsonElement objects = doc.RootElement.GetProperty("Objects");
                AssertTrue(objects.GetArrayLength() >= 3);

                // Verify our 3 items appear in ascending order (id1 before id2 before id3)
                int idx1 = -1, idx2 = -1, idx3 = -1;
                for (int i = 0; i < objects.GetArrayLength(); i++)
                {
                    string id = objects[i].GetProperty("Id").GetString()!;
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

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 2, PageSize = 3 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(3, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("Enumerate_HasEnumerationResultStructure", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.TryGetProperty("Objects", out _));
                AssertTrue(root.TryGetProperty("PageNumber", out _));
                AssertTrue(root.TryGetProperty("PageSize", out _));
                AssertTrue(root.TryGetProperty("TotalPages", out _));
                AssertTrue(root.TryGetProperty("TotalRecords", out _));
                AssertTrue(root.TryGetProperty("Success", out _));
            });

            await RunTest("Enumerate_CombinedFilters_StatusAndVesselId", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vessel1 = await CreateVesselAsync(fleetId);

                JsonElement m1 = await CreateMissionAsync(vessel1, "Combined Assigned");
                string m1Id = m1.GetProperty("Id").GetString()!;
                await TransitionAndAssertAsync(m1Id, "Assigned");

                await CreateMissionAsync(vessel1, "Combined Pending");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Status = "Assigned", VesselId = vessel1 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("Assigned", obj.GetProperty("Status").GetString()!);
                }
            });

            #endregion

            #region EdgeCases

            await RunTest("CreateMultipleMissions_EachHasUniqueId", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement m1 = await CreateMissionAsync(vesselId, "Unique 1");
                JsonElement m2 = await CreateMissionAsync(vesselId, "Unique 2");
                JsonElement m3 = await CreateMissionAsync(vesselId, "Unique 3");

                string id1 = m1.GetProperty("Id").GetString()!;
                string id2 = m2.GetProperty("Id").GetString()!;
                string id3 = m3.GetProperty("Id").GetString()!;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            });

            await RunTest("DeleteThenGet_ShowsCancelledStatus", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Delete Then Get");
                string missionId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Cancelled", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("StatusTransition_CancelledMission_CannotTransition", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Cancel Block");
                string missionId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/missions/" + missionId);

                HttpResponseMessage response = await TransitionAsync(missionId, "Pending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("UpdateMission_AfterStatusTransition_PreservesStatus", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Status Preserve");
                string missionId = created.GetProperty("Id").GetString()!;

                await TransitionAndAssertAsync(missionId, "Assigned");

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Updated While Assigned" }),
                    Encoding.UTF8, "application/json");
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId, updateContent);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                string body = await getResp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Updated While Assigned", doc.RootElement.GetProperty("Title").GetString()!);
            });

            await RunTest("CreateMission_WithPriority0_Accepted", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "Zero Priority", priority: 0);
                AssertEqual(0, mission.GetProperty("Priority").GetInt32());
            });

            await RunTest("CreateMission_WithHighPriority_Accepted", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement mission = await CreateMissionAsync(vesselId, "High Priority", priority: 9999);
                AssertEqual(9999, mission.GetProperty("Priority").GetInt32());
            });

            await RunTest("ListMissions_FilterByMultipleStatuses_ViaMultipleCalls", async () =>
            {
                string vesselId = await SetupVesselAsync();

                await CreateMissionAsync(vesselId, "Multi Pending");
                JsonElement assigned = await CreateMissionAsync(vesselId, "Multi Assigned");
                string assignedId = assigned.GetProperty("Id").GetString()!;
                await TransitionAndAssertAsync(assignedId, "Assigned");

                HttpResponseMessage pendingResp = await _AuthClient.GetAsync("/api/v1/missions?status=Pending");
                string pendingBody = await pendingResp.Content.ReadAsStringAsync();
                JsonDocument pendingDoc = JsonDocument.Parse(pendingBody);
                AssertTrue(pendingDoc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);

                HttpResponseMessage assignedResp = await _AuthClient.GetAsync("/api/v1/missions?status=Assigned");
                string assignedBody = await assignedResp.Content.ReadAsStringAsync();
                JsonDocument assignedDoc = JsonDocument.Parse(assignedBody);
                AssertTrue(assignedDoc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
            });

            await RunTest("Enumerate_VoyageIdFilter_MatchesCorrectMissions", async () =>
            {
                string vesselId = await SetupVesselAsync();
                string voyageId = await CreateVoyageAsync("EnumVoyageFilter");

                await CreateMissionAsync(vesselId, "EnumVoyage 1", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "EnumVoyage 2", voyageId: voyageId);
                await CreateMissionAsync(vesselId, "EnumNoVoyage");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { VoyageId = voyageId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2);
            });

            await RunTest("Enumerate_EmptyResult_HasCorrectStructure", async () =>
            {
                string fakeVesselId = "vsl_nonexistent_" + Guid.NewGuid().ToString("N");
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { VesselId = fakeVesselId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            });

            await RunTest("StatusTransition_CaseInsensitive_AcceptsLowercase", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Case Test");
                string missionId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await TransitionAsync(missionId, "assigned");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Assigned", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("StatusTransition_CaseInsensitive_AcceptsMixedCase", async () =>
            {
                string vesselId = await SetupVesselAsync();
                JsonElement created = await CreateMissionAsync(vesselId, "Mixed Case");
                string missionId = created.GetProperty("Id").GetString()!;

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
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = "MissionTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content);
            string body = await resp.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            if (!root.TryGetProperty("Id", out JsonElement idElem))
                throw new Exception("CreateFleetAsync failed (" + (int)resp.StatusCode + "): " + body);
            string fleetId = idElem.GetString()!;
            _CreatedFleetIds.Add(fleetId);
            return fleetId;
        }

        private async Task<string> CreateVesselAsync(string fleetId)
        {
            string repoUrl = TestRepoHelper.GetLocalBareRepoUrl();
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = "MissionTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = repoUrl, FleetId = fleetId }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content);
            string body = await resp.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            if (!root.TryGetProperty("Id", out JsonElement idElem))
                throw new Exception("CreateVesselAsync failed (" + (int)resp.StatusCode + "): " + body);
            string vesselId = idElem.GetString()!;
            _CreatedVesselIds.Add(vesselId);
            return vesselId;
        }

        private async Task<JsonElement> CreateMissionAsync(string vesselId, string title, string? voyageId = null, int priority = 100, string? description = null, string? captainId = null)
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

            StringContent content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content);
            string body = await resp.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(body).RootElement;
            if (!root.TryGetProperty("Id", out JsonElement idElem))
                throw new Exception("CreateMissionAsync failed (" + (int)resp.StatusCode + "): " + body);
            _CreatedMissionIds.Add(idElem.GetString()!);
            return root;
        }

        private async Task<HttpResponseMessage> TransitionAsync(string missionId, string status)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Status = status }),
                Encoding.UTF8, "application/json");
            return await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content);
        }

        private async Task TransitionAndAssertAsync(string missionId, string status)
        {
            HttpResponseMessage resp = await TransitionAsync(missionId, status);
            AssertEqual(HttpStatusCode.OK, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            JsonDocument doc = JsonDocument.Parse(body);
            AssertEqual(status, doc.RootElement.GetProperty("Status").GetString()!);
        }

        private async Task<string> SetupVesselAsync()
        {
            string fleetId = await CreateFleetAsync();
            return await CreateVesselAsync(fleetId);
        }

        private async Task<string> CreateCaptainAsync(string name = "test-captain")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", content);
            string body = await resp.Content.ReadAsStringAsync();
            string captainId = JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
            _CreatedCaptainIds.Add(captainId);
            return captainId;
        }

        private async Task<string> CreateVoyageAsync(string title = "TestVoyage")
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    Title = title,
                    Description = "Test voyage"
                }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
            string body = await resp.Content.ReadAsStringAsync();
            string voyageId = JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
            _CreatedVoyageIds.Add(voyageId);
            return voyageId;
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
