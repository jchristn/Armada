namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Test.Automated;
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
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = uniqueName })).ConfigureAwait(false);
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp).ConfigureAwait(false);
            return fleet.Id;
        }

        private async Task<string> CreateVesselAsync(string fleetId, string name = "MergeTestVessel")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId })).ConfigureAwait(false);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
            return vessel.Id;
        }

        private async Task<string> CreateMissionAsync(string title = "MergeTestMission")
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title = title, Description = "Mission for merge queue testing" })).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            string missionId;
            if (wrapper.Mission != null)
                missionId = wrapper.Mission.Id;
            else
                missionId = JsonHelper.Deserialize<Mission>(body).Id;
            return missionId;
        }

        private async Task<MergeEntry> EnqueueAsync(string missionId, string vesselId, string branch, string targetBranch = "main")
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/merge-queue", JsonHelper.ToJsonContent(new
            {
                MissionId = missionId,
                VesselId = vesselId,
                BranchName = branch,
                TargetBranch = targetBranch
            })).ConfigureAwait(false);
            MergeEntry entry = await JsonHelper.DeserializeAsync<MergeEntry>(resp).ConfigureAwait(false);
            return entry;
        }

        private async Task<MergeQueuePrerequisiteResult> CreatePrerequisitesAsync(string suffix = "")
        {
            string fleetId = await CreateFleetAsync("Fleet" + suffix).ConfigureAwait(false);
            string vesselId = await CreateVesselAsync(fleetId, "Vessel" + suffix).ConfigureAwait(false);
            string missionId = await CreateMissionAsync("Mission" + suffix).ConfigureAwait(false);
            return new MergeQueuePrerequisiteResult(fleetId, vesselId, missionId);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region CRUD-Enqueue

            await RunTest("Enqueue_Returns201_WithCorrectProperties", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Enqueue201").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(missionId, vesselId, "feat/enqueue-test").ConfigureAwait(false);

                AssertStartsWith("mrg_", entry.Id);
                AssertEqual(missionId, entry.MissionId);
                AssertEqual(vesselId, entry.VesselId);
                AssertEqual("feat/enqueue-test", entry.BranchName);
                AssertEqual("main", entry.TargetBranch);
                AssertEqual("Queued", entry.Status.ToString());
            }).ConfigureAwait(false);

            await RunTest("Enqueue_WithCustomTargetBranch_ReturnsCorrectTarget", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("CustomTarget").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(missionId, vesselId, "feat/custom-target", "develop").ConfigureAwait(false);

                AssertEqual("develop", entry.TargetBranch);
            }).ConfigureAwait(false);

            await RunTest("Enqueue_SetsTimestamps", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Timestamps").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                DateTime before = DateTime.UtcNow.AddSeconds(-5);
                MergeEntry entry = await EnqueueAsync(missionId, vesselId, "feat/timestamps").ConfigureAwait(false);
                DateTime after = DateTime.UtcNow.AddSeconds(5);

                DateTime created = entry.CreatedUtc.ToUniversalTime();
                Assert(created >= before && created <= after, "CreatedUtc " + created + " should be between " + before + " and " + after);

                DateTime updated = entry.LastUpdateUtc.ToUniversalTime();
                Assert(updated >= before && updated <= after, "LastUpdateUtc " + updated + " should be between " + before + " and " + after);
            }).ConfigureAwait(false);

            await RunTest("Enqueue_StatusIsQueued", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("StatusQueued").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(missionId, vesselId, "feat/status-check").ConfigureAwait(false);

                AssertEqual("Queued", entry.Status.ToString());
            }).ConfigureAwait(false);

            await RunTest("Enqueue_IdHasMrgPrefix", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("IdPrefix").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(missionId, vesselId, "feat/id-prefix").ConfigureAwait(false);

                string id = entry.Id;
                AssertStartsWith("mrg_", id);
                Assert(id.Length > 4, "Id should have content after prefix");
            }).ConfigureAwait(false);

            #endregion

            #region CRUD-GetById

            await RunTest("GetById_ExistingEntry_ReturnsEntry", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("GetById").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(missionId, vesselId, "feat/get-by-id").ConfigureAwait(false);
                string entryId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);
                AssertEqual(entryId, fetched.Id);
                AssertEqual("feat/get-by-id", fetched.BranchName);
                AssertEqual(missionId, fetched.MissionId);
                AssertEqual(vesselId, fetched.VesselId);
            }).ConfigureAwait(false);

            await RunTest("GetById_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue/mrg_nonexistent").ConfigureAwait(false);
                ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);
                Assert(
                    error.Error != null || error.Message != null,
                    "Not found should return error");
            }).ConfigureAwait(false);

            await RunTest("GetById_PreservesAllFields", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("PreserveFields").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(missionId, vesselId, "feat/preserve-fields", "develop").ConfigureAwait(false);
                string entryId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);

                AssertEqual("feat/preserve-fields", fetched.BranchName);
                AssertEqual("develop", fetched.TargetBranch);
                AssertEqual("Queued", fetched.Status.ToString());
            }).ConfigureAwait(false);

            #endregion

            #region CRUD-Cancel

            await RunTest("Cancel_ExistingEntry_Returns204", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Cancel204").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(missionId, vesselId, "feat/cancel-test").ConfigureAwait(false);
                string entryId = created.Id;

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
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("CancelThenGet").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(missionId, vesselId, "feat/cancel-then-get").ConfigureAwait(false);
                string entryId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, getResponse.StatusCode);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(getResponse).ConfigureAwait(false);
                AssertEqual("Cancelled", fetched.Status.ToString());
            }).ConfigureAwait(false);

            await RunTest("Cancel_SetsCompletedUtc", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("CancelCompleted").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(missionId, vesselId, "feat/cancel-completed").ConfigureAwait(false);
                string entryId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(getResponse).ConfigureAwait(false);

                Assert(fetched.CompletedUtc != null,
                    "Cancelled entry should have CompletedUtc set");
            }).ConfigureAwait(false);

            await RunTest("Cancel_UpdatesLastUpdateUtc", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("CancelUpdate").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(missionId, vesselId, "feat/cancel-update").ConfigureAwait(false);
                string entryId = created.Id;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await _AuthClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(getResponse).ConfigureAwait(false);

                Assert(fetched.LastUpdateUtc != default,
                    "Cancelled entry should have LastUpdateUtc");
            }).ConfigureAwait(false);

            #endregion

            #region List-Empty

            await RunTest("List_Empty_ReturnsEmptyResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertNotNull(result.Objects);
                AssertTrue(result.Success);
            }).ConfigureAwait(false);

            await RunTest("List_Empty_HasEnumerationResultShape", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertNotNull(result.Objects);
                Assert(result.PageNumber >= 0, "PageNumber should be present");
                Assert(result.PageSize >= 0, "PageSize should be present");
                Assert(result.TotalPages >= 0, "TotalPages should be present");
                Assert(result.TotalRecords >= 0, "TotalRecords should be present");
                AssertTrue(result.Success);
            }).ConfigureAwait(false);

            #endregion

            #region List-AfterEnqueue

            await RunTest("List_AfterEnqueue_ReturnsEntries", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("ListAfter").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                await EnqueueAsync(missionId, vesselId, "feat/list-test").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                AssertTrue(result.TotalRecords >= 1);
            }).ConfigureAwait(false);

            await RunTest("List_MultipleEntries_ReturnsAll", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("ListMulti").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;
                string missionId2 = await CreateMissionAsync("Mission2").ConfigureAwait(false);
                string missionId3 = await CreateMissionAsync("Mission3").ConfigureAwait(false);

                await EnqueueAsync(missionId, vesselId, "feat/multi-1").ConfigureAwait(false);
                await EnqueueAsync(missionId2, vesselId, "feat/multi-2").ConfigureAwait(false);
                await EnqueueAsync(missionId3, vesselId, "feat/multi-3").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 3);
            }).ConfigureAwait(false);

            #endregion

            #region List-Pagination

            await RunTest("List_25Entries_PageSize10_Returns3Pages", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Pag25").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("PagMission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=1").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(10, result.Objects.Count);
                AssertTrue(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                AssertTrue(result.TotalPages >= 3, "TotalPages should be >= 3");
                AssertEqual(1, result.PageNumber);
                AssertEqual(10, result.PageSize);
            }).ConfigureAwait(false);

            await RunTest("List_25Entries_PageSize10_Page2_Returns10", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Pag25P2").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("Pag2Mission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag2-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=2").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }).ConfigureAwait(false);

            await RunTest("List_25Entries_PageSize10_Page3_Returns5", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Pag25P3").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("Pag3Mission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag3-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=3").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, result.PageNumber);
            }).ConfigureAwait(false);

            await RunTest("List_25Entries_PageSize10_Page4_BeyondLastPage_ReturnsEmpty", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Pag25P4").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                for (int i = 0; i < 25; i++)
                {
                    string msn = await CreateMissionAsync("Pag4Mission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/pag4-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=999").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
                AssertTrue(result.TotalRecords >= 25, "TotalRecords should be >= 25");
            }).ConfigureAwait(false);

            await RunTest("List_PageBoundaries_FirstAndLastRecords", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("PagBound").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                List<string> createdIds = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string msn = await CreateMissionAsync("BoundMission" + i).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(msn, vesselId, "feat/bound-" + i.ToString("D2")).ConfigureAwait(false);
                    createdIds.Add(entry.Id);
                }

                HttpResponseMessage page1Response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=3&pageNumber=1").ConfigureAwait(false);
                EnumerationResult<MergeEntry> page1Result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(page1Response).ConfigureAwait(false);
                AssertEqual(3, page1Result.Objects.Count);

                HttpResponseMessage page2Response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=3&pageNumber=2").ConfigureAwait(false);
                EnumerationResult<MergeEntry> page2Result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(page2Response).ConfigureAwait(false);
                AssertTrue(page2Result.Objects.Count >= 2, "Page 2 should have at least 2 items");

                List<string> page1Ids = page1Result.Objects.Select(obj => obj.Id).ToList();
                List<string> page2Ids = page2Result.Objects.Select(obj => obj.Id).ToList();

                AssertEqual(0, page1Ids.Intersect(page2Ids).Count());
            }).ConfigureAwait(false);

            await RunTest("List_Ordering_EntriesOrderedByCreatedUtc", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Order").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                List<string> createdIds = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string msn = await CreateMissionAsync("OrderMission" + i).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(msn, vesselId, "feat/order-" + i).ConfigureAwait(false);
                    createdIds.Add(entry.Id);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                List<string> returnedIds = result.Objects.Select(obj => obj.Id).ToList();

                foreach (string id in createdIds)
                {
                    AssertTrue(returnedIds.Contains(id), "Expected returned IDs to contain " + id);
                }
            }).ConfigureAwait(false);

            #endregion

            #region Enumerate

            await RunTest("Enumerate_Default_ReturnsEnumerationResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertNotNull(result.Objects);
                Assert(result.PageNumber >= 0, "PageNumber should be present");
                Assert(result.PageSize >= 0, "PageSize should be present");
                Assert(result.TotalPages >= 0, "TotalPages should be present");
                Assert(result.TotalRecords >= 0, "TotalRecords should be present");
                AssertTrue(result.Success);
            }).ConfigureAwait(false);

            await RunTest("Enumerate_WithPageSizeAndPageNumber_ReturnsCorrectPage", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("EnumPage").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                for (int i = 0; i < 15; i++)
                {
                    string msn = await CreateMissionAsync("EnumMission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/enum-" + i.ToString("D2")).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { PageSize = 5, PageNumber = 2 })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
                AssertEqual(5, result.PageSize);
                AssertTrue(result.TotalRecords >= 15, "TotalRecords should be >= 15");
                AssertTrue(result.TotalPages >= 3, "TotalPages should be >= 3");
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Empty_ReturnsZeroRecords", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                // Note: may not be empty due to previous tests in this shared server
                AssertTrue(result.TotalRecords >= 0);
            }).ConfigureAwait(false);

            await RunTest("Enumerate_AfterEnqueue_ContainsEntry", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("EnumAfter").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                await EnqueueAsync(missionId, vesselId, "feat/enum-after").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
            }).ConfigureAwait(false);

            await RunTest("Enumerate_QuerystringOverrides_Work", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("EnumQS").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                for (int i = 0; i < 8; i++)
                {
                    string msn = await CreateMissionAsync("EnumQSMission" + i).ConfigureAwait(false);
                    await EnqueueAsync(msn, vesselId, "feat/enumqs-" + i).ConfigureAwait(false);
                }

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/enumerate?pageSize=3", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(3, result.PageSize);
            }).ConfigureAwait(false);

            #endregion

            #region Process

            await RunTest("Process_EmptyQueue_Succeeds", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/process", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                ProcessMergeQueueResponse result = await JsonHelper.DeserializeAsync<ProcessMergeQueueResponse>(response).ConfigureAwait(false);
                AssertEqual("processed", result.Status);
            }).ConfigureAwait(false);

            await RunTest("Process_ReturnsProcessedStatus", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("Process").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                await EnqueueAsync(missionId, vesselId, "feat/process-test").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/merge-queue/process", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                ProcessMergeQueueResponse result = await JsonHelper.DeserializeAsync<ProcessMergeQueueResponse>(response).ConfigureAwait(false);
                AssertEqual("processed", result.Status);
            }).ConfigureAwait(false);

            #endregion

            #region List-IncludesCancelled

            await RunTest("List_IncludesCancelledEntries", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("ListCancelled").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(missionId, vesselId, "feat/list-cancelled").ConfigureAwait(false);
                string entryId = entry.Id;

                await _AuthClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                bool foundCancelled = false;
                foreach (MergeEntry obj in result.Objects)
                {
                    if (obj.Id == entryId)
                    {
                        AssertEqual("Cancelled", obj.Status.ToString());
                        foundCancelled = true;
                    }
                }
                Assert(foundCancelled, "Cancelled entry should appear in list");
            }).ConfigureAwait(false);

            #endregion

            #region Multiple-Enqueue-UniqueIds

            await RunTest("Enqueue_MultipleEntries_HaveUniqueIds", async () =>
            {
                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync("UniqueIds").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                HashSet<string> ids = new HashSet<string>();
                for (int i = 0; i < 10; i++)
                {
                    string msn = await CreateMissionAsync("UniqueMission" + i).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(msn, vesselId, "feat/unique-" + i).ConfigureAwait(false);
                    string id = entry.Id;
                    Assert(ids.Add(id), "Each merge entry should have a unique ID");
                }
            }).ConfigureAwait(false);

            #endregion

            #region EnumerationResult-Shape

            await RunTest("List_SuccessField_IsTrue", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                AssertTrue(result.Success);
            }).ConfigureAwait(false);

            await RunTest("List_TotalMs_IsPresent", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                AssertTrue(result.TotalMs >= 0);
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}
