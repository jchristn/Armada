namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// Vessel API test suite covering CRUD, list, pagination, ordering, fleet filtering, and enumeration.
    /// </summary>
    public class VesselTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Vessel API Tests";

        #endregion

        #region Private-Members

        private HttpClient _Client;
        private HttpClient _UnauthClient;
        private List<string> _CreatedVesselIds = new List<string>();
        private List<string> _CreatedFleetIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the VesselTests class.
        /// </summary>
        public VesselTests(HttpClient authClient, HttpClient unauthClient)
        {
            _Client = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs all vessel tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            #region CRUD - Create

            await RunTest("Create Vessel With All Fields Returns 201 With Correct Properties", async () =>
            {
                string fleetId = await CreateFleetAsync("CreateAllFieldsFleet");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "FullVessel",
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/full",
                        LocalPath = "/home/user/repos/full",
                        WorkingDirectory = "/home/user/repos/full/src",
                        DefaultBranch = "develop",
                        Active = true
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string id = root.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(id);

                AssertStartsWith("vsl_", id);
                AssertStartsWith("FullVessel", root.GetProperty("Name").GetString()!);
                AssertEqual(fleetId, root.GetProperty("FleetId").GetString()!);
                AssertEqual("https://github.com/test/full", root.GetProperty("RepoUrl").GetString()!);
                AssertEqual("/home/user/repos/full", root.GetProperty("LocalPath").GetString()!);
                AssertEqual("/home/user/repos/full/src", root.GetProperty("WorkingDirectory").GetString()!);
                AssertEqual("develop", root.GetProperty("DefaultBranch").GetString()!);
                AssertTrue(root.GetProperty("Active").GetBoolean());
                AssertTrue(root.TryGetProperty("CreatedUtc", out _));
                AssertTrue(root.TryGetProperty("LastUpdateUtc", out _));
            });

            await RunTest("Create Vessel With Minimal Fields Returns 201", async () =>
            {
                string fleetId = await CreateFleetAsync("MinimalFleet");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "MinimalVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/minimal" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string id = root.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(id);

                AssertStartsWith("vsl_", id);
                AssertStartsWith("MinimalVessel", root.GetProperty("Name").GetString()!);
                AssertEqual(fleetId, root.GetProperty("FleetId").GetString()!);
                AssertEqual("main", root.GetProperty("DefaultBranch").GetString()!);
                AssertTrue(root.GetProperty("Active").GetBoolean());
            });

            await RunTest("Create Vessel Id Has Vsl Prefix", async () =>
            {
                string fleetId = await CreateFleetAsync();
                (string id, JsonDocument doc) = await CreateVesselAsync("PrefixTest", fleetId: fleetId);
                doc.Dispose();

                AssertStartsWith("vsl_", id);
            });

            await RunTest("Create Vessel Generates Unique Ids", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string id1 = await CreateVesselAndReturnIdAsync("Vessel1", fleetId: fleetId);
                string id2 = await CreateVesselAndReturnIdAsync("Vessel2", fleetId: fleetId);

                AssertNotEqual(id1, id2);
            });

            await RunTest("Create Vessel Sets CreatedUtc And LastUpdateUtc", async () =>
            {
                string fleetId = await CreateFleetAsync();
                DateTime beforeCreate = DateTime.UtcNow.AddSeconds(-1);

                (string id, JsonDocument doc) = await CreateVesselAsync("TimestampVessel", fleetId: fleetId);
                JsonElement root = doc.RootElement;

                string createdUtcStr = root.GetProperty("CreatedUtc").GetString()!;
                string lastUpdateUtcStr = root.GetProperty("LastUpdateUtc").GetString()!;
                DateTime createdUtc = DateTime.Parse(createdUtcStr, null, DateTimeStyles.RoundtripKind);
                DateTime lastUpdateUtc = DateTime.Parse(lastUpdateUtcStr, null, DateTimeStyles.RoundtripKind);

                Assert(createdUtc.ToUniversalTime() >= beforeCreate, "CreatedUtc " + createdUtc + " should be >= " + beforeCreate);
                Assert(lastUpdateUtc.ToUniversalTime() >= beforeCreate, "LastUpdateUtc " + lastUpdateUtc + " should be >= " + beforeCreate);
                doc.Dispose();
            });

            await RunTest("Create Vessel DefaultBranch Defaults To Main", async () =>
            {
                string fleetId = await CreateFleetAsync();

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "DefaultBranchVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/default-branch" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                string id = doc.RootElement.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(id);

                AssertEqual("main", doc.RootElement.GetProperty("DefaultBranch").GetString()!);
            });

            await RunTest("Create Vessel Active Defaults To True", async () =>
            {
                string fleetId = await CreateFleetAsync();

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "ActiveDefaultVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/active-default" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                string id = doc.RootElement.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(id);

                AssertTrue(doc.RootElement.GetProperty("Active").GetBoolean());
            });

            #endregion

            #region CRUD - Read

            await RunTest("Get Vessel Exists Returns Correct Data", async () =>
            {
                string fleetId = await CreateFleetAsync("GetFleet");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "GetVessel",
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/get",
                        DefaultBranch = "develop"
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage createResp = await _Client.PostAsync("/api/v1/vessels", content);
                string createBody = await createResp.Content.ReadAsStringAsync();
                string vesselId;
                using (JsonDocument createDoc = JsonDocument.Parse(createBody))
                {
                    vesselId = createDoc.RootElement.GetProperty("Id").GetString()!;
                    _CreatedVesselIds.Add(vesselId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(vesselId, root.GetProperty("Id").GetString()!);
                AssertStartsWith("GetVessel", root.GetProperty("Name").GetString()!);
                AssertEqual(fleetId, root.GetProperty("FleetId").GetString()!);
                AssertEqual("https://github.com/test/get", root.GetProperty("RepoUrl").GetString()!);
                AssertEqual("develop", root.GetProperty("DefaultBranch").GetString()!);
            });

            await RunTest("Get Vessel Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels/vsl_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _) ||
                    doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property");
            });

            await RunTest("Get Vessel Invalid Id Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels/invalid_id_format");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _) ||
                    doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property");
            });

            #endregion

            #region CRUD - Update

            await RunTest("Update Vessel Name Returns Updated Name", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("OriginalName", fleetId: fleetId);

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "UpdatedName", FleetId = fleetId, RepoUrl = "https://github.com/test/originalname" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("UpdatedName", doc.RootElement.GetProperty("Name").GetString()!);
            });

            await RunTest("Update Vessel RepoUrl Returns Updated RepoUrl", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("RepoUrlVessel", fleetId: fleetId, repoUrl: "https://github.com/test/old");

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "RepoUrlVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/new" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("https://github.com/test/new", doc.RootElement.GetProperty("RepoUrl").GetString()!);
            });

            await RunTest("Update Vessel DefaultBranch Returns Updated Branch", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("BranchVessel", fleetId: fleetId);

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "BranchVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/branchvessel", DefaultBranch = "release" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("release", doc.RootElement.GetProperty("DefaultBranch").GetString()!);
            });

            await RunTest("Update Vessel Multiple Fields All Updated", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("MultiUpdateVessel", fleetId: fleetId, repoUrl: "https://github.com/test/orig");

                string renamedName = "RenamedVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string renamedUrl = "https://github.com/test/renamed-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = renamedName,
                        FleetId = fleetId,
                        RepoUrl = renamedUrl,
                        DefaultBranch = "staging"
                    }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(renamedName, root.GetProperty("Name").GetString()!);
                AssertEqual(renamedUrl, root.GetProperty("RepoUrl").GetString()!);
                AssertEqual("staging", root.GetProperty("DefaultBranch").GetString()!);
            });

            await RunTest("Update Vessel Preserves Id And FleetId", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("PreserveIdVessel", fleetId: fleetId);

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "StillSameId", FleetId = fleetId, RepoUrl = "https://github.com/test/preserveidvessel" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(vesselId, doc.RootElement.GetProperty("Id").GetString()!);
                AssertEqual(fleetId, doc.RootElement.GetProperty("FleetId").GetString()!);
            });

            await RunTest("Update Vessel Verify Via Get", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("VerifyUpdateVessel", fleetId: fleetId);

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "VerifiedUpdate", FleetId = fleetId, RepoUrl = "https://github.com/test/verifyupdatevessel", DefaultBranch = "feature" }),
                    Encoding.UTF8, "application/json");
                await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual("VerifiedUpdate", doc.RootElement.GetProperty("Name").GetString()!);
                AssertEqual("feature", doc.RootElement.GetProperty("DefaultBranch").GetString()!);
            });

            #endregion

            #region CRUD - Delete

            await RunTest("Delete Vessel Exists Returns 204", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("ToDelete", fleetId: fleetId);

                HttpResponseMessage response = await _Client.DeleteAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
                _CreatedVesselIds.Remove(vesselId);
            });

            await RunTest("Delete Vessel Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.DeleteAsync("/api/v1/vessels/vsl_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(body))
                {
                    using JsonDocument doc = JsonDocument.Parse(body);
                    Assert(
                        doc.RootElement.TryGetProperty("Error", out _) ||
                        doc.RootElement.TryGetProperty("Message", out _),
                        "Should have Error or Message property");
                }
                else
                {
                    AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
                }
            });

            await RunTest("Get Vessel After Delete Returns Not Found", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("DeleteThenGet", fleetId: fleetId);

                HttpResponseMessage deleteResp = await _Client.DeleteAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                _CreatedVesselIds.Remove(vesselId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _) ||
                    doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property");
            });

            await RunTest("Delete Vessel Does Not Affect Other Vessels", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId1 = await CreateVesselAndReturnIdAsync("KeepMe", fleetId: fleetId);
                string vesselId2 = await CreateVesselAndReturnIdAsync("DeleteMe", fleetId: fleetId);

                await _Client.DeleteAsync("/api/v1/vessels/" + vesselId2);
                _CreatedVesselIds.Remove(vesselId2);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId1);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertStartsWith("KeepMe", doc.RootElement.GetProperty("Name").GetString()!);
            });

            #endregion

            #region List - Empty and Basic

            await RunTest("List Vessels Empty Returns Empty Array With Correct Envelope", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(JsonValueKind.Array, root.GetProperty("Objects").ValueKind);
                AssertTrue(root.GetProperty("Success").GetBoolean());
            });

            await RunTest("List Vessels After Create Returns Vessel", async () =>
            {
                string fleetId = await CreateFleetAsync();
                await CreateVesselAndReturnIdAsync("ListAfterCreate", fleetId: fleetId, repoUrl: "https://github.com/test/list");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1, "Should have at least 1 object");
                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 1, "Should have at least 1 total record");
            });

            #endregion

            #region List - Pagination

            await RunTest("List Vessels 25 Items PageSize 10 Page 1 Has 10 Items", async () =>
            {
                string fleetId = await CreateFleetAsync("PaginationFleet");
                for (int i = 0; i < 25; i++)
                {
                    await CreateVesselAndReturnIdAsync("PagVessel_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=1&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(10, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(25, root.GetProperty("TotalRecords").GetInt32());
                AssertEqual(3, root.GetProperty("TotalPages").GetInt32());
                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
                AssertEqual(10, root.GetProperty("PageSize").GetInt32());
            });

            await RunTest("List Vessels 25 Items PageSize 10 Page 2 Has 10 Items", async () =>
            {
                string fleetId = await CreateFleetAsync("PagFleet2");
                for (int i = 0; i < 25; i++)
                {
                    await CreateVesselAndReturnIdAsync("Pag2Vessel_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=2&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Vessels 25 Items PageSize 10 Page 3 Has 5 Items", async () =>
            {
                string fleetId = await CreateFleetAsync("PagFleet3");
                for (int i = 0; i < 25; i++)
                {
                    await CreateVesselAndReturnIdAsync("Pag3Vessel_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=3&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Vessels 25 Items Verify First Record Page 1 And Last Record Page 3", async () =>
            {
                string fleetId = await CreateFleetAsync("PagFleetFirstLast");
                for (int i = 0; i < 25; i++)
                {
                    await CreateVesselAndReturnIdAsync("FL_Vessel_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage page1Resp = await _Client.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=1&order=CreatedAscending&fleetId=" + fleetId);
                string page1Body = await page1Resp.Content.ReadAsStringAsync();
                using JsonDocument page1Doc = JsonDocument.Parse(page1Body);
                JsonElement page1Objects = page1Doc.RootElement.GetProperty("Objects");
                string firstItemName = page1Objects[0].GetProperty("Name").GetString()!;

                HttpResponseMessage page3Resp = await _Client.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=3&order=CreatedAscending&fleetId=" + fleetId);
                string page3Body = await page3Resp.Content.ReadAsStringAsync();
                using JsonDocument page3Doc = JsonDocument.Parse(page3Body);
                JsonElement page3Objects = page3Doc.RootElement.GetProperty("Objects");
                int page3Count = page3Objects.GetArrayLength();
                string lastItemName = page3Objects[page3Count - 1].GetProperty("Name").GetString()!;

                AssertStartsWith("FL_Vessel_00", firstItemName);
                AssertStartsWith("FL_Vessel_24", lastItemName);
            });

            await RunTest("List Vessels Page Beyond Last Page Returns Empty Objects", async () =>
            {
                string fleetId = await CreateFleetAsync("BeyondFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync("BeyondVessel_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=99&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            #endregion

            #region List - Ordering

            await RunTest("List Vessels Order Created Ascending Oldest First", async () =>
            {
                string fleetId = await CreateFleetAsync("OrderAscFleet");
                await CreateVesselAndReturnIdAsync("AscFirst", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("AscSecond", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("AscThird", fleetId: fleetId);

                HttpResponseMessage response = await _Client.GetAsync(
                    "/api/v1/vessels?order=CreatedAscending&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                Assert(objects.GetArrayLength() >= 3, "Should have at least 3 objects");
                string firstName = objects[0].GetProperty("Name").GetString()!;
                string lastName = objects[objects.GetArrayLength() - 1].GetProperty("Name").GetString()!;
                AssertStartsWith("AscFirst", firstName);
                AssertStartsWith("AscThird", lastName);
            });

            await RunTest("List Vessels Order Created Descending Newest First", async () =>
            {
                string fleetId = await CreateFleetAsync("OrderDescFleet");
                await CreateVesselAndReturnIdAsync("DescFirst", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("DescSecond", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("DescThird", fleetId: fleetId);

                HttpResponseMessage response = await _Client.GetAsync(
                    "/api/v1/vessels?order=CreatedDescending&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                Assert(objects.GetArrayLength() >= 3, "Should have at least 3 objects");
                string firstName = objects[0].GetProperty("Name").GetString()!;
                string lastName = objects[objects.GetArrayLength() - 1].GetProperty("Name").GetString()!;
                AssertStartsWith("DescThird", firstName);
                AssertStartsWith("DescFirst", lastName);
            });

            await RunTest("List Vessels Order Created Ascending Timestamps Are Ascending", async () =>
            {
                string fleetId = await CreateFleetAsync("TimestampAscFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync("TsAsc_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync(
                    "/api/v1/vessels?order=CreatedAscending&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                DateTime previous = DateTime.MinValue;
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    DateTime created = DateTime.Parse(obj.GetProperty("CreatedUtc").GetString()!);
                    Assert(created >= previous, "Timestamps should be in ascending order");
                    previous = created;
                }
            });

            await RunTest("List Vessels Order Created Descending Timestamps Are Descending", async () =>
            {
                string fleetId = await CreateFleetAsync("TimestampDescFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync("TsDesc_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync(
                    "/api/v1/vessels?order=CreatedDescending&fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                DateTime previous = DateTime.MaxValue;
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    DateTime created = DateTime.Parse(obj.GetProperty("CreatedUtc").GetString()!);
                    Assert(created <= previous, "Timestamps should be in descending order");
                    previous = created;
                }
            });

            #endregion

            #region List - Filter by FleetId

            await RunTest("List Vessels Filter By FleetId Returns Only Matching Vessels", async () =>
            {
                string fleetId1 = await CreateFleetAsync("FilterFleetA");
                string fleetId2 = await CreateFleetAsync("FilterFleetB");

                await CreateVesselAndReturnIdAsync("VesselInA", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync("VesselInB", fleetId: fleetId2);

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?fleetId=" + fleetId1);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(1, objects.GetArrayLength());
                AssertStartsWith("VesselInA", objects[0].GetProperty("Name").GetString()!);
            });

            await RunTest("List Vessels Filter By FleetId Multiple Fleets Correct Separation", async () =>
            {
                string fleetIdAlpha = await CreateFleetAsync("AlphaFleet");
                string fleetIdBeta = await CreateFleetAsync("BetaFleet");

                await CreateVesselAndReturnIdAsync("Alpha1", fleetId: fleetIdAlpha);
                await CreateVesselAndReturnIdAsync("Alpha2", fleetId: fleetIdAlpha);
                await CreateVesselAndReturnIdAsync("Alpha3", fleetId: fleetIdAlpha);
                await CreateVesselAndReturnIdAsync("Beta1", fleetId: fleetIdBeta);
                await CreateVesselAndReturnIdAsync("Beta2", fleetId: fleetIdBeta);

                HttpResponseMessage alphaResp = await _Client.GetAsync("/api/v1/vessels?fleetId=" + fleetIdAlpha);
                string alphaBody = await alphaResp.Content.ReadAsStringAsync();
                using JsonDocument alphaDoc = JsonDocument.Parse(alphaBody);
                AssertEqual(3, alphaDoc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, alphaDoc.RootElement.GetProperty("TotalRecords").GetInt32());

                HttpResponseMessage betaResp = await _Client.GetAsync("/api/v1/vessels?fleetId=" + fleetIdBeta);
                string betaBody = await betaResp.Content.ReadAsStringAsync();
                using JsonDocument betaDoc = JsonDocument.Parse(betaBody);
                AssertEqual(2, betaDoc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, betaDoc.RootElement.GetProperty("TotalRecords").GetInt32());
            });

            await RunTest("List Vessels Filter By FleetId All Vessels Have Correct FleetId", async () =>
            {
                string fleetId = await CreateFleetAsync("ConsistentFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync("Consistent_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?fleetId=" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                foreach (JsonElement vessel in objects.EnumerateArray())
                {
                    AssertEqual(fleetId, vessel.GetProperty("FleetId").GetString()!);
                }
            });

            await RunTest("List Vessels Filter By Nonexistent FleetId Returns Empty", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?fleetId=flt_doesnotexist");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(0, doc.RootElement.GetProperty("TotalRecords").GetInt32());
            });

            #endregion

            #region Enumerate (POST)

            await RunTest("Enumerate Default Query Returns All Vessels", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumAllFleet");
                await CreateVesselAndReturnIdAsync("EnumAll1", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumAll2", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumAll3", fleetId: fleetId);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                Assert(root.GetProperty("Objects").GetArrayLength() >= 3, "Should have at least 3 objects");
                Assert(root.GetProperty("TotalRecords").GetInt32() >= 3, "Should have at least 3 total records");
                AssertTrue(root.GetProperty("Success").GetBoolean());
            });

            await RunTest("Enumerate With PageSize And PageNumber", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumPagFleet");
                for (int i = 0; i < 15; i++)
                {
                    await CreateVesselAndReturnIdAsync("EnumPag_" + i.ToString("D2"), fleetId: fleetId);
                }

                StringContent page1Content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 5, FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage page1Resp = await _Client.PostAsync("/api/v1/vessels/enumerate", page1Content);
                string page1Body = await page1Resp.Content.ReadAsStringAsync();
                using JsonDocument page1Doc = JsonDocument.Parse(page1Body);

                AssertEqual(5, page1Doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(15, page1Doc.RootElement.GetProperty("TotalRecords").GetInt32());
                AssertEqual(3, page1Doc.RootElement.GetProperty("TotalPages").GetInt32());
                AssertEqual(1, page1Doc.RootElement.GetProperty("PageNumber").GetInt32());

                StringContent page2Content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 2, PageSize = 5, FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage page2Resp = await _Client.PostAsync("/api/v1/vessels/enumerate", page2Content);
                string page2Body = await page2Resp.Content.ReadAsStringAsync();
                using JsonDocument page2Doc = JsonDocument.Parse(page2Body);

                AssertEqual(5, page2Doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, page2Doc.RootElement.GetProperty("PageNumber").GetInt32());

                StringContent page3Content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 3, PageSize = 5, FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage page3Resp = await _Client.PostAsync("/api/v1/vessels/enumerate", page3Content);
                string page3Body = await page3Resp.Content.ReadAsStringAsync();
                using JsonDocument page3Doc = JsonDocument.Parse(page3Body);

                AssertEqual(5, page3Doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, page3Doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("Enumerate With FleetId Filter Returns Only Matching Vessels", async () =>
            {
                string fleetId1 = await CreateFleetAsync("EnumFilterFleet1");
                string fleetId2 = await CreateFleetAsync("EnumFilterFleet2");

                await CreateVesselAndReturnIdAsync("EnumFilter_A1", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync("EnumFilter_A2", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync("EnumFilter_B1", fleetId: fleetId2);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10, FleetId = fleetId1 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(2, objects.GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("TotalRecords").GetInt32());

                foreach (JsonElement vessel in objects.EnumerateArray())
                {
                    AssertEqual(fleetId1, vessel.GetProperty("FleetId").GetString()!);
                }
            });

            await RunTest("Enumerate Order Created Ascending Oldest First", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumOrderAscFleet");
                await CreateVesselAndReturnIdAsync("EnumOrdAsc_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdAsc_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdAsc_Third", fleetId: fleetId);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10, Order = "CreatedAscending", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertStartsWith("EnumOrdAsc_First", objects[0].GetProperty("Name").GetString()!);
                AssertStartsWith("EnumOrdAsc_Third", objects[objects.GetArrayLength() - 1].GetProperty("Name").GetString()!);
            });

            await RunTest("Enumerate Order Created Descending Newest First", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumOrderDescFleet");
                await CreateVesselAndReturnIdAsync("EnumOrdDesc_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdDesc_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdDesc_Third", fleetId: fleetId);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertStartsWith("EnumOrdDesc_Third", objects[0].GetProperty("Name").GetString()!);
                AssertStartsWith("EnumOrdDesc_First", objects[objects.GetArrayLength() - 1].GetProperty("Name").GetString()!);
            });

            await RunTest("Enumerate Order Created Ascending Verify CreatedUtc Order", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumCreatedAscFleet");
                await CreateVesselAndReturnIdAsync("CA_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CA_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CA_Third", fleetId: fleetId);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10, Order = "CreatedAscending", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(3, objects.GetArrayLength());
                DateTime previous = DateTime.MinValue;
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    DateTime created = DateTime.Parse(obj.GetProperty("CreatedUtc").GetString()!, null, DateTimeStyles.RoundtripKind);
                    Assert(created >= previous, "CreatedUtc should be in ascending order");
                    previous = created;
                }
                AssertStartsWith("CA_First", objects[0].GetProperty("Name").GetString()!);
                AssertStartsWith("CA_Third", objects[2].GetProperty("Name").GetString()!);
            });

            await RunTest("Enumerate Order Created Descending Verify CreatedUtc Order", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumCreatedDescFleet2");
                await CreateVesselAndReturnIdAsync("CD_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CD_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CD_Third", fleetId: fleetId);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(3, objects.GetArrayLength());
                DateTime previous = DateTime.MaxValue;
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    DateTime created = DateTime.Parse(obj.GetProperty("CreatedUtc").GetString()!, null, DateTimeStyles.RoundtripKind);
                    Assert(created <= previous, "CreatedUtc should be in descending order");
                    previous = created;
                }
                AssertStartsWith("CD_Third", objects[0].GetProperty("Name").GetString()!);
                AssertStartsWith("CD_First", objects[2].GetProperty("Name").GetString()!);
            });

            #endregion

            #region Enumerate - Pagination Consistency with GET

            await RunTest("Enumerate Pagination Consistent With Get", async () =>
            {
                string fleetId = await CreateFleetAsync("ConsistencyFleet");
                for (int i = 0; i < 12; i++)
                {
                    await CreateVesselAndReturnIdAsync("Consist_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage getResp = await _Client.GetAsync(
                    "/api/v1/vessels?pageSize=5&pageNumber=1&order=CreatedAscending&fleetId=" + fleetId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                using JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement getObjects = getDoc.RootElement.GetProperty("Objects");
                int getTotalRecords = getDoc.RootElement.GetProperty("TotalRecords").GetInt32();

                StringContent enumContent = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 5, Order = "CreatedAscending", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage enumResp = await _Client.PostAsync("/api/v1/vessels/enumerate", enumContent);
                string enumBody = await enumResp.Content.ReadAsStringAsync();
                using JsonDocument enumDoc = JsonDocument.Parse(enumBody);
                JsonElement enumObjects = enumDoc.RootElement.GetProperty("Objects");
                int enumTotalRecords = enumDoc.RootElement.GetProperty("TotalRecords").GetInt32();

                AssertEqual(getTotalRecords, enumTotalRecords);
                AssertEqual(getObjects.GetArrayLength(), enumObjects.GetArrayLength());

                for (int i = 0; i < getObjects.GetArrayLength(); i++)
                {
                    AssertEqual(
                        getObjects[i].GetProperty("Id").GetString()!,
                        enumObjects[i].GetProperty("Id").GetString()!);
                }
            });

            await RunTest("Enumerate Page 2 Consistent With Get Page 2", async () =>
            {
                string fleetId = await CreateFleetAsync("ConsistP2Fleet");
                for (int i = 0; i < 12; i++)
                {
                    await CreateVesselAndReturnIdAsync("ConsistP2_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage getResp = await _Client.GetAsync(
                    "/api/v1/vessels?pageSize=5&pageNumber=2&order=CreatedAscending&fleetId=" + fleetId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                using JsonDocument getDoc = JsonDocument.Parse(getBody);
                JsonElement getObjects = getDoc.RootElement.GetProperty("Objects");

                StringContent enumContent = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 2, PageSize = 5, Order = "CreatedAscending", FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage enumResp = await _Client.PostAsync("/api/v1/vessels/enumerate", enumContent);
                string enumBody = await enumResp.Content.ReadAsStringAsync();
                using JsonDocument enumDoc = JsonDocument.Parse(enumBody);
                JsonElement enumObjects = enumDoc.RootElement.GetProperty("Objects");

                AssertEqual(getObjects.GetArrayLength(), enumObjects.GetArrayLength());
                for (int i = 0; i < getObjects.GetArrayLength(); i++)
                {
                    AssertEqual(
                        getObjects[i].GetProperty("Id").GetString()!,
                        enumObjects[i].GetProperty("Id").GetString()!);
                }
            });

            #endregion

            #region CRUD - ProjectContext and StyleGuide

            await RunTest("Create Vessel With ProjectContext And StyleGuide Returns Both Fields", async () =>
            {
                string fleetId = await CreateFleetAsync("ContextFleet");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "ContextVessel",
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/context",
                        ProjectContext = "A .NET 8 web API with PostgreSQL.",
                        StyleGuide = "Use PascalCase for public members."
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string id = root.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(id);

                AssertEqual("A .NET 8 web API with PostgreSQL.", root.GetProperty("ProjectContext").GetString()!);
                AssertEqual("Use PascalCase for public members.", root.GetProperty("StyleGuide").GetString()!);
            });

            await RunTest("Create Vessel Without ProjectContext And StyleGuide Returns Nulls", async () =>
            {
                string fleetId = await CreateFleetAsync("NullContextFleet");
                (string id, JsonDocument doc) = await CreateVesselAsync("NullContextVessel", fleetId: fleetId);
                _CreatedVesselIds.Add(id);

                JsonElement root = doc.RootElement;
                AssertTrue(root.TryGetProperty("ProjectContext", out JsonElement pcElem), "ProjectContext property should exist");
                AssertTrue(pcElem.ValueKind == JsonValueKind.Null, "ProjectContext should be null");
                AssertTrue(root.TryGetProperty("StyleGuide", out JsonElement sgElem), "StyleGuide property should exist");
                AssertTrue(sgElem.ValueKind == JsonValueKind.Null, "StyleGuide should be null");
                doc.Dispose();
            });

            await RunTest("Update Vessel ProjectContext And StyleGuide Returns Updated Values", async () =>
            {
                string fleetId = await CreateFleetAsync("UpdateContextFleet");
                string vesselId = await CreateVesselAndReturnIdAsync("UpdateContextVessel", fleetId: fleetId);

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "UpdateContextVessel",
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/updatecontextvessel",
                        ProjectContext = "Updated project context",
                        StyleGuide = "Updated style guide"
                    }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual("Updated project context", doc.RootElement.GetProperty("ProjectContext").GetString()!);
                AssertEqual("Updated style guide", doc.RootElement.GetProperty("StyleGuide").GetString()!);
            });

            await RunTest("Update Vessel ProjectContext And StyleGuide Verify Via Get", async () =>
            {
                string fleetId = await CreateFleetAsync("GetContextFleet");
                string vesselId = await CreateVesselAndReturnIdAsync("GetContextVessel", fleetId: fleetId);

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "GetContextVessel",
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/getcontextvessel",
                        ProjectContext = "Persisted context",
                        StyleGuide = "Persisted style"
                    }),
                    Encoding.UTF8, "application/json");
                await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                using JsonDocument getDoc = JsonDocument.Parse(getBody);

                AssertEqual("Persisted context", getDoc.RootElement.GetProperty("ProjectContext").GetString()!);
                AssertEqual("Persisted style", getDoc.RootElement.GetProperty("StyleGuide").GetString()!);
            });

            await RunTest("Update Vessel Clear ProjectContext And StyleGuide To Null", async () =>
            {
                string fleetId = await CreateFleetAsync("ClearContextFleet");

                StringContent createContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "ClearContextVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/clearcontext",
                        ProjectContext = "To be cleared",
                        StyleGuide = "To be cleared"
                    }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage createResp = await _Client.PostAsync("/api/v1/vessels", createContent);
                string createBody = await createResp.Content.ReadAsStringAsync();
                using JsonDocument createDoc = JsonDocument.Parse(createBody);
                string vesselId = createDoc.RootElement.GetProperty("Id").GetString()!;
                _CreatedVesselIds.Add(vesselId);

                StringContent clearContent = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Name = "ClearContextVessel-cleared",
                        FleetId = fleetId,
                        RepoUrl = "https://github.com/test/clearcontext"
                    }),
                    Encoding.UTF8, "application/json");
                await _Client.PutAsync("/api/v1/vessels/" + vesselId, clearContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                string getBody = await getResp.Content.ReadAsStringAsync();
                using JsonDocument getDoc = JsonDocument.Parse(getBody);

                JsonElement pcElem = getDoc.RootElement.GetProperty("ProjectContext");
                JsonElement sgElem = getDoc.RootElement.GetProperty("StyleGuide");
                AssertTrue(pcElem.ValueKind == JsonValueKind.Null, "ProjectContext should be null after clearing");
                AssertTrue(sgElem.ValueKind == JsonValueKind.Null, "StyleGuide should be null after clearing");
            });

            #endregion

            #region Enumerate - Edge Cases

            await RunTest("Enumerate Empty Database Returns Empty Result", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            });

            await RunTest("Enumerate Page Beyond Last Page Returns Empty Objects", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumBeyondFleet");
                for (int i = 0; i < 3; i++)
                {
                    await CreateVesselAndReturnIdAsync("EnumBeyond_" + i, fleetId: fleetId);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 99, PageSize = 10, FleetId = fleetId }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("Enumerate With Nonexistent FleetId Returns Empty", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageNumber = 1, PageSize = 10, FleetId = "flt_doesnotexist" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(0, doc.RootElement.GetProperty("TotalRecords").GetInt32());
            });

            #endregion

            // Cleanup
            foreach (string id in _CreatedVesselIds)
            {
                try { await _Client.DeleteAsync("/api/v1/vessels/" + id); } catch { }
            }

            foreach (string id in _CreatedFleetIds)
            {
                try { await _Client.DeleteAsync("/api/v1/fleets/" + id); } catch { }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a fleet and returns its ID.
        /// </summary>
        private async Task<string> CreateFleetAsync(string name = "TestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/fleets", content);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            _CreatedFleetIds.Add(id);
            return id;
        }

        /// <summary>
        /// Creates a vessel and returns its ID and the parsed JSON document.
        /// </summary>
        private async Task<(string Id, JsonDocument Doc)> CreateVesselAsync(
            string name,
            string? fleetId = null,
            string? repoUrl = null,
            string? localPath = null,
            string? workingDirectory = null,
            string? defaultBranch = null,
            bool? active = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string effectiveRepoUrl = repoUrl ?? "https://github.com/test/" + uniqueName.ToLowerInvariant().Replace(" ", "-");

            object body;
            if (fleetId != null && localPath != null && workingDirectory != null && defaultBranch != null && active != null)
                body = new { Name = uniqueName, FleetId = fleetId, RepoUrl = effectiveRepoUrl, LocalPath = localPath, WorkingDirectory = workingDirectory, DefaultBranch = defaultBranch, Active = active };
            else if (fleetId != null && defaultBranch != null)
                body = new { Name = uniqueName, FleetId = fleetId, RepoUrl = effectiveRepoUrl, DefaultBranch = defaultBranch };
            else if (fleetId != null)
                body = new { Name = uniqueName, FleetId = fleetId, RepoUrl = effectiveRepoUrl };
            else
                body = new { Name = uniqueName, RepoUrl = effectiveRepoUrl };

            StringContent content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/vessels", content);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            JsonDocument doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            _CreatedVesselIds.Add(id);
            return (id, doc);
        }

        /// <summary>
        /// Creates a vessel and returns only its ID.
        /// </summary>
        private async Task<string> CreateVesselAndReturnIdAsync(
            string name,
            string? fleetId = null,
            string? repoUrl = null)
        {
            (string id, JsonDocument doc) = await CreateVesselAsync(name, fleetId: fleetId, repoUrl: repoUrl);
            doc.Dispose();
            return id;
        }

        #endregion
    }
}
