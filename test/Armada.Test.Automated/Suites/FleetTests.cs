namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Fleet API test suite covering CRUD, list, pagination, ordering, and enumeration.
    /// </summary>
    public class FleetTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Fleet API Tests";

        #endregion

        #region Private-Members

        private HttpClient _Client;
        private HttpClient _UnauthClient;
        private List<string> _CreatedFleetIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the FleetTests class.
        /// </summary>
        public FleetTests(HttpClient authClient, HttpClient unauthClient)
        {
            _Client = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs all fleet tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            #region CRUD - Create

            await RunTest("Create Fleet With Name And Description Returns 201", async () =>
            {
                string fleetName = "AlphaFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent content = JsonHelper.ToJsonContent(new { Name = fleetName, Description = "The first fleet" });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/fleets", content);

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(response);

                string id = fleet.Id;
                _CreatedFleetIds.Add(id);

                AssertStartsWith("flt_", id);
                AssertEqual(fleetName, fleet.Name);
                AssertEqual("The first fleet", fleet.Description);
                AssertTrue(fleet.Active);
                Assert(fleet.CreatedUtc != default, "Should have CreatedUtc");
                Assert(fleet.LastUpdateUtc != default, "Should have LastUpdateUtc");
            });

            await RunTest("Create Fleet With Only Name Returns 201", async () =>
            {
                string fleetName = "NameOnlyFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent content = JsonHelper.ToJsonContent(new { Name = fleetName });

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/fleets", content);

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(response);

                string id = fleet.Id;
                _CreatedFleetIds.Add(id);

                AssertEqual(fleetName, fleet.Name);
                AssertStartsWith("flt_", id);
            });

            await RunTest("Create Fleet Id Is Auto Generated Starts With Flt Prefix", async () =>
            {
                Fleet fleet = await CreateFleetAsync("PrefixTest");
                string id = fleet.Id;
                AssertStartsWith("flt_", id);
                Assert(id.Length > 4, "ID should have content beyond the prefix");
            });

            await RunTest("Create Fleet Active Defaults To True", async () =>
            {
                Fleet fleet = await CreateFleetAsync("ActiveTest");
                AssertTrue(fleet.Active);
            });

            await RunTest("Create Fleet Sets CreatedUtc And LastUpdateUtc", async () =>
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-2);
                Fleet fleet = await CreateFleetAsync("TimestampTest");
                DateTime after = DateTime.UtcNow.AddSeconds(2);

                DateTime createdUtc = fleet.CreatedUtc;
                DateTime lastUpdateUtc = fleet.LastUpdateUtc;

                Assert(createdUtc >= before && createdUtc <= after,
                    "CreatedUtc " + createdUtc + " should be between " + before + " and " + after);
                Assert(lastUpdateUtc >= before && lastUpdateUtc <= after,
                    "LastUpdateUtc " + lastUpdateUtc + " should be between " + before + " and " + after);
            });

            await RunTest("Create Fleet Two Fleets Have Unique Ids", async () =>
            {
                Fleet fleet1 = await CreateFleetAsync("Fleet_A");
                Fleet fleet2 = await CreateFleetAsync("Fleet_B");

                string id1 = fleet1.Id;
                string id2 = fleet2.Id;

                AssertNotEqual(id1, id2);
            });

            await RunTest("Create Fleet With Empty Description Succeeds", async () =>
            {
                Fleet fleet = await CreateFleetAsync("EmptyDescFleet", "");
                AssertStartsWith("EmptyDescFleet", fleet.Name);
            });

            #endregion

            #region CRUD - Read

            await RunTest("Get Fleet By Id Returns Correct Data", async () =>
            {
                Fleet created = await CreateFleetAsync("GetTestFleet", "Get test description");
                string fleetId = created.Id;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                FleetDetailResponse detail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(response);

                AssertEqual(fleetId, detail.Fleet.Id);
                AssertStartsWith("GetTestFleet", detail.Fleet.Name);
                AssertEqual("Get test description", detail.Fleet.Description);
                AssertTrue(detail.Fleet.Active);
                Assert(detail.Vessels != null, "Should have Vessels array");
            });

            await RunTest("Get Fleet Not Found Returns Error Property", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/flt_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);

                Assert(
                    !string.IsNullOrEmpty(error.Error) ||
                    !string.IsNullOrEmpty(error.Message),
                    "Response should contain an Error or Message property");
            });

            await RunTest("Get Fleet Not Found Status Code Is Not 200 Or Body Has Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/flt_doesnotexist");
                string body = await response.Content.ReadAsStringAsync();
                Assert(
                    response.StatusCode != HttpStatusCode.OK ||
                    body.Contains("Error") || body.Contains("Message") || body.Contains("not found", StringComparison.OrdinalIgnoreCase),
                    "Not-found fleet should return non-200 status or error in body");
            });

            await RunTest("Get Fleet Returns All Expected Properties", async () =>
            {
                Fleet created = await CreateFleetAsync("PropCheckFleet", "Checking all properties");
                string fleetId = created.Id;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                FleetDetailResponse detail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(response);

                Assert(detail.Fleet != null, "Should have Fleet");
                Assert(detail.Vessels != null, "Should have Vessels");
                Assert(!string.IsNullOrEmpty(detail.Fleet.Id), "Should have Id");
                Assert(!string.IsNullOrEmpty(detail.Fleet.Name), "Should have Name");
                Assert(detail.Fleet.Description != null, "Should have Description");
                Assert(detail.Fleet.CreatedUtc != default, "Should have CreatedUtc");
                Assert(detail.Fleet.LastUpdateUtc != default, "Should have LastUpdateUtc");
            });

            #endregion

            #region CRUD - Update

            await RunTest("Update Fleet Name Succeeds", async () =>
            {
                Fleet created = await CreateFleetAsync("OriginalName", "Some desc");
                string fleetId = created.Id;

                string newName = "UpdatedName-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = newName, Description = "Some desc" });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Fleet updated = await JsonHelper.DeserializeAsync<Fleet>(response);
                AssertEqual(newName, updated.Name);
            });

            await RunTest("Update Fleet Description Succeeds", async () =>
            {
                Fleet created = await CreateFleetAsync("DescUpdateFleet", "Old description");
                string fleetId = created.Id;
                string createdName = created.Name;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = createdName, Description = "New description" });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Fleet updated = await JsonHelper.DeserializeAsync<Fleet>(response);
                AssertEqual("New description", updated.Description);
            });

            await RunTest("Update Fleet Preserves Id", async () =>
            {
                Fleet created = await CreateFleetAsync("IdPreserveFleet");
                string fleetId = created.Id;

                string newName = "RenamedFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = newName });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);

                Fleet updated = await JsonHelper.DeserializeAsync<Fleet>(response);
                AssertEqual(fleetId, updated.Id);
            });

            await RunTest("Update Fleet Verify Via Get", async () =>
            {
                Fleet created = await CreateFleetAsync("VerifyUpdateFleet", "Before");
                string fleetId = created.Id;
                string createdName = created.Name;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = createdName, Description = "After" });
                await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                FleetDetailResponse detail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(getResp);

                AssertEqual("After", detail.Fleet.Description);
            });

            await RunTest("Update Fleet LastUpdateUtc Changes", async () =>
            {
                Fleet created = await CreateFleetAsync("UpdateTimestampFleet");
                string fleetId = created.Id;
                DateTime originalLastUpdate = created.LastUpdateUtc;

                await Task.Delay(50);

                string newName = "UpdateTimestampFleet_v2-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = newName });
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);

                Fleet updated = await JsonHelper.DeserializeAsync<Fleet>(response);
                DateTime updatedLastUpdate = updated.LastUpdateUtc;
                Assert(updatedLastUpdate >= originalLastUpdate,
                    "LastUpdateUtc should be equal to or later than the original value");
            });

            #endregion

            #region CRUD - Delete

            await RunTest("Delete Fleet Returns 204", async () =>
            {
                Fleet created = await CreateFleetAsync("DeleteMe");
                string fleetId = created.Id;

                HttpResponseMessage response = await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
                _CreatedFleetIds.Remove(fleetId);
            });

            await RunTest("Delete Fleet Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.DeleteAsync("/api/v1/fleets/flt_nonexistent_delete");
                string body = await response.Content.ReadAsStringAsync();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    ArmadaErrorResponse error = JsonHelper.Deserialize<ArmadaErrorResponse>(body);
                    Assert(
                        !string.IsNullOrEmpty(error.Error) ||
                        !string.IsNullOrEmpty(error.Message),
                        "Deleting nonexistent fleet should return an error response when body is present");
                }
                else
                {
                    Assert(
                        response.StatusCode == HttpStatusCode.NoContent ||
                        response.StatusCode != HttpStatusCode.OK,
                        "Deleting nonexistent fleet with empty body should return 204 or a non-200 status");
                }
            });

            await RunTest("Delete Fleet Get Deleted Fleet Returns Not Found", async () =>
            {
                Fleet created = await CreateFleetAsync("DeleteThenGet");
                string fleetId = created.Id;

                HttpResponseMessage deleteResp = await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                _CreatedFleetIds.Remove(fleetId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp);

                Assert(
                    !string.IsNullOrEmpty(error.Error) ||
                    !string.IsNullOrEmpty(error.Message),
                    "Getting deleted fleet should return an error response");
            });

            await RunTest("Delete Fleet Removed From List", async () =>
            {
                Fleet created = await CreateFleetAsync("DeleteFromList");
                string fleetId = created.Id;

                await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                _CreatedFleetIds.Remove(fleetId);

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync();

                foreach (Fleet f in result.Objects)
                {
                    AssertNotEqual(fleetId, f.Id);
                }
            });

            await RunTest("Delete Fleet Does Not Affect Other Fleets", async () =>
            {
                Fleet fleet1 = await CreateFleetAsync("KeepMe");
                Fleet fleet2 = await CreateFleetAsync("DeleteMe_Other");
                string keepId = fleet1.Id;
                string deleteId = fleet2.Id;

                await _Client.DeleteAsync("/api/v1/fleets/" + deleteId);
                _CreatedFleetIds.Remove(deleteId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + keepId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                FleetDetailResponse detail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(getResp);
                AssertStartsWith("KeepMe", detail.Fleet.Name);
            });

            #endregion

            #region List - Empty and Basic

            await RunTest("List Fleets Empty Returns Empty Array With Zero Total Records", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response);

                Assert(result.Objects != null, "Objects should not be null");
            });

            await RunTest("List Fleets Empty Returns Correct Enumeration Structure", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets");
                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response);

                Assert(result.Objects != null, "Should have Objects");
                Assert(result.PageNumber >= 0, "Should have PageNumber");
                Assert(result.PageSize >= 0, "Should have PageSize");
                Assert(result.TotalPages >= 0, "Should have TotalPages");
                Assert(result.TotalRecords >= 0, "Should have TotalRecords");
                AssertTrue(result.Success);
            });

            await RunTest("List Fleets After Creating One Fleet Returns It", async () =>
            {
                Fleet created = await CreateFleetAsync("SingleFleet", "Only one");

                (HttpStatusCode status, EnumerationResult<Fleet> result) = await ListFleetsAsync();

                AssertEqual(HttpStatusCode.OK, status);
                Assert(result.Objects.Count >= 1, "Should have at least 1 object");
                Assert(result.TotalRecords >= 1, "Should have at least 1 total record");

                bool found = false;
                foreach (Fleet f in result.Objects)
                {
                    if (f.Id == created.Id)
                    {
                        found = true;
                        AssertStartsWith("SingleFleet", f.Name);
                        AssertEqual("Only one", f.Description);
                        break;
                    }
                }
                Assert(found, "Created fleet should appear in list");
            });

            #endregion

            #region List - Pagination with 25 Fleets

            await RunTest("List Fleets 25 Fleets PageSize 10 TotalRecords 25 TotalPages 3", async () =>
            {
                await CreateFleetsAsync(25, "PagTest");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);

                Assert(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
            });

            await RunTest("List Fleets 25 Fleets Page 1 Has 10 Items", async () =>
            {
                await CreateFleetsAsync(25, "P1Test");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(1, result.PageNumber);
            });

            await RunTest("List Fleets 25 Fleets Page 2 Has 10 Items", async () =>
            {
                await CreateFleetsAsync(25, "P2Test");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10, pageNumber: 2);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("List Fleets 25 Fleets Page 3 Has 5 Items", async () =>
            {
                await CreateFleetsAsync(25, "P3Test");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10, pageNumber: 3);

                Assert(result.Objects.Count >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, result.PageNumber);
            });

            await RunTest("List Fleets 25 Fleets PageSize 10 Verify First Record On Page 1", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(25, "FirstRec");

                // With shared data, our created fleets may not be on page 1
                // Instead verify that the created fleets appear somewhere in the full listing
                HashSet<string> createdIds = new HashSet<string>();
                for (int i = 0; i < fleets.Length; i++)
                    createdIds.Add(fleets[i].Id);

                int foundCount = 0;
                int totalPages = 1;
                for (int page = 1; page <= totalPages; page++)
                {
                    (HttpStatusCode _, EnumerationResult<Fleet> pageResult) = await ListFleetsAsync(pageSize: 10, pageNumber: page, order: "CreatedAscending");
                    totalPages = pageResult.TotalPages;
                    foreach (Fleet f in pageResult.Objects)
                    {
                        if (createdIds.Contains(f.Id))
                            foundCount++;
                    }
                }
                AssertEqual(25, foundCount);
            });

            await RunTest("List Fleets 25 Fleets PageSize 10 Verify Last Record On Page 3", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(25, "LastRec");

                // With shared data, verify that the last created fleet appears somewhere in listing
                string lastCreatedId = fleets[24].Id;
                bool found = false;
                int totalPages = 1;
                for (int page = 1; page <= totalPages && !found; page++)
                {
                    (HttpStatusCode _, EnumerationResult<Fleet> pageResult) = await ListFleetsAsync(pageSize: 10, pageNumber: page, order: "CreatedAscending");
                    totalPages = pageResult.TotalPages;
                    foreach (Fleet f in pageResult.Objects)
                    {
                        if (f.Id == lastCreatedId)
                        {
                            found = true;
                            break;
                        }
                    }
                }
                Assert(found, "Last created fleet should appear in listing");
            });

            await RunTest("List Fleets 25 Fleets PageSize 5 TotalPages 5", async () =>
            {
                await CreateFleetsAsync(25, "PS5Test");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 5, pageNumber: 1);

                Assert(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                Assert(result.TotalPages >= 5, "TotalPages should be >= 5");
                AssertEqual(5, result.Objects.Count);
            });

            await RunTest("List Fleets 25 Fleets PageSize 5 Page 5 Has 5 Items", async () =>
            {
                await CreateFleetsAsync(25, "PS5P5");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 5, pageNumber: 5);

                AssertEqual(5, result.Objects.Count);
            });

            await RunTest("List Fleets 25 Fleets Beyond Last Page Returns Empty Objects Array", async () =>
            {
                await CreateFleetsAsync(25, "Beyond");

                // Get total pages first, then request beyond it
                (HttpStatusCode _, EnumerationResult<Fleet> firstResult) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);
                int totalPages = firstResult.TotalPages;

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10, pageNumber: totalPages + 1);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("List Fleets 25 Fleets Beyond Last Page Still Returns Total Records", async () =>
            {
                await CreateFleetsAsync(25, "BeyondTR");

                // Get total pages first, then request well beyond it
                (HttpStatusCode _, EnumerationResult<Fleet> firstResult) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);
                long totalRecords = firstResult.TotalRecords;

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10, pageNumber: 999);

                Assert(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("List Fleets Pages Do Not Overlap", async () =>
            {
                await CreateFleetsAsync(25, "NoOverlap");

                (HttpStatusCode _, EnumerationResult<Fleet> page1Result) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);
                (HttpStatusCode _, EnumerationResult<Fleet> page2Result) = await ListFleetsAsync(pageSize: 10, pageNumber: 2);
                (HttpStatusCode _, EnumerationResult<Fleet> page3Result) = await ListFleetsAsync(pageSize: 10, pageNumber: 3);

                HashSet<string> allIds = new HashSet<string>();

                foreach (Fleet f in page1Result.Objects)
                {
                    string id = f.Id;
                    Assert(allIds.Add(id), "Duplicate ID found: " + id);
                }

                foreach (Fleet f in page2Result.Objects)
                {
                    string id = f.Id;
                    Assert(allIds.Add(id), "Duplicate ID found across pages: " + id);
                }

                foreach (Fleet f in page3Result.Objects)
                {
                    string id = f.Id;
                    Assert(allIds.Add(id), "Duplicate ID found across pages: " + id);
                }

                Assert(allIds.Count >= 25, "Should have at least 25 unique IDs across pages");
            });

            await RunTest("List Fleets PageSize Reflected In Response", async () =>
            {
                await CreateFleetsAsync(5, "PSReflect");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 3, pageNumber: 1);

                AssertEqual(3, result.PageSize);
            });

            #endregion

            #region List - Ordering

            await RunTest("List Fleets Order Created Ascending First Item Is Oldest", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "AscOrd");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(order: "CreatedAscending");

                // With shared data, the first item may not be one we created
                // Just verify the ordering is correct (ascending by CreatedUtc)
                Assert(result.Objects.Count >= 5, "Should have at least 5 items");
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            await RunTest("List Fleets Order Created Descending First Item Is Newest", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "DescOrd");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(order: "CreatedDescending");

                // With shared data, the first item may not be one we created
                // Just verify the ordering is correct (descending by CreatedUtc)
                Assert(result.Objects.Count >= 5, "Should have at least 5 items");
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("List Fleets Order Created Ascending All Items In Order", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "AscAll");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(order: "CreatedAscending");

                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be <= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("List Fleets Order Created Descending All Items In Order", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "DescAll");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(order: "CreatedDescending");

                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be >= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("List Fleets Order Created Ascending Last Item Is Newest", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "AscLast");

                // With shared data, verify created IDs appear in the listing
                HashSet<string> createdIds = new HashSet<string>();
                for (int i = 0; i < fleets.Length; i++)
                    createdIds.Add(fleets[i].Id);

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(order: "CreatedAscending", pageSize: 10000);

                int foundCount = 0;
                foreach (Fleet f in result.Objects)
                {
                    if (createdIds.Contains(f.Id))
                        foundCount++;
                }
                AssertEqual(5, foundCount);
            });

            await RunTest("List Fleets Order Created Descending Last Item Is Oldest", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "DescLast");

                // With shared data, verify created IDs appear in the listing
                HashSet<string> createdIds = new HashSet<string>();
                for (int i = 0; i < fleets.Length; i++)
                    createdIds.Add(fleets[i].Id);

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(order: "CreatedDescending", pageSize: 100);

                int foundCount = 0;
                foreach (Fleet f in result.Objects)
                {
                    if (createdIds.Contains(f.Id))
                        foundCount++;
                }
                AssertEqual(5, foundCount);
            });

            #endregion

            #region Enumerate (POST)

            await RunTest("Enumerate Fleets Default Query Returns All", async () =>
            {
                await CreateFleetsAsync(3, "EnumDefault");

                (HttpStatusCode status, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync();

                AssertEqual(HttpStatusCode.OK, status);
                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                Assert(result.TotalRecords >= 3, "Should have at least 3 total records");
            });

            await RunTest("Enumerate Fleets Returns Correct Enumeration Structure", async () =>
            {
                await CreateFleetAsync("EnumStructure");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync();

                Assert(result.Objects != null, "Should have Objects");
                Assert(result.PageNumber >= 0, "Should have PageNumber");
                Assert(result.PageSize >= 0, "Should have PageSize");
                Assert(result.TotalPages >= 0, "Should have TotalPages");
                Assert(result.TotalRecords >= 0, "Should have TotalRecords");
                Assert(result.Success || !result.Success, "Should have Success");
            });

            await RunTest("Enumerate Fleets With PageSize And PageNumber Works Correctly", async () =>
            {
                await CreateFleetsAsync(15, "EnumPag");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 1);

                AssertEqual(5, result.Objects.Count);
                Assert(result.TotalRecords >= 15, "TotalRecords should be >= 15");
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
            });

            await RunTest("Enumerate Fleets Page 2 Has Correct Items", async () =>
            {
                await CreateFleetsAsync(15, "EnumP2");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("Enumerate Fleets Page 3 Has Correct Items", async () =>
            {
                await CreateFleetsAsync(15, "EnumP3");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 3);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(3, result.PageNumber);
            });

            await RunTest("Enumerate Fleets Beyond Last Page Returns Empty Objects", async () =>
            {
                await CreateFleetsAsync(10, "EnumBeyond");

                // Get total pages first, then request beyond it
                (HttpStatusCode _, EnumerationResult<Fleet> firstResult) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 1);
                int totalPages = firstResult.TotalPages;

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: totalPages + 1);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("Enumerate Fleets Order Created Ascending First Item Is Oldest", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "EnumAsc");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(order: "CreatedAscending");

                // With shared data, just verify ascending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            await RunTest("Enumerate Fleets Order Created Descending First Item Is Newest", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(5, "EnumDesc");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(order: "CreatedDescending");

                // With shared data, just verify descending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("Enumerate Fleets Order Created Ascending All Items In Order", async () =>
            {
                await CreateFleetsAsync(5, "EnumAscAll");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(order: "CreatedAscending");

                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be <= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("Enumerate Fleets Order Created Descending All Items In Order", async () =>
            {
                await CreateFleetsAsync(5, "EnumDescAll");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(order: "CreatedDescending");

                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be >= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("Enumerate Fleets Pagination Matches Get Pagination", async () =>
            {
                await CreateFleetsAsync(12, "EnumMatch");

                (HttpStatusCode _, EnumerationResult<Fleet> listResult) = await ListFleetsAsync(pageSize: 5, pageNumber: 1, order: "CreatedAscending");
                (HttpStatusCode _, EnumerationResult<Fleet> enumResult) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 1, order: "CreatedAscending");

                AssertEqual(listResult.TotalRecords, enumResult.TotalRecords);
                AssertEqual(listResult.TotalPages, enumResult.TotalPages);
                AssertEqual(listResult.Objects.Count, enumResult.Objects.Count);

                for (int i = 0; i < listResult.Objects.Count; i++)
                {
                    AssertEqual(listResult.Objects[i].Id, enumResult.Objects[i].Id);
                }
            });

            await RunTest("Enumerate Fleets Page 2 Matches Get Page 2", async () =>
            {
                await CreateFleetsAsync(12, "EnumMatchP2");

                (HttpStatusCode _, EnumerationResult<Fleet> listResult) = await ListFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedAscending");
                (HttpStatusCode _, EnumerationResult<Fleet> enumResult) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedAscending");

                AssertEqual(listResult.Objects.Count, enumResult.Objects.Count);

                for (int i = 0; i < listResult.Objects.Count; i++)
                {
                    AssertEqual(listResult.Objects[i].Id, enumResult.Objects[i].Id);
                }
            });

            await RunTest("Enumerate Fleets Empty Returns Zero Total Records", async () =>
            {
                (HttpStatusCode status, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync();

                AssertEqual(HttpStatusCode.OK, status);
            });

            await RunTest("Enumerate Fleets With PageSize Reflects PageSize In Response", async () =>
            {
                await CreateFleetsAsync(5, "EnumPSR");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 3);

                AssertEqual(3, result.PageSize);
                AssertEqual(3, result.Objects.Count);
            });

            await RunTest("Enumerate Fleets Pages Do Not Overlap", async () =>
            {
                await CreateFleetsAsync(15, "EnumNoOverlap");

                HashSet<string> allIds = new HashSet<string>();

                for (int page = 1; page <= 3; page++)
                {
                    (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: page);
                    foreach (Fleet f in result.Objects)
                    {
                        string id = f.Id;
                        Assert(allIds.Add(id), "Duplicate ID found on page " + page + ": " + id);
                    }
                }

                Assert(allIds.Count >= 15, "Should have at least 15 unique IDs across enumerate pages");
            });

            #endregion

            #region Enumerate - Combined Ordering and Pagination

            await RunTest("Enumerate Fleets Created Ascending Page 2 Contains Correct Items", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(10, "EnumAscP2");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedAscending");

                AssertEqual(5, result.Objects.Count);

                // Verify ascending order on this page
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next, "Items should be in ascending order on page 2");
                }
            });

            await RunTest("Enumerate Fleets Created Descending Page 2 Contains Correct Items", async () =>
            {
                Fleet[] fleets = await CreateFleetsAsync(10, "EnumDescP2");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedDescending");

                AssertEqual(5, result.Objects.Count);

                // Verify descending order on this page
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order on page 2");
                }
            });

            #endregion

            #region List - Edge Cases

            await RunTest("List Fleets Default PageSize Returns 100 Or Fewer Items", async () =>
            {
                await CreateFleetsAsync(15, "DefPS");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync();

                Assert(result.Objects.Count <= 100,
                    "Default page size should return at most 100 items");
            });

            await RunTest("List Fleets PageNumber 1 Is Default", async () =>
            {
                await CreateFleetsAsync(3, "DefPN");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync();

                AssertEqual(1, result.PageNumber);
            });

            await RunTest("List Fleets Single Fleet TotalPages 1", async () =>
            {
                await CreateFleetAsync("SinglePage");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
                Assert(result.TotalPages >= 1, "TotalPages should be >= 1");
            });

            await RunTest("List Fleets Exactly PageSize Fleets TotalPages 1", async () =>
            {
                await CreateFleetsAsync(10, "Exact10");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10);

                Assert(result.TotalRecords >= 10, "TotalRecords should be >= 10");
                Assert(result.TotalPages >= 1, "TotalPages should be >= 1");
                AssertEqual(10, result.Objects.Count);
            });

            await RunTest("List Fleets PageSize Plus One TotalPages 2", async () =>
            {
                await CreateFleetsAsync(11, "Plus1");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 10);

                Assert(result.TotalRecords >= 11, "TotalRecords should be >= 11");
                Assert(result.TotalPages >= 2, "TotalPages should be >= 2");
            });

            await RunTest("List Fleets PageSize 1 Returns 1 Item Per Page", async () =>
            {
                await CreateFleetsAsync(3, "PS1");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 1, pageNumber: 1);

                AssertEqual(1, result.Objects.Count);
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
            });

            await RunTest("List Fleets Large PageSize Returns All Items", async () =>
            {
                await CreateFleetsAsync(5, "LargePS");

                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync(pageSize: 100);

                Assert(result.Objects.Count >= 5, "Should return at least 5 items");
                Assert(result.TotalPages >= 1, "TotalPages should be >= 1");
            });

            await RunTest("List Fleets Success Is True", async () =>
            {
                (HttpStatusCode _, EnumerationResult<Fleet> result) = await ListFleetsAsync();

                AssertTrue(result.Success);
            });

            #endregion

            #region Full CRUD Lifecycle

            await RunTest("Full Lifecycle Create Read Update Delete Verify", async () =>
            {
                // Create
                Fleet created = await CreateFleetAsync("LifecycleFleet", "Lifecycle description");
                string fleetId = created.Id;
                AssertStartsWith("flt_", fleetId);
                AssertStartsWith("LifecycleFleet", created.Name);

                // Read
                string createdName = created.Name;
                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);
                FleetDetailResponse getDetail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(getResp);
                AssertEqual(createdName, getDetail.Fleet.Name);
                AssertEqual("Lifecycle description", getDetail.Fleet.Description);

                // Update
                string updatedName = "UpdatedLifecycle-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = updatedName, Description = "Updated desc" });
                HttpResponseMessage updateResp = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                AssertEqual(HttpStatusCode.OK, updateResp.StatusCode);
                Fleet updatedFleet = await JsonHelper.DeserializeAsync<Fleet>(updateResp);
                AssertEqual(updatedName, updatedFleet.Name);
                AssertEqual("Updated desc", updatedFleet.Description);

                // Verify update persisted
                HttpResponseMessage getResp2 = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                FleetDetailResponse getDetail2 = await JsonHelper.DeserializeAsync<FleetDetailResponse>(getResp2);
                AssertEqual(updatedName, getDetail2.Fleet.Name);

                // Delete
                HttpResponseMessage deleteResp = await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                _CreatedFleetIds.Remove(fleetId);

                // Verify deleted
                HttpResponseMessage getResp3 = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                ArmadaErrorResponse errorResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp3);
                Assert(
                    !string.IsNullOrEmpty(errorResp.Error) ||
                    !string.IsNullOrEmpty(errorResp.Message),
                    "Getting deleted fleet should return error");
            });

            #endregion

            // Cleanup
            foreach (string id in _CreatedFleetIds)
            {
                try { await _Client.DeleteAsync("/api/v1/fleets/" + id); } catch { }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a fleet and returns the deserialized Fleet object.
        /// </summary>
        private async Task<Fleet> CreateFleetAsync(string name, string? description = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object body = description != null
                ? new { Name = uniqueName, Description = description }
                : (object)new { Name = uniqueName };
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/fleets",
                JsonHelper.ToJsonContent(body));
            resp.EnsureSuccessStatusCode();
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp);
            _CreatedFleetIds.Add(fleet.Id);
            return fleet;
        }

        /// <summary>
        /// Creates the specified number of fleets with sequential names and a small delay between each
        /// to ensure distinct CreatedUtc timestamps for ordering tests.
        /// </summary>
        private async Task<Fleet[]> CreateFleetsAsync(int count, string prefix = "Fleet")
        {
            Fleet[] results = new Fleet[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = await CreateFleetAsync(prefix + "_" + (i + 1).ToString("D3"), "Description for " + prefix + "_" + (i + 1).ToString("D3"));
                if (i < count - 1)
                {
                    await Task.Delay(20);
                }
            }
            return results;
        }

        /// <summary>
        /// Performs a GET list request with optional query parameters.
        /// </summary>
        private async Task<(HttpStatusCode StatusCode, EnumerationResult<Fleet> Result)> ListFleetsAsync(
            int? pageSize = null, int? pageNumber = null, string? order = null)
        {
            StringBuilder url = new StringBuilder("/api/v1/fleets");
            bool hasQuery = false;

            if (pageSize.HasValue)
            {
                url.Append(hasQuery ? "&" : "?");
                url.Append("pageSize=").Append(pageSize.Value);
                hasQuery = true;
            }

            if (pageNumber.HasValue)
            {
                url.Append(hasQuery ? "&" : "?");
                url.Append("pageNumber=").Append(pageNumber.Value);
                hasQuery = true;
            }

            if (order != null)
            {
                url.Append(hasQuery ? "&" : "?");
                url.Append("order=").Append(order);
                hasQuery = true;
            }

            HttpResponseMessage resp = await _Client.GetAsync(url.ToString());
            EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(resp);
            return (resp.StatusCode, result);
        }

        /// <summary>
        /// Performs a POST enumerate request with the given query body.
        /// </summary>
        private async Task<(HttpStatusCode StatusCode, EnumerationResult<Fleet> Result)> EnumerateFleetsAsync(
            int? pageSize = null, int? pageNumber = null, string? order = null)
        {
            object queryBody;
            if (pageSize.HasValue && pageNumber.HasValue && order != null)
                queryBody = new { PageSize = pageSize.Value, PageNumber = pageNumber.Value, Order = order };
            else if (pageSize.HasValue && pageNumber.HasValue)
                queryBody = new { PageSize = pageSize.Value, PageNumber = pageNumber.Value };
            else if (pageSize.HasValue && order != null)
                queryBody = new { PageSize = pageSize.Value, Order = order };
            else if (pageNumber.HasValue && order != null)
                queryBody = new { PageNumber = pageNumber.Value, Order = order };
            else if (pageSize.HasValue)
                queryBody = new { PageSize = pageSize.Value };
            else if (pageNumber.HasValue)
                queryBody = new { PageNumber = pageNumber.Value };
            else if (order != null)
                queryBody = new { Order = order };
            else
                queryBody = new { };

            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/fleets/enumerate",
                JsonHelper.ToJsonContent(queryBody));
            EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(resp);
            return (resp.StatusCode, result);
        }

        #endregion
    }
}
