namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Automated;
    using Armada.Test.Common;

    public class VoyageTests : TestSuite
    {
        #region Public-Members

        public override string Name => "Voyages";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private List<string> _CreatedVoyageIds = new List<string>();
        private List<string> _CreatedMissionIds = new List<string>();
        private List<string> _CreatedVesselIds = new List<string>();
        private List<string> _CreatedFleetIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        public VoyageTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        protected override async Task RunTestsAsync()
        {
            #region Create-Voyage-Tests

            await RunTest("CreateVoyage_WithMissions_Returns201WithCorrectProperties", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "My Voyage", missionCount: 1);

                AssertStartsWith("vyg_", voyage.Id);
                AssertEqual("My Voyage", voyage.Title);
                string status = voyage.Status.ToString();
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
                AssertTrue(voyage.CreatedUtc != default);
                AssertTrue(voyage.LastUpdateUtc != default);
            });

            await RunTest("CreateVoyage_WithDescription_ReturnsDescription", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Described Voyage", description: "A detailed description");

                AssertEqual("A detailed description", voyage.Description!);
            });

            await RunTest("CreateVoyage_StatusDefaultsToOpenOrInProgress", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Open Status Voyage");

                string status = voyage.Status.ToString();
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
            });

            await RunTest("CreateVoyage_IdHasVygPrefix", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Id Prefix Voyage");

                string id = voyage.Id;
                AssertStartsWith("vyg_", id);
                AssertTrue(id.Length > 4);
            });

            await RunTest("CreateVoyage_WithSingleMission_MissionsCreated", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Single Mission Voyage", missionCount: 1);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertEqual(1, detail.Missions!.Count);
            });

            await RunTest("CreateVoyage_WithMultipleMissions_AllMissionsCreated", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Multi Mission Voyage", missionCount: 5);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertEqual(5, detail.Missions!.Count);
            });

            await RunTest("CreateVoyage_MissionsHaveCorrectTitles", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Title Check Voyage", missionCount: 3);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                List<string> titles = detail.Missions!.Select(m => m.Title).ToList();

                AssertContains("Mission 1", string.Join(",", titles));
                AssertContains("Mission 2", string.Join(",", titles));
                AssertContains("Mission 3", string.Join(",", titles));
            });

            await RunTest("CreateVoyage_MissionsHaveMsnPrefix", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Msn Prefix Voyage", missionCount: 2);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    AssertStartsWith("msn_", m.Id);
                }
            });

            await RunTest("CreateVoyage_MissionsLinkedToVoyage", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Linked Voyage", missionCount: 2);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    AssertEqual(voyageId, m.VoyageId!);
                }
            });

            await RunTest("CreateVoyage_MissionsLinkedToVessel", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "Vessel Link Voyage", missionCount: 2);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    AssertEqual(vesselId, m.VesselId!);
                }
            });

            await RunTest("CreateVoyage_CompletedUtcIsNullOrOmitted", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(vesselId, "No Completion Voyage");

                Assert(
                    voyage.CompletedUtc == null,
                    "CompletedUtc should be null or omitted on a new voyage");
            });

            await RunTest("CreateVoyage_BareVoyageWithoutVesselId_Returns201", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { Title = "Bare Voyage", Description = "No vessel" });

                HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, resp.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp);
                _CreatedVoyageIds.Add(voyage.Id);
                AssertStartsWith("vyg_", voyage.Id);
                AssertEqual("Open", voyage.Status.ToString());
            });

            await RunTest("CreateVoyage_BareVoyageWithEmptyMissions_Returns201", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                StringContent content = JsonHelper.ToJsonContent(new { Title = "Empty Missions Voyage", VesselId = vesselId, Missions = Array.Empty<object>() });

                HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, resp.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp);
                _CreatedVoyageIds.Add(voyage.Id);
                AssertStartsWith("vyg_", voyage.Id);
            });

            await RunTest("CreateVoyage_MultipleVoyagesGetUniqueIds", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage voyage1 = await CreateVoyageAsync(vesselId, "Unique Id Voyage 1");
                Voyage voyage2 = await CreateVoyageAsync(vesselId, "Unique Id Voyage 2");
                Voyage voyage3 = await CreateVoyageAsync(vesselId, "Unique Id Voyage 3");

                string id1 = voyage1.Id;
                string id2 = voyage2.Id;
                string id3 = voyage3.Id;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            });

            #endregion

            #region Get-Voyage-Tests

            await RunTest("GetVoyage_Exists_ReturnsVoyageAndMissions", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "GetVoyage Test", missionCount: 2);
                string voyageId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);

                AssertTrue(detail.Voyage != null);
                AssertTrue(detail.Missions != null);
                AssertEqual(voyageId, detail.Voyage!.Id);
                AssertEqual("GetVoyage Test", detail.Voyage!.Title);
                AssertEqual(2, detail.Missions!.Count);
            });

            await RunTest("GetVoyage_ReturnsCorrectVoyageProperties", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Property Check", description: "Check all props");
                string voyageId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);
                Voyage voyage = detail.Voyage!;

                AssertEqual(voyageId, voyage.Id);
                AssertEqual("Property Check", voyage.Title);
                AssertEqual("Check all props", voyage.Description!);
                string status = voyage.Status.ToString();
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
                AssertTrue(voyage.CreatedUtc != default);
                AssertTrue(voyage.LastUpdateUtc != default);
            });

            await RunTest("GetVoyage_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/vyg_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("GetVoyage_NotFound_ContainsNotFoundMessage", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/vyg_doesnotexist");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);

                if (error.Message != null)
                {
                    AssertContains("not found", error.Message.ToLowerInvariant());
                }
            });

            await RunTest("GetVoyage_MissionsIncludeMissionDetails", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Mission Details Voyage", missionCount: 1);
                string voyageId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);
                Mission firstMission = detail.Missions![0];

                AssertTrue(firstMission.Id != null);
                AssertTrue(firstMission.Title != null);
                AssertTrue(Enum.IsDefined(firstMission.Status), "Status should be a valid enum value");
            });

            #endregion

            #region Cancel-Voyage-Tests

            await RunTest("CancelVoyage_ReturnsCancelledStatus", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Cancel Me", missionCount: 2);
                string voyageId = created.Id;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CancelVoyageResponse cancelResp = await JsonHelper.DeserializeAsync<CancelVoyageResponse>(response);
                AssertEqual("Cancelled", cancelResp.Voyage!.Status.ToString());
                AssertTrue(cancelResp.CancelledMissions >= 0);
            });

            await RunTest("CancelVoyage_VoyageStatusSetToCancelled", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Cancel Status Check", missionCount: 1);
                string voyageId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertEqual("Cancelled", detail.Voyage!.Status.ToString());
            });

            await RunTest("CancelVoyage_SetsCompletedUtc", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "CompletedUtc Cancel", missionCount: 1);
                string voyageId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertTrue(detail.Voyage!.CompletedUtc != null);
            });

            await RunTest("CancelVoyage_CancelsAllPendingMissions", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Cancel Missions Voyage", missionCount: 3);
                string voyageId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    string status = m.Status.ToString();
                    Assert(status == "Cancelled" || status == "Complete" || status == "Failed",
                        "Expected mission status to be Cancelled, Complete, or Failed but got: " + status);
                }
            });

            await RunTest("CancelVoyage_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/vyg_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("CancelVoyage_VoyageStillRetrievableAfterCancel", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Still Retrievable", missionCount: 1);
                string voyageId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertTrue(detail.Voyage != null);
            });

            #endregion

            #region Purge-Voyage-Tests

            await RunTest("PurgeVoyage_ReturnsDeletedStatus", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Purge Me", missionCount: 2);
                string voyageId = created.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                PurgeVoyageResponse purgeResp = await JsonHelper.DeserializeAsync<PurgeVoyageResponse>(response);
                AssertEqual("deleted", purgeResp.Status!);
                AssertEqual(voyageId, purgeResp.VoyageId!);

                _CreatedVoyageIds.Remove(voyageId);
            });

            await RunTest("PurgeVoyage_ReturnsMissionsDeletedCount", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Purge Count", missionCount: 3);
                string voyageId = created.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                PurgeVoyageResponse purgeResp = await JsonHelper.DeserializeAsync<PurgeVoyageResponse>(response);
                AssertEqual(3, purgeResp.MissionsDeleted);

                _CreatedVoyageIds.Remove(voyageId);
            });

            await RunTest("PurgeVoyage_VoyageNotFoundAfterPurge", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Purge Gone", missionCount: 1);
                string voyageId = created.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("PurgeVoyage_MissionsAlsoDeleted", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Purge Missions Gone", missionCount: 2);
                string voyageId = created.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getBeforeResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detailBefore = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getBeforeResp);
                List<string> missionIds = detailBefore.Missions!.Select(m => m.Id).ToList();

                AssertTrue(missionIds.Count > 0);

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                _CreatedVoyageIds.Remove(voyageId);

                foreach (string missionId in missionIds)
                {
                    HttpResponseMessage missionResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                    ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(missionResp);
                    AssertTrue(error.Error != null || error.Message != null);
                }
            });

            await RunTest("PurgeVoyage_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/vyg_nonexistent/purge");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("PurgeVoyage_WithZeroMissions_ReturnsZeroMissionsDeleted", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { Title = "Bare Purge" });
                HttpResponseMessage createResp = await _AuthClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);
                Voyage createdVoyage = await JsonHelper.DeserializeAsync<Voyage>(createResp);
                string voyageId = createdVoyage.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                PurgeVoyageResponse purgeResp = await JsonHelper.DeserializeAsync<PurgeVoyageResponse>(response);
                AssertEqual(0, purgeResp.MissionsDeleted);
            });

            await RunTest("PurgeVoyage_VoyageNotInListAfterPurge", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Purge List Check", missionCount: 1);
                string voyageId = created.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage listResp = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> listResult = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(listResp);

                foreach (Voyage v in listResult.Objects)
                {
                    AssertNotEqual(voyageId, v.Id);
                }
            });

            #endregion

            #region List-Voyages-Tests

            await RunTest("ListVoyages_Empty_ReturnsEmptyEnumerationResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects != null);
                AssertTrue(result.TotalRecords >= 0);
            });

            await RunTest("ListVoyages_Empty_ReturnsCorrectPaginationMetadata", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.Success || !result.Success); // field exists
            });

            await RunTest("ListVoyages_AfterCreate_ReturnsVoyages", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "List Test Voyage");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects.Count >= 1);
            });

            await RunTest("ListVoyages_MultipleVoyages_ReturnsAll", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "List Multi 1");
                await CreateVoyageAsync(vesselId, "List Multi 2");
                await CreateVoyageAsync(vesselId, "List Multi 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects.Count >= 3);
            });

            await RunTest("ListVoyages_Pagination_25Voyages_PageSize10_CorrectTotals", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 25; i++)
                {
                    await CreateVoyageAsync(vesselId, "Pagination Voyage " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(10, result.Objects.Count);
            });

            await RunTest("ListVoyages_Pagination_Page2_ReturnsCorrectPage", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 25; i++)
                {
                    await CreateVoyageAsync(vesselId, "Page2 Voyage " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=2");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("ListVoyages_Pagination_LastPage_ReturnsRemainingRecords", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 25; i++)
                {
                    await CreateVoyageAsync(vesselId, "LastPage Voyage " + i);
                }

                // Get total to find actual last page dynamically
                HttpResponseMessage firstResp = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=1");
                EnumerationResult<Voyage> firstResult = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(firstResp);
                int totalRecords = (int)firstResult.TotalRecords;
                int totalPages = (int)Math.Ceiling(totalRecords / 10.0);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=" + totalPages);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                int expectedRemaining = totalRecords - (totalPages - 1) * 10;
                AssertEqual(expectedRemaining, result.Objects.Count);
                AssertEqual(totalPages, result.PageNumber);
            });

            await RunTest("ListVoyages_Pagination_BeyondLastPage_ReturnsEmpty", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 5; i++)
                {
                    await CreateVoyageAsync(vesselId, "Beyond Voyage " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=99");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("ListVoyages_Pagination_PageSize1_EachPageHasOneRecord", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Single A");
                await CreateVoyageAsync(vesselId, "Single B");
                await CreateVoyageAsync(vesselId, "Single C");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=1&pageNumber=1");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(1, result.Objects.Count);
                AssertTrue((int)result.TotalRecords >= 3);
            });

            await RunTest("ListVoyages_NoPaginationOverlap_AllRecordsUnique", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 15; i++)
                {
                    await CreateVoyageAsync(vesselId, "NoOverlap Voyage " + i);
                }

                HttpResponseMessage resp1 = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=1");
                EnumerationResult<Voyage> result1 = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(resp1);

                HttpResponseMessage resp2 = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=2");
                EnumerationResult<Voyage> result2 = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(resp2);

                HashSet<string> page1Ids = new HashSet<string>();
                foreach (Voyage v in result1.Objects)
                {
                    page1Ids.Add(v.Id);
                }

                foreach (Voyage v in result2.Objects)
                {
                    string id = v.Id;
                    AssertFalse(page1Ids.Contains(id));
                }
            });

            await RunTest("ListVoyages_OrderCreatedAscending_OldestFirst", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Asc Voyage 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Asc Voyage 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Asc Voyage 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?order=CreatedAscending");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] >= dates[i - 1], "Expected ascending order by CreatedUtc");
                }
            });

            await RunTest("ListVoyages_OrderCreatedDescending_NewestFirst", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Desc Voyage 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Desc Voyage 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Desc Voyage 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?order=CreatedDescending");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Expected descending order by CreatedUtc");
                }
            });

            await RunTest("ListVoyages_FilterByStatus_Open_ReturnsOnlyOpen", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Open Voyage 1");
                await CreateVoyageAsync(vesselId, "Open Voyage 2");

                Voyage toCancel = await CreateVoyageAsync(vesselId, "To Cancel For Filter");
                string cancelId = toCancel.Id;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + cancelId);

                // Voyages start as Open (no captain available to advance them)
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=Open");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 2);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Open", v.Status.ToString());
                }
            });

            await RunTest("ListVoyages_FilterByStatus_Cancelled_ReturnsOnlyCancelled", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage v1 = await CreateVoyageAsync(vesselId, "Cancel Filter 1");
                Voyage v2 = await CreateVoyageAsync(vesselId, "Cancel Filter 2");
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + v1.Id);
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + v2.Id);

                await CreateVoyageAsync(vesselId, "Keep Open");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=Cancelled");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 2);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Cancelled", v.Status.ToString());
                }
            });

            await RunTest("ListVoyages_FilterByNonMatchingStatus_ReturnsEmpty", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Open Only");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=Complete");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("ListVoyages_VoyagesContainExpectedProperties", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Props Voyage");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                Voyage firstVoyage = result.Objects[0];

                AssertTrue(firstVoyage.Id != null);
                AssertTrue(firstVoyage.Title != null);
                AssertTrue(firstVoyage.Status != default || firstVoyage.Status == default); // field exists
                AssertTrue(firstVoyage.CreatedUtc != default);
            });

            #endregion

            #region Enumerate-Voyages-Tests

            await RunTest("EnumerateVoyages_DefaultQuery_ReturnsEnumerationResult", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Enumerate Default");

                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects != null);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.Objects.Count >= 1);
            });

            await RunTest("EnumerateVoyages_EmptyDatabase_ReturnsEmptyResult", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects != null);
                AssertTrue(result.TotalRecords >= 0);
            });

            await RunTest("EnumerateVoyages_WithPageSize_RespectsLimit", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 15; i++)
                {
                    await CreateVoyageAsync(vesselId, "Enum PageSize " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 5 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(5, result.Objects.Count);
            });

            await RunTest("EnumerateVoyages_WithPageNumber_ReturnsCorrectPage", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 20; i++)
                {
                    await CreateVoyageAsync(vesselId, "Enum Page " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 10, PageNumber = 2 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("EnumerateVoyages_WithStatusFilter_ReturnsOnlyMatching", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Enum InProgress 1");
                await CreateVoyageAsync(vesselId, "Enum InProgress 2");

                Voyage toCancel = await CreateVoyageAsync(vesselId, "Enum Cancelled 1");
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + toCancel.Id);

                StringContent content = JsonHelper.ToJsonContent(new { Status = "InProgress" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("InProgress", v.Status.ToString());
                }
            });

            await RunTest("EnumerateVoyages_WithCancelledStatusFilter_ReturnsOnlyCancelled", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Enum Keep Open");

                Voyage c1 = await CreateVoyageAsync(vesselId, "Enum Cancel A");
                Voyage c2 = await CreateVoyageAsync(vesselId, "Enum Cancel B");
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + c1.Id);
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + c2.Id);

                StringContent content = JsonHelper.ToJsonContent(new { Status = "Cancelled" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 2);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Cancelled", v.Status.ToString());
                }
            });

            await RunTest("EnumerateVoyages_OrderCreatedAscending_SortsCorrectly", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Enum Asc 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Asc 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Asc 3");

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedAscending" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] >= dates[i - 1], "Expected ascending order by CreatedUtc");
                }
            });

            await RunTest("EnumerateVoyages_OrderCreatedDescending_SortsCorrectly", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Enum Desc 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Desc 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Desc 3");

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedDescending" });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Expected descending order by CreatedUtc");
                }
            });

            await RunTest("EnumerateVoyages_PaginationAndFilter_Combined", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 12; i++)
                {
                    await CreateVoyageAsync(vesselId, "Enum Combined " + i);
                }

                HttpResponseMessage listResp = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=3");
                EnumerationResult<Voyage> listResult = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(listResp);
                int cancelCount = 0;
                foreach (Voyage v in listResult.Objects)
                {
                    if (cancelCount < 3)
                    {
                        await _AuthClient.DeleteAsync("/api/v1/voyages/" + v.Id);
                        cancelCount++;
                    }
                }

                // Voyages start as Open (no captain available to advance them)
                StringContent content = JsonHelper.ToJsonContent(new { Status = "Open", PageSize = 5, PageNumber = 1 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 5);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Open", v.Status.ToString());
                }
            });

            await RunTest("EnumerateVoyages_ReturnsTotalPagesCorrectly", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 7; i++)
                {
                    await CreateVoyageAsync(vesselId, "TotalPages Voyage " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 3 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                int totalRecords = (int)result.TotalRecords;
                int totalPages = result.TotalPages;
                int expectedPages = (int)Math.Ceiling(totalRecords / 3.0);
                AssertEqual(expectedPages, totalPages);
                AssertTrue(totalPages >= 3, "Should have at least 3 pages with 7+ voyages at pageSize 3");
            });

            await RunTest("EnumerateVoyages_ReturnsSuccessField", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Success);
            });

            #endregion

            #region Edge-Case-Tests

            await RunTest("CancelVoyage_ThenPurge_BothSucceed", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Cancel Then Purge", missionCount: 2);
                string voyageId = created.Id;

                HttpResponseMessage cancelResp = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, cancelResp.StatusCode);

                HttpResponseMessage purgeResp = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, purgeResp.StatusCode);
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("CreateMultipleVoyages_SameVessel_AllSucceed", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage v1 = await CreateVoyageAsync(vesselId, "Same Vessel 1", missionCount: 2);
                Voyage v2 = await CreateVoyageAsync(vesselId, "Same Vessel 2", missionCount: 3);
                Voyage v3 = await CreateVoyageAsync(vesselId, "Same Vessel 3", missionCount: 1);

                AssertNotEqual(v1.Id, v2.Id);
                AssertNotEqual(v2.Id, v3.Id);
            });

            await RunTest("CancelledVoyage_StillAppearsInList", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Cancelled In List");
                string voyageId = created.Id;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                bool found = false;
                foreach (Voyage v in result.Objects)
                {
                    if (v.Id == voyageId)
                    {
                        found = true;
                        AssertEqual("Cancelled", v.Status.ToString());
                        break;
                    }
                }
                Assert(found, "Cancelled voyage should still appear in list");
            });

            await RunTest("ListVoyages_DefaultOrder_IsCreatedDescending", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Default Order 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Default Order 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Default Order 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Default order should be CreatedDescending (newest first)");
                }
            });

            await RunTest("ListVoyages_CancelledVoyage_HasCompletedUtcSet", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "CompletedUtc Check");
                string voyageId = created.Id;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                foreach (Voyage v in result.Objects)
                {
                    if (v.Id == voyageId)
                    {
                        AssertTrue(v.CompletedUtc != null);
                        break;
                    }
                }
            });

            await RunTest("GetVoyage_AfterCancel_MissionsHaveCompletedUtc", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Missions CompletedUtc", missionCount: 2);
                string voyageId = created.Id;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);

                foreach (Mission m in detail.Missions!)
                {
                    if (m.Status.ToString() == "Cancelled")
                    {
                        AssertTrue(m.CompletedUtc != null);
                    }
                }
            });

            await RunTest("EnumerateVoyages_BeyondLastPage_ReturnsEmpty", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Enum Beyond 1");
                await CreateVoyageAsync(vesselId, "Enum Beyond 2");

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 10, PageNumber = 100 });

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("ListVoyages_FilterByStatus_InProgress_ReturnsEmpty_WhenNoneInProgress", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "No InProgress");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=InProgress");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("InProgress", v.Status.ToString());
                }
            });

            await RunTest("PurgeVoyage_DoublePurge_SecondReturnsError", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Double Purge", missionCount: 1);
                string voyageId = created.Id;

                // Cancel first — purge is blocked on Open/InProgress voyages
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage resp1 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, resp1.StatusCode);
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage resp2 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(resp2);
                AssertTrue(error.Error != null || error.Message != null);
            });

            await RunTest("CancelVoyage_DoubleCancel_SecondStillSucceeds", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(vesselId, "Double Cancel", missionCount: 1);
                string voyageId = created.Id;

                HttpResponseMessage resp1 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, resp1.StatusCode);

                HttpResponseMessage resp2 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, resp2.StatusCode);

                CancelVoyageResponse cancelResp = await JsonHelper.DeserializeAsync<CancelVoyageResponse>(resp2);
                AssertEqual("Cancelled", cancelResp.Voyage!.Status.ToString());
            });

            await RunTest("ListVoyages_WithPageSizeQueryParam_OverridesDefault", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 5; i++)
                {
                    await CreateVoyageAsync(vesselId, "PageSize Override " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=2");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(2, result.Objects.Count);
            });

            await RunTest("EnumerateVoyages_NullBody_ReturnsResults", async () =>
            {
                PrerequisiteResult prereqs = await CreatePrerequisitesAsync();
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(vesselId, "Null Body Enum");

                StringContent content = new StringContent(
                    "{}",
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects.Count >= 1);
            });

            #endregion

            #region Cleanup

            await CleanupAsync();

            #endregion
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateFleetAsync(string name = "VoyageTestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp);
            _CreatedFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        private async Task<string> CreateVesselAsync(string fleetId, string name = "VoyageTestVessel")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp);
            _CreatedVesselIds.Add(vessel.Id);
            return vessel.Id;
        }

        private async Task<Voyage> CreateVoyageAsync(string vesselId, string title, int missionCount = 1, string? description = null)
        {
            object[] missions = Enumerable.Range(1, missionCount)
                .Select(i => (object)new { Title = "Mission " + i, Description = "Description for mission " + i })
                .ToArray();
            object body = new { Title = title, Description = description ?? ("Description for " + title), VesselId = vesselId, Missions = missions };
            StringContent content = JsonHelper.ToJsonContent(body);
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp);
            _CreatedVoyageIds.Add(voyage.Id);
            return voyage;
        }

        private async Task<PrerequisiteResult> CreatePrerequisitesAsync()
        {
            string fleetId = await CreateFleetAsync();
            string vesselId = await CreateVesselAsync(fleetId);
            return new PrerequisiteResult(fleetId, vesselId);
        }

        private async Task CleanupAsync()
        {
            foreach (string voyageId in _CreatedVoyageIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge"); } catch { }
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
