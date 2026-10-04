namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for vessel import batch and item persistence over a live database: create/read round-trips of
    /// every column, identifier backfill, update, tenant isolation for reads, enumeration, and deletes, paging and
    /// status filtering, the (tenant, batch, path) uniqueness constraint, batch delete removing its items, and items
    /// surviving deletion of the vessel they created.
    /// </summary>
    public sealed class VesselImportDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.VesselImport";
        private static readonly DateTime _Fixed = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Vessel Import database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("batch_create_read_roundtrips_all_fields", "Batch CreateAsync/ReadAsync round-trips every column", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                VesselImportBatch batch = NewBatch(Constants.DefaultTenantId);
                batch.UserId = Constants.DefaultUserId;
                batch.Status = VesselImportBatchStatusEnum.CompletedWithFailures;
                batch.HarborId = "hbr_x";
                batch.FleetId = "flt_x";
                batch.JobId = "job_x";
                batch.RequestedPathCount = 3;
                batch.CandidateCount = 9;
                batch.CreatedCount = 5;
                batch.SkippedCount = 3;
                batch.FailedCount = 1;
                batch.CreatedUtc = _Fixed;
                batch.CompletedUtc = _Fixed.AddMinutes(2);

                VesselImportBatch created = await db.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);
                AssertStartsWith("vib_", created.Id);

                VesselImportBatch? read = await db.VesselImportBatches.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(read, "batch");
                AssertEqual(Constants.DefaultTenantId, read!.TenantId);
                AssertEqual(Constants.DefaultUserId, read.UserId);
                AssertEqual(VesselImportBatchStatusEnum.CompletedWithFailures, read.Status);
                AssertEqual("hbr_x", read.HarborId);
                AssertEqual("flt_x", read.FleetId);
                AssertEqual("job_x", read.JobId);
                AssertEqual(3, read.RequestedPathCount);
                AssertEqual(9, read.CandidateCount);
                AssertEqual(5, read.CreatedCount);
                AssertEqual(3, read.SkippedCount);
                AssertEqual(1, read.FailedCount);
                AssertSameInstant(_Fixed, read.CreatedUtc, "CreatedUtc");
                AssertNotNull(read.CompletedUtc, "CompletedUtc");
                AssertSameInstant(_Fixed.AddMinutes(2), read.CompletedUtc!.Value, "CompletedUtc");
            }));

            cases.Add(CaseAsync("batch_create_backfills_empty_id", "Batch CreateAsync backfills an empty identifier", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                VesselImportBatch batch = NewBatch(Constants.DefaultTenantId);
                batch.Id = "";
                VesselImportBatch created = await testDb.Driver.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);
                AssertStartsWith("vib_", created.Id);
                AssertNotNull(await testDb.Driver.VesselImportBatches.ReadAsync(created.Id).ConfigureAwait(false), "backfilled batch");
            }));

            cases.Add(CaseAsync("batch_update_persists", "Batch UpdateAsync persists status, counts, and completion", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                VesselImportBatch created = await testDb.Driver.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                created.Status = VesselImportBatchStatusEnum.Completed;
                created.CreatedCount = 4;
                created.CompletedUtc = _Fixed;
                await testDb.Driver.VesselImportBatches.UpdateAsync(created).ConfigureAwait(false);

                VesselImportBatch? read = await testDb.Driver.VesselImportBatches.ReadAsync(Constants.DefaultTenantId, created.Id).ConfigureAwait(false);
                AssertNotNull(read, "batch");
                AssertEqual(VesselImportBatchStatusEnum.Completed, read!.Status);
                AssertEqual(4, read.CreatedCount);
                AssertSameInstant(_Fixed, read.CompletedUtc!.Value, "CompletedUtc");
            }));

            cases.Add(CaseAsync("batch_tenant_isolation", "Another tenant cannot read, enumerate, or delete a batch", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string other = await CreateTenantAsync(db).ConfigureAwait(false);

                VesselImportBatch created = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                AssertNull(await db.VesselImportBatches.ReadAsync(other, created.Id).ConfigureAwait(false), "cross-tenant read");

                EnumerationResult<VesselImportBatch> otherPage = await db.VesselImportBatches.EnumerateAsync(other, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(0L, otherPage.TotalRecords, "cross-tenant enumerate");

                await db.VesselImportBatches.DeleteAsync(other, created.Id).ConfigureAwait(false);
                AssertNotNull(await db.VesselImportBatches.ReadAsync(created.Id).ConfigureAwait(false), "cross-tenant delete must be a no-op");
            }));

            cases.Add(CaseAsync("batch_enumerate_pages_and_filters_status", "Batch EnumerateAsync pages newest first and filters by status", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                for (int i = 0; i < 5; i++)
                {
                    VesselImportBatch batch = NewBatch(Constants.DefaultTenantId);
                    batch.CreatedUtc = _Fixed.AddMinutes(i);
                    batch.Status = i < 2 ? VesselImportBatchStatusEnum.Completed : VesselImportBatchStatusEnum.Discovered;
                    await db.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);
                }

                EnumerationQuery query = new EnumerationQuery();
                query.PageSize = 2;
                query.PageNumber = 3;
                EnumerationResult<VesselImportBatch> page3 = await db.VesselImportBatches.EnumerateAsync(Constants.DefaultTenantId, query).ConfigureAwait(false);
                AssertEqual(5L, page3.TotalRecords);
                AssertEqual(3, page3.TotalPages);
                AssertEqual(1, page3.Objects.Count);
                AssertSameInstant(_Fixed, page3.Objects[0].CreatedUtc, "oldest is last when newest first");

                EnumerationQuery statusQuery = new EnumerationQuery();
                statusQuery.Status = "completed";
                EnumerationResult<VesselImportBatch> completed = await db.VesselImportBatches.EnumerateAsync(Constants.DefaultTenantId, statusQuery).ConfigureAwait(false);
                AssertEqual(2L, completed.TotalRecords, "status filter");
            }));

            cases.Add(CaseAsync("items_create_many_and_enumerate_by_batch", "Item CreateManyAsync and EnumerateByBatchAsync round-trip every column ordered by path", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);

                VesselImportItem b = NewItem(batch, "/code/b");
                b.ProposedName = "b-2";
                b.RemoteUrl = "https://example.com/b.git";
                b.DefaultBranch = "master";
                b.CandidateStatus = VesselImportCandidateStatusEnum.AlreadyOnboarded;
                b.ExistingVesselId = "vsl_existing";
                b.Outcome = VesselImportOutcomeEnum.Failed;
                b.OutcomeReason = "NameConflict";
                b.OutcomeMessage = "name in use";
                b.VesselId = "vsl_new";
                b.Id = "";
                VesselImportItem a = NewItem(batch, "/code/a");

                List<VesselImportItem> created = await db.VesselImportItems.CreateManyAsync(new List<VesselImportItem> { b, a }).ConfigureAwait(false);
                AssertEqual(2, created.Count);
                AssertStartsWith("vii_", b.Id, "backfilled id");

                List<VesselImportItem> items = await db.VesselImportItems.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertEqual(2, items.Count);
                AssertEqual("/code/a", items[0].Path, "ordered by path");
                VesselImportItem rb = items[1];
                AssertEqual("b-2", rb.ProposedName);
                AssertEqual("https://example.com/b.git", rb.RemoteUrl);
                AssertEqual("master", rb.DefaultBranch);
                AssertEqual(VesselImportCandidateStatusEnum.AlreadyOnboarded, rb.CandidateStatus);
                AssertEqual("vsl_existing", rb.ExistingVesselId);
                AssertEqual(VesselImportOutcomeEnum.Failed, rb.Outcome);
                AssertEqual("NameConflict", rb.OutcomeReason);
                AssertEqual("name in use", rb.OutcomeMessage);
                AssertEqual("vsl_new", rb.VesselId);
                AssertEqual(batch.Id, rb.BatchId);
            }));

            cases.Add(CaseAsync("item_update_and_tenant_read", "Item UpdateAsync persists the outcome; another tenant reads null", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string other = await CreateTenantAsync(db).ConfigureAwait(false);
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                VesselImportItem item = await db.VesselImportItems.CreateAsync(NewItem(batch, "/code/x")).ConfigureAwait(false);

                item.Outcome = VesselImportOutcomeEnum.Created;
                item.VesselId = "vsl_created";
                await db.VesselImportItems.UpdateAsync(item).ConfigureAwait(false);

                VesselImportItem? read = await db.VesselImportItems.ReadAsync(Constants.DefaultTenantId, item.Id).ConfigureAwait(false);
                AssertNotNull(read, "item");
                AssertEqual(VesselImportOutcomeEnum.Created, read!.Outcome);
                AssertEqual("vsl_created", read.VesselId);
                AssertNull(await db.VesselImportItems.ReadAsync(other, item.Id).ConfigureAwait(false), "cross-tenant item read");
                List<VesselImportItem> otherItems = await db.VesselImportItems.EnumerateByBatchAsync(other, batch.Id).ConfigureAwait(false);
                AssertEqual(0, otherItems.Count, "cross-tenant enumerate by batch");
            }));

            cases.Add(CaseAsync("item_unique_tenant_batch_path", "A duplicate (tenant, batch, path) item is rejected", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                await db.VesselImportItems.CreateAsync(NewItem(batch, "/code/dup")).ConfigureAwait(false);
                await AssertThrowsAsync<Exception>(() => db.VesselImportItems.CreateAsync(NewItem(batch, "/code/dup")), "duplicate path");

                VesselImportBatch second = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                await db.VesselImportItems.CreateAsync(NewItem(second, "/code/dup")).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("batch_delete_removes_items", "Batch DeleteAsync removes its items; DeleteByBatchAsync empties a batch", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                VesselImportItem item = await db.VesselImportItems.CreateAsync(NewItem(batch, "/code/1")).ConfigureAwait(false);

                VesselImportBatch keep = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                await db.VesselImportItems.CreateAsync(NewItem(keep, "/code/2")).ConfigureAwait(false);
                await db.VesselImportItems.DeleteByBatchAsync(Constants.DefaultTenantId, keep.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselImportItems.EnumerateByBatchAsync(Constants.DefaultTenantId, keep.Id).ConfigureAwait(false)).Count, "DeleteByBatch");
                AssertNotNull(await db.VesselImportBatches.ReadAsync(keep.Id).ConfigureAwait(false), "DeleteByBatch keeps the batch");

                await db.VesselImportBatches.DeleteAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertNull(await db.VesselImportBatches.ReadAsync(batch.Id).ConfigureAwait(false), "batch deleted");
                AssertNull(await db.VesselImportItems.ReadAsync(item.Id).ConfigureAwait(false), "item deleted with batch");
            }));

            cases.Add(CaseAsync("items_survive_vessel_delete", "Items keep their rows when the created vessel is deleted", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = new Vessel("import-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.com/r.git");
                vessel.TenantId = Constants.DefaultTenantId;
                vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                VesselImportItem item = NewItem(batch, "/code/r");
                item.VesselId = vessel.Id;
                item.Outcome = VesselImportOutcomeEnum.Created;
                await db.VesselImportItems.CreateAsync(item).ConfigureAwait(false);

                await db.Vessels.DeleteAsync(vessel.Id).ConfigureAwait(false);
                VesselImportItem? read = await db.VesselImportItems.ReadAsync(item.Id).ConfigureAwait(false);
                AssertNotNull(read, "item survives");
                AssertEqual(vessel.Id, read!.VesselId);
            }));

            cases.Add(CaseAsync("model_counts_clamp_to_zero", "Batch counts clamp negative values to zero", TestTags.Positive, () =>
            {
                VesselImportBatch batch = new VesselImportBatch();
                batch.CandidateCount = -4;
                batch.FailedCount = -1;
                AssertEqual(0, batch.CandidateCount);
                AssertEqual(0, batch.FailedCount);
                AssertStartsWith("vib_", batch.Id);
                AssertStartsWith("vii_", new VesselImportItem().Id);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("null_arguments_throw", "Null entities and identifiers throw ArgumentNullException", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.VesselImportBatches.CreateAsync(null!));
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.VesselImportBatches.ReadAsync(null!));
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.VesselImportItems.CreateManyAsync(null!));
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.VesselImportItems.EnumerateByBatchAsync(Constants.DefaultTenantId, null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Import Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static VesselImportBatch NewBatch(string tenantId)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.TenantId = tenantId;
            return batch;
        }

        private static VesselImportItem NewItem(VesselImportBatch batch, string path)
        {
            VesselImportItem item = new VesselImportItem();
            item.TenantId = batch.TenantId;
            item.BatchId = batch.Id;
            item.Path = path;
            item.ProposedName = System.IO.Path.GetFileName(path);
            return item;
        }

        private static async Task<string> CreateTenantAsync(DatabaseDriver db)
        {
            TenantMetadata tenant = new TenantMetadata("Import Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(tenant).ConfigureAwait(false);
            return tenant.Id;
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
