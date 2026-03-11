namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for event routes: listing, filtering, pagination, and enumeration.
    /// </summary>
    public class EventTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Event Routes";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new EventTests suite with shared HTTP clients.
        /// </summary>
        public EventTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateFleetAsync()
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = "EventTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateVesselAsync(string fleetId)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = "EventTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateCaptainAsync(string name = "event-captain")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<JsonElement> CreateMissionAsync(string title, string? vesselId = null, string? voyageId = null)
        {
            object payload;
            if (vesselId != null && voyageId != null)
                payload = new { Title = title, Description = "Test mission", VesselId = vesselId, VoyageId = voyageId };
            else if (vesselId != null)
                payload = new { Title = title, Description = "Test mission", VesselId = vesselId };
            else if (voyageId != null)
                payload = new { Title = title, Description = "Test mission", VoyageId = voyageId };
            else
                payload = new { Title = title, Description = "Test mission" };

            StringContent content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement;
        }

        private async Task TransitionAsync(string missionId, string status)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Status = status }),
                Encoding.UTF8, "application/json");
            await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content).ConfigureAwait(false);
        }

        private async Task AssignCaptainToMissionAsync(string missionId, string captainId)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { CaptainId = captainId }),
                Encoding.UTF8, "application/json");
            await _AuthClient.PutAsync("/api/v1/missions/" + missionId, content).ConfigureAwait(false);
        }

        private async Task<string> CreateVoyageAsync(string vesselId)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    Title = "EventVoyage",
                    VesselId = vesselId,
                    Missions = new[]
                    {
                        new { Title = "VoyageMission1", Description = "desc" }
                    }
                }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages", content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region List-Empty-Tests

            await RunTest("ListEvents_Empty_ReturnsEmptyArray", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(JsonValueKind.Array, doc.RootElement.GetProperty("Objects").ValueKind);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Empty_ReturnsCorrectEnumerationStructure", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.GetProperty("Success").GetBoolean());
                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
                AssertTrue(root.GetProperty("PageSize").GetInt32() > 0);
                AssertTrue(root.GetProperty("TotalRecords").GetInt64() >= 0);
            }).ConfigureAwait(false);

            #endregion

            #region Events-Generated-Via-Transitions

            await RunTest("ListEvents_AfterStatusTransition_ContainsEvents", async () =>
            {
                JsonElement mission = await CreateMissionAsync("TransitionEvent").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_AfterTransition_EventHasCorrectType", async () =>
            {
                JsonElement mission = await CreateMissionAsync("TypeCheck").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                bool hasStatusChanged = false;
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    if (evt.GetProperty("EventType").GetString() == "mission.status_changed")
                    {
                        hasStatusChanged = true;
                        break;
                    }
                }
                AssertTrue(hasStatusChanged);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_AfterTransition_EventHasCorrectFields", async () =>
            {
                JsonElement mission = await CreateMissionAsync("FieldCheck").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                JsonElement? statusEvent = null;
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    if (evt.GetProperty("EventType").GetString() == "mission.status_changed"
                        && evt.GetProperty("MissionId").GetString() == missionId)
                    {
                        statusEvent = evt;
                        break;
                    }
                }

                AssertNotNull(statusEvent);
                AssertStartsWith("evt_", statusEvent!.Value.GetProperty("Id").GetString()!);
                AssertEqual("mission", statusEvent.Value.GetProperty("EntityType").GetString());
                AssertEqual(missionId, statusEvent.Value.GetProperty("EntityId").GetString());
                AssertEqual(missionId, statusEvent.Value.GetProperty("MissionId").GetString());
                AssertTrue(statusEvent.Value.TryGetProperty("Message", out _));
                AssertTrue(statusEvent.Value.TryGetProperty("CreatedUtc", out _));
            }).ConfigureAwait(false);

            await RunTest("ListEvents_MultipleTransitions_GenerateMultipleEvents", async () =>
            {
                JsonElement mission = await CreateMissionAsync("MultiTransition").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                await TransitionAsync(missionId, "InProgress").ConfigureAwait(false);
                await TransitionAsync(missionId, "Testing").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                int statusChangedCount = 0;
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    if (evt.GetProperty("EventType").GetString() == "mission.status_changed"
                        && evt.GetProperty("MissionId").GetString() == missionId)
                    {
                        statusChangedCount++;
                    }
                }
                AssertEqual(3, statusChangedCount);
            }).ConfigureAwait(false);

            #endregion

            #region Pagination-Tests

            await RunTest("ListEvents_Pagination_MultiPage_VerifyCounts", async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    JsonElement mission = await CreateMissionAsync("PageTest-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=5&pageNumber=1").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                AssertTrue(root.GetProperty("TotalRecords").GetInt64() >= 12);
                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
                AssertEqual(5, root.GetProperty("PageSize").GetInt32());
                AssertTrue(root.GetProperty("Success").GetBoolean());
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_Page2", async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    JsonElement mission = await CreateMissionAsync("Page2Test-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=5&pageNumber=2").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, root.GetProperty("PageNumber").GetInt32());
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_LastPage_PartialResults", async () =>
            {
                for (int i = 0; i < 7; i++)
                {
                    JsonElement mission = await CreateMissionAsync("LastPage-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                long totalRecords;
                {
                    HttpResponseMessage countResp = await _AuthClient.GetAsync("/api/v1/events?pageSize=1000").ConfigureAwait(false);
                    string countBody = await countResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    totalRecords = JsonDocument.Parse(countBody).RootElement.GetProperty("TotalRecords").GetInt64();
                }

                int pageSize = 5;
                int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=" + pageSize + "&pageNumber=" + totalPages).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                int expectedOnLastPage = (int)(totalRecords % pageSize);
                if (expectedOnLastPage == 0) expectedOnLastPage = pageSize;

                AssertEqual(expectedOnLastPage, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(totalPages, root.GetProperty("PageNumber").GetInt32());
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_BeyondLastPage_ReturnsEmpty", async () =>
            {
                JsonElement mission = await CreateMissionAsync("BeyondPage").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=10&pageNumber=100").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 1);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_FirstPageHasCorrectEventIds", async () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    JsonElement mission = await CreateMissionAsync("IdCheck-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=3&pageNumber=1").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(3, objects.GetArrayLength());
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertStartsWith("evt_", evt.GetProperty("Id").GetString()!);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Ordering_DefaultIsCreatedDescending", async () =>
            {
                JsonElement m1 = await CreateMissionAsync("Order-1").ConfigureAwait(false);
                await TransitionAsync(m1.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                JsonElement m2 = await CreateMissionAsync("Order-2").ConfigureAwait(false);
                await TransitionAsync(m2.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                JsonElement m3 = await CreateMissionAsync("Order-3").ConfigureAwait(false);
                await TransitionAsync(m3.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);

                DateTime first = DateTime.Parse(objects[0].GetProperty("CreatedUtc").GetString()!);
                DateTime last = DateTime.Parse(objects[objects.GetArrayLength() - 1].GetProperty("CreatedUtc").GetString()!);
                AssertTrue(first >= last);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-Type-Tests

            await RunTest("ListEvents_FilterByType_MissionStatusChanged", async () =>
            {
                JsonElement mission = await CreateMissionAsync("TypeFilter").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?type=mission.status_changed").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual("mission.status_changed", evt.GetProperty("EventType").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByType_NoMatches_ReturnsEmpty", async () =>
            {
                JsonElement mission = await CreateMissionAsync("NoTypeMatch").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?type=nonexistent.type").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-MissionId-Tests

            await RunTest("ListEvents_FilterByMissionId", async () =>
            {
                JsonElement mission1 = await CreateMissionAsync("MissionFilter-1").ConfigureAwait(false);
                string missionId1 = mission1.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId1, "Assigned").ConfigureAwait(false);

                JsonElement mission2 = await CreateMissionAsync("MissionFilter-2").ConfigureAwait(false);
                string missionId2 = mission2.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId2, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?missionId=" + missionId1).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual(missionId1, evt.GetProperty("MissionId").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByMissionId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?missionId=msn_nonexistent").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-VesselId-Tests

            await RunTest("ListEvents_FilterByVesselId", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);

                JsonElement mission = await CreateMissionAsync("VesselFilter", vesselId: vesselId).ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?vesselId=" + vesselId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual(vesselId, evt.GetProperty("VesselId").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByVesselId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?vesselId=vsl_nonexistent").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-CaptainId-Tests

            await RunTest("ListEvents_FilterByCaptainId", async () =>
            {
                string captainId = await CreateCaptainAsync("filter-captain").ConfigureAwait(false);

                JsonElement mission = await CreateMissionAsync("CaptainFilter").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;

                await AssignCaptainToMissionAsync(missionId, captainId).ConfigureAwait(false);
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?captainId=" + captainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual(captainId, evt.GetProperty("CaptainId").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByCaptainId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?captainId=cpt_nonexistent").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-VoyageId-Tests

            await RunTest("ListEvents_FilterByVoyageId", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);
                string voyageId = await CreateVoyageAsync(vesselId).ConfigureAwait(false);

                JsonElement mission = await CreateMissionAsync("VoyageFilter", voyageId: voyageId).ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?voyageId=" + voyageId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertTrue(evt.TryGetProperty("VoyageId", out JsonElement voyageIdElem));
                    AssertEqual(voyageId, voyageIdElem.GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByVoyageId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?voyageId=vyg_nonexistent").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-Limit-Tests

            await RunTest("ListEvents_FilterByLimit", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    JsonElement mission = await CreateMissionAsync("LimitTest-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?limit=3").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(3, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, root.GetProperty("PageSize").GetInt32());
            }).ConfigureAwait(false);

            await RunTest("ListEvents_WithLimit_RespectsLimit", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?limit=10").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            #endregion

            #region Combined-Filters-Tests

            await RunTest("ListEvents_CombinedFilters_TypeAndMissionId", async () =>
            {
                JsonElement mission = await CreateMissionAsync("CombinedFilter").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/events?type=mission.status_changed&missionId=" + missionId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual("mission.status_changed", evt.GetProperty("EventType").GetString());
                    AssertEqual(missionId, evt.GetProperty("MissionId").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_CombinedFilters_TypeAndVesselId", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);

                JsonElement mission = await CreateMissionAsync("CombinedVessel", vesselId: vesselId).ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/events?type=mission.status_changed&vesselId=" + vesselId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual("mission.status_changed", evt.GetProperty("EventType").GetString());
                    AssertEqual(vesselId, evt.GetProperty("VesselId").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_CombinedFilters_LimitAndType", async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    JsonElement mission = await CreateMissionAsync("LimitType-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/events?type=mission.status_changed&limit=2").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(2, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            #endregion

            #region Enumerate-Tests

            await RunTest("EnumerateEvents_Default_ReturnsEnumerationResult", async () =>
            {
                StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.GetProperty("Success").GetBoolean());
                AssertTrue(root.TryGetProperty("Objects", out _));
                AssertTrue(root.TryGetProperty("PageNumber", out _));
                AssertTrue(root.TryGetProperty("PageSize", out _));
                AssertTrue(root.TryGetProperty("TotalPages", out _));
                AssertTrue(root.TryGetProperty("TotalRecords", out _));
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_EmptyDatabase_ReturnsZeroRecords", async () =>
            {
                StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                // Note: database may not be empty at this point due to previous tests
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 0);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithPageSizeAndPageNumber", async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    JsonElement mission = await CreateMissionAsync("EnumPage-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 5, PageNumber = 2 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, root.GetProperty("PageNumber").GetInt32());
                AssertEqual(5, root.GetProperty("PageSize").GetInt32());
                AssertTrue(root.GetProperty("TotalRecords").GetInt64() >= 12);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithEventTypeFilter", async () =>
            {
                JsonElement mission = await CreateMissionAsync("EnumType").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { EventType = "mission.status_changed" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual("mission.status_changed", evt.GetProperty("EventType").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithEventTypeFilter_NoMatches", async () =>
            {
                JsonElement mission = await CreateMissionAsync("EnumNoMatch").ConfigureAwait(false);
                string missionId = mission.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { EventType = "nonexistent.type" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_Ordering_CreatedDescending", async () =>
            {
                JsonElement m1 = await CreateMissionAsync("EnumOrd-1").ConfigureAwait(false);
                await TransitionAsync(m1.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                JsonElement m2 = await CreateMissionAsync("EnumOrd-2").ConfigureAwait(false);
                await TransitionAsync(m2.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                JsonElement m3 = await CreateMissionAsync("EnumOrd-3").ConfigureAwait(false);
                await TransitionAsync(m3.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedDescending" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);

                DateTime first = DateTime.Parse(objects[0].GetProperty("CreatedUtc").GetString()!);
                DateTime last = DateTime.Parse(objects[objects.GetArrayLength() - 1].GetProperty("CreatedUtc").GetString()!);
                AssertTrue(first >= last);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_Ordering_CreatedAscending", async () =>
            {
                JsonElement m1 = await CreateMissionAsync("EnumAsc-1").ConfigureAwait(false);
                await TransitionAsync(m1.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                JsonElement m2 = await CreateMissionAsync("EnumAsc-2").ConfigureAwait(false);
                await TransitionAsync(m2.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                JsonElement m3 = await CreateMissionAsync("EnumAsc-3").ConfigureAwait(false);
                await TransitionAsync(m3.GetProperty("Id").GetString()!, "Assigned").ConfigureAwait(false);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedAscending" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);

                DateTime first = DateTime.Parse(objects[0].GetProperty("CreatedUtc").GetString()!);
                DateTime last = DateTime.Parse(objects[objects.GetArrayLength() - 1].GetProperty("CreatedUtc").GetString()!);
                AssertTrue(first <= last);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithMissionIdFilter", async () =>
            {
                JsonElement mission1 = await CreateMissionAsync("EnumMsn-1").ConfigureAwait(false);
                string missionId1 = mission1.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId1, "Assigned").ConfigureAwait(false);

                JsonElement mission2 = await CreateMissionAsync("EnumMsn-2").ConfigureAwait(false);
                string missionId2 = mission2.GetProperty("Id").GetString()!;
                await TransitionAsync(missionId2, "Assigned").ConfigureAwait(false);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { MissionId = missionId1 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 1);
                foreach (JsonElement evt in objects.EnumerateArray())
                {
                    AssertEqual(missionId1, evt.GetProperty("MissionId").GetString());
                }
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_QuerystringOverrides_PageSize", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    JsonElement mission = await CreateMissionAsync("EnumQS-" + i).ConfigureAwait(false);
                    string missionId = mission.GetProperty("Id").GetString()!;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate?pageSize=3", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(3, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, doc.RootElement.GetProperty("PageSize").GetInt32());
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}
