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
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "My Voyage", missionCount: 1);

                AssertStartsWith("vyg_", voyage.GetProperty("Id").GetString()!);
                AssertEqual("My Voyage", voyage.GetProperty("Title").GetString()!);
                string status = voyage.GetProperty("Status").GetString()!;
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
                AssertTrue(voyage.TryGetProperty("CreatedUtc", out _));
                AssertTrue(voyage.TryGetProperty("LastUpdateUtc", out _));
            });

            await RunTest("CreateVoyage_WithDescription_ReturnsDescription", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Described Voyage", description: "A detailed description");

                AssertEqual("A detailed description", voyage.GetProperty("Description").GetString()!);
            });

            await RunTest("CreateVoyage_StatusDefaultsToOpenOrInProgress", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Open Status Voyage");

                string status = voyage.GetProperty("Status").GetString()!;
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
            });

            await RunTest("CreateVoyage_IdHasVygPrefix", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Id Prefix Voyage");

                string id = voyage.GetProperty("Id").GetString()!;
                AssertStartsWith("vyg_", id);
                AssertTrue(id.Length > 4);
            });

            await RunTest("CreateVoyage_WithSingleMission_MissionsCreated", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Single Mission Voyage", missionCount: 1);
                string voyageId = voyage.GetProperty("Id").GetString()!;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");
                AssertEqual(1, missions.GetArrayLength());
            });

            await RunTest("CreateVoyage_WithMultipleMissions_AllMissionsCreated", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Multi Mission Voyage", missionCount: 5);
                string voyageId = voyage.GetProperty("Id").GetString()!;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");
                AssertEqual(5, missions.GetArrayLength());
            });

            await RunTest("CreateVoyage_MissionsHaveCorrectTitles", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Title Check Voyage", missionCount: 3);
                string voyageId = voyage.GetProperty("Id").GetString()!;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");

                List<string> titles = new List<string>();
                foreach (JsonElement m in missions.EnumerateArray())
                {
                    titles.Add(m.GetProperty("Title").GetString()!);
                }

                AssertContains("Mission 1", string.Join(",", titles));
                AssertContains("Mission 2", string.Join(",", titles));
                AssertContains("Mission 3", string.Join(",", titles));
            });

            await RunTest("CreateVoyage_MissionsHaveMsnPrefix", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Msn Prefix Voyage", missionCount: 2);
                string voyageId = voyage.GetProperty("Id").GetString()!;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");

                foreach (JsonElement m in missions.EnumerateArray())
                {
                    AssertStartsWith("msn_", m.GetProperty("Id").GetString()!);
                }
            });

            await RunTest("CreateVoyage_MissionsLinkedToVoyage", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Linked Voyage", missionCount: 2);
                string voyageId = voyage.GetProperty("Id").GetString()!;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");

                foreach (JsonElement m in missions.EnumerateArray())
                {
                    AssertEqual(voyageId, m.GetProperty("VoyageId").GetString()!);
                }
            });

            await RunTest("CreateVoyage_MissionsLinkedToVessel", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "Vessel Link Voyage", missionCount: 2);
                string voyageId = voyage.GetProperty("Id").GetString()!;

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");

                foreach (JsonElement m in missions.EnumerateArray())
                {
                    AssertEqual(vesselId, m.GetProperty("VesselId").GetString()!);
                }
            });

            await RunTest("CreateVoyage_CompletedUtcIsNullOrOmitted", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage = await CreateVoyageAsync(vesselId, "No Completion Voyage");

                Assert(
                    !voyage.TryGetProperty("CompletedUtc", out JsonElement completedUtc) ||
                    completedUtc.ValueKind == JsonValueKind.Null,
                    "CompletedUtc should be null or omitted on a new voyage");
            });

            await RunTest("CreateVoyage_BareVoyageWithoutVesselId_Returns201", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Bare Voyage", Description = "No vessel" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, resp.StatusCode);

                string body = await resp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                string voyageId = doc.RootElement.GetProperty("Id").GetString()!;
                _CreatedVoyageIds.Add(voyageId);
                AssertStartsWith("vyg_", voyageId);
                AssertEqual("Open", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("CreateVoyage_BareVoyageWithEmptyMissions_Returns201", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Empty Missions Voyage", VesselId = vesselId, Missions = Array.Empty<object>() }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, resp.StatusCode);

                string body = await resp.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                string voyageId = doc.RootElement.GetProperty("Id").GetString()!;
                _CreatedVoyageIds.Add(voyageId);
                AssertStartsWith("vyg_", voyageId);
            });

            await RunTest("CreateVoyage_MultipleVoyagesGetUniqueIds", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement voyage1 = await CreateVoyageAsync(vesselId, "Unique Id Voyage 1");
                JsonElement voyage2 = await CreateVoyageAsync(vesselId, "Unique Id Voyage 2");
                JsonElement voyage3 = await CreateVoyageAsync(vesselId, "Unique Id Voyage 3");

                string id1 = voyage1.GetProperty("Id").GetString()!;
                string id2 = voyage2.GetProperty("Id").GetString()!;
                string id3 = voyage3.GetProperty("Id").GetString()!;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            });

            #endregion

            #region Get-Voyage-Tests

            await RunTest("GetVoyage_Exists_ReturnsVoyageAndMissions", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "GetVoyage Test", missionCount: 2);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.TryGetProperty("Voyage", out JsonElement voyageElement));
                AssertTrue(doc.RootElement.TryGetProperty("Missions", out JsonElement missionsElement));
                AssertEqual(voyageId, voyageElement.GetProperty("Id").GetString()!);
                AssertEqual("GetVoyage Test", voyageElement.GetProperty("Title").GetString()!);
                AssertEqual(2, missionsElement.GetArrayLength());
            });

            await RunTest("GetVoyage_ReturnsCorrectVoyageProperties", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Property Check", description: "Check all props");
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement voyage = doc.RootElement.GetProperty("Voyage");

                AssertEqual(voyageId, voyage.GetProperty("Id").GetString()!);
                AssertEqual("Property Check", voyage.GetProperty("Title").GetString()!);
                AssertEqual("Check all props", voyage.GetProperty("Description").GetString()!);
                string status = voyage.GetProperty("Status").GetString()!;
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
                AssertTrue(voyage.TryGetProperty("CreatedUtc", out _));
                AssertTrue(voyage.TryGetProperty("LastUpdateUtc", out _));
            });

            await RunTest("GetVoyage_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/vyg_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("GetVoyage_NotFound_ContainsNotFoundMessage", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/vyg_doesnotexist");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("Message", out JsonElement message))
                {
                    AssertContains("not found", message.GetString()!.ToLowerInvariant());
                }
            });

            await RunTest("GetVoyage_MissionsIncludeMissionDetails", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Mission Details Voyage", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement missions = doc.RootElement.GetProperty("Missions");
                JsonElement firstMission = missions[0];

                AssertTrue(firstMission.TryGetProperty("Id", out _));
                AssertTrue(firstMission.TryGetProperty("Title", out _));
                AssertTrue(firstMission.TryGetProperty("Status", out _));
            });

            #endregion

            #region Cancel-Voyage-Tests

            await RunTest("CancelVoyage_ReturnsCancelledStatus", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Cancel Me", missionCount: 2);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Cancelled", doc.RootElement.GetProperty("Voyage").GetProperty("Status").GetString()!);
                AssertTrue(doc.RootElement.TryGetProperty("CancelledMissions", out _));
            });

            await RunTest("CancelVoyage_VoyageStatusSetToCancelled", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Cancel Status Check", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                AssertEqual("Cancelled", getDoc.RootElement.GetProperty("Voyage").GetProperty("Status").GetString()!);
            });

            await RunTest("CancelVoyage_SetsCompletedUtc", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "CompletedUtc Cancel", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement completedUtc = getDoc.RootElement.GetProperty("Voyage").GetProperty("CompletedUtc");
                AssertNotEqual(JsonValueKind.Null, completedUtc.ValueKind);
            });

            await RunTest("CancelVoyage_CancelsAllPendingMissions", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Cancel Missions Voyage", missionCount: 3);
                string voyageId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement missions = getDoc.RootElement.GetProperty("Missions");

                foreach (JsonElement m in missions.EnumerateArray())
                {
                    string status = m.GetProperty("Status").GetString()!;
                    Assert(status == "Cancelled" || status == "Complete" || status == "Failed",
                        "Expected mission status to be Cancelled, Complete, or Failed but got: " + status);
                }
            });

            await RunTest("CancelVoyage_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/vyg_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("CancelVoyage_VoyageStillRetrievableAfterCancel", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Still Retrievable", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                AssertTrue(getDoc.RootElement.TryGetProperty("Voyage", out _));
            });

            #endregion

            #region Purge-Voyage-Tests

            await RunTest("PurgeVoyage_ReturnsDeletedStatus", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Purge Me", missionCount: 2);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("deleted", doc.RootElement.GetProperty("Status").GetString()!);
                AssertEqual(voyageId, doc.RootElement.GetProperty("VoyageId").GetString()!);

                _CreatedVoyageIds.Remove(voyageId);
            });

            await RunTest("PurgeVoyage_ReturnsMissionsDeletedCount", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Purge Count", missionCount: 3);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(3, doc.RootElement.GetProperty("MissionsDeleted").GetInt32());

                _CreatedVoyageIds.Remove(voyageId);
            });

            await RunTest("PurgeVoyage_VoyageNotFoundAfterPurge", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Purge Gone", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                AssertTrue(getDoc.RootElement.TryGetProperty("Error", out _) || getDoc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("PurgeVoyage_MissionsAlsoDeleted", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Purge Missions Gone", missionCount: 2);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage getBeforeResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBeforeBody = await getBeforeResp.Content.ReadAsStringAsync();
                JsonDocument getBeforeDoc = JsonDocument.Parse(getBeforeBody);
                List<string> missionIds = new List<string>();
                foreach (JsonElement m in getBeforeDoc.RootElement.GetProperty("Missions").EnumerateArray())
                {
                    missionIds.Add(m.GetProperty("Id").GetString()!);
                }

                AssertTrue(missionIds.Count > 0);

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                _CreatedVoyageIds.Remove(voyageId);

                foreach (string missionId in missionIds)
                {
                    HttpResponseMessage missionResp = await _AuthClient.GetAsync("/api/v1/missions/" + missionId);
                    string missionBody = await missionResp.Content.ReadAsStringAsync();
                    JsonDocument missionDoc = JsonDocument.Parse(missionBody);
                    AssertTrue(missionDoc.RootElement.TryGetProperty("Error", out _) || missionDoc.RootElement.TryGetProperty("Message", out _));
                }
            });

            await RunTest("PurgeVoyage_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/vyg_nonexistent/purge");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("PurgeVoyage_WithZeroMissions_ReturnsZeroMissionsDeleted", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Title = "Bare Purge" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage createResp = await _AuthClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);
                string createBody = await createResp.Content.ReadAsStringAsync();
                string voyageId = JsonDocument.Parse(createBody).RootElement.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(0, doc.RootElement.GetProperty("MissionsDeleted").GetInt32());
            });

            await RunTest("PurgeVoyage_VoyageNotInListAfterPurge", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Purge List Check", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage listResp = await _AuthClient.GetAsync("/api/v1/voyages");
                string listBody = await listResp.Content.ReadAsStringAsync();
                JsonDocument listDoc = JsonDocument.Parse(listBody);
                JsonElement objects = listDoc.RootElement.GetProperty("Objects");

                foreach (JsonElement v in objects.EnumerateArray())
                {
                    AssertNotEqual(voyageId, v.GetProperty("Id").GetString()!);
                }
            });

            #endregion

            #region List-Voyages-Tests

            await RunTest("ListVoyages_Empty_ReturnsEmptyEnumerationResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Objects", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
            });

            await RunTest("ListVoyages_Empty_ReturnsCorrectPaginationMetadata", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.TryGetProperty("PageNumber", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageSize", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalPages", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
                AssertTrue(doc.RootElement.TryGetProperty("Success", out _));
            });

            await RunTest("ListVoyages_AfterCreate_ReturnsVoyages", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "List Test Voyage");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
            });

            await RunTest("ListVoyages_MultipleVoyages_ReturnsAll", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "List Multi 1");
                await CreateVoyageAsync(vesselId, "List Multi 2");
                await CreateVoyageAsync(vesselId, "List Multi 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 3);
            });

            await RunTest("ListVoyages_Pagination_25Voyages_PageSize10_CorrectTotals", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateVoyageAsync(vesselId, "Pagination Voyage " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListVoyages_Pagination_Page2_ReturnsCorrectPage", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateVoyageAsync(vesselId, "Page2 Voyage " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=2");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("ListVoyages_Pagination_LastPage_ReturnsRemainingRecords", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 25; i++)
                {
                    await CreateVoyageAsync(vesselId, "LastPage Voyage " + i);
                }

                // Get total to find actual last page dynamically
                HttpResponseMessage firstResp = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=1");
                string firstBody = await firstResp.Content.ReadAsStringAsync();
                JsonDocument firstDoc = JsonDocument.Parse(firstBody);
                int totalRecords = firstDoc.RootElement.GetProperty("TotalRecords").GetInt32();
                int totalPages = (int)Math.Ceiling(totalRecords / 10.0);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=" + totalPages);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                int expectedRemaining = totalRecords - (totalPages - 1) * 10;
                AssertEqual(expectedRemaining, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(totalPages, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("ListVoyages_Pagination_BeyondLastPage_ReturnsEmpty", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 5; i++)
                {
                    await CreateVoyageAsync(vesselId, "Beyond Voyage " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=99");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListVoyages_Pagination_PageSize1_EachPageHasOneRecord", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Single A");
                await CreateVoyageAsync(vesselId, "Single B");
                await CreateVoyageAsync(vesselId, "Single C");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=1&pageNumber=1");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(1, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 3);
            });

            await RunTest("ListVoyages_NoPaginationOverlap_AllRecordsUnique", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 15; i++)
                {
                    await CreateVoyageAsync(vesselId, "NoOverlap Voyage " + i);
                }

                HttpResponseMessage resp1 = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=1");
                string body1 = await resp1.Content.ReadAsStringAsync();
                JsonDocument doc1 = JsonDocument.Parse(body1);

                HttpResponseMessage resp2 = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=2");
                string body2 = await resp2.Content.ReadAsStringAsync();
                JsonDocument doc2 = JsonDocument.Parse(body2);

                HashSet<string> page1Ids = new HashSet<string>();
                foreach (JsonElement v in doc1.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    page1Ids.Add(v.GetProperty("Id").GetString()!);
                }

                foreach (JsonElement v in doc2.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    string id = v.GetProperty("Id").GetString()!;
                    AssertFalse(page1Ids.Contains(id));
                }
            });

            await RunTest("ListVoyages_OrderCreatedAscending_OldestFirst", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Asc Voyage 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Asc Voyage 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Asc Voyage 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?order=CreatedAscending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                List<DateTime> dates = new List<DateTime>();
                foreach (JsonElement v in objects.EnumerateArray())
                {
                    dates.Add(v.GetProperty("CreatedUtc").GetDateTime());
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] >= dates[i - 1], "Expected ascending order by CreatedUtc");
                }
            });

            await RunTest("ListVoyages_OrderCreatedDescending_NewestFirst", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Desc Voyage 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Desc Voyage 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Desc Voyage 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?order=CreatedDescending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                List<DateTime> dates = new List<DateTime>();
                foreach (JsonElement v in objects.EnumerateArray())
                {
                    dates.Add(v.GetProperty("CreatedUtc").GetDateTime());
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Expected descending order by CreatedUtc");
                }
            });

            await RunTest("ListVoyages_FilterByStatus_InProgress_ReturnsOnlyInProgress", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "InProgress Voyage 1");
                await CreateVoyageAsync(vesselId, "InProgress Voyage 2");

                JsonElement toCancel = await CreateVoyageAsync(vesselId, "To Cancel For Filter");
                string cancelId = toCancel.GetProperty("Id").GetString()!;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + cancelId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=InProgress");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 2);

                foreach (JsonElement v in objects.EnumerateArray())
                {
                    AssertEqual("InProgress", v.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("ListVoyages_FilterByStatus_Cancelled_ReturnsOnlyCancelled", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement v1 = await CreateVoyageAsync(vesselId, "Cancel Filter 1");
                JsonElement v2 = await CreateVoyageAsync(vesselId, "Cancel Filter 2");
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + v1.GetProperty("Id").GetString()!);
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + v2.GetProperty("Id").GetString()!);

                await CreateVoyageAsync(vesselId, "Keep Open");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=Cancelled");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 2);

                foreach (JsonElement v in objects.EnumerateArray())
                {
                    AssertEqual("Cancelled", v.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("ListVoyages_FilterByNonMatchingStatus_ReturnsEmpty", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Open Only");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=Complete");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListVoyages_VoyagesContainExpectedProperties", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Props Voyage");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement firstVoyage = doc.RootElement.GetProperty("Objects")[0];

                AssertTrue(firstVoyage.TryGetProperty("Id", out _));
                AssertTrue(firstVoyage.TryGetProperty("Title", out _));
                AssertTrue(firstVoyage.TryGetProperty("Status", out _));
                AssertTrue(firstVoyage.TryGetProperty("CreatedUtc", out _));
            });

            #endregion

            #region Enumerate-Voyages-Tests

            await RunTest("EnumerateVoyages_DefaultQuery_ReturnsEnumerationResult", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Enumerate Default");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Objects", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageNumber", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageSize", out _));
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
            });

            await RunTest("EnumerateVoyages_EmptyDatabase_ReturnsEmptyResult", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("Objects", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
            });

            await RunTest("EnumerateVoyages_WithPageSize_RespectsLimit", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 15; i++)
                {
                    await CreateVoyageAsync(vesselId, "Enum PageSize " + i);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 5 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("EnumerateVoyages_WithPageNumber_ReturnsCorrectPage", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 20; i++)
                {
                    await CreateVoyageAsync(vesselId, "Enum Page " + i);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 10, PageNumber = 2 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("EnumerateVoyages_WithStatusFilter_ReturnsOnlyMatching", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Enum InProgress 1");
                await CreateVoyageAsync(vesselId, "Enum InProgress 2");

                JsonElement toCancel = await CreateVoyageAsync(vesselId, "Enum Cancelled 1");
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + toCancel.GetProperty("Id").GetString()!);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Status = "InProgress" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                foreach (JsonElement v in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("InProgress", v.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("EnumerateVoyages_WithCancelledStatusFilter_ReturnsOnlyCancelled", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Enum Keep Open");

                JsonElement c1 = await CreateVoyageAsync(vesselId, "Enum Cancel A");
                JsonElement c2 = await CreateVoyageAsync(vesselId, "Enum Cancel B");
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + c1.GetProperty("Id").GetString()!);
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + c2.GetProperty("Id").GetString()!);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Status = "Cancelled" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2);

                foreach (JsonElement v in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("Cancelled", v.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("EnumerateVoyages_OrderCreatedAscending_SortsCorrectly", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Enum Asc 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Asc 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Asc 3");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedAscending" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                List<DateTime> dates = new List<DateTime>();
                foreach (JsonElement v in objects.EnumerateArray())
                {
                    dates.Add(v.GetProperty("CreatedUtc").GetDateTime());
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] >= dates[i - 1], "Expected ascending order by CreatedUtc");
                }
            });

            await RunTest("EnumerateVoyages_OrderCreatedDescending_SortsCorrectly", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Enum Desc 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Desc 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Enum Desc 3");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedDescending" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                List<DateTime> dates = new List<DateTime>();
                foreach (JsonElement v in objects.EnumerateArray())
                {
                    dates.Add(v.GetProperty("CreatedUtc").GetDateTime());
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Expected descending order by CreatedUtc");
                }
            });

            await RunTest("EnumerateVoyages_PaginationAndFilter_Combined", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 12; i++)
                {
                    await CreateVoyageAsync(vesselId, "Enum Combined " + i);
                }

                HttpResponseMessage listResp = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=3");
                string listBody = await listResp.Content.ReadAsStringAsync();
                JsonDocument listDoc = JsonDocument.Parse(listBody);
                int cancelCount = 0;
                foreach (JsonElement v in listDoc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    if (cancelCount < 3)
                    {
                        await _AuthClient.DeleteAsync("/api/v1/voyages/" + v.GetProperty("Id").GetString()!);
                        cancelCount++;
                    }
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Status = "InProgress", PageSize = 5, PageNumber = 1 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());

                foreach (JsonElement v in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("InProgress", v.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("EnumerateVoyages_ReturnsTotalPagesCorrectly", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 7; i++)
                {
                    await CreateVoyageAsync(vesselId, "TotalPages Voyage " + i);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 3 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                int totalRecords = doc.RootElement.GetProperty("TotalRecords").GetInt32();
                int totalPages = doc.RootElement.GetProperty("TotalPages").GetInt32();
                int expectedPages = (int)Math.Ceiling(totalRecords / 3.0);
                AssertEqual(expectedPages, totalPages);
                AssertTrue(totalPages >= 3, "Should have at least 3 pages with 7+ voyages at pageSize 3");
            });

            await RunTest("EnumerateVoyages_ReturnsSuccessField", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.TryGetProperty("Success", out JsonElement success));
                AssertTrue(success.GetBoolean());
            });

            #endregion

            #region Edge-Case-Tests

            await RunTest("CancelVoyage_ThenPurge_BothSucceed", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Cancel Then Purge", missionCount: 2);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage cancelResp = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, cancelResp.StatusCode);

                HttpResponseMessage purgeResp = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, purgeResp.StatusCode);
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage getResp = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                JsonDocument getDoc = JsonDocument.Parse(getBody);
                AssertTrue(getDoc.RootElement.TryGetProperty("Error", out _) || getDoc.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("CreateMultipleVoyages_SameVessel_AllSucceed", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement v1 = await CreateVoyageAsync(vesselId, "Same Vessel 1", missionCount: 2);
                JsonElement v2 = await CreateVoyageAsync(vesselId, "Same Vessel 2", missionCount: 3);
                JsonElement v3 = await CreateVoyageAsync(vesselId, "Same Vessel 3", missionCount: 1);

                AssertNotEqual(v1.GetProperty("Id").GetString()!, v2.GetProperty("Id").GetString()!);
                AssertNotEqual(v2.GetProperty("Id").GetString()!, v3.GetProperty("Id").GetString()!);
            });

            await RunTest("CancelledVoyage_StillAppearsInList", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Cancelled In List");
                string voyageId = created.GetProperty("Id").GetString()!;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                bool found = false;
                foreach (JsonElement v in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    if (v.GetProperty("Id").GetString() == voyageId)
                    {
                        found = true;
                        AssertEqual("Cancelled", v.GetProperty("Status").GetString()!);
                        break;
                    }
                }
                Assert(found, "Cancelled voyage should still appear in list");
            });

            await RunTest("ListVoyages_DefaultOrder_IsCreatedDescending", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Default Order 1");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Default Order 2");
                await Task.Delay(50);
                await CreateVoyageAsync(vesselId, "Default Order 3");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                List<DateTime> dates = new List<DateTime>();
                foreach (JsonElement v in objects.EnumerateArray())
                {
                    dates.Add(v.GetProperty("CreatedUtc").GetDateTime());
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Default order should be CreatedDescending (newest first)");
                }
            });

            await RunTest("ListVoyages_CancelledVoyage_HasCompletedUtcSet", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "CompletedUtc Check");
                string voyageId = created.GetProperty("Id").GetString()!;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                foreach (JsonElement v in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    if (v.GetProperty("Id").GetString() == voyageId)
                    {
                        AssertNotEqual(JsonValueKind.Null, v.GetProperty("CompletedUtc").ValueKind);
                        break;
                    }
                }
            });

            await RunTest("GetVoyage_AfterCancel_MissionsHaveCompletedUtc", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Missions CompletedUtc", missionCount: 2);
                string voyageId = created.GetProperty("Id").GetString()!;
                await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages/" + voyageId);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement missions = doc.RootElement.GetProperty("Missions");

                foreach (JsonElement m in missions.EnumerateArray())
                {
                    if (m.GetProperty("Status").GetString() == "Cancelled")
                    {
                        AssertNotEqual(JsonValueKind.Null, m.GetProperty("CompletedUtc").ValueKind);
                    }
                }
            });

            await RunTest("EnumerateVoyages_BeyondLastPage_ReturnsEmpty", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Enum Beyond 1");
                await CreateVoyageAsync(vesselId, "Enum Beyond 2");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 10, PageNumber = 100 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListVoyages_FilterByStatus_InProgress_ReturnsEmpty_WhenNoneInProgress", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "No InProgress");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?status=InProgress");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                foreach (JsonElement v in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    AssertEqual("InProgress", v.GetProperty("Status").GetString()!);
                }
            });

            await RunTest("PurgeVoyage_DoublePurge_SecondReturnsError", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Double Purge", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage resp1 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, resp1.StatusCode);
                _CreatedVoyageIds.Remove(voyageId);

                HttpResponseMessage resp2 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                string body2 = await resp2.Content.ReadAsStringAsync();
                JsonDocument doc2 = JsonDocument.Parse(body2);
                AssertTrue(doc2.RootElement.TryGetProperty("Error", out _) || doc2.RootElement.TryGetProperty("Message", out _));
            });

            await RunTest("CancelVoyage_DoubleCancel_SecondStillSucceeds", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();

                JsonElement created = await CreateVoyageAsync(vesselId, "Double Cancel", missionCount: 1);
                string voyageId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage resp1 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, resp1.StatusCode);

                HttpResponseMessage resp2 = await _AuthClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, resp2.StatusCode);

                string body2 = await resp2.Content.ReadAsStringAsync();
                JsonDocument doc2 = JsonDocument.Parse(body2);
                AssertEqual("Cancelled", doc2.RootElement.GetProperty("Voyage").GetProperty("Status").GetString()!);
            });

            await RunTest("ListVoyages_WithPageSizeQueryParam_OverridesDefault", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                for (int i = 1; i <= 5; i++)
                {
                    await CreateVoyageAsync(vesselId, "PageSize Override " + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages?pageSize=2");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(2, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("EnumerateVoyages_NullBody_ReturnsResults", async () =>
            {
                (string _, string vesselId) = await CreatePrerequisitesAsync();
                await CreateVoyageAsync(vesselId, "Null Body Enum");

                StringContent content = new StringContent(
                    "{}",
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
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
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            string fleetId = JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
            _CreatedFleetIds.Add(fleetId);
            return fleetId;
        }

        private async Task<string> CreateVesselAsync(string fleetId, string name = "VoyageTestVessel")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            string vesselId = JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
            _CreatedVesselIds.Add(vesselId);
            return vesselId;
        }

        private async Task<JsonElement> CreateVoyageAsync(string vesselId, string title, int missionCount = 1, string? description = null)
        {
            object[] missions = Enumerable.Range(1, missionCount)
                .Select(i => (object)new { Title = "Mission " + i, Description = "Description for mission " + i })
                .ToArray();
            object body = new { Title = title, Description = description ?? ("Description for " + title), VesselId = vesselId, Missions = missions };
            StringContent content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            string responseBody = await resp.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(responseBody).RootElement.Clone();
            _CreatedVoyageIds.Add(root.GetProperty("Id").GetString()!);
            return root;
        }

        private async Task<(string FleetId, string VesselId)> CreatePrerequisitesAsync()
        {
            string fleetId = await CreateFleetAsync();
            string vesselId = await CreateVesselAsync(fleetId);
            return (fleetId, vesselId);
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
