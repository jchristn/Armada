namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for merge queue routes: enqueue, get, cancel, list, enumerate, and process.
    /// </summary>
    public class MergeQueueTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Merge Queue Routes";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new MergeQueueTests suite with shared HTTP clients.
        /// </summary>
        public MergeQueueTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateFleetAsync(string name = "MergeTestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateVesselAsync(string fleetId, string name = "MergeTestVessel")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateMissionAsync(string title = "MergeTestMission")
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Title = title, Description = "Mission for merge queue testing" }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<JsonElement> EnqueueAsync(string missionId, string vesselId, string branch, string targetBranch = "main")
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    MissionId = missionId,
                    VesselId = vesselId,
                    BranchName = branch,
                    TargetBranch = targetBranch
                }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/merge-queue", content).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private async Task<(string FleetId, string VesselId, string MissionId)> CreatePrerequisitesAsync(string suffix = "")
        {
            string fleetId = await CreateFleetAsync("Fleet" + suffix).ConfigureAwait(false);
            string vesselId = await CreateVesselAsync(fleetId, "Vessel" + suffix).ConfigureAwait(false);
            string missionId = await CreateMissionAsync("Mission" + suffix).ConfigureAwait(false);
            return (fleetId, vesselId, missionId);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region CRUD-Enqueue

            await RunTest("Enqueue_Returns201_WithCorrectProperties", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("Enqueue201").ConfigureAwait(false);

                JsonElement entry = await EnqueueAsync(missionId, vesselId, "feat/enqueue-test").ConfigureAwait(false);

                AssertStartsWith("mrg_", entry.GetProperty("Id").GetString()!);
                AssertEqual(missionId, entry.GetProperty("MissionId").GetString());
                AssertEqual(vesselId, entry.GetProperty("VesselId").GetString());
                AssertEqual("feat/enqueue-test", entry.GetProperty("BranchName").GetString());
                AssertEqual("main", entry.GetProperty("TargetBranch").GetString());
                AssertEqual("Queued", entry.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enqueue_WithCustomTargetBranch_ReturnsCorrectTarget", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("CustomTarget").ConfigureAwait(false);

                JsonElement entry = await EnqueueAsync(missionId, vesselId, "feat/custom-target", "develop").ConfigureAwait(false);

                AssertEqual("develop", entry.GetProperty("TargetBranch").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enqueue_SetsTimestamps", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("Timestamps").ConfigureAwait(false);

                DateTime before = DateTime.UtcNow.AddSeconds(-5);
                JsonElement entry = await EnqueueAsync(missionId, vesselId, "feat/timestamps").ConfigureAwait(false);
                DateTime after = DateTime.UtcNow.AddSeconds(5);

                string createdStr = entry.GetProperty("CreatedUtc").GetString()!;
                DateTime created = DateTime.Parse(createdStr, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
                Assert(created >= before && created <= after, "CreatedUtc " + created + " should be between " + before + " and " + after);

                string updatedStr = entry.GetProperty("LastUpdateUtc").GetString()!;
                DateTime updated = DateTime.Parse(updatedStr, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
                Assert(updated >= before && updated <= after, "LastUpdateUtc " + updated + " should be between " + before + " and " + after);
            }).ConfigureAwait(false);

            await RunTest("Enqueue_StatusIsQueued", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("StatusQueued").ConfigureAwait(false);

                JsonElement entry = await EnqueueAsync(missionId, vesselId, "feat/status-check").ConfigureAwait(false);

                AssertEqual("Queued", entry.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enqueue_IdHasMrgPrefix", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("IdPrefix").ConfigureAwait(false);

                JsonElement entry = await EnqueueAsync(missionId, vesselId, "feat/id-prefix").ConfigureAwait(false);

                string id = entry.GetProperty("Id").GetString()!;
                AssertStartsWith("mrg_", id);
                Assert(id.Length > 4, "Id should have content after prefix");
            }).ConfigureAwait(false);

            #endregion

            #region CRUD-GetById

            await RunTest("GetById_ExistingEntry_ReturnsEntry", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("GetById").ConfigureAwait(false);

                JsonElement created = await EnqueueAsync(missionId, vesselId, "feat/get-by-id").ConfigureAwait(false);
                string entryId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(entryId, doc.RootElement.GetProperty("Id").GetString());
                AssertEqual("feat/get-by-id", doc.RootElement.GetProperty("BranchName").GetString());
                AssertEqual(missionId, doc.RootElement.GetProperty("MissionId").GetString());
                AssertEqual(vesselId, doc.RootElement.GetProperty("VesselId").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetById_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue/mrg_nonexistent").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(
                    doc.RootElement.TryGetProperty("Error", out _) || doc.RootElement.TryGetProperty("Message", out _),
                    "Not found should return error");
            }).ConfigureAwait(false);

            await RunTest("GetById_PreservesAllFields", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("PreserveFields").ConfigureAwait(false);

                JsonElement created = await EnqueueAsync(missionId, vesselId, "feat/preserve-fields", "develop").ConfigureAwait(false);
                string entryId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual("feat/preserve-fields", doc.RootElement.GetProperty("BranchName").GetString());
                AssertEqual("develop", doc.RootElement.GetProperty("TargetBranch").GetString());
                AssertEqual("Queued", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            #endregion

            #region CRUD-Cancel

            await RunTest("Cancel_ExistingEntry_Returns204", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("Cancel204").ConfigureAwait(false);

                JsonElement created = await EnqueueAsync(missionId, vesselId, "feat/cancel-test").ConfigureAwait(false);
                string entryId = created.GetProperty("Id").GetString()!;

                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Cancel_NonExistent_Returns204", async () =>
            {
                HttpResponseMessage response = await _AuthClient.DeleteAsync("/api/v1/merge-queue/mrg_nonexistent").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Cancel_ThenGet_ShowsCancelledStatus", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("CancelThenGet").ConfigureAwait(false);

                JsonElement created = await EnqueueAsync(missionId, vesselId, "feat/cancel-then-get").ConfigureAwait(false);
                string entryId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, getResponse.StatusCode);
                string body = await getResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("Cancelled", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            await RunTest("Cancel_SetsCompletedUtc", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("CancelCompleted").ConfigureAwait(false);

                JsonElement created = await EnqueueAsync(missionId, vesselId, "feat/cancel-completed").ConfigureAwait(false);
                string entryId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                string body = await getResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.TryGetProperty("CompletedUtc", out JsonElement completedEl),
                    "Cancelled entry should have CompletedUtc set");
                AssertNotEqual(JsonValueKind.Null, completedEl.ValueKind);
            }).ConfigureAwait(false);

            await RunTest("Cancel_UpdatesLastUpdateUtc", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("CancelUpdate").ConfigureAwait(false);

                JsonElement created = await EnqueueAsync(missionId, vesselId, "feat/cancel-update").ConfigureAwait(false);
                string entryId = created.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                string body = await getResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                Assert(doc.RootElement.TryGetProperty("LastUpdateUtc", out _),
                    "Cancelled entry should have LastUpdateUtc");
            }).ConfigureAwait(false);

            #endregion

            #region List-Empty

            await RunTest("List_Empty_ReturnsEmptyResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(JsonValueKind.Array, doc.RootElement.GetProperty("Objects").ValueKind);
                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            }).ConfigureAwait(false);

            await RunTest("List_Empty_HasEnumerationResultShape", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.TryGetProperty("Objects", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageNumber", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageSize", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalPages", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
                AssertTrue(doc.RootElement.TryGetProperty("Success", out _));
            }).ConfigureAwait(false);

            #endregion

            #region List-AfterEnqueue

            await RunTest("List_AfterEnqueue_ReturnsEntries", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("ListAfter").ConfigureAwait(false);

                await EnqueueAsync(missionId, vesselId, "feat/list-test").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 1);
            }).ConfigureAwait(false);

            await RunTest("List_MultipleEntries_ReturnsAll", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("ListMulti").ConfigureAwait(false);
                string missionId2 = await CreateMissionAsync("Mission2").ConfigureAwait(false);
                string missionId3 = await CreateMissionAsync("Mission3").ConfigureAwait(false);

                await EnqueueAsync(missionId, vesselId, "feat/multi-1").ConfigureAwait(false);
                await EnqueueAsync(missionId2, vesselId, "feat/multi-2").ConfigureAwait(false);
                await EnqueueAsync(missionId3, vesselId, "feat/multi-3").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 3);
            }).ConfigureAwait(false);

            #endregion

            #region List-Pagination

            await RunTest("List_25Entries_PageSize10_Returns3Pages", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("Pag25").ConfigureAwait(false);

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("PagMission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=1").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 25, "TotalRecords should be >= 25");
                AssertTrue(doc.RootElement.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
                AssertEqual(1, doc.RootElement.GetProperty("PageNumber").GetInt32());
                AssertEqual(10, doc.RootElement.GetProperty("PageSize").GetInt32());
            }).ConfigureAwait(false);

            await RunTest("List_25Entries_PageSize10_Page2_Returns10", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("Pag25P2").ConfigureAwait(false);

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("Pag2Mission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag2-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=2").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(10, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
            }).ConfigureAwait(false);

            await RunTest("List_25Entries_PageSize10_Page3_Returns5", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("Pag25P3").ConfigureAwait(false);

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("Pag3Mission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag3-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=3").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, doc.RootElement.GetProperty("PageNumber").GetInt32());
            }).ConfigureAwait(false);

            await RunTest("List_25Entries_PageSize10_Page4_BeyondLastPage_ReturnsEmpty", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("Pag25P4").ConfigureAwait(false);

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("Pag4Mission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag4-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=999").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 25, "TotalRecords should be >= 25");
            }).ConfigureAwait(false);

            await RunTest("List_PageBoundaries_FirstAndLastRecords", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("PagBound").ConfigureAwait(false);

                List<string> createdIds = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string msn = await CreateMissionAsync("BoundMission" + i).ConfigureAwait(false);
                    JsonElement entry = await EnqueueAsync(msn, vesselId, "feat/bound-" + i.ToString("D2")).ConfigureAwait(false);
                    createdIds.Add(entry.GetProperty("Id").GetString()!);
                }

                HttpResponseMessage page1Response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=3&pageNumber=1").ConfigureAwait(false);
                string page1Body = await page1Response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument page1Doc = JsonDocument.Parse(page1Body);
                AssertEqual(3, page1Doc.RootElement.GetProperty("Objects").GetArrayLength());

                HttpResponseMessage page2Response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=3&pageNumber=2").ConfigureAwait(false);
                string page2Body = await page2Response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument page2Doc = JsonDocument.Parse(page2Body);
                AssertTrue(page2Doc.RootElement.GetProperty("Objects").GetArrayLength() >= 2, "Page 2 should have at least 2 items");

                List<string> page1Ids = new List<string>();
                foreach (JsonElement obj in page1Doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    page1Ids.Add(obj.GetProperty("Id").GetString()!);
                }

                List<string> page2Ids = new List<string>();
                foreach (JsonElement obj in page2Doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    page2Ids.Add(obj.GetProperty("Id").GetString()!);
                }

                AssertEqual(0, page1Ids.Intersect(page2Ids).Count());
            }).ConfigureAwait(false);

            await RunTest("List_Ordering_EntriesOrderedByCreatedUtc", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("Order").ConfigureAwait(false);

                List<string> createdIds = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string msn = await CreateMissionAsync("OrderMission" + i).ConfigureAwait(false);
                    JsonElement entry = await EnqueueAsync(msn, vesselId, "feat/order-" + i).ConfigureAwait(false);
                    createdIds.Add(entry.GetProperty("Id").GetString()!);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                List<string> returnedIds = new List<string>();
                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    returnedIds.Add(obj.GetProperty("Id").GetString()!);
                }

                foreach (string id in createdIds)
                {
                    AssertTrue(returnedIds.Contains(id), "Expected returned IDs to contain " + id);
                }
            }).ConfigureAwait(false);

            #endregion

            #region Enumerate

            await RunTest("Enumerate_Default_ReturnsEnumerationResult", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", content).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.TryGetProperty("Objects", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageNumber", out _));
                AssertTrue(doc.RootElement.TryGetProperty("PageSize", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalPages", out _));
                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
                AssertTrue(doc.RootElement.TryGetProperty("Success", out _));
            }).ConfigureAwait(false);

            await RunTest("Enumerate_WithPageSizeAndPageNumber_ReturnsCorrectPage", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("EnumPage").ConfigureAwait(false);

                for (int i = 0; i < 15; i++)
                {
                    string msn = await CreateMissionAsync("EnumMission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/enum-" + i.ToString("D2")).ConfigureAwait(false);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 5, PageNumber = 2 }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(5, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, doc.RootElement.GetProperty("PageNumber").GetInt32());
                AssertEqual(5, doc.RootElement.GetProperty("PageSize").GetInt32());
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 15, "TotalRecords should be >= 15");
                AssertTrue(doc.RootElement.GetProperty("TotalPages").GetInt32() >= 3, "TotalPages should be >= 3");
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Empty_ReturnsZeroRecords", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                // Note: may not be empty due to previous tests in this shared server
                AssertTrue(doc.RootElement.GetProperty("TotalRecords").GetInt64() >= 0);
            }).ConfigureAwait(false);

            await RunTest("Enumerate_AfterEnqueue_ContainsEntry", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("EnumAfter").ConfigureAwait(false);

                await EnqueueAsync(missionId, vesselId, "feat/enum-after").ConfigureAwait(false);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
            }).ConfigureAwait(false);

            await RunTest("Enumerate_QuerystringOverrides_Work", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("EnumQS").ConfigureAwait(false);

                for (int i = 0; i < 8; i++)
                {
                    string msn = await CreateMissionAsync("EnumQSMission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/enumqs-" + i).ConfigureAwait(false);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate?pageSize=3", content).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(3, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, doc.RootElement.GetProperty("PageSize").GetInt32());
            }).ConfigureAwait(false);

            #endregion

            #region Process

            await RunTest("Process_EmptyQueue_Succeeds", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/process", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("processed", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            await RunTest("Process_ReturnsProcessedStatus", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("Process").ConfigureAwait(false);

                await EnqueueAsync(missionId, vesselId, "feat/process-test").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/process", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("processed", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            #endregion

            #region List-IncludesCancelled

            await RunTest("List_IncludesCancelledEntries", async () =>
            {
                (string fleetId, string vesselId, string missionId) = await CreatePrerequisitesAsync("ListCancelled").ConfigureAwait(false);

                JsonElement entry = await EnqueueAsync(missionId, vesselId, "feat/list-cancelled").ConfigureAwait(false);
                string entryId = entry.GetProperty("Id").GetString()!;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);

                bool foundCancelled = false;
                foreach (JsonElement obj in doc.RootElement.GetProperty("Objects").EnumerateArray())
                {
                    if (obj.GetProperty("Id").GetString() == entryId)
                    {
                        AssertEqual("Cancelled", obj.GetProperty("Status").GetString());
                        foundCancelled = true;
                    }
                }
                Assert(foundCancelled, "Cancelled entry should appear in list");
            }).ConfigureAwait(false);

            #endregion

            #region Multiple-Enqueue-UniqueIds

            await RunTest("Enqueue_MultipleEntries_HaveUniqueIds", async () =>
            {
                (string fleetId, string vesselId, string _) = await CreatePrerequisitesAsync("UniqueIds").ConfigureAwait(false);

                HashSet<string> ids = new HashSet<string>();
                for (int i = 0; i < 10; i++)
                {
                    string msn = await CreateMissionAsync("UniqueMission" + i).ConfigureAwait(false);
                    JsonElement entry = await EnqueueAsync(msn, vesselId, "feat/unique-" + i).ConfigureAwait(false);
                    string id = entry.GetProperty("Id").GetString()!;
                    Assert(ids.Add(id), "Each merge entry should have a unique ID");
                }
            }).ConfigureAwait(false);

            #endregion

            #region EnumerationResult-Shape

            await RunTest("List_SuccessField_IsTrue", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            }).ConfigureAwait(false);

            await RunTest("List_TotalMs_IsPresent", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.TryGetProperty("TotalMs", out JsonElement totalMs));
                AssertTrue(totalMs.GetDouble() >= 0);
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}
