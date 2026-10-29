namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box insight storage on every provider (migrations 105 and 106): vessels.serialize_overlapping_missions,
    /// success flight recorder rows (window, scope, paging and expiry) and recovery outcome rows (pending paging,
    /// settling once, statistics grouped and paged in SQL, deletes).
    /// </summary>
    public sealed class BlackBoxInsightDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.BlackBoxInsights";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("vessel_serialize_overlapping_missions", "SerializeOverlappingMissions defaults to false and round-trips through create, update and reads", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                Vessel v = await db.Vessels.CreateAsync(new Vessel("bbi-serialize-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/s.git")).ConfigureAwait(false);
                AssertFalse((await db.Vessels.ReadAsync(v.Id).ConfigureAwait(false))!.SerializeOverlappingMissions, "default false");
                v.SerializeOverlappingMissions = true;
                await db.Vessels.UpdateAsync(v).ConfigureAwait(false);
                AssertTrue((await db.Vessels.ReadAsync(v.Id).ConfigureAwait(false))!.SerializeOverlappingMissions, "updated");
                Vessel created = new Vessel("bbi-serialize-on-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/s2.git") { SerializeOverlappingMissions = true };
                created = await db.Vessels.CreateAsync(created).ConfigureAwait(false);
                AssertTrue((await db.Vessels.ReadAsync(created.Id).ConfigureAwait(false))!.SerializeOverlappingMissions, "created true");
            }));

            cases.Add(Case("successes", "Success rows round-trip, page newest first within a vessel's window with tenant and owner scope, find the latest per mission and expire", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                DateTime t0 = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
                List<string> files = Enumerable.Range(0, 250).Select(i => "src/f" + i + ".cs").ToList();
                BlackBoxSuccess a = await db.BlackBoxSuccesses.CreateAsync(new BlackBoxSuccess { TenantId = "ten_a", UserId = "usr_a", VesselId = "vsl_s", MissionId = "msn_1", MissionTitle = "First é", BaseCommit = "abc", TargetCommit = "def", TargetBranch = "main", FilesChanged = files, FileCount = 250, LandedUtc = t0, QueueMs = 10, RunMs = 2000, TotalMs = 2010, ExpiresUtc = t0.AddDays(7) }).ConfigureAwait(false);
                await db.BlackBoxSuccesses.CreateAsync(new BlackBoxSuccess { TenantId = "ten_a", UserId = "usr_b", VesselId = "vsl_s", MissionId = "msn_2", LandedUtc = t0.AddHours(1), ExpiresUtc = t0.AddDays(7) }).ConfigureAwait(false);
                await db.BlackBoxSuccesses.CreateAsync(new BlackBoxSuccess { TenantId = "ten_a", VesselId = "vsl_s", MissionId = "msn_1", LandedUtc = t0.AddHours(2), ExpiresUtc = t0.AddDays(7) }).ConfigureAwait(false);
                await db.BlackBoxSuccesses.CreateAsync(new BlackBoxSuccess { TenantId = "ten_b", VesselId = "vsl_s", MissionId = "msn_3", LandedUtc = t0.AddHours(1), ExpiresUtc = t0.AddDays(7) }).ConfigureAwait(false);
                await db.BlackBoxSuccesses.CreateAsync(new BlackBoxSuccess { TenantId = "ten_a", VesselId = "vsl_other", MissionId = "msn_4", LandedUtc = t0, ExpiresUtc = t0.AddDays(7) }).ConfigureAwait(false);

                BlackBoxSuccess read = (await db.BlackBoxSuccesses.ReadAsync(a.Id).ConfigureAwait(false))!;
                AssertTrue(read.Id.StartsWith("bbs_", StringComparison.Ordinal), "prefix");
                AssertEqual("First é", read.MissionTitle); AssertEqual("abc", read.BaseCommit); AssertEqual("def", read.TargetCommit);
                AssertEqual(200, read.FilesChanged.Count, "files capped at 200"); AssertEqual(250, read.FileCount, "total kept");
                AssertEqual(2000L, read.RunMs); AssertEqual(t0, read.LandedUtc);
                AssertNull(await db.BlackBoxSuccesses.ReadAsync(a.Id, "ten_b").ConfigureAwait(false), "tenant scope");
                AssertEqual(t0.AddHours(2), (await db.BlackBoxSuccesses.ReadLatestByMissionAsync("msn_1").ConfigureAwait(false))!.LandedUtc, "latest per mission");

                EnumerationResult<BlackBoxSuccess> window = await db.BlackBoxSuccesses.EnumerateByVesselAsync("vsl_s", "ten_a", null, t0.AddMinutes(-1), t0.AddHours(3), new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, window.TotalRecords, "tenant and vessel"); AssertEqual(2, window.Objects.Count); AssertEqual(2, window.TotalPages);
                AssertEqual("msn_1,msn_2", String.Join(",", window.Objects.Select(s => s.MissionId)), "newest first");
                AssertEqual(1L, (await db.BlackBoxSuccesses.EnumerateByVesselAsync("vsl_s", "ten_a", "usr_b", t0.AddMinutes(-1), t0.AddHours(3), new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "owner scope");
                AssertEqual(1L, (await db.BlackBoxSuccesses.EnumerateByVesselAsync("vsl_s", "ten_a", null, t0.AddMinutes(30), t0.AddMinutes(90), new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "window");
                AssertEqual(4L, (await db.BlackBoxSuccesses.EnumerateByVesselAsync("vsl_s", null, null, t0.AddMinutes(-1), t0.AddHours(3), new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "any tenant");
                AssertEqual(0, (await db.BlackBoxSuccesses.EnumerateByVesselAsync("vsl_s", "ten_a", null, t0.AddMinutes(-1), t0.AddHours(3), new EnumerationQuery { PageNumber = 9 }).ConfigureAwait(false)).Objects.Count, "past the end");

                AssertEqual(5L, await db.BlackBoxSuccesses.CountAsync().ConfigureAwait(false));
                AssertEqual(0, await db.BlackBoxSuccesses.DeleteExpiredAsync(t0.AddDays(6)).ConfigureAwait(false), "nothing expired yet");
                AssertEqual(5, await db.BlackBoxSuccesses.DeleteExpiredAsync(t0.AddDays(8)).ConfigureAwait(false), "expired");
                AssertEqual(0L, await db.BlackBoxSuccesses.CountAsync().ConfigureAwait(false));
            }));

            cases.Add(Case("recovery_outcomes", "Outcome rows round-trip, Pending rows page oldest first, settle once, group into per-path statistics paged in SQL and delete", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                DateTime t0 = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
                BlackBoxRecoveryOutcome p1 = await db.BlackBoxRecoveryOutcomes.CreateAsync(Row("ten_a", "usr_a", "bbx_1", "vsl_a", "BBX-LAND-001", "bbx-land-001.pull-request", BlackBoxRecoveryOutcomeEnum.Pending, t0)).ConfigureAwait(false);
                await db.BlackBoxRecoveryOutcomes.CreateAsync(Row("ten_a", "usr_a", "bbx_1", "vsl_a", "BBX-LAND-001", "bbx-land-001.pull-request", BlackBoxRecoveryOutcomeEnum.Succeeded, t0.AddMinutes(1))).ConfigureAwait(false);
                await db.BlackBoxRecoveryOutcomes.CreateAsync(Row("ten_a", "usr_b", "bbx_2", "vsl_b", "BBX-LAND-001", "bbx-land-001.stash-retry", BlackBoxRecoveryOutcomeEnum.Failed, t0.AddMinutes(2))).ConfigureAwait(false);
                await db.BlackBoxRecoveryOutcomes.CreateAsync(Row("ten_a", "usr_b", "bbx_2", "vsl_b", "BBX-LAND-001", "bbx-land-001.stash-retry", BlackBoxRecoveryOutcomeEnum.RunFailed, t0.AddMinutes(3))).ConfigureAwait(false);
                await db.BlackBoxRecoveryOutcomes.CreateAsync(Row("ten_a", "usr_b", "bbx_2", "vsl_b", null, "manual.path", BlackBoxRecoveryOutcomeEnum.Expired, t0.AddMinutes(4))).ConfigureAwait(false);
                BlackBoxRecoveryOutcome p2 = await db.BlackBoxRecoveryOutcomes.CreateAsync(Row("ten_b", "usr_c", "bbx_3", "vsl_c", "BBX-EXEC-001", "bbx-exec-001.wait", BlackBoxRecoveryOutcomeEnum.Pending, t0.AddMinutes(5))).ConfigureAwait(false);

                BlackBoxRecoveryOutcome read = (await db.BlackBoxRecoveryOutcomes.ReadAsync(p1.Id).ConfigureAwait(false))!;
                AssertTrue(read.Id.StartsWith("bro_", StringComparison.Ordinal), "prefix");
                AssertEqual("bbx-land-001.pull-request", read.PathId); AssertEqual("BBX-LAND-001", read.RuleId); AssertEqual("pbk_rb", read.RunbookId); AssertTrue(read.RunSucceeded); AssertEqual(t0, read.CreatedUtc);
                AssertEqual(2L, (await db.BlackBoxRecoveryOutcomes.EnumerateByEntryAsync("bbx_1", new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "by entry");

                EnumerationResult<BlackBoxRecoveryOutcome> pending = await db.BlackBoxRecoveryOutcomes.EnumeratePendingAsync(new EnumerationQuery { PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(2L, pending.TotalRecords); AssertEqual(p1.Id, pending.Objects.Single().Id, "oldest first");
                AssertTrue(await db.BlackBoxRecoveryOutcomes.SettleAsync(p1.Id, BlackBoxRecoveryOutcomeEnum.Succeeded, "Complete", t0.AddHours(1)).ConfigureAwait(false), "settled");
                AssertFalse(await db.BlackBoxRecoveryOutcomes.SettleAsync(p1.Id, BlackBoxRecoveryOutcomeEnum.Failed, "Failed", t0.AddHours(2)).ConfigureAwait(false), "settles once");
                BlackBoxRecoveryOutcome settled = (await db.BlackBoxRecoveryOutcomes.ReadAsync(p1.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Succeeded, settled.Outcome); AssertEqual("Complete", settled.OutcomeStatus); AssertEqual(t0.AddHours(1), settled.ResolvedUtc);

                EnumerationResult<BlackBoxRecoveryStat> stats = await db.BlackBoxRecoveryOutcomes.EnumerateStatsAsync("ten_a", null, null, null, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(3L, stats.TotalRecords, "three paths in tenant A");
                BlackBoxRecoveryStat pr = stats.Objects.Single(s => s.PathId == "bbx-land-001.pull-request");
                AssertEqual(2L, pr.Runs); AssertEqual(2L, pr.Succeeded); AssertEqual(0L, pr.Pending); AssertEqual(1.0, pr.SuccessRate); AssertEqual(t0.AddMinutes(1), pr.LastRunUtc);
                BlackBoxRecoveryStat stash = stats.Objects.Single(s => s.PathId == "bbx-land-001.stash-retry");
                AssertEqual(2L, stash.Failed, "Failed and RunFailed both count as failed"); AssertEqual(0.0, stash.SuccessRate);
                BlackBoxRecoveryStat manual = stats.Objects.Single(s => s.PathId == "manual.path");
                AssertEqual(1L, manual.Expired); AssertNull(manual.SuccessRate, "nothing settled"); AssertNull(manual.RuleId, "no rule");
                AssertEqual("bbx-land-001.pull-request,bbx-land-001.stash-retry,manual.path", String.Join(",", stats.Objects.Select(s => s.PathId)), "most runs first, then path id");
                EnumerationResult<BlackBoxRecoveryStat> page2 = await db.BlackBoxRecoveryOutcomes.EnumerateStatsAsync("ten_a", null, null, null, new EnumerationQuery { PageNumber = 2, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual("manual.path", page2.Objects.Single().PathId, "paged in SQL"); AssertEqual(2, page2.TotalPages);
                AssertEqual(1L, (await db.BlackBoxRecoveryOutcomes.EnumerateStatsAsync("ten_a", "usr_a", null, null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "owner scope");
                AssertEqual(2L, (await db.BlackBoxRecoveryOutcomes.EnumerateStatsAsync("ten_a", null, null, "vsl_b", new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "vessel filter");
                AssertEqual(2L, (await db.BlackBoxRecoveryOutcomes.EnumerateStatsAsync(null, null, "BBX-LAND-001", null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "rule filter, any tenant");
                AssertEqual(4L, (await db.BlackBoxRecoveryOutcomes.EnumerateStatsAsync(null, null, null, null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "every tenant");

                AssertEqual(1, await db.BlackBoxRecoveryOutcomes.DeleteByEntryAsync("bbx_3").ConfigureAwait(false));
                AssertNull(await db.BlackBoxRecoveryOutcomes.ReadAsync(p2.Id).ConfigureAwait(false), "deleted");
                AssertEqual(2, await db.BlackBoxRecoveryOutcomes.DeleteCreatedBeforeAsync(t0.AddMinutes(1).AddSeconds(30)).ConfigureAwait(false), "aged out");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box insight storage (successes, recovery outcomes, vessel serialize flag)", cases: cases);
        }

        private static BlackBoxRecoveryOutcome Row(string tenant, string user, string entry, string vessel, string? rule, string path, BlackBoxRecoveryOutcomeEnum outcome, DateTime created)
        {
            return new BlackBoxRecoveryOutcome
            {
                TenantId = tenant,
                UserId = user,
                EntryId = entry,
                MissionId = "msn_" + entry,
                VesselId = vessel,
                RuleId = rule,
                PathId = path,
                RunbookId = "pbk_rb",
                RunSucceeded = outcome != BlackBoxRecoveryOutcomeEnum.RunFailed,
                Outcome = outcome,
                CreatedUtc = created
            };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
