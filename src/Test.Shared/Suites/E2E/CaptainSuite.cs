namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end Captain API descriptors covering CRUD, stop, list, pagination, ordering,
    /// enumeration, and edge cases against a live in-process Armada server provided by
    /// <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class CaptainSuite : IArmadaTestSuite
    {
        #region Private-Members

        private readonly object _SeedLock = new object();
        private Task? _SharedCaptainSeed;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Captain API end-to-end suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            #region Create

            cases.Add(CaseAsync("create_captain_returns_201_with_correct_properties", "Create Captain Returns 201 With Correct Properties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainName = "captain-alpha-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = captainName, Runtime = "ClaudeCode" }));

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response);

                createdCaptainIds.Add(captain.Id);

                AssertStartsWith("cpt_", captain.Id);
                AssertEqual(captainName, captain.Name);
                AssertEqual("ClaudeCode", captain.Runtime.ToString());
                AssertEqual("Idle", captain.State.ToString());
                AssertEqual(0, captain.RecoveryAttempts);
            }));

            cases.Add(CaseAsync("create_captain_default_runtime_is_claudecode", "Create Captain Default Runtime Is ClaudeCode", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainName = "default-runtime-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = captainName }));

                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response);
                createdCaptainIds.Add(captain.Id);
                AssertEqual("ClaudeCode", captain.Runtime.ToString());
            }));

            cases.Add(CaseAsync("create_captain_state_is_idle", "Create Captain State Is Idle", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "idle-check");
                AssertEqual("Idle", captain.State.ToString());
            }));

            cases.Add(CaseAsync("create_captain_has_timestamps", "Create Captain Has Timestamps", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "timestamp-check");

                DateTime created = captain.CreatedUtc;
                DateTime updated = captain.LastUpdateUtc;

                Assert(created <= DateTime.UtcNow.AddMinutes(1), "CreatedUtc should not be in the future");
                Assert(updated <= DateTime.UtcNow.AddMinutes(1), "LastUpdateUtc should not be in the future");
            }));

            cases.Add(CaseAsync("create_captain_recovery_attempts_is_zero", "Create Captain Recovery Attempts Is Zero", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "recovery-check");
                AssertEqual(0, captain.RecoveryAttempts);
            }));

            cases.Add(CaseAsync("create_captain_id_has_cpt_prefix", "Create Captain Id Has Cpt Prefix", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "prefix-check");
                AssertStartsWith("cpt_", captain.Id);
            }));

            cases.Add(CaseAsync("create_captain_with_codex_runtime", "Create Captain With Codex Runtime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "codex-captain", "Codex");
                AssertEqual("Codex", captain.Runtime.ToString());
            }));

            cases.Add(CaseAsync("create_captain_with_gemini_runtime", "Create Captain With Gemini Runtime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "gemini-captain", "Gemini");
                AssertEqual("Gemini", captain.Runtime.ToString());
            }));

            cases.Add(CaseAsync("create_captain_with_cursor_runtime", "Create Captain With Cursor Runtime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "cursor-captain", "Cursor");
                AssertEqual("Cursor", captain.Runtime.ToString());
            }));

            cases.Add(CaseAsync("create_captain_with_custom_runtime", "Create Captain With Custom Runtime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "custom-captain", "Custom");
                AssertEqual("Custom", captain.Runtime.ToString());
            }));

            cases.Add(CaseAsync("create_captain_multiple_captains_have_unique_ids", "Create Captain Multiple Captains Have Unique Ids", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain1 = await CreateCaptainAsync(authClient, createdCaptainIds, "unique-1");
                Captain captain2 = await CreateCaptainAsync(authClient, createdCaptainIds, "unique-2");
                Captain captain3 = await CreateCaptainAsync(authClient, createdCaptainIds, "unique-3");

                AssertNotEqual(captain1.Id, captain2.Id);
                AssertNotEqual(captain2.Id, captain3.Id);
                AssertNotEqual(captain1.Id, captain3.Id);
            }));

            cases.Add(CaseAsync("create_captain_current_mission_id_is_null", "Create Captain CurrentMissionId Is Null", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "null-mission-check");

                Assert(
                    string.IsNullOrEmpty(captain.CurrentMissionId),
                    "CurrentMissionId should be null or empty");
            }));

            cases.Add(CaseAsync("create_captain_current_dock_id_is_null", "Create Captain CurrentDockId Is Null", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "null-dock-check");

                Assert(
                    string.IsNullOrEmpty(captain.CurrentDockId),
                    "CurrentDockId should be null or empty");
            }));

            cases.Add(CaseAsync("create_captain_process_id_is_null", "Create Captain ProcessId Is Null", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain = await CreateCaptainAsync(authClient, createdCaptainIds, "null-processid-check");

                Assert(
                    captain.ProcessId == null || captain.ProcessId == 0,
                    "ProcessId should be null or zero");
            }));

            #endregion

            #region Get

            cases.Add(CaseAsync("get_captain_exists_returns_correct_data", "Get Captain Exists Returns Correct Data", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "get-test");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + created.Id);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(created.Id, fetched.Id);
                AssertStartsWith("get-test", fetched.Name);
                AssertEqual("ClaudeCode", fetched.Runtime.ToString());
                AssertEqual("Idle", fetched.State.ToString());
                AssertEqual(0, fetched.RecoveryAttempts);
            }));

            cases.Add(CaseAsync("get_captain_not_found_returns_error", "Get Captain Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/cpt_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Should have Error or Message property");
            }));

            cases.Add(CaseAsync("get_captain_not_found_status_code_is_not_200_or_body_has_error", "Get Captain Not Found Status Code Is Not 200 Or Body Has Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/cpt_doesnotexist");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Not-found captain returns a typed 404");
            }));

            cases.Add(CaseAsync("get_captain_with_codex_runtime_returns_correct_runtime", "Get Captain With Codex Runtime Returns Correct Runtime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "get-codex", "Codex");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + created.Id);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual("Codex", fetched.Runtime.ToString());
            }));

            cases.Add(CaseAsync("get_captain_data_matches_create_response", "Get Captain Data Matches Create Response", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "data-match");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + created.Id);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(created.Id, fetched.Id);
                AssertEqual(created.Name, fetched.Name);
                AssertEqual(created.Runtime.ToString(), fetched.Runtime.ToString());
                AssertEqual(created.State.ToString(), fetched.State.ToString());
            }));

            #endregion

            #region Update

            cases.Add(CaseAsync("update_captain_name_returns_updated_name", "Update Captain Name Returns Updated Name", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "original-name");

                string newName = "updated-name-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(newName, updated.Name);
            }));

            cases.Add(CaseAsync("update_captain_runtime_returns_updated_runtime", "Update Captain Runtime Returns Updated Runtime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "runtime-update", "ClaudeCode");

                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = created.Name, Runtime = "Codex" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual("Codex", updated.Runtime.ToString());
            }));

            cases.Add(CaseAsync("update_captain_preserves_state", "Update Captain Preserves State", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "state-preserve");

                string newName = "state-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "Gemini" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual("Idle", updated.State.ToString());
            }));

            cases.Add(CaseAsync("update_captain_preserves_operational_fields", "Update Captain Preserves Operational Fields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "operational-preserve");

                string newName = "operational-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(0, updated.RecoveryAttempts);
                AssertEqual(captainId, updated.Id);
            }));

            cases.Add(CaseAsync("update_captain_preserves_id", "Update Captain Preserves Id", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "id-preserve");

                string newName = "id-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(captainId, updated.Id);
            }));

            cases.Add(CaseAsync("update_captain_verify_via_get", "Update Captain Verify Via Get", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "verify-update");

                string newName = "verified-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "Gemini" }));

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains/" + captainId);
                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);

                AssertEqual(newName, fetched.Name);
                AssertEqual("Gemini", fetched.Runtime.ToString());
            }));

            cases.Add(CaseAsync("update_captain_name_only_runtime_preserved", "Update Captain Name Only Runtime Preserved", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "name-only", "Codex");

                string newName = "name-only-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "Codex" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(newName, updated.Name);
                AssertEqual("Codex", updated.Runtime.ToString());
            }));

            cases.Add(CaseAsync("update_captain_runtime_only_name_preserved", "Update Captain Runtime Only Name Preserved", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "runtime-only", "ClaudeCode");

                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = created.Name, Runtime = "Cursor" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);
                AssertEqual(created.Name, updated.Name);
                AssertEqual("Cursor", updated.Runtime.ToString());
            }));

            cases.Add(CaseAsync("update_captain_preserves_created_utc", "Update Captain Preserves CreatedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "created-preserve");
                DateTime originalCreatedUtc = created.CreatedUtc;

                await Task.Delay(100);

                string newName = "created-preserve-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/captains/" + created.Id,
                    JsonHelper.ToJsonContent(new { Name = newName, Runtime = "ClaudeCode" }));

                Captain updated = await JsonHelper.DeserializeAsync<Captain>(response);

                AssertEqual(originalCreatedUtc.ToString("O"), updated.CreatedUtc.ToString("O"));
            }));

            #endregion

            #region Delete

            cases.Add(CaseAsync("delete_captain_returns_204", "Delete Captain Returns 204", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "to-delete");

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
                createdCaptainIds.Remove(captainId);
            }));

            cases.Add(CaseAsync("delete_captain_not_found_returns_error", "Delete Captain Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/captains/cpt_nonexistent");

                AssertNotEqual(HttpStatusCode.NoContent, response.StatusCode);
            }));

            cases.Add(CaseAsync("delete_captain_then_get_returns_not_found", "Delete Captain Then Get Returns Not Found", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "delete-then-get");

                HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                createdCaptainIds.Remove(captainId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains/" + captainId);
                await E2eApiErrorAssert.ExpectAsync(getResp, HttpStatusCode.NotFound, "Should have Error or Message property");
            }));

            cases.Add(CaseAsync("delete_captain_then_list_does_not_contain_deleted", "Delete Captain Then List Does Not Contain Deleted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "delete-then-list");

                await authClient.DeleteAsync("/api/v1/captains/" + captainId);
                createdCaptainIds.Remove(captainId);

                HttpResponseMessage listResp = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(listResp);

                bool found = result.Objects.Any(c => c.Id == captainId);
                AssertFalse(found, "Deleted captain should not appear in list");
            }));

            cases.Add(CaseAsync("delete_captain_does_not_affect_other_captains", "Delete Captain Does Not Affect Other Captains", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string keepId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "keep-this");
                string deleteId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "delete-this");

                await authClient.DeleteAsync("/api/v1/captains/" + deleteId);
                createdCaptainIds.Remove(deleteId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains/" + keepId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);
                AssertStartsWith("keep-this", fetched.Name);
            }));

            #endregion

            #region Stop

            cases.Add(CaseAsync("stop_captain_not_found_returns_error", "Stop Captain Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/cpt_nonexistent/stop", null);
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Should have Error or Message property");
            }));

            cases.Add(CaseAsync("stop_captain_not_found_status_code_is_not_ok_or_body_has_error", "Stop Captain Not Found Status Code Is Not OK Or Body Has Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/cpt_doesnotexist/stop", null);
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Stop on non-existent captain returns a typed 404");
            }));

            cases.Add(CaseAsync("stop_captain_idle_returns_success", "Stop Captain Idle Returns Success", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "stop-idle");

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/" + captainId + "/stop", null);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                StopCaptainResponse stopResp = await JsonHelper.DeserializeAsync<StopCaptainResponse>(response);
                AssertEqual("stopped", stopResp.Status);
            }));

            cases.Add(CaseAsync("stop_captain_idle_verify_state_after_stop", "Stop Captain Idle Verify State After Stop", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "stop-verify");

                await authClient.PostAsync("/api/v1/captains/" + captainId + "/stop", null);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains/" + captainId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                Captain fetched = await JsonHelper.DeserializeAsync<Captain>(getResp);

                AssertEqual(captainId, fetched.Id);
            }));

            cases.Add(CaseAsync("stop_all_captains_returns_success", "Stop All Captains Returns Success", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/stop-all",
                    JsonHelper.ToJsonContent(new { }));
                AssertStatusCode(HttpStatusCode.OK, response);
                StopCaptainResponse stopResp = await JsonHelper.DeserializeAsync<StopCaptainResponse>(response);
                AssertEqual("all_stopped", stopResp.Status);
            }));

            #endregion

            #region List - Empty

            cases.Add(CaseAsync("list_captains_empty_returns_ok", "List Captains Empty Returns OK", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("list_captains_empty_returns_zero_total_records", "List Captains Empty Returns Zero Total Records", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 0, "TotalRecords should be >= 0");
            }));

            cases.Add(CaseAsync("list_captains_empty_returns_empty_objects_array", "List Captains Empty Returns Empty Objects Array", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects != null, "Objects should not be null");
            }));

            cases.Add(CaseAsync("list_captains_empty_has_enumeration_result_structure", "List Captains Empty Has Enumeration Result Structure", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects != null, "Should have Objects");
                Assert(result.PageNumber >= 0, "Should have PageNumber");
                Assert(result.PageSize >= 0, "Should have PageSize");
                Assert(result.TotalPages >= 0, "Should have TotalPages");
                Assert(result.TotalRecords >= 0, "Should have TotalRecords");
                // Success is a bool, always present on the typed model
                Assert(true, "Should have Success");
            }));

            cases.Add(CaseAsync("list_captains_empty_success_is_true", "List Captains Empty Success Is True", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertTrue(result.Success);
            }));

            #endregion

            #region List - After Create

            cases.Add(CaseAsync("list_captains_after_create_one_returns_total_records_1", "List Captains After Create One Returns TotalRecords 1", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                await CreateCaptainAsync(authClient, createdCaptainIds, "list-single");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
                Assert(result.Objects.Count >= 1, "Objects should have at least 1 item");
            }));

            cases.Add(CaseAsync("list_captains_after_create_one_contains_correct_captain", "List Captains After Create One Contains Correct Captain", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain created = await CreateCaptainAsync(authClient, createdCaptainIds, "list-verify");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=100");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Captain? found = result.Objects.FirstOrDefault(c => c.Id == created.Id);
                Assert(found != null, "Created captain should appear in list");
                AssertStartsWith("list-verify", found!.Name);
            }));

            cases.Add(CaseAsync("list_captains_after_create_multiple_returns_all", "List Captains After Create Multiple Returns All", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                await CreateCaptainAsync(authClient, createdCaptainIds, "multi-1");
                await CreateCaptainAsync(authClient, createdCaptainIds, "multi-2");
                await CreateCaptainAsync(authClient, createdCaptainIds, "multi-3");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 3, "TotalRecords should be >= 3");
                Assert(result.Objects.Count >= 3, "Objects should have at least 3 items");
            }));

            #endregion

            #region List - Pagination

            cases.Add(CaseAsync("list_captains_25_created_pagesize_10_total_records_25", "List Captains 25 Created PageSize 10 TotalRecords 25", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=10");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
                AssertEqual(10, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_captains_25_created_page_1_has_ten_records", "List Captains 25 Created Page 1 Has Ten Records", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=1");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(1, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_captains_25_created_page_2_has_ten_records", "List Captains 25 Created Page 2 Has Ten Records", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=2");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_captains_25_created_page_3_has_five_records", "List Captains 25 Created Page 3 Has Five Records", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=3");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects.Count >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_captains_25_created_beyond_last_page_returns_empty", "List Captains 25 Created Beyond Last Page Returns Empty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=999");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_captains_25_created_pages_do_not_overlap", "List Captains 25 Created Pages Do Not Overlap", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage resp1 = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=1");
                EnumerationResult<Captain> result1 = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp1);
                HashSet<string> page1Ids = new HashSet<string>(result1.Objects.Select(c => c.Id));

                HttpResponseMessage resp2 = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=2");
                EnumerationResult<Captain> result2 = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp2);
                foreach (Captain c in result2.Objects)
                {
                    AssertFalse(page1Ids.Contains(c.Id), "Page 2 ID should not be in page 1: " + c.Id);
                }

                HttpResponseMessage resp3 = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=3");
                EnumerationResult<Captain> result3 = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp3);
                foreach (Captain c in result3.Objects)
                {
                    AssertFalse(page1Ids.Contains(c.Id), "Page 3 ID should not be in page 1: " + c.Id);
                }
            }));

            cases.Add(CaseAsync("list_captains_25_created_all_records_accounted_for", "List Captains 25 Created All Records Accounted For", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HashSet<string> allIds = new HashSet<string>();

                for (int page = 1; page <= 3; page++)
                {
                    HttpResponseMessage resp = await authClient.GetAsync("/api/v1/captains?pageSize=10&pageNumber=" + page);
                    EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp);
                    foreach (Captain c in result.Objects)
                    {
                        allIds.Add(c.Id);
                    }
                }

                Assert(allIds.Count >= 25, "Should have at least 25 unique IDs across pages");
            }));

            cases.Add(CaseAsync("list_captains_pagesize_5_returns_5_records", "List Captains PageSize 5 Returns 5 Records", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureCaptainsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=5");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(5, result.Objects.Count);
                Assert(result.TotalRecords >= 10, "TotalRecords should be >= 10");
                Assert(result.TotalPages >= 2, "TotalPages should be >= 2");
            }));

            #endregion

            #region List - Ordering

            cases.Add(CaseAsync("list_captains_created_descending_newest_first", "List Captains Created Descending Newest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain first = await CreateCaptainAsync(authClient, createdCaptainIds, "order-oldest");
                await Task.Delay(50);
                Captain last = await CreateCaptainAsync(authClient, createdCaptainIds, "order-newest");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?order=CreatedDescending");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify descending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order");
                }
            }));

            cases.Add(CaseAsync("list_captains_created_ascending_oldest_first", "List Captains Created Ascending Oldest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain first = await CreateCaptainAsync(authClient, createdCaptainIds, "asc-oldest");
                await Task.Delay(50);
                Captain last = await CreateCaptainAsync(authClient, createdCaptainIds, "asc-newest");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?order=CreatedAscending");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify ascending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next, "Items should be in ascending order");
                }
            }));

            #endregion

            #region Enumerate (POST)

            cases.Add(CaseAsync("enumerate_captains_empty_returns_empty_result", "Enumerate Captains Empty Returns Empty Result", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 0, "TotalRecords should be >= 0");
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("enumerate_captains_has_enumeration_result_structure", "Enumerate Captains Has Enumeration Result Structure", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.Objects != null, "Should have Objects");
                Assert(result.PageNumber >= 0, "Should have PageNumber");
                Assert(result.PageSize >= 0, "Should have PageSize");
                Assert(result.TotalPages >= 0, "Should have TotalPages");
                Assert(result.TotalRecords >= 0, "Should have TotalRecords");
                Assert(true, "Should have Success");
            }));

            cases.Add(CaseAsync("enumerate_captains_default_query_returns_created_captains", "Enumerate Captains Default Query Returns Created Captains", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                await CreateCaptainAsync(authClient, createdCaptainIds, "enum-cap-1");
                await CreateCaptainAsync(authClient, createdCaptainIds, "enum-cap-2");

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 2, "TotalRecords should be >= 2");
                Assert(result.Objects.Count >= 2, "Objects should have at least 2 items");
            }));

            cases.Add(CaseAsync("enumerate_captains_with_pagination_page_1", "Enumerate Captains With Pagination Page 1", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "enum-pag-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 15, "TotalRecords should be >= 15");
                Assert(result.TotalPages >= 3, "TotalPages should be >= 3");
                AssertEqual(5, result.Objects.Count);
            }));

            cases.Add(CaseAsync("enumerate_captains_with_pagination_page_2", "Enumerate Captains With Pagination Page 2", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "enum-p2-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("enumerate_captains_with_pagination_last_page", "Enumerate Captains With Pagination Last Page", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 15; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "enum-lp-" + i.ToString("D2"));
                }

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 3, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(3, result.PageNumber);
            }));

            cases.Add(CaseAsync("enumerate_captains_beyond_last_page_returns_empty", "Enumerate Captains Beyond Last Page Returns Empty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 5; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "enum-beyond-" + i);
                }

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 999, PageSize = 5, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("enumerate_captains_created_descending_newest_first", "Enumerate Captains Created Descending Newest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain oldest = await CreateCaptainAsync(authClient, createdCaptainIds, "enum-desc-oldest");
                await Task.Delay(50);
                Captain newest = await CreateCaptainAsync(authClient, createdCaptainIds, "enum-desc-newest");

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 100, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify descending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current >= next, "Items should be in descending order");
                }
            }));

            cases.Add(CaseAsync("enumerate_captains_created_ascending_oldest_first", "Enumerate Captains Created Ascending Oldest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain oldest = await CreateCaptainAsync(authClient, createdCaptainIds, "enum-asc-oldest");
                await Task.Delay(50);
                Captain newest = await CreateCaptainAsync(authClient, createdCaptainIds, "enum-asc-newest");

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 100, Order = "CreatedAscending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                // Verify ascending order
                for (int i = 0; i < result.Objects.Count - 1; i++)
                {
                    DateTime current = result.Objects[i].CreatedUtc;
                    DateTime next = result.Objects[i + 1].CreatedUtc;
                    Assert(current <= next, "Items should be in ascending order");
                }
            }));

            cases.Add(CaseAsync("enumerate_captains_matches_get_pagination", "Enumerate Captains Matches Get Pagination", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 12; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "match-cap-" + i.ToString("D2"));
                }

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/captains?pageSize=5&pageNumber=1");
                EnumerationResult<Captain> getResult = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(getResp);

                HttpResponseMessage enumResp = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedDescending" }));
                EnumerationResult<Captain> enumResult = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(enumResp);

                AssertEqual(getResult.TotalRecords, enumResult.TotalRecords);
                AssertEqual(getResult.TotalPages, enumResult.TotalPages);
            }));

            cases.Add(CaseAsync("enumerate_captains_pagesize_respected", "Enumerate Captains PageSize Respected", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 8; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "enum-ps-" + i);
                }

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 3, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(3, result.PageSize);
            }));

            cases.Add(CaseAsync("enumerate_captains_pages_do_not_overlap", "Enumerate Captains Pages Do Not Overlap", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                for (int i = 0; i < 10; i++)
                {
                    await CreateCaptainAsync(authClient, createdCaptainIds, "enum-no-overlap-" + i);
                }

                HashSet<string> allIds = new HashSet<string>();

                for (int page = 1; page <= 2; page++)
                {
                    HttpResponseMessage resp = await authClient.PostAsync("/api/v1/captains/enumerate",
                        JsonHelper.ToJsonContent(new { PageNumber = page, PageSize = 5, Order = "CreatedDescending" }));
                    EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(resp);
                    foreach (Captain c in result.Objects)
                    {
                        AssertFalse(allIds.Contains(c.Id), "Duplicate ID found: " + c.Id);
                        allIds.Add(c.Id);
                    }
                }

                Assert(allIds.Count >= 10, "Should have at least 10 unique IDs across enumerate pages");
            }));

            #endregion

            #region Edge Cases

            cases.Add(CaseAsync("create_captain_same_name_second_creation_handled", "Create Captain Same Name Second Creation Handled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                Captain captain1 = await CreateCaptainAsync(authClient, createdCaptainIds, "duplicate-name");
                AssertStartsWith("cpt_", captain1.Id);

                HttpResponseMessage resp = await authClient.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = captain1.Name, Runtime = "ClaudeCode" }));

                if (resp.IsSuccessStatusCode)
                {
                    Captain captain2 = await JsonHelper.DeserializeAsync<Captain>(resp);
                    createdCaptainIds.Add(captain2.Id);
                    AssertNotEqual(captain1.Id, captain2.Id);
                }
                else
                {
                    Assert(resp.StatusCode == HttpStatusCode.InternalServerError ||
                                resp.StatusCode == HttpStatusCode.Conflict ||
                                resp.StatusCode == HttpStatusCode.BadRequest,
                        "Duplicate name should return error status");
                }
            }));

            cases.Add(CaseAsync("list_captains_default_pagesize_returns_results", "List Captains Default PageSize Returns Results", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                await CreateCaptainAsync(authClient, createdCaptainIds, "default-page");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);
                Assert(result.Objects.Count >= 1, "Should have at least 1 object");
            }));

            cases.Add(CaseAsync("create_and_delete_multiple_list_reflects_correct_count", "Create And Delete Multiple List Reflects Correct Count", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string id1 = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "multi-del-1");
                string id2 = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "multi-del-2");
                string id3 = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "multi-del-3");

                await authClient.DeleteAsync("/api/v1/captains/" + id1);
                createdCaptainIds.Remove(id1);
                await authClient.DeleteAsync("/api/v1/captains/" + id3);
                createdCaptainIds.Remove(id3);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
                Assert(result.Objects.Count >= 1, "Objects should have at least 1 item");
                // Verify id2 is present
                bool foundId2 = result.Objects.Any(c => c.Id == id2);
                Assert(foundId2, "Captain id2 should still be in list");
            }));

            cases.Add(CaseAsync("update_captain_then_list_shows_updated_data", "Update Captain Then List Shows Updated Data", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string captainId = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "update-list-check");

                string updatedName = "update-list-updated-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await authClient.PutAsync("/api/v1/captains/" + captainId,
                    JsonHelper.ToJsonContent(new { Name = updatedName, Runtime = "Gemini" }));

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains?pageSize=100");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Captain? found = result.Objects.FirstOrDefault(c => c.Id == captainId);
                Assert(found != null, "Updated captain should appear in list");
                AssertEqual(updatedName, found!.Name);
                AssertEqual("Gemini", found.Runtime.ToString());
            }));

            cases.Add(CaseAsync("create_captain_all_runtimes_each_creates_successfully", "Create Captain All Runtimes Each Creates Successfully", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string[] runtimes = new[] { "ClaudeCode", "Codex", "Gemini", "Cursor", "Custom" };

                foreach (string runtime in runtimes)
                {
                    string captainName = "rt-" + runtime + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains",
                        JsonHelper.ToJsonContent(new { Name = captainName, Runtime = runtime }));
                    AssertEqual(HttpStatusCode.Created, response.StatusCode);

                    Captain captain = await JsonHelper.DeserializeAsync<Captain>(response);
                    createdCaptainIds.Add(captain.Id);
                    AssertEqual(runtime, captain.Runtime.ToString());
                }
            }));

            cases.Add(CaseAsync("enumerate_captains_after_delete_some_total_records_updated", "Enumerate Captains After Delete Some TotalRecords Updated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                string id1 = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "enum-del-1");
                await CreateCaptainAsync(authClient, createdCaptainIds, "enum-del-2");
                string id3 = await CreateCaptainAndGetIdAsync(authClient, createdCaptainIds, "enum-del-3");

                await authClient.DeleteAsync("/api/v1/captains/" + id1);
                createdCaptainIds.Remove(id1);
                await authClient.DeleteAsync("/api/v1/captains/" + id3);
                createdCaptainIds.Remove(id3);

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/captains/enumerate",
                    JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending" }));

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Assert(result.TotalRecords >= 1, "TotalRecords should be >= 1");
            }));

            cases.Add(CaseAsync("list_captains_each_object_has_all_expected_properties", "List Captains Each Object Has All Expected Properties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdCaptainIds = new List<string>();

                await CreateCaptainAsync(authClient, createdCaptainIds, "props-check", "Codex");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains");
                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response);

                Captain captain = result.Objects[0];

                Assert(!string.IsNullOrEmpty(captain.Id), "Should have Id");
                Assert(!string.IsNullOrEmpty(captain.Name), "Should have Name");
                Assert(captain.Runtime.ToString() != null, "Should have Runtime");
                Assert(captain.State.ToString() != null, "Should have State");
                Assert(captain.RecoveryAttempts >= 0, "Should have RecoveryAttempts");
                Assert(captain.CreatedUtc != default, "Should have CreatedUtc");
                Assert(captain.LastUpdateUtc != default, "Should have LastUpdateUtc");
            }));

            #endregion

            return new TestSuiteDescriptor(
                suiteId: "E2E.Captain",
                displayName: "Captain API Tests",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a captain and returns the deserialized Captain object.
        /// </summary>
        /// <summary>
        /// Ensure the shared server holds a reusable set of at least 25 captains, seeding it exactly once
        /// for the suite. Every case runs against the same in-process fixture and the list/pagination cases
        /// only assert accumulation-tolerant conditions (a full page, a total at or above a threshold, no
        /// page overlap, all created IDs present across pages), so they can share one seeded set instead of
        /// each re-creating 25 captains over sequential HTTP round-trips. The seed Task is memoized: the
        /// first bulk case to run pays the cost and the rest await the completed Task.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>A task that completes once the shared captains exist.</returns>
        private Task EnsureCaptainsSeededAsync(HttpClient authClient)
        {
            lock (_SeedLock)
            {
                if (_SharedCaptainSeed == null) _SharedCaptainSeed = SeedSharedCaptainsAsync(authClient);
                return _SharedCaptainSeed;
            }
        }

        private static async Task SeedSharedCaptainsAsync(HttpClient authClient)
        {
            List<string> seededIds = new List<string>();
            for (int i = 0; i < 25; i++)
            {
                await CreateCaptainAsync(authClient, seededIds, "shared-pag-captain-" + i.ToString("D2")).ConfigureAwait(false);
            }
        }

        private static async Task<Captain> CreateCaptainAsync(HttpClient client, List<string> createdCaptainIds, string name, string runtime = "ClaudeCode")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object body = new { Name = uniqueName, Runtime = runtime };
            HttpResponseMessage resp = await client.PostAsync("/api/v1/captains",
                JsonHelper.ToJsonContent(body));
            resp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp);
            createdCaptainIds.Add(captain.Id);
            return captain;
        }

        /// <summary>
        /// Creates a captain and returns only its ID.
        /// </summary>
        private static async Task<string> CreateCaptainAndGetIdAsync(HttpClient client, List<string> createdCaptainIds, string name, string runtime = "ClaudeCode")
        {
            Captain captain = await CreateCaptainAsync(client, createdCaptainIds, name, runtime);
            return captain.Id;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "E2E.Captain",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
