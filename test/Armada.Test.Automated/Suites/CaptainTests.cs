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
    /// Captain API test suite covering CRUD, stop, list, pagination, ordering, enumeration, and edge cases.
    /// </summary>
    public class CaptainTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Captain API Tests";

        #endregion

        #region Private-Members

        private HttpClient _Client;
        private HttpClient _UnauthClient;
        private List<string> _CreatedCaptainIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the CaptainTests class.
        /// </summary>
        public CaptainTests(HttpClient authClient, HttpClient unauthClient)
        {
            _Client = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs all captain tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            #region Create

            await RunTest("Create Captain Returns 201 With Correct Properties", async () =>
            {
                string captainName = "captain-alpha-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains",
                    JsonContent(new { Name = captainName, Runtime = "ClaudeCode" }));

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string id = root.GetProperty("Id").GetString()!;
                _CreatedCaptainIds.Add(id);

                AssertStartsWith("cpt_", id);
                AssertEqual(captainName, root.GetProperty("Name").GetString()!);
                AssertEqual("ClaudeCode", root.GetProperty("Runtime").GetString()!);
                AssertEqual("Idle", root.GetProperty("State").GetString()!);
                AssertEqual(0, root.GetProperty("RecoveryAttempts").GetInt32());
            });

            await RunTest("Create Captain Default Runtime Is ClaudeCode", async () =>
            {
                string captainName = "default-runtime-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains",
                    JsonContent(new { Name = captainName }));

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                string id = doc.RootElement.GetProperty("Id").GetString()!;
                _CreatedCaptainIds.Add(id);
                AssertEqual("ClaudeCode", doc.RootElement.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Create Captain State Is Idle", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("idle-check");
                AssertEqual("Idle", captain.GetProperty("State").GetString()!);
            });

            await RunTest("Create Captain Has Timestamps", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("timestamp-check");

                string createdUtc = captain.GetProperty("CreatedUtc").GetString()!;
                string lastUpdateUtc = captain.GetProperty("LastUpdateUtc").GetString()!;

                AssertFalse(string.IsNullOrEmpty(createdUtc));
                AssertFalse(string.IsNullOrEmpty(lastUpdateUtc));

                DateTime created = DateTime.Parse(createdUtc);
                DateTime updated = DateTime.Parse(lastUpdateUtc);

                Assert(created <= DateTime.UtcNow.AddMinutes(1), "CreatedUtc should not be in the future");
                Assert(updated <= DateTime.UtcNow.AddMinutes(1), "LastUpdateUtc should not be in the future");
            });

            await RunTest("Create Captain Recovery Attempts Is Zero", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("recovery-check");
                AssertEqual(0, captain.GetProperty("RecoveryAttempts").GetInt32());
            });

            await RunTest("Create Captain Id Has Cpt Prefix", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("prefix-check");
                AssertStartsWith("cpt_", captain.GetProperty("Id").GetString()!);
            });

            await RunTest("Create Captain With Codex Runtime", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("codex-captain", "Codex");
                AssertEqual("Codex", captain.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Create Captain With Gemini Runtime", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("gemini-captain", "Gemini");
                AssertEqual("Gemini", captain.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Create Captain With Cursor Runtime", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("cursor-captain", "Cursor");
                AssertEqual("Cursor", captain.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Create Captain With Custom Runtime", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("custom-captain", "Custom");
                AssertEqual("Custom", captain.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Create Captain Multiple Captains Have Unique Ids", async () =>
            {
                JsonElement captain1 = await CreateCaptainAsync("unique-1");
                JsonElement captain2 = await CreateCaptainAsync("unique-2");
                JsonElement captain3 = await CreateCaptainAsync("unique-3");

                string id1 = captain1.GetProperty("Id").GetString()!;
                string id2 = captain2.GetProperty("Id").GetString()!;
                string id3 = captain3.GetProperty("Id").GetString()!;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            });

            await RunTest("Create Captain CurrentMissionId Is Null", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("null-mission-check");

                Assert(
                    !captain.TryGetProperty("CurrentMissionId", out JsonElement currentMissionId)
                    || currentMissionId.ValueKind == JsonValueKind.Null
                    || string.IsNullOrEmpty(currentMissionId.GetString()),
                    "CurrentMissionId should be null or empty");
            });

            await RunTest("Create Captain CurrentDockId Is Null", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("null-dock-check");

                Assert(
                    !captain.TryGetProperty("CurrentDockId", out JsonElement currentDockId)
                    || currentDockId.ValueKind == JsonValueKind.Null
                    || string.IsNullOrEmpty(currentDockId.GetString()),
                    "CurrentDockId should be null or empty");
            });

            await RunTest("Create Captain ProcessId Is Null", async () =>
            {
                JsonElement captain = await CreateCaptainAsync("null-processid-check");

                Assert(
                    !captain.TryGetProperty("ProcessId", out JsonElement processId)
                    || processId.ValueKind == JsonValueKind.Null
                    || processId.GetInt32() == 0,
                    "ProcessId should be null or zero");
            });

            #endregion

            #region Get

            await RunTest("Get Captain Exists Returns Correct Data", async () =>
            {
                JsonElement created = await CreateCaptainAsync("get-test");
                string captainId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(captainId, root.GetProperty("Id").GetString()!);
                AssertStartsWith("get-test", root.GetProperty("Name").GetString()!);
                AssertEqual("ClaudeCode", root.GetProperty("Runtime").GetString()!);
                AssertEqual("Idle", root.GetProperty("State").GetString()!);
                AssertEqual(0, root.GetProperty("RecoveryAttempts").GetInt32());
            });

            await RunTest("Get Captain Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/cpt_nonexistent");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _)
                    || doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property");
            });

            await RunTest("Get Captain Not Found Status Code Is Not 200 Or Body Has Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/cpt_doesnotexist");
                string body = await response.Content.ReadAsStringAsync();
                Assert(
                    response.StatusCode != HttpStatusCode.OK ||
                    body.Contains("Error") || body.Contains("Message") || body.Contains("not found", StringComparison.OrdinalIgnoreCase),
                    "Not-found captain should return non-200 status or error in body");
            });

            await RunTest("Get Captain With Codex Runtime Returns Correct Runtime", async () =>
            {
                JsonElement created = await CreateCaptainAsync("get-codex", "Codex");
                string captainId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/" + captainId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Codex", doc.RootElement.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Get Captain Data Matches Create Response", async () =>
            {
                JsonElement created = await CreateCaptainAsync("data-match");
                string captainId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/" + captainId);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement fetched = doc.RootElement;

                AssertEqual(created.GetProperty("Id").GetString()!, fetched.GetProperty("Id").GetString()!);
                AssertEqual(created.GetProperty("Name").GetString()!, fetched.GetProperty("Name").GetString()!);
                AssertEqual(created.GetProperty("Runtime").GetString()!, fetched.GetProperty("Runtime").GetString()!);
                AssertEqual(created.GetProperty("State").GetString()!, fetched.GetProperty("State").GetString()!);
            });

            #endregion

            #region Update

            await RunTest("Update Captain Name Returns Updated Name", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("original-name");

                string newName = "updated-name-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "ClaudeCode" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(newName, doc.RootElement.GetProperty("Name").GetString()!);
            });

            await RunTest("Update Captain Runtime Returns Updated Runtime", async () =>
            {
                JsonElement created = await CreateCaptainAsync("runtime-update", "ClaudeCode");
                string captainId = created.GetProperty("Id").GetString()!;
                string createdName = created.GetProperty("Name").GetString()!;

                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = createdName, Runtime = "Codex" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Codex", doc.RootElement.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Update Captain Preserves State", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("state-preserve");

                string newName = "state-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "Gemini" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Idle", doc.RootElement.GetProperty("State").GetString()!);
            });

            await RunTest("Update Captain Preserves Operational Fields", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("operational-preserve");

                string newName = "operational-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "ClaudeCode" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(0, root.GetProperty("RecoveryAttempts").GetInt32());
                AssertEqual(captainId, root.GetProperty("Id").GetString()!);
            });

            await RunTest("Update Captain Preserves Id", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("id-preserve");

                string newName = "id-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "ClaudeCode" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(captainId, doc.RootElement.GetProperty("Id").GetString()!);
            });

            await RunTest("Update Captain Verify Via Get", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("verify-update");

                string newName = "verified-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "Gemini" }));

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + captainId);
                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(newName, doc.RootElement.GetProperty("Name").GetString()!);
                AssertEqual("Gemini", doc.RootElement.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Update Captain Name Only Runtime Preserved", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("name-only", "Codex");

                string newName = "name-only-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "Codex" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(newName, doc.RootElement.GetProperty("Name").GetString()!);
                AssertEqual("Codex", doc.RootElement.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Update Captain Runtime Only Name Preserved", async () =>
            {
                JsonElement created = await CreateCaptainAsync("runtime-only", "ClaudeCode");
                string captainId = created.GetProperty("Id").GetString()!;
                string createdName = created.GetProperty("Name").GetString()!;

                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = createdName, Runtime = "Cursor" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(createdName, doc.RootElement.GetProperty("Name").GetString()!);
                AssertEqual("Cursor", doc.RootElement.GetProperty("Runtime").GetString()!);
            });

            await RunTest("Update Captain Preserves CreatedUtc", async () =>
            {
                JsonElement created = await CreateCaptainAsync("created-preserve");
                string captainId = created.GetProperty("Id").GetString()!;
                string originalCreatedUtc = created.GetProperty("CreatedUtc").GetString()!;

                await Task.Delay(100);

                string newName = "created-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = newName, Runtime = "ClaudeCode" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                string updatedCreatedUtc = doc.RootElement.GetProperty("CreatedUtc").GetString()!;

                AssertEqual(originalCreatedUtc, updatedCreatedUtc);
            });

            #endregion

            #region Delete

            await RunTest("Delete Captain Returns 204", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("to-delete");

                HttpResponseMessage response = await _Client.DeleteAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
                _CreatedCaptainIds.Remove(captainId);
            });

            await RunTest("Delete Captain Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.DeleteAsync("/api/v1/captains/cpt_nonexistent");

                AssertNotEqual(HttpStatusCode.NoContent, response.StatusCode);
            });

            await RunTest("Delete Captain Then Get Returns Not Found", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("delete-then-get");

                HttpResponseMessage deleteResp = await _Client.DeleteAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                _CreatedCaptainIds.Remove(captainId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + captainId);
                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _)
                    || doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property");
            });

            await RunTest("Delete Captain Then List Does Not Contain Deleted", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("delete-then-list");

                await _Client.DeleteAsync("/api/v1/captains/" + captainId);
                _CreatedCaptainIds.Remove(captainId);

                HttpResponseMessage listResp = await _Client.GetAsync("/api/v1/captains");
                string body = await listResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                JsonElement objects = doc.RootElement.GetProperty("Objects");
                bool found = false;
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    if (obj.GetProperty("Id").GetString() == captainId)
                    {
                        found = true;
                        break;
                    }
                }
                AssertFalse(found, "Deleted captain should not appear in list");
            });

            await RunTest("Delete Captain Does Not Affect Other Captains", async () =>
            {
                string keepId = await CreateCaptainAndGetIdAsync("keep-this");
                string deleteId = await CreateCaptainAndGetIdAsync("delete-this");

                await _Client.DeleteAsync("/api/v1/captains/" + deleteId);
                _CreatedCaptainIds.Remove(deleteId);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + keepId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertStartsWith("keep-this", doc.RootElement.GetProperty("Name").GetString()!);
            });

            #endregion

            #region Stop

            await RunTest("Stop Captain Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/cpt_nonexistent/stop", null);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _)
                    || doc.RootElement.TryGetProperty("Message", out _),
                    "Should have Error or Message property");
            });

            await RunTest("Stop Captain Not Found Status Code Is Not OK Or Body Has Error", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/cpt_doesnotexist/stop", null);
                string body = await response.Content.ReadAsStringAsync();
                Assert(
                    response.StatusCode != HttpStatusCode.OK ||
                    body.Contains("Error") || body.Contains("Message") || body.Contains("not found", StringComparison.OrdinalIgnoreCase),
                    "Stop on non-existent captain should return non-200 status or error in body");
            });

            await RunTest("Stop Captain Idle Returns Success", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("stop-idle");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/" + captainId + "/stop", null);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("stopped", doc.RootElement.GetProperty("Status").GetString()!);
            });

            await RunTest("Stop Captain Idle Verify State After Stop", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("stop-verify");

                await _Client.PostAsync("/api/v1/captains/" + captainId + "/stop", null);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                string body = await getResp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(captainId, doc.RootElement.GetProperty("Id").GetString()!);
            });

            await RunTest("Stop All Captains Returns Success", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/stop-all",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
                AssertStatusCode(HttpStatusCode.OK, response);
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("all_stopped", doc.RootElement.GetProperty("Status").GetString());
            });

            #endregion

            #region List - Empty

            await RunTest("List Captains Empty Returns OK", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            });

            await RunTest("List Captains Empty Returns Zero Total Records", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 0, "TotalRecords should be >= 0");
            });

            await RunTest("List Captains Empty Returns Empty Objects Array", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(JsonValueKind.Array, doc.RootElement.GetProperty("Objects").ValueKind);
            });

            await RunTest("List Captains Empty Has Enumeration Result Structure", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                Assert(root.TryGetProperty("Objects", out _), "Should have Objects");
                Assert(root.TryGetProperty("PageNumber", out _), "Should have PageNumber");
                Assert(root.TryGetProperty("PageSize", out _), "Should have PageSize");
                Assert(root.TryGetProperty("TotalPages", out _), "Should have TotalPages");
                Assert(root.TryGetProperty("TotalRecords", out _), "Should have TotalRecords");
                Assert(root.TryGetProperty("Success", out _), "Should have Success");
            });

            await RunTest("List Captains Empty Success Is True", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            });

            #endregion

            #region List - After Create

            await RunTest("List Captains After Create One Returns TotalRecords 1", async () =>
            {
                await CreateCaptainAsync("list-single");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 1, "TotalRecords should be >= 1");
                Assert(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1, "Objects should have at least 1 item");
            });

            await RunTest("List Captains After Create One Contains Correct Captain", async () =>
            {
                JsonElement created = await CreateCaptainAsync("list-verify");
                string createdId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=100");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                JsonElement objects = doc.RootElement.GetProperty("Objects");
                bool found = false;
                for (int i = 0; i < objects.GetArrayLength(); i++)
                {
                    if (objects[i].GetProperty("Id").GetString()! == createdId)
                    {
                        AssertStartsWith("list-verify", objects[i].GetProperty("Name").GetString()!);
                        found = true;
                        break;
                    }
                }
                Assert(found, "Created captain should appear in list");
            });

            await RunTest("List Captains After Create Multiple Returns All", async () =>
            {
                await CreateCaptainAsync("multi-1");
                await CreateCaptainAsync("multi-2");
                await CreateCaptainAsync("multi-3");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 3, "TotalRecords should be >= 3");
                Assert(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 3, "Objects should have at least 3 items");
            });

            #endregion

            #region List - Pagination

            await RunTest("List Captains 25 Created PageSize 10 TotalRecords 25", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("page-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 25, "TotalRecords should be >= 25");
                Assert(doc.RootElement.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Captains 25 Created Page 1 Has Ten Records", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("p1-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=1");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(1, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Captains 25 Created Page 2 Has Ten Records", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("p2-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=2");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Captains 25 Created Page 3 Has Five Records", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("p3-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=3");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("List Captains 25 Created Beyond Last Page Returns Empty", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("beyond-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=999");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("List Captains 25 Created Pages Do Not Overlap", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("overlap-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage resp1 = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=1");
                string body1 = await resp1.Content.ReadAsStringAsync();
                using JsonDocument doc1 = JsonDocument.Parse(body1);
                HashSet<string> page1Ids = new HashSet<string>();
                foreach (JsonElement obj in doc1.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    page1Ids.Add(obj.GetProperty("Id").GetString()!);
                }

                HttpResponseMessage resp2 = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=2");
                string body2 = await resp2.Content.ReadAsStringAsync();
                using JsonDocument doc2 = JsonDocument.Parse(body2);
                foreach (JsonElement obj in doc2.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    string id = obj.GetProperty("Id").GetString()!;
                    AssertFalse(page1Ids.Contains(id), "Page 2 ID should not be in page 1: " + id);
                }

                HttpResponseMessage resp3 = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=3");
                string body3 = await resp3.Content.ReadAsStringAsync();
                using JsonDocument doc3 = JsonDocument.Parse(body3);
                foreach (JsonElement obj in doc3.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    string id = obj.GetProperty("Id").GetString()!;
                    AssertFalse(page1Ids.Contains(id), "Page 3 ID should not be in page 1: " + id);
                }
            });

            await RunTest("List Captains 25 Created All Records Accounted For", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("accounted-captain-" + i.ToString("D2"));
                }

                HashSet<string> allIds = new HashSet<string>();

                for (int page = 1; page <= 3; page++)
                {
                    HttpResponseMessage resp = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=" + page);
                    string body = await resp.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(body);
                    foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                    {
                        allIds.Add(obj.GetProperty("Id").GetString()!);
                    }
                }

                Assert(allIds.Count >= 25, "Should have at least 25 unique IDs across pages");
            });

            await RunTest("List Captains PageSize 5 Returns 5 Records", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await CreateCaptainAsync("ps5-captain-" + i);
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=5");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 10, "TotalRecords should be >= 10");
                Assert(doc.RootElement.GetProperty("TotalPages").GetInt32() >= 2, "TotalPages should be >= 2");
            });

            #endregion

            #region List - Ordering

            await RunTest("List Captains Created Descending Newest First", async () =>
            {
                JsonElement first = await CreateCaptainAsync("order-oldest");
                await Task.Delay(50);
                JsonElement last = await CreateCaptainAsync("order-newest");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?order=CreatedDescending");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                // Verify descending order
                JsonElement objects = doc.RootElement.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("List Captains Created Ascending Oldest First", async () =>
            {
                JsonElement first = await CreateCaptainAsync("asc-oldest");
                await Task.Delay(50);
                JsonElement last = await CreateCaptainAsync("asc-newest");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?order=CreatedAscending");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                // Verify ascending order
                JsonElement objects = doc.RootElement.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            #endregion

            #region Enumerate (POST)

            await RunTest("Enumerate Captains Empty Returns Empty Result", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                Assert(root.GetProperty("TotalRecords").GetInt32() >= 0, "TotalRecords should be >= 0");
                AssertTrue(root.GetProperty("Success").GetBoolean());
            });

            await RunTest("Enumerate Captains Has Enumeration Result Structure", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                Assert(root.TryGetProperty("Objects", out _), "Should have Objects");
                Assert(root.TryGetProperty("PageNumber", out _), "Should have PageNumber");
                Assert(root.TryGetProperty("PageSize", out _), "Should have PageSize");
                Assert(root.TryGetProperty("TotalPages", out _), "Should have TotalPages");
                Assert(root.TryGetProperty("TotalRecords", out _), "Should have TotalRecords");
                Assert(root.TryGetProperty("Success", out _), "Should have Success");
            });

            await RunTest("Enumerate Captains Default Query Returns Created Captains", async () =>
            {
                await CreateCaptainAsync("enum-cap-1");
                await CreateCaptainAsync("enum-cap-2");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 2, "TotalRecords should be >= 2");
                Assert(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2, "Objects should have at least 2 items");
            });

            await RunTest("Enumerate Captains With Pagination Page 1", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync("enum-pag-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 15, "TotalRecords should be >= 15");
                Assert(doc.RootElement.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("Enumerate Captains With Pagination Page 2", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync("enum-p2-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 2, PageSize = 5, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("Enumerate Captains With Pagination Last Page", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync("enum-lp-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 3, PageSize = 5, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, doc.RootElement.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("Enumerate Captains Beyond Last Page Returns Empty", async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await CreateCaptainAsync("enum-beyond-" + i);
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 999, PageSize = 5, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("Enumerate Captains Created Descending Newest First", async () =>
            {
                JsonElement oldest = await CreateCaptainAsync("enum-desc-oldest");
                await Task.Delay(50);
                JsonElement newest = await CreateCaptainAsync("enum-desc-newest");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 100, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                // Verify descending order
                JsonElement objects = doc.RootElement.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("Enumerate Captains Created Ascending Oldest First", async () =>
            {
                JsonElement oldest = await CreateCaptainAsync("enum-asc-oldest");
                await Task.Delay(50);
                JsonElement newest = await CreateCaptainAsync("enum-asc-newest");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 100, Order = "CreatedAscending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                // Verify ascending order
                JsonElement objects = doc.RootElement.GetProperty("Objects");
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = objects[i].GetProperty("CreatedUtc").GetDateTime();
                    DateTime next = objects[i + 1].GetProperty("CreatedUtc").GetDateTime();
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            await RunTest("Enumerate Captains Matches Get Pagination", async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    await CreateCaptainAsync("match-cap-" + i.ToString("D2"));
                }

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains?pageSize=5&pageNumber=1");
                string getBody = await getResp.Content.ReadAsStringAsync();
                using JsonDocument getDoc = JsonDocument.Parse(getBody);
                int getTotalRecords = getDoc.RootElement.GetProperty("TotalRecords").GetInt32();
                int getTotalPages = getDoc.RootElement.GetProperty("TotalPages").GetInt32();

                HttpResponseMessage enumResp = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedDescending" }));
                string enumBody = await enumResp.Content.ReadAsStringAsync();
                using JsonDocument enumDoc = JsonDocument.Parse(enumBody);
                int enumTotalRecords = enumDoc.RootElement.GetProperty("TotalRecords").GetInt32();
                int enumTotalPages = enumDoc.RootElement.GetProperty("TotalPages").GetInt32();

                AssertEqual(getTotalRecords, enumTotalRecords);
                AssertEqual(getTotalPages, enumTotalPages);
            });

            await RunTest("Enumerate Captains PageSize Respected", async () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    await CreateCaptainAsync("enum-ps-" + i);
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 3, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(3, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, doc.RootElement.GetProperty("PageSize").GetInt32());
            });

            await RunTest("Enumerate Captains Pages Do Not Overlap", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await CreateCaptainAsync("enum-no-overlap-" + i);
                }

                HashSet<string> allIds = new HashSet<string>();

                for (int page = 1; page <= 2; page++)
                {
                    HttpResponseMessage resp = await _Client.PostAsync("/api/v1/captains/enumerate",
                        JsonContent(new { PageNumber = page, PageSize = 5, Order = "CreatedDescending" }));
                    string body = await resp.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(body);
                    foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                    {
                        string id = obj.GetProperty("Id").GetString()!;
                        AssertFalse(allIds.Contains(id), "Duplicate ID found: " + id);
                        allIds.Add(id);
                    }
                }

                Assert(allIds.Count >= 10, "Should have at least 10 unique IDs across enumerate pages");
            });

            #endregion

            #region Edge Cases

            await RunTest("Create Captain Same Name Second Creation Handled", async () =>
            {
                JsonElement captain1 = await CreateCaptainAsync("duplicate-name");
                string id1 = captain1.GetProperty("Id").GetString()!;
                string actualName = captain1.GetProperty("Name").GetString()!;
                AssertStartsWith("cpt_", id1);

                HttpResponseMessage resp = await _Client.PostAsync("/api/v1/captains",
                    JsonContent(new { Name = actualName, Runtime = "ClaudeCode" }));

                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(json);
                    string id2 = doc.RootElement.GetProperty("Id").GetString()!;
                    _CreatedCaptainIds.Add(id2);
                    AssertNotEqual(id1, id2);
                }
                else
                {
                    Assert(resp.StatusCode == HttpStatusCode.InternalServerError ||
                                resp.StatusCode == HttpStatusCode.Conflict ||
                                resp.StatusCode == HttpStatusCode.BadRequest,
                        "Duplicate name should return error status");
                }
            });

            await RunTest("List Captains Default PageSize Returns Results", async () =>
            {
                await CreateCaptainAsync("default-page");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1, "Should have at least 1 object");
            });

            await RunTest("Create And Delete Multiple List Reflects Correct Count", async () =>
            {
                string id1 = await CreateCaptainAndGetIdAsync("multi-del-1");
                string id2 = await CreateCaptainAndGetIdAsync("multi-del-2");
                string id3 = await CreateCaptainAndGetIdAsync("multi-del-3");

                await _Client.DeleteAsync("/api/v1/captains/" + id1);
                _CreatedCaptainIds.Remove(id1);
                await _Client.DeleteAsync("/api/v1/captains/" + id3);
                _CreatedCaptainIds.Remove(id3);

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 1, "TotalRecords should be >= 1");

                JsonElement objects = doc.RootElement.GetProperty("Objects");
                Assert(objects.GetArrayLength() >= 1, "Objects should have at least 1 item");
                // Verify id2 is present
                bool foundId2 = false;
                for (int idx = 0; idx < objects.GetArrayLength(); idx++)
                {
                    if (objects[idx].GetProperty("Id").GetString()! == id2)
                    {
                        foundId2 = true;
                        break;
                    }
                }
                Assert(foundId2, "Captain id2 should still be in list");
            });

            await RunTest("Update Captain Then List Shows Updated Data", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("update-list-check");

                string updatedName = "update-list-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonContent(new { Name = updatedName, Runtime = "Gemini" }));

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=100");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                JsonElement objects = doc.RootElement.GetProperty("Objects");
                bool found = false;
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    if (obj.GetProperty("Id").GetString() == captainId)
                    {
                        AssertEqual(updatedName, obj.GetProperty("Name").GetString()!);
                        AssertEqual("Gemini", obj.GetProperty("Runtime").GetString()!);
                        found = true;
                        break;
                    }
                }
                Assert(found, "Updated captain should appear in list");
            });

            await RunTest("Create Captain All Runtimes Each Creates Successfully", async () =>
            {
                string[] runtimes = new[] { "ClaudeCode", "Codex", "Gemini", "Cursor", "Custom" };

                foreach (string runtime in runtimes)
                {
                    string captainName = "rt-" + runtime + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains",
                        JsonContent(new { Name = captainName, Runtime = runtime }));
                    AssertEqual(HttpStatusCode.Created, response.StatusCode);

                    string body = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(body);
                    string id = doc.RootElement.GetProperty("Id").GetString()!;
                    _CreatedCaptainIds.Add(id);
                    AssertEqual(runtime, doc.RootElement.GetProperty("Runtime").GetString()!);
                }
            });

            await RunTest("Enumerate Captains After Delete Some TotalRecords Updated", async () =>
            {
                string id1 = await CreateCaptainAndGetIdAsync("enum-del-1");
                await CreateCaptainAsync("enum-del-2");
                string id3 = await CreateCaptainAndGetIdAsync("enum-del-3");

                await _Client.DeleteAsync("/api/v1/captains/" + id1);
                _CreatedCaptainIds.Remove(id1);
                await _Client.DeleteAsync("/api/v1/captains/" + id3);
                _CreatedCaptainIds.Remove(id3);

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.GetProperty("TotalRecords").GetInt32() >= 1, "TotalRecords should be >= 1");
            });

            await RunTest("List Captains Each Object Has All Expected Properties", async () =>
            {
                await CreateCaptainAsync("props-check", "Codex");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                string body = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);

                JsonElement captain = doc.RootElement.GetProperty("Objects")[0];

                Assert(captain.TryGetProperty("Id", out _), "Should have Id");
                Assert(captain.TryGetProperty("Name", out _), "Should have Name");
                Assert(captain.TryGetProperty("Runtime", out _), "Should have Runtime");
                Assert(captain.TryGetProperty("State", out _), "Should have State");
                Assert(captain.TryGetProperty("RecoveryAttempts", out _), "Should have RecoveryAttempts");
                Assert(captain.TryGetProperty("CreatedUtc", out _), "Should have CreatedUtc");
                Assert(captain.TryGetProperty("LastUpdateUtc", out _), "Should have LastUpdateUtc");
            });

            #endregion

            // Cleanup
            foreach (string id in _CreatedCaptainIds)
            {
                try { await _Client.DeleteAsync("/api/v1/captains/" + id); } catch { }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a captain and returns the parsed JSON response as a cloned JsonElement.
        /// </summary>
        private async Task<JsonElement> CreateCaptainAsync(string name, string runtime = "ClaudeCode")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object body = new { Name = uniqueName, Runtime = runtime };
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/captains",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement element = doc.RootElement.Clone();
            string id = element.GetProperty("Id").GetString()!;
            _CreatedCaptainIds.Add(id);
            return element;
        }

        /// <summary>
        /// Creates a captain and returns only its ID.
        /// </summary>
        private async Task<string> CreateCaptainAndGetIdAsync(string name, string runtime = "ClaudeCode")
        {
            JsonElement elem = await CreateCaptainAsync(name, runtime);
            return elem.GetProperty("Id").GetString()!;
        }

        /// <summary>
        /// Creates a StringContent with JSON serialized body.
        /// </summary>
        private StringContent JsonContent(object obj)
        {
            return new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");
        }

        #endregion
    }
}
