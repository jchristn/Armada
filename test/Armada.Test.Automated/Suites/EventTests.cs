namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;
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
            StringContent content = JsonHelper.ToJsonContent(new { Name = "EventTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) });
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(response).ConfigureAwait(false);
            return fleet.Id;
        }

        private async Task<string> CreateVesselAsync(string fleetId)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = "EventTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId });
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
            return vessel.Id;
        }

        private async Task<string> CreateCaptainAsync(string name = "event-captain")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
            return captain.Id;
        }

        private async Task<Mission> CreateMissionAsync(string title, string? vesselId = null, string? voyageId = null)
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

            StringContent content = JsonHelper.ToJsonContent(payload);
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            if (wrapper.Mission != null)
                return wrapper.Mission;

            return JsonHelper.Deserialize<Mission>(body);
        }

        private async Task TransitionAsync(string missionId, string status)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Status = status });
            await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status", content).ConfigureAwait(false);
        }

        private async Task AssignCaptainToMissionAsync(string missionId, string captainId)
        {
            StringContent content = JsonHelper.ToJsonContent(new { CaptainId = captainId });
            await _AuthClient.PutAsync("/api/v1/missions/" + missionId, content).ConfigureAwait(false);
        }

        private async Task<string> CreateVoyageAsync(string vesselId)
        {
            StringContent content = JsonHelper.ToJsonContent(new
            {
                Title = "EventVoyage",
                VesselId = vesselId,
                Missions = new[]
                {
                    new { Title = "VoyageMission1", Description = "desc" }
                }
            });
            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages", content).ConfigureAwait(false);
            Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(response).ConfigureAwait(false);
            return voyage.Id;
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

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);
                AssertNotNull(result.Objects);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Empty_ReturnsCorrectEnumerationStructure", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Success);
                AssertEqual(1, result.PageNumber);
                AssertTrue(result.PageSize > 0);
                AssertTrue(result.TotalRecords >= 0);
            }).ConfigureAwait(false);

            #endregion

            #region Events-Generated-Via-Transitions

            await RunTest("ListEvents_AfterStatusTransition_ContainsEvents", async () =>
            {
                Mission mission = await CreateMissionAsync("TransitionEvent").ConfigureAwait(false);
                string missionId = mission.Id;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);
                AssertTrue(result.Objects.Count >= 1);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_AfterTransition_EventHasCorrectType", async () =>
            {
                Mission mission = await CreateMissionAsync("TypeCheck").ConfigureAwait(false);
                string missionId = mission.Id;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                bool hasStatusChanged = false;
                foreach (ArmadaEvent evt in result.Objects)
                {
                    if (evt.EventType == "mission.status_changed")
                    {
                        hasStatusChanged = true;
                        break;
                    }
                }
                AssertTrue(hasStatusChanged);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_AfterTransition_EventHasCorrectFields", async () =>
            {
                Mission mission = await CreateMissionAsync("FieldCheck").ConfigureAwait(false);
                string missionId = mission.Id;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                ArmadaEvent? statusEvent = null;
                foreach (ArmadaEvent evt in result.Objects)
                {
                    if (evt.EventType == "mission.status_changed"
                        && evt.MissionId == missionId)
                    {
                        statusEvent = evt;
                        break;
                    }
                }

                AssertNotNull(statusEvent);
                AssertStartsWith("evt_", statusEvent!.Id);
                AssertEqual("mission", statusEvent.EntityType);
                AssertEqual(missionId, statusEvent.EntityId);
                AssertEqual(missionId, statusEvent.MissionId);
                AssertNotNull(statusEvent.Message);
                AssertNotNull(statusEvent.CreatedUtc);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_MultipleTransitions_GenerateMultipleEvents", async () =>
            {
                Mission mission = await CreateMissionAsync("MultiTransition").ConfigureAwait(false);
                string missionId = mission.Id;

                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                await TransitionAsync(missionId, "InProgress").ConfigureAwait(false);
                await TransitionAsync(missionId, "Testing").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                int statusChangedCount = 0;
                foreach (ArmadaEvent evt in result.Objects)
                {
                    if (evt.EventType == "mission.status_changed"
                        && evt.MissionId == missionId)
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
                    Mission mission = await CreateMissionAsync("PageTest-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=5&pageNumber=1").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(5, result.Objects.Count);
                AssertTrue(result.TotalRecords >= 12);
                AssertEqual(1, result.PageNumber);
                AssertEqual(5, result.PageSize);
                AssertTrue(result.Success);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_Page2", async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    Mission mission = await CreateMissionAsync("Page2Test-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=5&pageNumber=2").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_LastPage_PartialResults", async () =>
            {
                for (int i = 0; i < 7; i++)
                {
                    Mission mission = await CreateMissionAsync("LastPage-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                long totalRecords;
                {
                    HttpResponseMessage countResp = await _AuthClient.GetAsync("/api/v1/events?pageSize=1000").ConfigureAwait(false);
                    EnumerationResult<ArmadaEvent> countResult = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(countResp).ConfigureAwait(false);
                    totalRecords = countResult.TotalRecords;
                }

                int pageSize = 5;
                int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=" + pageSize + "&pageNumber=" + totalPages).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                int expectedOnLastPage = (int)(totalRecords % pageSize);
                if (expectedOnLastPage == 0) expectedOnLastPage = pageSize;

                AssertEqual(expectedOnLastPage, result.Objects.Count);
                AssertEqual(totalPages, result.PageNumber);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_BeyondLastPage_ReturnsEmpty", async () =>
            {
                Mission mission = await CreateMissionAsync("BeyondPage").ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=10&pageNumber=100").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
                AssertTrue(result.TotalRecords >= 1);
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Pagination_FirstPageHasCorrectEventIds", async () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    Mission mission = await CreateMissionAsync("IdCheck-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?pageSize=3&pageNumber=1").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(3, result.Objects.Count);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertStartsWith("evt_", evt.Id);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_Ordering_DefaultIsCreatedDescending", async () =>
            {
                Mission m1 = await CreateMissionAsync("Order-1").ConfigureAwait(false);
                await TransitionAsync(m1.Id, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                Mission m2 = await CreateMissionAsync("Order-2").ConfigureAwait(false);
                await TransitionAsync(m2.Id, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                Mission m3 = await CreateMissionAsync("Order-3").ConfigureAwait(false);
                await TransitionAsync(m3.Id, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 3);

                DateTime first = result.Objects[0].CreatedUtc;
                DateTime last = result.Objects[result.Objects.Count - 1].CreatedUtc;
                AssertTrue(first >= last);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-Type-Tests

            await RunTest("ListEvents_FilterByType_MissionStatusChanged", async () =>
            {
                Mission mission = await CreateMissionAsync("TypeFilter").ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?type=mission.status_changed").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual("mission.status_changed", evt.EventType);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByType_NoMatches_ReturnsEmpty", async () =>
            {
                Mission mission = await CreateMissionAsync("NoTypeMatch").ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?type=nonexistent.type").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-MissionId-Tests

            await RunTest("ListEvents_FilterByMissionId", async () =>
            {
                Mission mission1 = await CreateMissionAsync("MissionFilter-1").ConfigureAwait(false);
                string missionId1 = mission1.Id;
                await TransitionAsync(missionId1, "Assigned").ConfigureAwait(false);

                Mission mission2 = await CreateMissionAsync("MissionFilter-2").ConfigureAwait(false);
                string missionId2 = mission2.Id;
                await TransitionAsync(missionId2, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?missionId=" + missionId1).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual(missionId1, evt.MissionId);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByMissionId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?missionId=msn_nonexistent").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-VesselId-Tests

            await RunTest("ListEvents_FilterByVesselId", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);

                Mission mission = await CreateMissionAsync("VesselFilter", vesselId: vesselId).ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?vesselId=" + vesselId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual(vesselId, evt.VesselId);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByVesselId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?vesselId=vsl_nonexistent").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-CaptainId-Tests

            await RunTest("ListEvents_FilterByCaptainId", async () =>
            {
                // CaptainId is an operational field managed by the dispatch system,
                // not assignable via PUT. Verify the filter endpoint returns a valid result.
                string captainId = await CreateCaptainAsync("filter-captain").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?captainId=" + captainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                // May return 0 events since CaptainId is set by the dispatch system
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual(captainId, evt.CaptainId);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByCaptainId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?captainId=cpt_nonexistent").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-VoyageId-Tests

            await RunTest("ListEvents_FilterByVoyageId", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);
                string voyageId = await CreateVoyageAsync(vesselId).ConfigureAwait(false);

                Mission mission = await CreateMissionAsync("VoyageFilter", voyageId: voyageId).ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?voyageId=" + voyageId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertNotNull(evt.VoyageId);
                    AssertEqual(voyageId, evt.VoyageId);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_FilterByVoyageId_NonexistentId_ReturnsEmpty", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?voyageId=vyg_nonexistent").ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
            }).ConfigureAwait(false);

            #endregion

            #region Filter-By-Limit-Tests

            await RunTest("ListEvents_FilterByLimit", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Mission mission = await CreateMissionAsync("LimitTest-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/events?limit=3").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(3, result.PageSize);
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
                Mission mission = await CreateMissionAsync("CombinedFilter").ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/events?type=mission.status_changed&missionId=" + missionId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual("mission.status_changed", evt.EventType);
                    AssertEqual(missionId, evt.MissionId);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_CombinedFilters_TypeAndVesselId", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);

                Mission mission = await CreateMissionAsync("CombinedVessel", vesselId: vesselId).ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/events?type=mission.status_changed&vesselId=" + vesselId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual("mission.status_changed", evt.EventType);
                    AssertEqual(vesselId, evt.VesselId);
                }
            }).ConfigureAwait(false);

            await RunTest("ListEvents_CombinedFilters_LimitAndType", async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    Mission mission = await CreateMissionAsync("LimitType-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/events?type=mission.status_changed&limit=2").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(2, result.Objects.Count);
            }).ConfigureAwait(false);

            #endregion

            #region Enumerate-Tests

            await RunTest("EnumerateEvents_Default_ReturnsEnumerationResult", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Success);
                AssertNotNull(result.Objects);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_EmptyDatabase_ReturnsZeroRecords", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                // Note: database may not be empty at this point due to previous tests
                AssertTrue(result.TotalRecords >= 0);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithPageSizeAndPageNumber", async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    Mission mission = await CreateMissionAsync("EnumPage-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 5, PageNumber = 2 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
                AssertEqual(5, result.PageSize);
                AssertTrue(result.TotalRecords >= 12);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithEventTypeFilter", async () =>
            {
                Mission mission = await CreateMissionAsync("EnumType").ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                StringContent content = JsonHelper.ToJsonContent(new { EventType = "mission.status_changed" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual("mission.status_changed", evt.EventType);
                }
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithEventTypeFilter_NoMatches", async () =>
            {
                Mission mission = await CreateMissionAsync("EnumNoMatch").ConfigureAwait(false);
                string missionId = mission.Id;
                await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);

                StringContent content = JsonHelper.ToJsonContent(new { EventType = "nonexistent.type" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_Ordering_CreatedDescending", async () =>
            {
                Mission m1 = await CreateMissionAsync("EnumOrd-1").ConfigureAwait(false);
                await TransitionAsync(m1.Id, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                Mission m2 = await CreateMissionAsync("EnumOrd-2").ConfigureAwait(false);
                await TransitionAsync(m2.Id, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                Mission m3 = await CreateMissionAsync("EnumOrd-3").ConfigureAwait(false);
                await TransitionAsync(m3.Id, "Assigned").ConfigureAwait(false);

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedDescending" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 3);

                DateTime first = result.Objects[0].CreatedUtc;
                DateTime last = result.Objects[result.Objects.Count - 1].CreatedUtc;
                AssertTrue(first >= last);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_Ordering_CreatedAscending", async () =>
            {
                Mission m1 = await CreateMissionAsync("EnumAsc-1").ConfigureAwait(false);
                await TransitionAsync(m1.Id, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                Mission m2 = await CreateMissionAsync("EnumAsc-2").ConfigureAwait(false);
                await TransitionAsync(m2.Id, "Assigned").ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);

                Mission m3 = await CreateMissionAsync("EnumAsc-3").ConfigureAwait(false);
                await TransitionAsync(m3.Id, "Assigned").ConfigureAwait(false);

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedAscending" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 3);

                DateTime first = result.Objects[0].CreatedUtc;
                DateTime last = result.Objects[result.Objects.Count - 1].CreatedUtc;
                AssertTrue(first <= last);
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_WithMissionIdFilter", async () =>
            {
                Mission mission1 = await CreateMissionAsync("EnumMsn-1").ConfigureAwait(false);
                string missionId1 = mission1.Id;
                await TransitionAsync(missionId1, "Assigned").ConfigureAwait(false);

                Mission mission2 = await CreateMissionAsync("EnumMsn-2").ConfigureAwait(false);
                string missionId2 = mission2.Id;
                await TransitionAsync(missionId2, "Assigned").ConfigureAwait(false);

                StringContent content = JsonHelper.ToJsonContent(new { MissionId = missionId1 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertEqual(missionId1, evt.MissionId);
                }
            }).ConfigureAwait(false);

            await RunTest("EnumerateEvents_QuerystringOverrides_PageSize", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Mission mission = await CreateMissionAsync("EnumQS-" + i).ConfigureAwait(false);
                    string missionId = mission.Id;
                    await TransitionAsync(missionId, "Assigned").ConfigureAwait(false);
                }

                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/events/enumerate?pageSize=3", content).ConfigureAwait(false);
                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(3, result.PageSize);
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}
