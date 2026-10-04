namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// Descriptors for the persistence added by background discovery and fleet categorization (migration 74): the new
    /// vessel_import_batches columns, the vessel_import_items selected flag, fleet recommendations with ordered vessel
    /// child rows (replace, enumerate, applied fleet, tenant scoping, batch delete cleanup), the cross-tenant in-progress
    /// enumeration used at startup, and atomic captain reserve/release.
    /// </summary>
    public sealed class VesselImportCategorizationDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.VesselImportCategorization";
        private static readonly DateTime _Fixed = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("batch_categorization_columns_roundtrip", "Batch discovery and categorization columns round-trip through create and update", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                VesselImportBatch batch = NewBatch(Constants.DefaultTenantId);
                batch.Status = VesselImportBatchStatusEnum.Discovering;
                batch.DiscoveryJobId = "job_disc";
                batch.Truncated = true;
                batch.ErrorMessage = "scan failed";
                batch = await db.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);

                VesselImportBatch? read = await db.VesselImportBatches.ReadAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertNotNull(read, "batch");
                AssertEqual(VesselImportBatchStatusEnum.Discovering, read!.Status);
                AssertEqual("job_disc", read.DiscoveryJobId);
                AssertTrue(read.Truncated, "truncated");
                AssertEqual("scan failed", read.ErrorMessage);
                AssertEqual(VesselImportCategorizationStatusEnum.None, read.CategorizationStatus, "default categorization status");
                AssertFalse(read.CategorizationApplyAutomatically, "default auto apply");

                read.CategorizationStatus = VesselImportCategorizationStatusEnum.Running;
                read.CategorizationCaptainId = "cpt_x";
                read.CategorizationJobId = "job_cat";
                read.CategorizationPrompt = "Group these\nrepositories";
                read.CategorizationApplyAutomatically = true;
                read.CategorizationError = "boom";
                read.CategorizationStartedUtc = _Fixed;
                read.CategorizationCompletedUtc = _Fixed.AddMinutes(3);
                await db.VesselImportBatches.UpdateAsync(read).ConfigureAwait(false);

                VesselImportBatch? updated = await db.VesselImportBatches.ReadAsync(batch.Id).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Running, updated!.CategorizationStatus);
                AssertEqual("cpt_x", updated.CategorizationCaptainId);
                AssertEqual("job_cat", updated.CategorizationJobId);
                AssertEqual("Group these\nrepositories", updated.CategorizationPrompt);
                AssertTrue(updated.CategorizationApplyAutomatically, "auto apply");
                AssertEqual("boom", updated.CategorizationError);
                AssertSameInstant(_Fixed, updated.CategorizationStartedUtc!.Value, "started");
                AssertSameInstant(_Fixed.AddMinutes(3), updated.CategorizationCompletedUtc!.Value, "completed");
            }));

            cases.Add(CaseAsync("item_selected_roundtrip", "Item Selected flag round-trips through create, update, and enumerate", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);

                VesselImportItem item = NewItem(batch, "/tmp/sel-a");
                item = await db.VesselImportItems.CreateAsync(item).ConfigureAwait(false);
                AssertFalse((await db.VesselImportItems.ReadAsync(item.Id).ConfigureAwait(false))!.Selected, "default false");

                item.Selected = true;
                await db.VesselImportItems.UpdateAsync(item).ConfigureAwait(false);
                List<VesselImportItem> items = await db.VesselImportItems.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertTrue(items.Single().Selected, "selected after update");
            }));

            cases.Add(CaseAsync("recommendations_replace_and_enumerate", "ReplaceForBatchAsync stores recommendations with ordered vessels and replaces earlier ones", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);

                List<VesselImportFleetRecommendation> first = new List<VesselImportFleetRecommendation>
                {
                    NewRecommendation("Payments", 0, "vsl_c", "vsl_a", "vsl_b"),
                    NewRecommendation("Tooling", 1, "vsl_d")
                };
                first[0].Description = "Money things";
                first[0].Rationale = "They share a ledger";
                List<VesselImportFleetRecommendation> stored = await db.VesselImportFleetRecommendations.ReplaceForBatchAsync(Constants.DefaultTenantId, batch.Id, first).ConfigureAwait(false);
                AssertTrue(stored.All(r => r.Id.StartsWith("vfr_", StringComparison.Ordinal)), "vfr_ ids");
                AssertTrue(stored.All(r => r.BatchId == batch.Id && r.TenantId == Constants.DefaultTenantId), "ids filled in");

                List<VesselImportFleetRecommendation> read = await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertEqual(2, read.Count);
                AssertEqual("Payments", read[0].Name);
                AssertEqual("Money things", read[0].Description);
                AssertEqual("They share a ledger", read[0].Rationale);
                AssertEqual("vsl_c,vsl_a,vsl_b", String.Join(",", read[0].VesselIds), "vessel order preserved");
                AssertEqual("Tooling", read[1].Name);
                AssertEqual("vsl_d", read[1].VesselIds.Single());

                await db.VesselImportFleetRecommendations.ReplaceForBatchAsync(Constants.DefaultTenantId, batch.Id,
                    new List<VesselImportFleetRecommendation> { NewRecommendation("Everything", 0, "vsl_a", "vsl_b", "vsl_c", "vsl_d") }).ConfigureAwait(false);
                List<VesselImportFleetRecommendation> replaced = await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertEqual(1, replaced.Count, "replaced");
                AssertEqual(4, replaced[0].VesselIds.Count);
            }));

            cases.Add(CaseAsync("recommendation_applied_fleet_and_tenant_scope", "UpdateAppliedFleetAsync persists; another tenant sees and changes nothing", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string otherTenant = await CreateTenantAsync(db).ConfigureAwait(false);
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                List<VesselImportFleetRecommendation> stored = await db.VesselImportFleetRecommendations.ReplaceForBatchAsync(Constants.DefaultTenantId, batch.Id,
                    new List<VesselImportFleetRecommendation> { NewRecommendation("Web", 0, "vsl_w") }).ConfigureAwait(false);

                await db.VesselImportFleetRecommendations.UpdateAppliedFleetAsync(otherTenant, stored[0].Id, "flt_other").ConfigureAwait(false);
                AssertNull((await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false))[0].AppliedFleetId, "other tenant cannot update");
                AssertEqual(0, (await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(otherTenant, batch.Id).ConfigureAwait(false)).Count, "other tenant reads nothing");
                await db.VesselImportFleetRecommendations.DeleteByBatchAsync(otherTenant, batch.Id).ConfigureAwait(false);
                AssertEqual(1, (await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false)).Count, "other tenant cannot delete");

                await db.VesselImportFleetRecommendations.UpdateAppliedFleetAsync(Constants.DefaultTenantId, stored[0].Id, "flt_web").ConfigureAwait(false);
                AssertEqual("flt_web", (await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false))[0].AppliedFleetId);
            }));

            cases.Add(CaseAsync("batch_delete_removes_recommendations", "Deleting a batch removes its recommendations and vessel links", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                VesselImportBatch batch = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                await db.VesselImportFleetRecommendations.ReplaceForBatchAsync(Constants.DefaultTenantId, batch.Id,
                    new List<VesselImportFleetRecommendation> { NewRecommendation("Data", 0, "vsl_1", "vsl_2") }).ConfigureAwait(false);

                await db.VesselImportBatches.DeleteAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false)).Count);

                VesselImportBatch second = await db.VesselImportBatches.CreateAsync(NewBatch(Constants.DefaultTenantId)).ConfigureAwait(false);
                await db.VesselImportFleetRecommendations.ReplaceForBatchAsync(Constants.DefaultTenantId, second.Id,
                    new List<VesselImportFleetRecommendation> { NewRecommendation("Data", 0, "vsl_1") }).ConfigureAwait(false);
                await db.VesselImportFleetRecommendations.DeleteByBatchAsync(Constants.DefaultTenantId, second.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, second.Id).ConfigureAwait(false)).Count, "DeleteByBatchAsync");
            }));

            cases.Add(CaseAsync("enumerate_in_progress_spans_tenants", "EnumerateInProgressAsync returns Discovering and Pending/Running categorization batches from every tenant", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string otherTenant = await CreateTenantAsync(db).ConfigureAwait(false);

                VesselImportBatch discovering = NewBatch(Constants.DefaultTenantId);
                discovering.Status = VesselImportBatchStatusEnum.Discovering;
                discovering = await db.VesselImportBatches.CreateAsync(discovering).ConfigureAwait(false);

                VesselImportBatch running = NewBatch(otherTenant);
                running.Status = VesselImportBatchStatusEnum.Completed;
                running.CategorizationStatus = VesselImportCategorizationStatusEnum.Running;
                running = await db.VesselImportBatches.CreateAsync(running).ConfigureAwait(false);

                VesselImportBatch done = NewBatch(Constants.DefaultTenantId);
                done.Status = VesselImportBatchStatusEnum.Completed;
                done.CategorizationStatus = VesselImportCategorizationStatusEnum.Completed;
                done = await db.VesselImportBatches.CreateAsync(done).ConfigureAwait(false);

                List<VesselImportBatch> active = await db.VesselImportBatches.EnumerateInProgressAsync().ConfigureAwait(false);
                AssertTrue(active.Any(b => b.Id == discovering.Id), "discovering");
                AssertTrue(active.Any(b => b.Id == running.Id), "running in other tenant");
                AssertFalse(active.Any(b => b.Id == done.Id), "completed excluded");
            }));

            cases.Add(CaseAsync("captain_reserve_and_release", "TryReserveAsync only reserves Idle captains in the tenant; TryReleaseAsync only releases the reserved state", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string otherTenant = await CreateTenantAsync(db).ConfigureAwait(false);
                Captain captain = new Captain("reserve-me");
                captain.TenantId = Constants.DefaultTenantId;
                captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);

                AssertFalse(await db.Captains.TryReserveAsync(otherTenant, captain.Id, CaptainStateEnum.Analyzing).ConfigureAwait(false), "other tenant cannot reserve");
                AssertTrue(await db.Captains.TryReserveAsync(Constants.DefaultTenantId, captain.Id, CaptainStateEnum.Analyzing).ConfigureAwait(false), "reserve idle");
                AssertEqual(CaptainStateEnum.Analyzing, (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.State);
                AssertFalse(await db.Captains.TryReserveAsync(Constants.DefaultTenantId, captain.Id, CaptainStateEnum.Analyzing).ConfigureAwait(false), "second reserve fails");

                Captain reserved = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                reserved.ProcessId = 4242;
                await db.Captains.UpdateAsync(reserved).ConfigureAwait(false);

                AssertFalse(await db.Captains.TryReleaseAsync(Constants.DefaultTenantId, captain.Id, CaptainStateEnum.Planning).ConfigureAwait(false), "wrong state is not released");
                AssertTrue(await db.Captains.TryReleaseAsync(Constants.DefaultTenantId, captain.Id, CaptainStateEnum.Analyzing).ConfigureAwait(false), "release");
                Captain released = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertEqual(CaptainStateEnum.Idle, released.State);
                AssertNull(released.ProcessId, "process cleared");

                await AssertThrowsAsync<ArgumentException>(() => db.Captains.TryReserveAsync(Constants.DefaultTenantId, captain.Id, CaptainStateEnum.Idle), "Idle is not a reservation state").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Import Categorization Database",
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

        private static VesselImportFleetRecommendation NewRecommendation(string name, int sortOrder, params string[] vesselIds)
        {
            VesselImportFleetRecommendation recommendation = new VesselImportFleetRecommendation();
            recommendation.Name = name;
            recommendation.SortOrder = sortOrder;
            recommendation.VesselIds = vesselIds.ToList();
            return recommendation;
        }

        private static async Task<string> CreateTenantAsync(DatabaseDriver db)
        {
            TenantMetadata tenant = new TenantMetadata("Categorize Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
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
