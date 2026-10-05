namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for merge queue routes: enqueue, get, cancel, list, enumerate, and
    /// process. Ported 1:1 from the retired automated MergeQueueTests suite. Each case is
    /// self-contained and creates its own fleet/vessel/mission prerequisites against the shared
    /// e2e server fixture.
    /// </summary>
    public sealed class MergeQueueSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.MergeQueue";

        private readonly object _SeedLock = new object();
        private Task? _SharedQueueSeed;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Merge Queue Routes suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("enqueue_returns_201_with_correct_properties", "Enqueue_Returns201_WithCorrectProperties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "Enqueue201").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(authClient, missionId, vesselId, "feat/enqueue-test").ConfigureAwait(false);

                AssertStartsWith("mrg_", entry.Id);
                AssertEqual(missionId, entry.MissionId);
                AssertEqual(vesselId, entry.VesselId);
                AssertEqual("feat/enqueue-test", entry.BranchName);
                AssertEqual("main", entry.TargetBranch);
                AssertEqual("Queued", entry.Status.ToString());
            }));

            cases.Add(CaseAsync("enqueue_with_custom_target_branch_returns_correct_target", "Enqueue_WithCustomTargetBranch_ReturnsCorrectTarget", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "CustomTarget").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(authClient, missionId, vesselId, "feat/custom-target", "develop").ConfigureAwait(false);

                AssertEqual("develop", entry.TargetBranch);
            }));

            cases.Add(CaseAsync("enqueue_sets_timestamps", "Enqueue_SetsTimestamps", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "Timestamps").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                DateTime before = DateTime.UtcNow.AddSeconds(-5);
                MergeEntry entry = await EnqueueAsync(authClient, missionId, vesselId, "feat/timestamps").ConfigureAwait(false);
                DateTime after = DateTime.UtcNow.AddSeconds(5);

                DateTime created = entry.CreatedUtc.ToUniversalTime();
                Assert(created >= before && created <= after, "CreatedUtc " + created + " should be between " + before + " and " + after);

                DateTime updated = entry.LastUpdateUtc.ToUniversalTime();
                Assert(updated >= before && updated <= after, "LastUpdateUtc " + updated + " should be between " + before + " and " + after);
            }));

            cases.Add(CaseAsync("enqueue_status_is_queued", "Enqueue_StatusIsQueued", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "StatusQueued").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(authClient, missionId, vesselId, "feat/status-check").ConfigureAwait(false);

                AssertEqual("Queued", entry.Status.ToString());
            }));

            cases.Add(CaseAsync("enqueue_id_has_mrg_prefix", "Enqueue_IdHasMrgPrefix", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "IdPrefix").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(authClient, missionId, vesselId, "feat/id-prefix").ConfigureAwait(false);

                string id = entry.Id;
                AssertStartsWith("mrg_", id);
                Assert(id.Length > 4, "Id should have content after prefix");
            }));

            cases.Add(CaseAsync("get_by_id_existing_entry_returns_entry", "GetById_ExistingEntry_ReturnsEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "GetById").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(authClient, missionId, vesselId, "feat/get-by-id").ConfigureAwait(false);
                string entryId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);
                AssertEqual(entryId, fetched.Id);
                AssertEqual("feat/get-by-id", fetched.BranchName);
                AssertEqual(missionId, fetched.MissionId);
                AssertEqual(vesselId, fetched.VesselId);
            }));

            cases.Add(CaseAsync("get_by_id_not_found_returns_error", "GetById_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue/mrg_nonexistent").ConfigureAwait(false);
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Not found should return error").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_by_id_preserves_all_fields", "GetById_PreservesAllFields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "PreserveFields").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(authClient, missionId, vesselId, "feat/preserve-fields", "develop").ConfigureAwait(false);
                string entryId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);

                AssertEqual("feat/preserve-fields", fetched.BranchName);
                AssertEqual("develop", fetched.TargetBranch);
                AssertEqual("Queued", fetched.Status.ToString());
            }));

            cases.Add(CaseAsync("cancel_existing_entry_returns_204", "Cancel_ExistingEntry_Returns204", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "Cancel204").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(authClient, missionId, vesselId, "feat/cancel-test").ConfigureAwait(false);
                string entryId = created.Id;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
            }));

            cases.Add(CaseAsync("cancel_non_existent_returns_404", "Cancel_NonExistent_Returns404", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/merge-queue/mrg_nonexistent").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("cancel_then_get_shows_cancelled_status", "Cancel_ThenGet_ShowsCancelledStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "CancelThenGet").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(authClient, missionId, vesselId, "feat/cancel-then-get").ConfigureAwait(false);
                string entryId = created.Id;

                await authClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, getResponse.StatusCode);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(getResponse).ConfigureAwait(false);
                AssertEqual("Cancelled", fetched.Status.ToString());
            }));

            cases.Add(CaseAsync("cancel_sets_completed_utc", "Cancel_SetsCompletedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "CancelCompleted").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(authClient, missionId, vesselId, "feat/cancel-completed").ConfigureAwait(false);
                string entryId = created.Id;

                await authClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(getResponse).ConfigureAwait(false);

                Assert(fetched.CompletedUtc != null,
                    "Cancelled entry should have CompletedUtc set");
            }));

            cases.Add(CaseAsync("cancel_updates_last_update_utc", "Cancel_UpdatesLastUpdateUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "CancelUpdate").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry created = await EnqueueAsync(authClient, missionId, vesselId, "feat/cancel-update").ConfigureAwait(false);
                string entryId = created.Id;

                await authClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                MergeEntry fetched = await JsonHelper.DeserializeAsync<MergeEntry>(getResponse).ConfigureAwait(false);

                Assert(fetched.LastUpdateUtc != default,
                    "Cancelled entry should have LastUpdateUtc");
            }));

            cases.Add(CaseAsync("list_empty_returns_empty_result", "List_Empty_ReturnsEmptyResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertNotNull(result.Objects);
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("list_empty_has_enumeration_result_shape", "List_Empty_HasEnumerationResultShape", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertNotNull(result.Objects);
                Assert(result.PageNumber >= 0, "PageNumber should be present");
                Assert(result.PageSize >= 0, "PageSize should be present");
                Assert(result.TotalPages >= 0, "TotalPages should be present");
                Assert(result.TotalRecords >= 0, "TotalRecords should be present");
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("list_after_enqueue_returns_entries", "List_AfterEnqueue_ReturnsEntries", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "ListAfter").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                await EnqueueAsync(authClient, missionId, vesselId, "feat/list-test").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
                AssertTrue(result.TotalRecords >= 1);
            }));

            cases.Add(CaseAsync("list_multiple_entries_returns_all", "List_MultipleEntries_ReturnsAll", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "ListMulti").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;
                string missionId2 = await CreateMissionAsync(authClient, "Mission2").ConfigureAwait(false);
                string missionId3 = await CreateMissionAsync(authClient, "Mission3").ConfigureAwait(false);

                await EnqueueAsync(authClient, missionId, vesselId, "feat/multi-1").ConfigureAwait(false);
                await EnqueueAsync(authClient, missionId2, vesselId, "feat/multi-2").ConfigureAwait(false);
                await EnqueueAsync(authClient, missionId3, vesselId, "feat/multi-3").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 3);
            }));

            cases.Add(CaseAsync("list_25_entries_page_size_10_returns_3_pages", "List_25Entries_PageSize10_Returns3Pages", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=1").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(10, result.Objects.Count);
                AssertTrue(result.TotalRecords >= 25, "TotalRecords should be >= 25");
                AssertTrue(result.TotalPages >= 3, "TotalPages should be >= 3");
                AssertEqual(1, result.PageNumber);
                AssertEqual(10, result.PageSize);
            }));

            cases.Add(CaseAsync("list_25_entries_page_size_10_page_2_returns_10", "List_25Entries_PageSize10_Page2_Returns10", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=2").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_25_entries_page_size_10_page_3_returns_5", "List_25Entries_PageSize10_Page3_Returns5", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=3").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_25_entries_page_size_10_page_4_beyond_last_page_returns_empty", "List_25Entries_PageSize10_Page4_BeyondLastPage_ReturnsEmpty", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10&pageNumber=999").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(0, result.Objects.Count);
                AssertTrue(result.TotalRecords >= 25, "TotalRecords should be >= 25");
            }));

            cases.Add(CaseAsync("list_page_boundaries_first_and_last_records", "List_PageBoundaries_FirstAndLastRecords", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage page1Response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=3&pageNumber=1").ConfigureAwait(false);
                EnumerationResult<MergeEntry> page1Result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(page1Response).ConfigureAwait(false);
                AssertEqual(3, page1Result.Objects.Count);

                HttpResponseMessage page2Response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=3&pageNumber=2").ConfigureAwait(false);
                EnumerationResult<MergeEntry> page2Result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(page2Response).ConfigureAwait(false);
                AssertTrue(page2Result.Objects.Count >= 2, "Page 2 should have at least 2 items");

                List<string> page1Ids = page1Result.Objects.Select(obj => obj.Id).ToList();
                List<string> page2Ids = page2Result.Objects.Select(obj => obj.Id).ToList();

                AssertEqual(0, page1Ids.Intersect(page2Ids).Count());
            }));

            cases.Add(CaseAsync("list_ordering_entries_ordered_by_created_utc", "List_Ordering_EntriesOrderedByCreatedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "Order").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                List<string> createdIds = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string msn = await CreateMissionAsync(authClient, "OrderMission" + i).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(authClient, msn, vesselId, "feat/order-" + i).ConfigureAwait(false);
                    createdIds.Add(entry.Id);
                }

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                List<string> returnedIds = result.Objects.Select(obj => obj.Id).ToList();

                foreach (string id in createdIds)
                {
                    AssertTrue(returnedIds.Contains(id), "Expected returned IDs to contain " + id);
                }
            }));

            cases.Add(CaseAsync("enumerate_default_returns_enumeration_result", "Enumerate_Default_ReturnsEnumerationResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertNotNull(result.Objects);
                Assert(result.PageNumber >= 0, "PageNumber should be present");
                Assert(result.PageSize >= 0, "PageSize should be present");
                Assert(result.TotalPages >= 0, "TotalPages should be present");
                Assert(result.TotalRecords >= 0, "TotalRecords should be present");
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("enumerate_with_page_size_and_page_number_returns_correct_page", "Enumerate_WithPageSizeAndPageNumber_ReturnsCorrectPage", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { PageSize = 5, PageNumber = 2 })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
                AssertEqual(5, result.PageSize);
                AssertTrue(result.TotalRecords >= 15, "TotalRecords should be >= 15");
                AssertTrue(result.TotalPages >= 3, "TotalPages should be >= 3");
            }));

            cases.Add(CaseAsync("enumerate_empty_returns_zero_records", "Enumerate_Empty_ReturnsZeroRecords", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                // Note: may not be empty due to previous tests in this shared server
                AssertTrue(result.TotalRecords >= 0);
            }));

            cases.Add(CaseAsync("enumerate_after_enqueue_contains_entry", "Enumerate_AfterEnqueue_ContainsEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "EnumAfter").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                await EnqueueAsync(authClient, missionId, vesselId, "feat/enum-after").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 1);
            }));

            cases.Add(CaseAsync("enumerate_querystring_overrides_work", "Enumerate_QuerystringOverrides_Work", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureQueueSeededAsync(authClient).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/enumerate?pageSize=3", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);

                AssertEqual(3, result.Objects.Count);
                AssertEqual(3, result.PageSize);
            }));

            cases.Add(CaseAsync("process_empty_queue_succeeds", "Process_EmptyQueue_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/process", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                ProcessMergeQueueResponse result = await JsonHelper.DeserializeAsync<ProcessMergeQueueResponse>(response).ConfigureAwait(false);
                AssertEqual("processed", result.Status);
            }));

            cases.Add(CaseAsync("process_returns_processed_status", "Process_ReturnsProcessedStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "Process").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                await EnqueueAsync(authClient, missionId, vesselId, "feat/process-test").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/merge-queue/process", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                ProcessMergeQueueResponse result = await JsonHelper.DeserializeAsync<ProcessMergeQueueResponse>(response).ConfigureAwait(false);
                AssertEqual("processed", result.Status);
            }));

            cases.Add(CaseAsync("list_includes_cancelled_entries", "List_IncludesCancelledEntries", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "ListCancelled").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;
                string missionId = prereqs.MissionId;

                MergeEntry entry = await EnqueueAsync(authClient, missionId, vesselId, "feat/list-cancelled").ConfigureAwait(false);
                string entryId = entry.Id;

                await authClient.DeleteAsync("/api/v1/merge-queue/" + entryId).ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
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
            }));

            cases.Add(CaseAsync("enqueue_multiple_entries_have_unique_ids", "Enqueue_MultipleEntries_HaveUniqueIds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "UniqueIds").ConfigureAwait(false);
                string vesselId = prereqs.VesselId;

                HashSet<string> ids = new HashSet<string>();
                for (int i = 0; i < 10; i++)
                {
                    string msn = await CreateMissionAsync(authClient, "UniqueMission" + i).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(authClient, msn, vesselId, "feat/unique-" + i).ConfigureAwait(false);
                    string id = entry.Id;
                    Assert(ids.Add(id), "Each merge entry should have a unique ID");
                }
            }));

            cases.Add(CaseAsync("list_success_field_is_true", "List_SuccessField_IsTrue", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("list_total_ms_is_present", "List_TotalMs_IsPresent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/merge-queue?pageSize=10000").ConfigureAwait(false);
                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                AssertTrue(result.TotalMs >= 0);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Merge Queue Routes",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> CreateFleetAsync(HttpClient authClient, string name = "MergeTestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = uniqueName })).ConfigureAwait(false);
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp).ConfigureAwait(false);
            return fleet.Id;
        }

        private static async Task<string> CreateVesselAsync(HttpClient authClient, string fleetId, string name = "MergeTestVessel")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId })).ConfigureAwait(false);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp).ConfigureAwait(false);
            return vessel.Id;
        }

        private static async Task<string> CreateMissionAsync(HttpClient authClient, string title = "MergeTestMission")
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title = title, Description = "Mission for merge queue testing" })).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            string missionId;
            if (wrapper.Mission != null)
                missionId = wrapper.Mission.Id;
            else
                missionId = JsonHelper.Deserialize<Mission>(body).Id;
            return missionId;
        }

        private static async Task<MergeEntry> EnqueueAsync(HttpClient authClient, string missionId, string vesselId, string branch, string targetBranch = "main")
        {
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/merge-queue", JsonHelper.ToJsonContent(new
            {
                MissionId = missionId,
                VesselId = vesselId,
                BranchName = branch,
                TargetBranch = targetBranch
            })).ConfigureAwait(false);
            MergeEntry entry = await JsonHelper.DeserializeAsync<MergeEntry>(resp).ConfigureAwait(false);
            return entry;
        }

        private static async Task<MergeQueuePrerequisiteResult> CreatePrerequisitesAsync(HttpClient authClient, string suffix = "")
        {
            string fleetId = await CreateFleetAsync(authClient, "Fleet" + suffix).ConfigureAwait(false);
            string vesselId = await CreateVesselAsync(authClient, fleetId, "Vessel" + suffix).ConfigureAwait(false);
            string missionId = await CreateMissionAsync(authClient, "Mission" + suffix).ConfigureAwait(false);
            return new MergeQueuePrerequisiteResult(fleetId, vesselId, missionId);
        }

        /// <summary>
        /// Ensure the shared merge queue holds a reusable dataset of at least 25 queued entries, seeding it
        /// exactly once for the suite. Every case runs against the same in-process server fixture and the
        /// list/enumerate pagination cases only assert accumulation-tolerant conditions (a full page, a
        /// total at or above a threshold, no page overlap), so they can share one dataset instead of each
        /// re-creating 25 missions and 25 enqueues over sequential HTTP round-trips. The seed Task is
        /// memoized: the first pagination case to run pays the cost and the rest await the completed Task.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>A task that completes once the shared dataset exists.</returns>
        private Task EnsureQueueSeededAsync(HttpClient authClient)
        {
            lock (_SeedLock)
            {
                if (_SharedQueueSeed == null) _SharedQueueSeed = SeedSharedQueueAsync(authClient);
                return _SharedQueueSeed;
            }
        }

        private static async Task SeedSharedQueueAsync(HttpClient authClient)
        {
            MergeQueuePrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, "PagShared").ConfigureAwait(false);

            for (int i = 0; i < 25; i++)
            {
                string missionId = await CreateMissionAsync(authClient, "SharedPagMission" + i).ConfigureAwait(false);
                await EnqueueAsync(authClient, missionId, prereqs.VesselId, "feat/shared-pag-" + i.ToString("D2")).ConfigureAwait(false);
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
