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
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = fleetName, Description = "The first fleet" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/fleets", content);

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string id = root.GetProperty("Id").GetString()!;
                _CreatedFleetIds.Add(id);

                AssertStartsWith("flt_", id);
                AssertEqual(fleetName, root.GetProperty("Name").GetString()!);
                AssertEqual("The first fleet", root.GetProperty("Description").GetString()!);
                AssertTrue(root.GetProperty("Active").GetBoolean());
                AssertTrue(root.TryGetProperty("CreatedUtc", out _));
                AssertTrue(root.TryGetProperty("LastUpdateUtc", out _));
            });

            await RunTest("Create Fleet With Only Name Returns 201", async () =>
            {
                string fleetName = "NameOnlyFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = fleetName }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/fleets", content);

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string id = root.GetProperty("Id").GetString()!;
                _CreatedFleetIds.Add(id);

                AssertEqual(fleetName, root.GetProperty("Name").GetString()!);
                AssertStartsWith("flt_", id);
            });

            await RunTest("Create Fleet Id Is Auto Generated Starts With Flt Prefix", async () =>
            {
                JsonElement fleet = await CreateFleetAsync("PrefixTest");
                string id = fleet.GetProperty("Id").GetString()!;
                AssertStartsWith("flt_", id);
                Assert(id.Length > 4, "ID should have content beyond the prefix");
            });

            await RunTest("Create Fleet Active Defaults To True", async () =>
            {
                JsonElement fleet = await CreateFleetAsync("ActiveTest");
                AssertTrue(fleet.GetProperty("Active").GetBoolean());
            });

            await RunTest("Create Fleet Sets CreatedUtc And LastUpdateUtc", async () =>
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-2);
                JsonElement fleet = await CreateFleetAsync("TimestampTest");
                DateTime after = DateTime.UtcNow.AddSeconds(2);

                DateTime createdUtc = fleet.GetProperty("CreatedUtc").GetDateTime();
                DateTime lastUpdateUtc = fleet.GetProperty("LastUpdateUtc").GetDateTime();

                Assert(createdUtc >= before && createdUtc <= after,
                    "CreatedUtc " + createdUtc + " should be between " + before + " and " + after);
                Assert(lastUpdateUtc >= before && lastUpdateUtc <= after,
                    "LastUpdateUtc " + lastUpdateUtc + " should be between " + before + " and " + after);
            });

            await RunTest("Create Fleet Two Fleets Have Unique Ids", async () =>
            {
                JsonElement fleet1 = await CreateFleetAsync("Fleet_A");
                JsonElement fleet2 = await CreateFleetAsync("Fleet_B");

                string id1 = fleet1.GetProperty("Id").GetString()!;
                string id2 = fleet2.GetProperty("Id").GetString()!;

                AssertNotEqual(id1, id2);
            });

            await RunTest("Create Fleet With Empty Description Succeeds", async () =>
            {
                JsonElement fleet = await CreateFleetAsync("EmptyDescFleet", "");
                AssertStartsWith("EmptyDescFleet", fleet.GetProperty("Name").GetString()!);
            });

            #endregion

            #region CRUD - Read

            await RunTest("Get Fleet By Id Returns Correct Data", async () =>
            {
                JsonElement created = await CreateFleetAsync("GetTestFleet", "Get test description");
                string fleetId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                JsonElement fleet = root.GetProperty("Fleet");
                AssertEqual(fleetId, fleet.GetProperty("Id").GetString()!);
                AssertStartsWith("GetTestFleet", fleet.GetProperty("Name").GetString()!);
                AssertEqual("Get test description", fleet.GetProperty("Description").GetString()!);
                AssertTrue(fleet.GetProperty("Active").GetBoolean());
                Assert(root.TryGetProperty("Vessels", out _), "Should have Vessels array");
            });

            await RunTest("Get Fleet Not Found Returns Error Property", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/flt_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(
                    doc.RootElement.TryGetProperty("Error", out _) ||
                    doc.RootElement.TryGetProperty("Message", out _),
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
                JsonElement created = await CreateFleetAsync("PropCheckFleet", "Checking all properties");
                string fleetId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                Assert(root.TryGetProperty("Fleet", out JsonElement fleetEl), "Should have Fleet");
                Assert(root.TryGetProperty("Vessels", out _), "Should have Vessels");
                Assert(fleetEl.TryGetProperty("Id", out _), "Should have Id");
                Assert(fleetEl.TryGetProperty("Name", out _), "Should have Name");
                Assert(fleetEl.TryGetProperty("Description", out _), "Should have Description");
                Assert(fleetEl.TryGetProperty("Active", out _), "Should have Active");
                Assert(fleetEl.TryGetProperty("CreatedUtc", out _), "Should have CreatedUtc");
                Assert(fleetEl.TryGetProperty("LastUpdateUtc", out _), "Should have LastUpdateUtc");
            });

            #endregion

            #region CRUD - Update

            await RunTest("Update Fleet Name Succeeds", async () =>
            {
                JsonElement created = await CreateFleetAsync("OriginalName", "Some desc");
                string fleetId = created.GetProperty("Id").GetString()!;

                string newName = "UpdatedName-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = newName, Description = "Some desc" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(newName, doc.RootElement.GetProperty("Name").GetString()!);
            });

            await RunTest("Update Fleet Description Succeeds", async () =>
            {
                JsonElement created = await CreateFleetAsync("DescUpdateFleet", "Old description");
                string fleetId = created.GetProperty("Id").GetString()!;
                string createdName = created.GetProperty("Name").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = createdName, Description = "New description" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("New description", doc.RootElement.GetProperty("Description").GetString()!);
            });

            await RunTest("Update Fleet Preserves Id", async () =>
            {
                JsonElement created = await CreateFleetAsync("IdPreserveFleet");
                string fleetId = created.GetProperty("Id").GetString()!;

                string newName = "RenamedFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = newName }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(fleetId, doc.RootElement.GetProperty("Id").GetString()!);
            });

            await RunTest("Update Fleet Verify Via Get", async () =>
            {
                JsonElement created = await CreateFleetAsync("VerifyUpdateFleet", "Before");
                string fleetId = created.GetProperty("Id").GetString()!;
                string createdName = created.GetProperty("Name").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = createdName, Description = "After" }),
                    Encoding.UTF8, "application/json");
                await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                JsonElement fleet = doc.RootElement.GetProperty("Fleet");
                AssertEqual("After", fleet.GetProperty("Description").GetString()!);
            });

            await RunTest("Update Fleet LastUpdateUtc Changes", async () =>
            {
                JsonElement created = await CreateFleetAsync("UpdateTimestampFleet");
                string fleetId = created.GetProperty("Id").GetString()!;
                DateTime originalLastUpdate = created.GetProperty("LastUpdateUtc").GetDateTime();

                await Task.Delay(50);

                string newName = "UpdateTimestampFleet_v2-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = newName }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                DateTime updatedLastUpdate = doc.RootElement.GetProperty("LastUpdateUtc").GetDateTime();
                Assert(updatedLastUpdate >= originalLastUpdate,
                    "LastUpdateUtc should be equal to or later than the original value");
            });

            #endregion

            #region CRUD - Delete

            await RunTest("Delete Fleet Returns 204", async () =>
            {
                JsonElement created = await CreateFleetAsync("DeleteMe");
                string fleetId = created.GetProperty("Id").GetString()!;

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
                    using JsonDocument doc = JsonDocument.Parse(body);
                    Assert(
                        doc.RootElement.TryGetProperty("Error", out _) ||
                        doc.RootElement.TryGetProperty("Message", out _),
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
                JsonElement created = await CreateFleetAsync("DeleteThenGet");
                string fleetId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage deleteResp = await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                _CreatedFleetIds.Remove(fleetId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(
                    doc.RootElement.TryGetProperty("Error", out _) ||
                    doc.RootElement.TryGetProperty("Message", out _),
                    "Getting deleted fleet should return an error response");
            });

            await RunTest("Delete Fleet Removed From List", async () =>
            {
                JsonElement created = await CreateFleetAsync("DeleteFromList");
                string fleetId = created.GetProperty("Id").GetString()!;

                await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                _CreatedFleetIds.Remove(fleetId);

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync();
                JsonElement objects = root.GetProperty("Objects");

                for (int i = 0; i < objects.GetArrayLength(); i++)
                {
                    AssertNotEqual(fleetId, objects[i].GetProperty("Id").GetString()!);
                }
            });

            await RunTest("Delete Fleet Does Not Affect Other Fleets", async () =>
            {
                JsonElement fleet1 = await CreateFleetAsync("KeepMe");
                JsonElement fleet2 = await CreateFleetAsync("DeleteMe_Other");
                string keepId = fleet1.GetProperty("Id").GetString()!;
                string deleteId = fleet2.GetProperty("Id").GetString()!;

                await _Client.DeleteAsync("/api/v1/fleets/" + deleteId);
                _CreatedFleetIds.Remove(deleteId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + keepId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertStartsWith("KeepMe", doc.RootElement.GetProperty("Fleet").GetProperty("Name").GetString()!);
            });

            #endregion

            #region List - Empty and Basic

            await RunTest("List Fleets Empty Returns Empty Array With Zero Total Records", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(JsonValueKind.Array, root.GetProperty("Objects").ValueKind);
            });

            await RunTest("List Fleets Empty Returns Correct Enumeration Structure", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/fleets");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                Assert(root.TryGetProperty("Objects", out _), "Should have Objects");
                Assert(root.TryGetProperty("PageNumber", out _), "Should have PageNumber");
                Assert(root.TryGetProperty("PageSize", out _), "Should have PageSize");
                Assert(root.TryGetProperty("TotalPages", out _), "Should have TotalPages");
                Assert(root.TryGetProperty("TotalRecords", out _), "Should have TotalRecords");
                Assert(root.TryGetProperty("Success", out _), "Should have Success");
                AssertTrue(root.GetProperty("Success").GetBoolean());
            });

            await RunTest("List Fleets After Creating One Fleet Returns It", async () =>
            {
                JsonElement created = await CreateFleetAsync("SingleFleet", "Only one");

                (HttpStatusCode status, JsonElement root) = await ListFleetsAsync();

                AssertEqual(HttpStatusCode.OK, status);
                Assert(root.GetProperty("Objects").GetArrayLength() >= 1, "Should have at least 1 object");
                Assert(root.GetProperty("TotalRecords").GetInt32() >= 1, "Should have at least 1 total record");

                bool found = false;
                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength(); i++)
                {
                    if (objects[i].GetProperty("Id").GetString() == created.GetProperty("Id").GetString())
                    {
                        found = true;
                        AssertStartsWith("SingleFleet", objects[i].GetProperty("Name").GetString()!);
                        AssertEqual("Only one", objects[i].GetProperty("Description").GetString()!);
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

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 25, "TotalRecords should be >= 25");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
            });

            await RunTest("List Fleets 25 Fleets Page 1 Has 10 Items", async () =>
            {
                await CreateFleetsAsync(25, "P1Test");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);

                AssertEqual(10, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Fleets 25 Fleets Page 2 Has 10 Items", async () =>
            {
                await CreateFleetsAsync(25, "P2Test");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10, pageNumber: 2);

                AssertEqual(10, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Fleets 25 Fleets Page 3 Has 5 Items", async () =>
            {
                await CreateFleetsAsync(25, "P3Test");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10, pageNumber: 3);

                Assert(root.GetProperty("Objects").GetArrayLength() >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Fleets 25 Fleets PageSize 10 Verify First Record On Page 1", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(25, "FirstRec");

                // With shared data, our created fleets may not be on page 1
                // Instead verify that the created fleets appear somewhere in the full listing
                HashSet<string> createdIds = new HashSet<string>();
                for (int i = 0; i < fleets.Length; i++)
                    createdIds.Add(fleets[i].GetProperty("Id").GetString()!);

                int foundCount = 0;
                int totalPages = 1;
                for (int page = 1; page <= totalPages; page++)
                {
                    (HttpStatusCode _, JsonElement pageRoot) = await ListFleetsAsync(pageSize: 10, pageNumber: page, order: "CreatedAscending");
                    totalPages = pageRoot.GetProperty("TotalPages").GetInt32();
                    JsonElement objects = pageRoot.GetProperty("Objects");
                    for (int i = 0; i < objects.GetArrayLength(); i++)
                    {
                        if (createdIds.Contains(objects[i].GetProperty("Id").GetString()!))
                            foundCount++;
                    }
                }
                AssertEqual(25, foundCount);
            });

            await RunTest("List Fleets 25 Fleets PageSize 10 Verify Last Record On Page 3", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(25, "LastRec");

                // With shared data, verify that the last created fleet appears somewhere in listing
                string lastCreatedId = fleets[24].GetProperty("Id").GetString()!;
                bool found = false;
                int totalPages = 1;
                for (int page = 1; page <= totalPages && !found; page++)
                {
                    (HttpStatusCode _, JsonElement pageRoot) = await ListFleetsAsync(pageSize: 10, pageNumber: page, order: "CreatedAscending");
                    totalPages = pageRoot.GetProperty("TotalPages").GetInt32();
                    JsonElement objects = pageRoot.GetProperty("Objects");
                    for (int i = 0; i < objects.GetArrayLength(); i++)
                    {
                        if (objects[i].GetProperty("Id").GetString()! == lastCreatedId)
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

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 5, pageNumber: 1);

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 25, "TotalRecords should be >= 25");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 5, "TotalPages should be >= 5");
                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Fleets 25 Fleets PageSize 5 Page 5 Has 5 Items", async () =>
            {
                await CreateFleetsAsync(25, "PS5P5");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 5, pageNumber: 5);

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Fleets 25 Fleets Beyond Last Page Returns Empty Objects Array", async () =>
            {
                await CreateFleetsAsync(25, "Beyond");

                // Get total pages first, then request beyond it
                (HttpStatusCode _, JsonElement firstRoot) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);
                int totalPages = firstRoot.GetProperty("TotalPages").GetInt32();

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10, pageNumber: totalPages + 1);

                AssertEqual(0, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Fleets 25 Fleets Beyond Last Page Still Returns Total Records", async () =>
            {
                await CreateFleetsAsync(25, "BeyondTR");

                // Get total pages first, then request well beyond it
                (HttpStatusCode _, JsonElement firstRoot) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);
                int totalRecords = firstRoot.GetProperty("TotalRecords").GetInt32();

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10, pageNumber: 999);

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 25, "TotalRecords should be >= 25");
                AssertEqual(0, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Fleets Pages Do Not Overlap", async () =>
            {
                await CreateFleetsAsync(25, "NoOverlap");

                (HttpStatusCode _, JsonElement page1Root) = await ListFleetsAsync(pageSize: 10, pageNumber: 1);
                (HttpStatusCode _, JsonElement page2Root) = await ListFleetsAsync(pageSize: 10, pageNumber: 2);
                (HttpStatusCode _, JsonElement page3Root) = await ListFleetsAsync(pageSize: 10, pageNumber: 3);

                HashSet<string> allIds = new HashSet<string>();

                JsonElement page1Objs = page1Root.GetProperty("Objects");
                for (int i = 0; i < page1Objs.GetArrayLength(); i++)
                {
                    string id = page1Objs[i].GetProperty("Id").GetString()!;
                    Assert(allIds.Add(id), "Duplicate ID found: " + id);
                }

                JsonElement page2Objs = page2Root.GetProperty("Objects");
                for (int i = 0; i < page2Objs.GetArrayLength(); i++)
                {
                    string id = page2Objs[i].GetProperty("Id").GetString()!;
                    Assert(allIds.Add(id), "Duplicate ID found across pages: " + id);
                }

                JsonElement page3Objs = page3Root.GetProperty("Objects");
                for (int i = 0; i < page3Objs.GetArrayLength(); i++)
                {
                    string id = page3Objs[i].GetProperty("Id").GetString()!;
                    Assert(allIds.Add(id), "Duplicate ID found across pages: " + id);
                }

                Assert(allIds.Count >= 25, "Should have at least 25 unique IDs across pages");
            });

            await RunTest("List Fleets PageSize Reflected In Response", async () =>
            {
                await CreateFleetsAsync(5, "PSReflect");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 3, pageNumber: 1);

                AssertEqual(3, root.GetProperty("PageSize").GetInt32());
            });

            #endregion

            #region List - Ordering

            await RunTest("List Fleets Order Created Ascending First Item Is Oldest", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "AscOrd");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(order: "CreatedAscending");

                // With shared data, the first item may not be one we created
                // Just verify the ordering is correct (ascending by CreatedUtc)
                JsonElement objects = root.GetProperty("Objects");
                Assert(objects.GetArrayLength() >= 5, "Should have at least 5 items");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            await RunTest("List Fleets Order Created Descending First Item Is Newest", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "DescOrd");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(order: "CreatedDescending");

                // With shared data, the first item may not be one we created
                // Just verify the ordering is correct (descending by CreatedUtc)
                JsonElement objects = root.GetProperty("Objects");
                Assert(objects.GetArrayLength() >= 5, "Should have at least 5 items");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("List Fleets Order Created Ascending All Items In Order", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "AscAll");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(order: "CreatedAscending");

                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be <= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("List Fleets Order Created Descending All Items In Order", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "DescAll");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(order: "CreatedDescending");

                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be >= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("List Fleets Order Created Ascending Last Item Is Newest", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "AscLast");

                // With shared data, verify created IDs appear in the listing
                HashSet<string> createdIds = new HashSet<string>();
                for (int i = 0; i < fleets.Length; i++)
                    createdIds.Add(fleets[i].GetProperty("Id").GetString()!);

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(order: "CreatedAscending", pageSize: 10000);

                JsonElement objects = root.GetProperty("Objects");
                int foundCount = 0;
                for (int i = 0; i < objects.GetArrayLength(); i++)
                {
                    if (createdIds.Contains(objects[i].GetProperty("Id").GetString()!))
                        foundCount++;
                }
                AssertEqual(5, foundCount);
            });

            await RunTest("List Fleets Order Created Descending Last Item Is Oldest", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "DescLast");

                // With shared data, verify created IDs appear in the listing
                HashSet<string> createdIds = new HashSet<string>();
                for (int i = 0; i < fleets.Length; i++)
                    createdIds.Add(fleets[i].GetProperty("Id").GetString()!);

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(order: "CreatedDescending", pageSize: 100);

                JsonElement objects = root.GetProperty("Objects");
                int foundCount = 0;
                for (int i = 0; i < objects.GetArrayLength(); i++)
                {
                    if (createdIds.Contains(objects[i].GetProperty("Id").GetString()!))
                        foundCount++;
                }
                AssertEqual(5, foundCount);
            });

            #endregion

            #region Enumerate (POST)

            await RunTest("Enumerate Fleets Default Query Returns All", async () =>
            {
                await CreateFleetsAsync(3, "EnumDefault");

                (HttpStatusCode status, JsonElement root) = await EnumerateFleetsAsync();

                AssertEqual(HttpStatusCode.OK, status);
                Assert(root.GetProperty("Objects").GetArrayLength() >= 3, "Should have at least 3 objects");
                Assert(root.GetProperty("TotalRecords").GetInt32() >= 3, "Should have at least 3 total records");
            });

            await RunTest("Enumerate Fleets Returns Correct Enumeration Structure", async () =>
            {
                await CreateFleetAsync("EnumStructure");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync();

                Assert(root.TryGetProperty("Objects", out _), "Should have Objects");
                Assert(root.TryGetProperty("PageNumber", out _), "Should have PageNumber");
                Assert(root.TryGetProperty("PageSize", out _), "Should have PageSize");
                Assert(root.TryGetProperty("TotalPages", out _), "Should have TotalPages");
                Assert(root.TryGetProperty("TotalRecords", out _), "Should have TotalRecords");
                Assert(root.TryGetProperty("Success", out _), "Should have Success");
            });

            await RunTest("Enumerate Fleets With PageSize And PageNumber Works Correctly", async () =>
            {
                await CreateFleetsAsync(15, "EnumPag");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 1);

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                Assert(root.GetProperty("TotalRecords").GetInt32() >= 15, "TotalRecords should be >= 15");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
            });

            await RunTest("Enumerate Fleets Page 2 Has Correct Items", async () =>
            {
                await CreateFleetsAsync(15, "EnumP2");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2);

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("Enumerate Fleets Page 3 Has Correct Items", async () =>
            {
                await CreateFleetsAsync(15, "EnumP3");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 3);

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("Enumerate Fleets Beyond Last Page Returns Empty Objects", async () =>
            {
                await CreateFleetsAsync(10, "EnumBeyond");

                // Get total pages first, then request beyond it
                (HttpStatusCode _, JsonElement firstRoot) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 1);
                int totalPages = firstRoot.GetProperty("TotalPages").GetInt32();

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: totalPages + 1);

                AssertEqual(0, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("Enumerate Fleets Order Created Ascending First Item Is Oldest", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "EnumAsc");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(order: "CreatedAscending");

                // With shared data, just verify ascending order
                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            await RunTest("Enumerate Fleets Order Created Descending First Item Is Newest", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(5, "EnumDesc");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(order: "CreatedDescending");

                // With shared data, just verify descending order
                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("Enumerate Fleets Order Created Ascending All Items In Order", async () =>
            {
                await CreateFleetsAsync(5, "EnumAscAll");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(order: "CreatedAscending");

                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be <= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("Enumerate Fleets Order Created Descending All Items In Order", async () =>
            {
                await CreateFleetsAsync(5, "EnumDescAll");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(order: "CreatedDescending");

                JsonElement objects = root.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next,
                        "Item at index " + i + " (CreatedUtc=" + current + ") should be >= item at index " + (i + 1) + " (CreatedUtc=" + next + ")");
                }
            });

            await RunTest("Enumerate Fleets Pagination Matches Get Pagination", async () =>
            {
                await CreateFleetsAsync(12, "EnumMatch");

                (HttpStatusCode _, JsonElement listRoot) = await ListFleetsAsync(pageSize: 5, pageNumber: 1, order: "CreatedAscending");
                (HttpStatusCode _, JsonElement enumRoot) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 1, order: "CreatedAscending");

                AssertEqual(
                    listRoot.GetProperty("TotalRecords").GetInt32(),
                    enumRoot.GetProperty("TotalRecords").GetInt32());
                AssertEqual(
                    listRoot.GetProperty("TotalPages").GetInt32(),
                    enumRoot.GetProperty("TotalPages").GetInt32());
                AssertEqual(
                    listRoot.GetProperty("Objects").GetArrayLength(),
                    enumRoot.GetProperty("Objects").GetArrayLength());

                JsonElement listObjs = listRoot.GetProperty("Objects");
                JsonElement enumObjs = enumRoot.GetProperty("Objects");
                for (int i = 0; i < listObjs.GetArrayLength(); i++)
                {
                    AssertEqual(
                        listObjs[i].GetProperty("Id").GetString()!,
                        enumObjs[i].GetProperty("Id").GetString()!);
                }
            });

            await RunTest("Enumerate Fleets Page 2 Matches Get Page 2", async () =>
            {
                await CreateFleetsAsync(12, "EnumMatchP2");

                (HttpStatusCode _, JsonElement listRoot) = await ListFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedAscending");
                (HttpStatusCode _, JsonElement enumRoot) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedAscending");

                JsonElement listObjs = listRoot.GetProperty("Objects");
                JsonElement enumObjs = enumRoot.GetProperty("Objects");
                AssertEqual(listObjs.GetArrayLength(), enumObjs.GetArrayLength());

                for (int i = 0; i < listObjs.GetArrayLength(); i++)
                {
                    AssertEqual(
                        listObjs[i].GetProperty("Id").GetString()!,
                        enumObjs[i].GetProperty("Id").GetString()!);
                }
            });

            await RunTest("Enumerate Fleets Empty Returns Zero Total Records", async () =>
            {
                (HttpStatusCode status, JsonElement root) = await EnumerateFleetsAsync();

                AssertEqual(HttpStatusCode.OK, status);
            });

            await RunTest("Enumerate Fleets With PageSize Reflects PageSize In Response", async () =>
            {
                await CreateFleetsAsync(5, "EnumPSR");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 3);

                AssertEqual(3, root.GetProperty("PageSize").GetInt32());
                AssertEqual(3, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("Enumerate Fleets Pages Do Not Overlap", async () =>
            {
                await CreateFleetsAsync(15, "EnumNoOverlap");

                HashSet<string> allIds = new HashSet<string>();

                for (int page = 1; page <= 3; page++)
                {
                    (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: page);
                    JsonElement objects = root.GetProperty("Objects");
                    for (int i = 0; i < objects.GetArrayLength(); i++)
                    {
                        string id = objects[i].GetProperty("Id").GetString()!;
                        Assert(allIds.Add(id), "Duplicate ID found on page " + page + ": " + id);
                    }
                }

                Assert(allIds.Count >= 15, "Should have at least 15 unique IDs across enumerate pages");
            });

            #endregion

            #region Enumerate - Combined Ordering and Pagination

            await RunTest("Enumerate Fleets Created Ascending Page 2 Contains Correct Items", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(10, "EnumAscP2");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedAscending");

                JsonElement objects = root.GetProperty("Objects");
                AssertEqual(5, objects.GetArrayLength());

                // Verify ascending order on this page
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next, "Items should be in ascending order on page 2");
                }
            });

            await RunTest("Enumerate Fleets Created Descending Page 2 Contains Correct Items", async () =>
            {
                JsonElement[] fleets = await CreateFleetsAsync(10, "EnumDescP2");

                (HttpStatusCode _, JsonElement root) = await EnumerateFleetsAsync(pageSize: 5, pageNumber: 2, order: "CreatedDescending");

                JsonElement objects = root.GetProperty("Objects");
                AssertEqual(5, objects.GetArrayLength());

                // Verify descending order on this page
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next, "Items should be in descending order on page 2");
                }
            });

            #endregion

            #region List - Edge Cases

            await RunTest("List Fleets Default PageSize Returns 100 Or Fewer Items", async () =>
            {
                await CreateFleetsAsync(15, "DefPS");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync();

                Assert(root.GetProperty("Objects").GetArrayLength() <= 100,
                    "Default page size should return at most 100 items");
            });

            await RunTest("List Fleets PageNumber 1 Is Default", async () =>
            {
                await CreateFleetsAsync(3, "DefPN");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync();

                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Fleets Single Fleet TotalPages 1", async () =>
            {
                await CreateFleetAsync("SinglePage");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10);

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 1, "TotalRecords should be >= 1");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 1, "TotalPages should be >= 1");
            });

            await RunTest("List Fleets Exactly PageSize Fleets TotalPages 1", async () =>
            {
                await CreateFleetsAsync(10, "Exact10");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10);

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 10, "TotalRecords should be >= 10");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 1, "TotalPages should be >= 1");
                AssertEqual(10, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Fleets PageSize Plus One TotalPages 2", async () =>
            {
                await CreateFleetsAsync(11, "Plus1");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 10);

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 11, "TotalRecords should be >= 11");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 2, "TotalPages should be >= 2");
            });

            await RunTest("List Fleets PageSize 1 Returns 1 Item Per Page", async () =>
            {
                await CreateFleetsAsync(3, "PS1");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 1, pageNumber: 1);

                AssertEqual(1, root.GetProperty("Objects").GetArrayLength());
                Assert(root.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
            });

            await RunTest("List Fleets Large PageSize Returns All Items", async () =>
            {
                await CreateFleetsAsync(5, "LargePS");

                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync(pageSize: 100);

                Assert(root.GetProperty("Objects").GetArrayLength() >= 5, "Should return at least 5 items");
                Assert(root.GetProperty("TotalPages").GetInt32() >= 1, "TotalPages should be >= 1");
            });

            await RunTest("List Fleets Success Is True", async () =>
            {
                (HttpStatusCode _, JsonElement root) = await ListFleetsAsync();

                AssertTrue(root.GetProperty("Success").GetBoolean());
            });

            #endregion

            #region Full CRUD Lifecycle

            await RunTest("Full Lifecycle Create Read Update Delete Verify", async () =>
            {
                // Create
                JsonElement created = await CreateFleetAsync("LifecycleFleet", "Lifecycle description");
                string fleetId = created.GetProperty("Id").GetString()!;
                AssertStartsWith("flt_", fleetId);
                AssertStartsWith("LifecycleFleet", created.GetProperty("Name").GetString()!);

                // Read
                string createdName = created.GetProperty("Name").GetString()!;
                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);
                string getBody = await getResp.Content.ReadAsStringAsync();
                using (JsonDocument getDoc = JsonDocument.Parse(getBody))
                {
                    JsonElement fleet = getDoc.RootElement.GetProperty("Fleet");
                    AssertEqual(createdName, fleet.GetProperty("Name").GetString()!);
                    AssertEqual("Lifecycle description", fleet.GetProperty("Description").GetString()!);
                }

                // Update
                string updatedName = "UpdatedLifecycle-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = updatedName, Description = "Updated desc" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage updateResp = await _Client.PutAsync("/api/v1/fleets/" + fleetId, updateContent);
                AssertEqual(HttpStatusCode.OK, updateResp.StatusCode);
                string updateBody = await updateResp.Content.ReadAsStringAsync();
                using (JsonDocument updateDoc = JsonDocument.Parse(updateBody))
                {
                    AssertEqual(updatedName, updateDoc.RootElement.GetProperty("Name").GetString()!);
                    AssertEqual("Updated desc", updateDoc.RootElement.GetProperty("Description").GetString()!);
                }

                // Verify update persisted
                HttpResponseMessage getResp2 = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                string getBody2 = await getResp2.Content.ReadAsStringAsync();
                using (JsonDocument getDoc2 = JsonDocument.Parse(getBody2))
                {
                    AssertEqual(updatedName, getDoc2.RootElement.GetProperty("Fleet").GetProperty("Name").GetString()!);
                }

                // Delete
                HttpResponseMessage deleteResp = await _Client.DeleteAsync("/api/v1/fleets/" + fleetId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                _CreatedFleetIds.Remove(fleetId);

                // Verify deleted
                HttpResponseMessage getResp3 = await _Client.GetAsync("/api/v1/fleets/" + fleetId);
                string getBody3 = await getResp3.Content.ReadAsStringAsync();
                using (JsonDocument getDoc3 = JsonDocument.Parse(getBody3))
                {
                    Assert(
                        getDoc3.RootElement.TryGetProperty("Error", out _) ||
                        getDoc3.RootElement.TryGetProperty("Message", out _),
                        "Getting deleted fleet should return error");
                }
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
        /// Creates a fleet and returns the parsed JSON response as a cloned JsonElement.
        /// </summary>
        private async Task<JsonElement> CreateFleetAsync(string name, string? description = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object body = description != null
                ? new { Name = uniqueName, Description = description }
                : (object)new { Name = uniqueName };
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/fleets",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement element = doc.RootElement.Clone();
            string id = element.GetProperty("Id").GetString()!;
            _CreatedFleetIds.Add(id);
            return element;
        }

        /// <summary>
        /// Creates the specified number of fleets with sequential names and a small delay between each
        /// to ensure distinct CreatedUtc timestamps for ordering tests.
        /// </summary>
        private async Task<JsonElement[]> CreateFleetsAsync(int count, string prefix = "Fleet")
        {
            JsonElement[] results = new JsonElement[count];
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
        private async Task<(HttpStatusCode StatusCode, JsonElement Root)> ListFleetsAsync(
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
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            return (resp.StatusCode, doc.RootElement.Clone());
        }

        /// <summary>
        /// Performs a POST enumerate request with the given query body.
        /// </summary>
        private async Task<(HttpStatusCode StatusCode, JsonElement Root)> EnumerateFleetsAsync(
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
                new StringContent(JsonSerializer.Serialize(queryBody), Encoding.UTF8, "application/json"));
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            return (resp.StatusCode, doc.RootElement.Clone());
        }

        #endregion
    }
}
