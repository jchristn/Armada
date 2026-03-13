namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;
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

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "FullVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/full",
                    LocalPath = "/home/user/repos/full",
                    WorkingDirectory = "/home/user/repos/full/src",
                    DefaultBranch = "develop",
                    Active = true
                });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                _CreatedVesselIds.Add(vessel.Id);

                AssertStartsWith("vsl_", vessel.Id);
                AssertStartsWith("FullVessel", vessel.Name);
                AssertEqual(fleetId, vessel.FleetId);
                AssertEqual("https://github.com/test/full", vessel.RepoUrl);
                AssertEqual("/home/user/repos/full", vessel.LocalPath);
                AssertEqual("/home/user/repos/full/src", vessel.WorkingDirectory);
                AssertEqual("develop", vessel.DefaultBranch);
                AssertTrue(vessel.Active);
                Assert(vessel.CreatedUtc != default, "CreatedUtc should be set");
                Assert(vessel.LastUpdateUtc != default, "LastUpdateUtc should be set");
            });

            await RunTest("Create Vessel With Minimal Fields Returns 201", async () =>
            {
                string fleetId = await CreateFleetAsync("MinimalFleet");

                StringContent content = JsonHelper.ToJsonContent(new { Name = "MinimalVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/minimal" });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                _CreatedVesselIds.Add(vessel.Id);

                AssertStartsWith("vsl_", vessel.Id);
                AssertStartsWith("MinimalVessel", vessel.Name);
                AssertEqual(fleetId, vessel.FleetId);
                AssertEqual("main", vessel.DefaultBranch);
                AssertTrue(vessel.Active);
            });

            await RunTest("Create Vessel Id Has Vsl Prefix", async () =>
            {
                string fleetId = await CreateFleetAsync();
                Vessel vessel = await CreateVesselAsync("PrefixTest", fleetId: fleetId);

                AssertStartsWith("vsl_", vessel.Id);
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

                Vessel vessel = await CreateVesselAsync("TimestampVessel", fleetId: fleetId);

                DateTime createdUtc = vessel.CreatedUtc;
                DateTime lastUpdateUtc = vessel.LastUpdateUtc;

                Assert(createdUtc.ToUniversalTime() >= beforeCreate, "CreatedUtc " + createdUtc + " should be >= " + beforeCreate);
                Assert(lastUpdateUtc.ToUniversalTime() >= beforeCreate, "LastUpdateUtc " + lastUpdateUtc + " should be >= " + beforeCreate);
            });

            await RunTest("Create Vessel DefaultBranch Defaults To Main", async () =>
            {
                string fleetId = await CreateFleetAsync();

                StringContent content = JsonHelper.ToJsonContent(new { Name = "DefaultBranchVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/default-branch" });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                _CreatedVesselIds.Add(vessel.Id);

                AssertEqual("main", vessel.DefaultBranch);
            });

            await RunTest("Create Vessel Active Defaults To True", async () =>
            {
                string fleetId = await CreateFleetAsync();

                StringContent content = JsonHelper.ToJsonContent(new { Name = "ActiveDefaultVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/active-default" });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                _CreatedVesselIds.Add(vessel.Id);

                AssertTrue(vessel.Active);
            });

            #endregion

            #region CRUD - Read

            await RunTest("Get Vessel Exists Returns Correct Data", async () =>
            {
                string fleetId = await CreateFleetAsync("GetFleet");

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "GetVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/get",
                    DefaultBranch = "develop"
                });

                HttpResponseMessage createResp = await _Client.PostAsync("/api/v1/vessels", content);
                Vessel created = await JsonHelper.DeserializeAsync<Vessel>(createResp);
                string vesselId = created.Id;
                _CreatedVesselIds.Add(vesselId);

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual(vesselId, vessel.Id);
                AssertStartsWith("GetVessel", vessel.Name);
                AssertEqual(fleetId, vessel.FleetId);
                AssertEqual("https://github.com/test/get", vessel.RepoUrl);
                AssertEqual("develop", vessel.DefaultBranch);
            });

            await RunTest("Get Vessel Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels/vsl_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                Assert(
                    error.Error != null || error.Message != null,
                    "Should have Error or Message property");
            });

            await RunTest("Get Vessel Invalid Id Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels/invalid_id_format");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                Assert(
                    error.Error != null || error.Message != null,
                    "Should have Error or Message property");
            });

            #endregion

            #region CRUD - Update

            await RunTest("Update Vessel Name Returns Updated Name", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("OriginalName", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "UpdatedName", FleetId = fleetId, RepoUrl = "https://github.com/test/originalname" });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                AssertEqual("UpdatedName", vessel.Name);
            });

            await RunTest("Update Vessel RepoUrl Returns Updated RepoUrl", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("RepoUrlVessel", fleetId: fleetId, repoUrl: "https://github.com/test/old");

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "RepoUrlVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/new" });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                AssertEqual("https://github.com/test/new", vessel.RepoUrl);
            });

            await RunTest("Update Vessel DefaultBranch Returns Updated Branch", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("BranchVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "BranchVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/branchvessel", DefaultBranch = "release" });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                AssertEqual("release", vessel.DefaultBranch);
            });

            await RunTest("Update Vessel Multiple Fields All Updated", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("MultiUpdateVessel", fleetId: fleetId, repoUrl: "https://github.com/test/orig");

                string renamedName = "RenamedVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string renamedUrl = "https://github.com/test/renamed-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = JsonHelper.ToJsonContent(new
                {
                    Name = renamedName,
                    FleetId = fleetId,
                    RepoUrl = renamedUrl,
                    DefaultBranch = "staging"
                });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual(renamedName, vessel.Name);
                AssertEqual(renamedUrl, vessel.RepoUrl);
                AssertEqual("staging", vessel.DefaultBranch);
            });

            await RunTest("Update Vessel Preserves Id And FleetId", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("PreserveIdVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "StillSameId", FleetId = fleetId, RepoUrl = "https://github.com/test/preserveidvessel" });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual(vesselId, vessel.Id);
                AssertEqual(fleetId, vessel.FleetId);
            });

            await RunTest("Update Vessel Verify Via Get", async () =>
            {
                string fleetId = await CreateFleetAsync();
                string vesselId = await CreateVesselAndReturnIdAsync("VerifyUpdateVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "VerifiedUpdate", FleetId = fleetId, RepoUrl = "https://github.com/test/verifyupdatevessel", DefaultBranch = "feature" });
                await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);

                AssertEqual("VerifiedUpdate", vessel.Name);
                AssertEqual("feature", vessel.DefaultBranch);
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
                    ArmadaErrorResponse error = JsonHelper.Deserialize<ArmadaErrorResponse>(body);
                    Assert(
                        error.Error != null || error.Message != null,
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
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp);
                Assert(
                    error.Error != null || error.Message != null,
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

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);
                AssertStartsWith("KeepMe", vessel.Name);
            });

            #endregion

            #region List - Empty and Basic

            await RunTest("List Vessels Empty Returns Empty Array With Correct Envelope", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects != null, "Objects should not be null");
                AssertTrue(result.Success);
            });

            await RunTest("List Vessels After Create Returns Vessel", async () =>
            {
                string fleetId = await CreateFleetAsync();
                await CreateVesselAndReturnIdAsync("ListAfterCreate", fleetId: fleetId, repoUrl: "https://github.com/test/list");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);
                Assert(result.Objects.Count >= 1, "Should have at least 1 object");
                Assert(result.TotalRecords >= 1, "Should have at least 1 total record");
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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(25, result.TotalRecords);
                AssertEqual(3, result.TotalPages);
                AssertEqual(1, result.PageNumber);
                AssertEqual(10, result.PageSize);
            });

            await RunTest("List Vessels 25 Items PageSize 10 Page 2 Has 10 Items", async () =>
            {
                string fleetId = await CreateFleetAsync("PagFleet2");
                for (int i = 0; i < 25; i++)
                {
                    await CreateVesselAndReturnIdAsync("Pag2Vessel_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=2&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("List Vessels 25 Items PageSize 10 Page 3 Has 5 Items", async () =>
            {
                string fleetId = await CreateFleetAsync("PagFleet3");
                for (int i = 0; i < 25; i++)
                {
                    await CreateVesselAndReturnIdAsync("Pag3Vessel_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=3&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(3, result.PageNumber);
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
                EnumerationResult<Vessel> page1Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page1Resp);
                string firstItemName = page1Result.Objects[0].Name;

                HttpResponseMessage page3Resp = await _Client.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=3&order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> page3Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page3Resp);
                string lastItemName = page3Result.Objects[page3Result.Objects.Count - 1].Name;

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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                string firstName = result.Objects[0].Name;
                string lastName = result.Objects[result.Objects.Count - 1].Name;
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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                string firstName = result.Objects[0].Name;
                string lastName = result.Objects[result.Objects.Count - 1].Name;
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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                DateTime previous = DateTime.MinValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                DateTime previous = DateTime.MaxValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
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
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(1, result.Objects.Count);
                AssertStartsWith("VesselInA", result.Objects[0].Name);
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
                EnumerationResult<Vessel> alphaResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(alphaResp);
                AssertEqual(3, alphaResult.Objects.Count);
                AssertEqual(3, alphaResult.TotalRecords);

                HttpResponseMessage betaResp = await _Client.GetAsync("/api/v1/vessels?fleetId=" + fleetIdBeta);
                EnumerationResult<Vessel> betaResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(betaResp);
                AssertEqual(2, betaResult.Objects.Count);
                AssertEqual(2, betaResult.TotalRecords);
            });

            await RunTest("List Vessels Filter By FleetId All Vessels Have Correct FleetId", async () =>
            {
                string fleetId = await CreateFleetAsync("ConsistentFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync("Consistent_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                foreach (Vessel vessel in result.Objects)
                {
                    AssertEqual(fleetId, vessel.FleetId);
                }
            });

            await RunTest("List Vessels Filter By Nonexistent FleetId Returns Empty", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/vessels?fleetId=flt_doesnotexist");
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
                AssertEqual(0, result.TotalRecords);
            });

            #endregion

            #region Enumerate (POST)

            await RunTest("Enumerate Default Query Returns All Vessels", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumAllFleet");
                await CreateVesselAndReturnIdAsync("EnumAll1", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumAll2", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumAll3", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10 });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                Assert(result.TotalRecords >= 3, "Should have at least 3 total records");
                AssertTrue(result.Success);
            });

            await RunTest("Enumerate With PageSize And PageNumber", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumPagFleet");
                for (int i = 0; i < 15; i++)
                {
                    await CreateVesselAndReturnIdAsync("EnumPag_" + i.ToString("D2"), fleetId: fleetId);
                }

                StringContent page1Content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, FleetId = fleetId });
                HttpResponseMessage page1Resp = await _Client.PostAsync("/api/v1/vessels/enumerate", page1Content);
                EnumerationResult<Vessel> page1Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page1Resp);

                AssertEqual(5, page1Result.Objects.Count);
                AssertEqual(15, page1Result.TotalRecords);
                AssertEqual(3, page1Result.TotalPages);
                AssertEqual(1, page1Result.PageNumber);

                StringContent page2Content = JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 5, FleetId = fleetId });
                HttpResponseMessage page2Resp = await _Client.PostAsync("/api/v1/vessels/enumerate", page2Content);
                EnumerationResult<Vessel> page2Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page2Resp);

                AssertEqual(5, page2Result.Objects.Count);
                AssertEqual(2, page2Result.PageNumber);

                StringContent page3Content = JsonHelper.ToJsonContent(new { PageNumber = 3, PageSize = 5, FleetId = fleetId });
                HttpResponseMessage page3Resp = await _Client.PostAsync("/api/v1/vessels/enumerate", page3Content);
                EnumerationResult<Vessel> page3Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page3Resp);

                AssertEqual(5, page3Result.Objects.Count);
                AssertEqual(3, page3Result.PageNumber);
            });

            await RunTest("Enumerate With FleetId Filter Returns Only Matching Vessels", async () =>
            {
                string fleetId1 = await CreateFleetAsync("EnumFilterFleet1");
                string fleetId2 = await CreateFleetAsync("EnumFilterFleet2");

                await CreateVesselAndReturnIdAsync("EnumFilter_A1", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync("EnumFilter_A2", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync("EnumFilter_B1", fleetId: fleetId2);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, FleetId = fleetId1 });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(2, result.Objects.Count);
                AssertEqual(2, result.TotalRecords);

                foreach (Vessel vessel in result.Objects)
                {
                    AssertEqual(fleetId1, vessel.FleetId);
                }
            });

            await RunTest("Enumerate Order Created Ascending Oldest First", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumOrderAscFleet");
                await CreateVesselAndReturnIdAsync("EnumOrdAsc_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdAsc_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdAsc_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertStartsWith("EnumOrdAsc_First", result.Objects[0].Name);
                AssertStartsWith("EnumOrdAsc_Third", result.Objects[result.Objects.Count - 1].Name);
            });

            await RunTest("Enumerate Order Created Descending Newest First", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumOrderDescFleet");
                await CreateVesselAndReturnIdAsync("EnumOrdDesc_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdDesc_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("EnumOrdDesc_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending", FleetId = fleetId });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertStartsWith("EnumOrdDesc_Third", result.Objects[0].Name);
                AssertStartsWith("EnumOrdDesc_First", result.Objects[result.Objects.Count - 1].Name);
            });

            await RunTest("Enumerate Order Created Ascending Verify CreatedUtc Order", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumCreatedAscFleet");
                await CreateVesselAndReturnIdAsync("CA_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CA_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CA_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(3, result.Objects.Count);
                DateTime previous = DateTime.MinValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
                    Assert(created >= previous, "CreatedUtc should be in ascending order");
                    previous = created;
                }
                AssertStartsWith("CA_First", result.Objects[0].Name);
                AssertStartsWith("CA_Third", result.Objects[2].Name);
            });

            await RunTest("Enumerate Order Created Descending Verify CreatedUtc Order", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumCreatedDescFleet2");
                await CreateVesselAndReturnIdAsync("CD_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CD_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync("CD_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending", FleetId = fleetId });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(3, result.Objects.Count);
                DateTime previous = DateTime.MaxValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
                    Assert(created <= previous, "CreatedUtc should be in descending order");
                    previous = created;
                }
                AssertStartsWith("CD_Third", result.Objects[0].Name);
                AssertStartsWith("CD_First", result.Objects[2].Name);
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
                EnumerationResult<Vessel> getResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(getResp);

                StringContent enumContent = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage enumResp = await _Client.PostAsync("/api/v1/vessels/enumerate", enumContent);
                EnumerationResult<Vessel> enumResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(enumResp);

                AssertEqual(getResult.TotalRecords, enumResult.TotalRecords);
                AssertEqual(getResult.Objects.Count, enumResult.Objects.Count);

                for (int i = 0; i < getResult.Objects.Count; i++)
                {
                    AssertEqual(getResult.Objects[i].Id, enumResult.Objects[i].Id);
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
                EnumerationResult<Vessel> getResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(getResp);

                StringContent enumContent = JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 5, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage enumResp = await _Client.PostAsync("/api/v1/vessels/enumerate", enumContent);
                EnumerationResult<Vessel> enumResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(enumResp);

                AssertEqual(getResult.Objects.Count, enumResult.Objects.Count);
                for (int i = 0; i < getResult.Objects.Count; i++)
                {
                    AssertEqual(getResult.Objects[i].Id, enumResult.Objects[i].Id);
                }
            });

            #endregion

            #region CRUD - ProjectContext and StyleGuide

            await RunTest("Create Vessel With ProjectContext And StyleGuide Returns Both Fields", async () =>
            {
                string fleetId = await CreateFleetAsync("ContextFleet");

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "ContextVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/context",
                    ProjectContext = "A .NET 8 web API with PostgreSQL.",
                    StyleGuide = "Use PascalCase for public members."
                });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                _CreatedVesselIds.Add(vessel.Id);

                AssertEqual("A .NET 8 web API with PostgreSQL.", vessel.ProjectContext);
                AssertEqual("Use PascalCase for public members.", vessel.StyleGuide);
            });

            await RunTest("Create Vessel Without ProjectContext And StyleGuide Returns Nulls", async () =>
            {
                string fleetId = await CreateFleetAsync("NullContextFleet");
                Vessel vessel = await CreateVesselAsync("NullContextVessel", fleetId: fleetId);
                _CreatedVesselIds.Add(vessel.Id);

                AssertTrue(vessel.ProjectContext == null, "ProjectContext should be null or absent");
                AssertTrue(vessel.StyleGuide == null, "StyleGuide should be null or absent");
            });

            await RunTest("Update Vessel ProjectContext And StyleGuide Returns Updated Values", async () =>
            {
                string fleetId = await CreateFleetAsync("UpdateContextFleet");
                string vesselId = await CreateVesselAndReturnIdAsync("UpdateContextVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new
                {
                    Name = "UpdateContextVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/updatecontextvessel",
                    ProjectContext = "Updated project context",
                    StyleGuide = "Updated style guide"
                });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual("Updated project context", vessel.ProjectContext);
                AssertEqual("Updated style guide", vessel.StyleGuide);
            });

            await RunTest("Update Vessel ProjectContext And StyleGuide Verify Via Get", async () =>
            {
                string fleetId = await CreateFleetAsync("GetContextFleet");
                string vesselId = await CreateVesselAndReturnIdAsync("GetContextVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new
                {
                    Name = "GetContextVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/getcontextvessel",
                    ProjectContext = "Persisted context",
                    StyleGuide = "Persisted style"
                });
                await _Client.PutAsync("/api/v1/vessels/" + vesselId, updateContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);

                AssertEqual("Persisted context", vessel.ProjectContext);
                AssertEqual("Persisted style", vessel.StyleGuide);
            });

            await RunTest("Update Vessel Clear ProjectContext And StyleGuide To Null", async () =>
            {
                string fleetId = await CreateFleetAsync("ClearContextFleet");

                StringContent createContent = JsonHelper.ToJsonContent(new
                {
                    Name = "ClearContextVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/clearcontext",
                    ProjectContext = "To be cleared",
                    StyleGuide = "To be cleared"
                });
                HttpResponseMessage createResp = await _Client.PostAsync("/api/v1/vessels", createContent);
                Vessel created = await JsonHelper.DeserializeAsync<Vessel>(createResp);
                string vesselId = created.Id;
                _CreatedVesselIds.Add(vesselId);

                StringContent clearContent = JsonHelper.ToJsonContent(new
                {
                    Name = "ClearContextVessel-cleared",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/clearcontext"
                });
                await _Client.PutAsync("/api/v1/vessels/" + vesselId, clearContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/vessels/" + vesselId);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);

                AssertTrue(vessel.ProjectContext == null, "ProjectContext should be null after clearing");
                AssertTrue(vessel.StyleGuide == null, "StyleGuide should be null after clearing");
            });

            #endregion

            #region Enumerate - Edge Cases

            await RunTest("Enumerate Empty Database Returns Empty Result", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10 });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);
                AssertTrue(result.Success);
            });

            await RunTest("Enumerate Page Beyond Last Page Returns Empty Objects", async () =>
            {
                string fleetId = await CreateFleetAsync("EnumBeyondFleet");
                for (int i = 0; i < 3; i++)
                {
                    await CreateVesselAndReturnIdAsync("EnumBeyond_" + i, fleetId: fleetId);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 99, PageSize = 10, FleetId = fleetId });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("Enumerate With Nonexistent FleetId Returns Empty", async () =>
            {
                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, FleetId = "flt_doesnotexist" });
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
                AssertEqual(0, result.TotalRecords);
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
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/fleets", content);
            resp.EnsureSuccessStatusCode();
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp);
            _CreatedFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        /// <summary>
        /// Creates a vessel and returns the typed Vessel object.
        /// </summary>
        private async Task<Vessel> CreateVesselAsync(
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

            StringContent content = JsonHelper.ToJsonContent(body);
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/vessels", content);
            resp.EnsureSuccessStatusCode();
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp);
            _CreatedVesselIds.Add(vessel.Id);
            return vessel;
        }

        /// <summary>
        /// Creates a vessel and returns only its ID.
        /// </summary>
        private async Task<string> CreateVesselAndReturnIdAsync(
            string name,
            string? fleetId = null,
            string? repoUrl = null)
        {
            Vessel vessel = await CreateVesselAsync(name, fleetId: fleetId, repoUrl: repoUrl);
            return vessel.Id;
        }

        #endregion
    }
}
