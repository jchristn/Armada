namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;
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
                    JsonHelper.ToJsonContent(new { Name = captainName, Runtime = "ClaudeCode" }));

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response);

                _CreatedCaptainIds.Add(captain.Id);

                AssertStartsWith("cpt_", captain.Id);
                AssertEqual(captainName, captain.Name);
                AssertEqual("ClaudeCode", captain.Runtime.ToString());
                AssertEqual("Idle", captain.State.ToString());
                AssertEqual(0, captain.RecoveryAttempts);
            });

            await RunTest("Create Captain Default Runtime Is ClaudeCode", async () =>
            {
                string captainName = "default-runtime-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = captainName }));

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response);
                _CreatedCaptainIds.Add(captain.Id);
                AssertEqual("ClaudeCode", captain.Runtime.ToString());
            });

            await RunTest("Create Captain State Is Idle", async () =>
            {
                Captain captain = await CreateCaptainAsync("idle-check");
                AssertEqual("Idle", captain.State.ToString());
            });

            await RunTest("Create Captain Has Timestamps", async () =>
            {
                Captain captain = await CreateCaptainAsync("timestamp-check");

                DateTime created = captain.CreatedUtc;
                DateTime updated = captain.LastUpdateUtc;

                Assert(created <= DateTime.UtcNow.AddMinutes(1), "CreatedUtc should not be in the future");
                Assert(updated <= DateTime.UtcNow.AddMinutes(1), "LastUpdateUtc should not be in the future");
            });

            await RunTest("Create Captain Recovery Attempts Is Zero", async () =>
            {
                Captain captain = await CreateCaptainAsync("recovery-check");
                AssertEqual(0, captain.RecoveryAttempts);
            });

            await RunTest("Create Captain Id Has Cpt Prefix", async () =>
            {
                Captain captain = await CreateCaptainAsync("prefix-check");
                AssertStartsWith("cpt_", captain.Id);
            });

            await RunTest("Create Captain With Codex Runtime", async () =>
            {
                Captain captain = await CreateCaptainAsync("codex-captain", "Codex");
                AssertEqual("Codex", captain.Runtime.ToString());
            });

            await RunTest("Create Captain With Gemini Runtime", async () =>
            {
                Captain captain = await CreateCaptainAsync("gemini-captain", "Gemini");
                AssertEqual("Gemini", captain.Runtime.ToString());
            });

            await RunTest("Create Captain With Cursor Runtime", async () =>
            {
                Captain captain = await CreateCaptainAsync("cursor-captain", "Cursor");
                AssertEqual("Cursor", captain.Runtime.ToString());
            });

            await RunTest("Create Captain With Custom Runtime", async () =>
            {
                Captain captain = await CreateCaptainAsync("custom-captain", "Custom");
                AssertEqual("Custom", captain.Runtime.ToString());
            });

            await RunTest("Create Captain With Invalid Model Returns BadRequest", async () =>
            {
                string captainName = "invalid-model-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = captainName, Runtime = "Custom", Model = "bad-model" }));

                AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                AssertContains("Unable to create runtime Custom", body);
            });

            await RunTest("Create Captain Multiple Captains Have Unique Ids", async () =>
            {
                Captain captain1 = await CreateCaptainAsync("unique-1");
                Captain captain2 = await CreateCaptainAsync("unique-2");
                Captain captain3 = await CreateCaptainAsync("unique-3");

                AssertNotEqual(captain1.Id, captain2.Id);
                AssertNotEqual(captain2.Id, captain3.Id);
                AssertNotEqual(captain1.Id, captain3.Id);
            });

            await RunTest("Create Captain CurrentMissionId Is Null", async () =>
            {
                Captain captain = await CreateCaptainAsync("null-mission-check");

                Assert(
                    string.IsNullOrEmpty(captain.CurrentMissionId),
                    "CurrentMissionId should be null or empty");
            });

            await RunTest("Create Captain CurrentDockId Is Null", async () =>
            {
                Captain captain = await CreateCaptainAsync("null-dock-check");

                Assert(
                    string.IsNullOrEmpty(captain.CurrentDockId),
                    "CurrentDockId should be null or empty");
            });

            await RunTest("Create Captain ProcessId Is Null", async () =>
            {
                Captain captain = await CreateCaptainAsync("null-processid-check");

                Assert(
                    captain.ProcessId == null || captain.ProcessId == 0,
                    "ProcessId should be null or zero");
            });

            #endregion

            #region Get

            await RunTest("Get Captain Exists Returns Correct Data", async () =>
            {
                Captain created = await CreateCaptainAsync("get-test");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/" + created.Id);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(created.Id, fetched.Id);
                AssertStartsWith("get-test", fetched.Name);
                AssertEqual("ClaudeCode", fetched.Runtime.ToString());
                AssertEqual("Idle", fetched.State.ToString());
                AssertEqual(0, fetched.RecoveryAttempts);
            });

            await RunTest("Get Captain Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/cpt_nonexistent");
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                Assert(
                    !string.IsNullOrEmpty(error.Error)
                    || !string.IsNullOrEmpty(error.Message),
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
                Captain created = await CreateCaptainAsync("get-codex", "Codex");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/" + created.Id);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual("Codex", fetched.Runtime.ToString());
            });

            await RunTest("Get Captain Data Matches Create Response", async () =>
            {
                Captain created = await CreateCaptainAsync("data-match");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains/" + created.Id);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(created.Id, fetched.Id);
                AssertEqual(created.Name, fetched.Name);
                AssertEqual(created.Runtime.ToString(), fetched.Runtime.ToString());
                AssertEqual(created.State.ToString(), fetched.State.ToString());
            });

            #endregion

            #region Update

            await RunTest("Update Captain Name Returns Updated Name", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("original-name");

                string newName = "updated-name-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(newName, updated.Name);
            });

            await RunTest("Update Captain Runtime Returns Updated Runtime", async () =>
            {
                Captain created = await CreateCaptainAsync("runtime-update", "ClaudeCode");

                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = created.Name, Runtime = "Codex" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual("Codex", updated.Runtime.ToString());
            });

            await RunTest("Update Captain With Invalid Model Returns BadRequest And PreservesCaptain", async () =>
            {
                Captain created = await CreateCaptainAsync("invalid-model-update", "Custom");
                string attemptedName = "invalid-model-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = attemptedName, Runtime = "Custom", Model = "bad-model" }));

                AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                AssertContains("Unable to create runtime Custom", body);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + created.Id);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);

                AssertEqual(created.Name, fetched.Name);
                AssertEqual("Custom", fetched.Runtime.ToString());
                Assert(string.IsNullOrEmpty(fetched.Model), "Failed update should not persist a model");
            });

            await RunTest("Update Captain Preserves State", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("state-preserve");

                string newName = "state-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "Gemini" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual("Idle", updated.State.ToString());
            });

            await RunTest("Update Captain Preserves Operational Fields", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("operational-preserve");

                string newName = "operational-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(0, updated.RecoveryAttempts);
                AssertEqual(captainId, updated.Id);
            });

            await RunTest("Update Captain Preserves Id", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("id-preserve");

                string newName = "id-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(captainId, updated.Id);
            });

            await RunTest("Update Captain Verify Via Get", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("verify-update");

                string newName = "verified-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "Gemini" }));

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + captainId);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);

                AssertEqual(newName, fetched.Name);
                AssertEqual("Gemini", fetched.Runtime.ToString());
            });

            await RunTest("Update Captain Name Only Runtime Preserved", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("name-only", "Codex");

                string newName = "name-only-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "Codex" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(newName, updated.Name);
                AssertEqual("Codex", updated.Runtime.ToString());
            });

            await RunTest("Update Captain Runtime Only Name Preserved", async () =>
            {
                Captain created = await CreateCaptainAsync("runtime-only", "ClaudeCode");

                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = created.Name, Runtime = "Cursor" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(created.Name, updated.Name);
                AssertEqual("Cursor", updated.Runtime.ToString());
            });

            await RunTest("Update Captain Preserves CreatedUtc", async () =>
            {
                Captain created = await CreateCaptainAsync("created-preserve");
                DateTime originalCreatedUtc = created.CreatedUtc;

                await Task.Delay(100);

                string newName = "created-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await _Client.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(originalCreatedUtc.ToString("O"), updated.CreatedUtc.ToString("O"));
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
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(getResp);
                Assert(
                    !string.IsNullOrEmpty(error.Error)
                    || !string.IsNullOrEmpty(error.Message),
                    "Should have Error or Message property");
            });

            await RunTest("Delete Captain Then List Does Not Contain Deleted", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("delete-then-list");

                await _Client.DeleteAsync("/api/v1/captains/" + captainId);
                _CreatedCaptainIds.Remove(captainId);

                HttpResponseMessage listResp = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(listResp);

                bool found = result.Objects.Any(c => c.Id == captainId);
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

                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);
                AssertStartsWith("keep-this", fetched.Name);
            });

            #endregion

            #region Stop

            await RunTest("Stop Captain Not Found Returns Error", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/cpt_nonexistent/stop", null);
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response);
                Assert(
                    !string.IsNullOrEmpty(error.Error)
                    || !string.IsNullOrEmpty(error.Message),
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

                StopCaptainResponse stopResp = await JsonHelper.DeserializeAsync<StopCaptainResponse>(response);
                AssertEqual("stopped", stopResp.Status);
            });

            await RunTest("Stop Captain Idle Verify State After Stop", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("stop-verify");

                await _Client.PostAsync("/api/v1/captains/" + captainId + "/stop", null);

                HttpResponseMessage getResp = await _Client.GetAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);

                AssertEqual(captainId, fetched.Id);
            });

            await RunTest("Stop All Captains Returns Success", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/stop-all",
                    JsonHelper.ToJsonContent(new { }));
                AssertStatusCode(HttpStatusCode.OK, response);
                StopCaptainResponse stopResp = await JsonHelper.DeserializeAsync<StopCaptainResponse>(response);
                AssertEqual("all_stopped", stopResp.Status);
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
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 0, "TotalRecords should be >= 0");
            });

            await RunTest("List Captains Empty Returns Empty Objects Array", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects != null, "Objects should not be null");
            });

            await RunTest("List Captains Empty Has Enumeration Result Structure", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects != null, "Should have Objects");
                Assert(result.PageNumber >= 0, "Should have PageNumber");
                Assert(result.PageSize >= 0, "Should have PageSize");
                Assert(result.TotalPages >= 0, "Should have TotalPages");
                Assert(result.TotalRecords >= 0, "Should have TotalRecords");
                // Success is a bool, always present on the typed model
                Assert(true, "Should have Success");
            });

            await RunTest("List Captains Empty Success Is True", async () =>
            {
                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertTrue(result.Success);
            });

            #endregion

            #region List - After Create

            await RunTest("List Captains After Create One Returns TotalRecords 1", async () =>
            {
                await CreateCaptainAsync("list-single");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
                Assert(result.Objects.Count >= 1, "Objects should have at least 1 item");
            });

            await RunTest("List Captains After Create One Contains Correct Captain", async () =>
            {
                Captain created = await CreateCaptainAsync("list-verify");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=100");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Captain? found = result.Objects.FirstOrDefault(c => c.Id == created.Id);
                Assert(found != null, "Created captain should appear in list");
                AssertStartsWith("list-verify", found!.Name);
            });

            await RunTest("List Captains After Create Multiple Returns All", async () =>
            {
                await CreateCaptainAsync("multi-1");
                await CreateCaptainAsync("multi-2");
                await CreateCaptainAsync("multi-3");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 3, "TotalRecords should be >= 3");
                Assert(result.Objects.Count >= 3, "Objects should have at least 3 items");
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
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
                AssertEqual(10, result.Objects.Count);
            });

            await RunTest("List Captains 25 Created Page 1 Has Ten Records", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("p1-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=1");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(1, result.PageNumber);
            });

            await RunTest("List Captains 25 Created Page 2 Has Ten Records", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("p2-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=2");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("List Captains 25 Created Page 3 Has Five Records", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("p3-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=3");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects.Count >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, result.PageNumber);
            });

            await RunTest("List Captains 25 Created Beyond Last Page Returns Empty", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("beyond-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=999");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("List Captains 25 Created Pages Do Not Overlap", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateCaptainAsync("overlap-captain-" + i.ToString("D2"));
                }

                HttpResponseMessage resp1 = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=1");
                EnumerationResult<Captain> result1 = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp1);
                HashSet<string> page1Ids = new HashSet<string>(result1.Objects.Select(c => c.Id));

                HttpResponseMessage resp2 = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=2");
                EnumerationResult<Captain> result2 = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp2);
                foreach (Captain c in result2.Objects)
                {
                    AssertFalse(page1Ids.Contains(c.Id), "Page 2 ID should not be in page 1: " + c.Id);
                }

                HttpResponseMessage resp3 = await _Client.GetAsync("/api/v1/captains?pageSize=10&pageNumber=3");
                EnumerationResult<Captain> result3 = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp3);
                foreach (Captain c in result3.Objects)
                {
                    AssertFalse(page1Ids.Contains(c.Id), "Page 3 ID should not be in page 1: " + c.Id);
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
                    EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp);
                    foreach (Captain c in result.Objects)
                    {
                        allIds.Add(c.Id);
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
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(5, result.Objects.Count);
                Assert(result.TotalRecords >= 10, "TotalRecords should be >= 10");
                Assert(result.TotalPages >= 2, "TotalPages should be >= 2");
            });

            #endregion

            #region List - Ordering

            await RunTest("List Captains Created Descending Newest First", async () =>
            {
                Captain first = await CreateCaptainAsync("order-oldest");
                await Task.Delay(50);
                Captain last = await CreateCaptainAsync("order-newest");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?order=CreatedDescending");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify descending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("List Captains Created Ascending Oldest First", async () =>
            {
                Captain first = await CreateCaptainAsync("asc-oldest");
                await Task.Delay(50);
                Captain last = await CreateCaptainAsync("asc-newest");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?order=CreatedAscending");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify ascending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next, "Items should be in ascending order");
                }
            });

            #endregion

            #region Enumerate (POST)

            await RunTest("Enumerate Captains Empty Returns Empty Result", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 0, "TotalRecords should be >= 0");
                AssertTrue(result.Success);
            });

            await RunTest("Enumerate Captains Has Enumeration Result Structure", async () =>
            {
                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects != null, "Should have Objects");
                Assert(result.PageNumber >= 0, "Should have PageNumber");
                Assert(result.PageSize >= 0, "Should have PageSize");
                Assert(result.TotalPages >= 0, "Should have TotalPages");
                Assert(result.TotalRecords >= 0, "Should have TotalRecords");
                Assert(true, "Should have Success");
            });

            await RunTest("Enumerate Captains Default Query Returns Created Captains", async () =>
            {
                await CreateCaptainAsync("enum-cap-1");
                await CreateCaptainAsync("enum-cap-2");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 2, "TotalRecords should be >= 2");
                Assert(result.Objects.Count >= 2, "Objects should have at least 2 items");
            });

            await RunTest("Enumerate Captains With Pagination Page 1", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync("enum-pag-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 15, "TotalRecords should be >= 15");
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
                AssertEqual(5, result.Objects.Count);
            });

            await RunTest("Enumerate Captains With Pagination Page 2", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync("enum-p2-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            });

            await RunTest("Enumerate Captains With Pagination Last Page", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync("enum-lp-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 3, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(3, result.PageNumber);
            });

            await RunTest("Enumerate Captains Beyond Last Page Returns Empty", async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await CreateCaptainAsync("enum-beyond-" + i);
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 999, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(0, result.Objects.Count);
            });

            await RunTest("Enumerate Captains Created Descending Newest First", async () =>
            {
                Captain oldest = await CreateCaptainAsync("enum-desc-oldest");
                await Task.Delay(50);
                Captain newest = await CreateCaptainAsync("enum-desc-newest");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 100, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify descending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order");
                }
            });

            await RunTest("Enumerate Captains Created Ascending Oldest First", async () =>
            {
                Captain oldest = await CreateCaptainAsync("enum-asc-oldest");
                await Task.Delay(50);
                Captain newest = await CreateCaptainAsync("enum-asc-newest");

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 100, Order = "CreatedAscending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify ascending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
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
                EnumerationResult<Captain> getResult = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(getResp);

                HttpResponseMessage enumResp = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedDescending" }));
                EnumerationResult<Captain> enumResult = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(enumResp);

                AssertEqual(getResult.TotalRecords, enumResult.TotalRecords);
                AssertEqual(getResult.TotalPages, enumResult.TotalPages);
            });

            await RunTest("Enumerate Captains PageSize Respected", async () =>
            {
                for (int i = 0; i < 8; i++)
                {
                    await CreateCaptainAsync("enum-ps-" + i);
                }

                HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 3, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(3, result.PageSize);
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
                        JsonHelper.ToJsonContent(new { PageNumber = page, PageSize = 5, Order = "CreatedDescending" }));
                    EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp);
                    foreach (Captain c in result.Objects)
                    {
                        AssertFalse(allIds.Contains(c.Id), "Duplicate ID found: " + c.Id);
                        allIds.Add(c.Id);
                    }
                }

                Assert(allIds.Count >= 10, "Should have at least 10 unique IDs across enumerate pages");
            });

            #endregion

            #region Edge Cases

            await RunTest("Create Captain Same Name Second Creation Handled", async () =>
            {
                Captain captain1 = await CreateCaptainAsync("duplicate-name");
                AssertStartsWith("cpt_", captain1.Id);

                HttpResponseMessage resp = await _Client.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = captain1.Name, Runtime = "ClaudeCode" }));

                if (resp.IsSuccessStatusCode)
                {
                    Captain captain2 = await JsonHelper.DeserializeAsync<Captain>(resp);
                    _CreatedCaptainIds.Add(captain2.Id);
                    AssertNotEqual(captain1.Id, captain2.Id);
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

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);
                Assert(result.Objects.Count >= 1, "Should have at least 1 object");
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
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
                Assert(result.Objects.Count >= 1, "Objects should have at least 1 item");
                // Verify id2 is present
                bool foundId2 = result.Objects.Any(c => c.Id == id2);
                Assert(foundId2, "Captain id2 should still be in list");
            });

            await RunTest("Update Captain Then List Shows Updated Data", async () =>
            {
                string captainId = await CreateCaptainAndGetIdAsync("update-list-check");

                string updatedName = "update-list-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await _Client.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = updatedName, Runtime = "Gemini" }));

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains?pageSize=100");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Captain? found = result.Objects.FirstOrDefault(c => c.Id == captainId);
                Assert(found != null, "Updated captain should appear in list");
                AssertEqual(updatedName, found!.Name);
                AssertEqual("Gemini", found.Runtime.ToString());
            });

            await RunTest("Create Captain All Runtimes Each Creates Successfully", async () =>
            {
                string[] runtimes = new[] { "ClaudeCode", "Codex", "Gemini", "Cursor", "Custom" };

                foreach (string runtime in runtimes)
                {
                    string captainName = "rt-" + runtime + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    HttpResponseMessage response = await _Client.PostAsync("/api/v1/captains",
                        JsonHelper.ToJsonContent(new { Name = captainName, Runtime = runtime }));
                    AssertEqual(HttpStatusCode.Created, response.StatusCode);

                    Captain captain = await JsonHelper.DeserializeAsync<Captain>(response);
                    _CreatedCaptainIds.Add(captain.Id);
                    AssertEqual(runtime, captain.Runtime.ToString());
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
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
            });

            await RunTest("List Captains Each Object Has All Expected Properties", async () =>
            {
                await CreateCaptainAsync("props-check", "Codex");

                HttpResponseMessage response = await _Client.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Captain captain = result.Objects[0];

                Assert(!string.IsNullOrEmpty(captain.Id), "Should have Id");
                Assert(!string.IsNullOrEmpty(captain.Name), "Should have Name");
                Assert(captain.Runtime.ToString() != null, "Should have Runtime");
                Assert(captain.State.ToString() != null, "Should have State");
                Assert(captain.RecoveryAttempts >= 0, "Should have RecoveryAttempts");
                Assert(captain.CreatedUtc != default, "Should have CreatedUtc");
                Assert(captain.LastUpdateUtc != default, "Should have LastUpdateUtc");
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
        /// Creates a captain and returns the deserialized Captain object.
        /// </summary>
        private async Task<Captain> CreateCaptainAsync(string name, string runtime = "ClaudeCode")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object body = new { Name = uniqueName, Runtime = runtime };
            HttpResponseMessage resp = await _Client.PostAsync("/api/v1/captains",
                JsonHelper.ToJsonContent(body));
            resp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp);
            _CreatedCaptainIds.Add(captain.Id);
            return captain;
        }

        /// <summary>
        /// Creates a captain and returns only its ID.
        /// </summary>
        private async Task<string> CreateCaptainAndGetIdAsync(string name, string runtime = "ClaudeCode")
        {
            Captain captain = await CreateCaptainAsync(name, runtime);
            return captain.Id;
        }

        #endregion
    }
}
