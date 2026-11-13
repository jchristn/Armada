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
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Atomic rollup retention, weighted means, late events and tenant boundaries on every provider.</summary>
    public sealed class UsageRollupsSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Database.UsageRollups";
            return new TestSuiteDescriptor(suite, "Usage rollups", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "partial_retry_late_weighting", "Batched pruning preserves exact means, gaps, late samples and query totals", async token =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DateTime hour = UsageRollupBuilder.Hour(DateTime.UtcNow.AddDays(-8));
                    for (int index = 0; index < 501; index++)
                        await AddAsync(db.Driver, hour.AddSeconds(index), index == 500 ? null : index % 2 == 0 ? 20 : 80, token).ConfigureAwait(false);
                    DateTime cutoff = hour.AddHours(1);
                    AssertEqual(500, await db.Driver.UsageSnapshots.DeleteOlderThanAsync(cutoff, token).ConfigureAwait(false));
                    UsageQuery query = new UsageQuery { TenantId = "ten_rollup", FromUtc = hour, ToUtc = cutoff, BucketMinutes = 60, Metric = "limits", GroupBy = "account" };
                    UsageSeriesPoint partial = (await new UsageAggregationService(db.Driver).SeriesAsync(query, token).ConfigureAwait(false)).Series.Single().Points.Single();
                    AssertEqual(501, partial.ObservationCount, "pruned and remaining raw samples both count exactly once");
                    AssertEqual(50d, partial.MeanPercent!.Value, "null percentage never changes the denominator");
                    AssertEqual(1, await db.Driver.UsageSnapshots.DeleteOlderThanAsync(cutoff, token).ConfigureAwait(false));
                    AssertEqual(0, await db.Driver.UsageSnapshots.DeleteOlderThanAsync(cutoff, token).ConfigureAwait(false));
                    await AddAsync(db.Driver, hour.AddSeconds(5), 100, token).ConfigureAwait(false);
                    AssertEqual(1, await db.Driver.UsageSnapshots.DeleteOlderThanAsync(cutoff, token).ConfigureAwait(false));
                    UsageHourlyRollup rollup = (await db.Driver.UsageRollups.EnumerateAsync(new UsageRollupQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false)).Objects.Single();
                    AssertEqual(502, rollup.SampleCount); AssertEqual(501, rollup.PercentSampleCount);
                    AssertTrue(Math.Abs(rollup.MeanPercent!.Value - 25100d / 501) < 0.0000001);
                    AssertEqual(501, rollup.OkCount); AssertEqual(1, rollup.FailedCount);
                    AssertEqual("Failed", rollup.LastStatus); AssertNull(rollup.LastPercent, "late earlier success cannot erase a newer failure gap");
                    AssertEqual(hour.AddSeconds(500), rollup.LastObservedUtc); AssertTrue(rollup.RawPruned);
                    UsageSeriesPoint final = (await new UsageAggregationService(db.Driver).SeriesAsync(query, token).ConfigureAwait(false)).Series.Single().Points.Single();
                    AssertEqual(502, final.ObservationCount); AssertEqual("Failed", final.Status); AssertNull(final.LastPercent);
                    query.FromUtc = hour.AddMinutes(10);
                    UsageSeriesResult partialHour = await new UsageAggregationService(db.Driver).SeriesAsync(query, token).ConfigureAwait(false);
                    AssertEqual(0, partialHour.Series.Count, "partial retained hours never invent fine-grained samples");
                    AssertNotNull(partialHour.Coverage.Warning, "an unaligned historical bound explains the missing partial hour");
                    AssertEqual(0L, (await db.Driver.UsageRollups.EnumerateAsync(new UsageRollupQuery { TenantId = "ten_other" }, token).ConfigureAwait(false)).TotalRecords);
                    AssertEqual(1, await db.Driver.UsageRollups.DeleteByTenantAsync("ten_rollup", token).ConfigureAwait(false));
                }),
                new TestCaseDescriptor(suite, "membership_expiry_tenant_cascade", "Last-member closure expires metadata and tenant deletion removes every retained usage row", async token =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DateTime old = DateTime.UtcNow.AddDays(-100);
                    await db.Driver.Tenants.CreateAsync(new TenantMetadata { Id = "ten_rollup", Name = "Rollup tenant" }, token).ConfigureAwait(false);
                    await AddAsync(db.Driver, old, 20, token).ConfigureAwait(false);
                    await db.Driver.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = "ten_rollup", HostId = "host_rollup", Runtime = AgentRuntimeEnum.ClaudeCode, AccountKey = "account_rollup", LastGoodSnapshotId = "original", LastGoodCapturedUtc = old }, token).ConfigureAwait(false);
                    foreach (string captain in new[] { "cpt_first", "cpt_second" })
                        await db.Driver.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = "ten_rollup", CaptainId = captain, HostId = "host_rollup", Runtime = AgentRuntimeEnum.ClaudeCode, AccountKey = "account_rollup", EffectiveFromUtc = old }, token).ConfigureAwait(false);
                    await db.Driver.UsageMemberships.CloseAsync("ten_rollup", "cpt_first", old.AddHours(1), token).ConfigureAwait(false);
                    AssertNull((await db.Driver.UsageSnapshots.ReadLatestStateAsync("ten_rollup", "host_rollup", AgentRuntimeEnum.ClaudeCode, "account_rollup", token).ConfigureAwait(false))!.ExpiredUtc);
                    await db.Driver.UsageMemberships.CloseAsync("ten_rollup", "cpt_second", old.AddHours(2), token).ConfigureAwait(false);
                    AssertEqual(old.AddHours(2), (await db.Driver.UsageSnapshots.ReadLatestStateAsync("ten_rollup", "host_rollup", AgentRuntimeEnum.ClaudeCode, "account_rollup", token).ConfigureAwait(false))!.ExpiredUtc);
                    AssertEqual(3, await db.Driver.UsageSnapshots.PruneInactiveStateAsync(old.AddDays(1), token).ConfigureAwait(false));
                    await db.Driver.UsageSnapshots.DeleteOlderThanAsync(old.AddDays(1), token).ConfigureAwait(false);
                    AssertEqual(1L, (await db.Driver.UsageRollups.EnumerateAsync(new UsageRollupQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false)).TotalRecords);
                    await db.Driver.Tenants.DeleteAsync("ten_rollup", token).ConfigureAwait(false);
                    AssertEqual(0L, (await db.Driver.UsageRollups.EnumerateAsync(new UsageRollupQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false)).TotalRecords);
                    AssertEqual(0L, (await db.Driver.UsageSnapshots.EnumerateAsync(new UsageSnapshotQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false)).TotalRecords);
                }),
                new TestCaseDescriptor(suite, "token_expiry_does_not_invent_consumption", "Retained quota history cannot reconstruct deleted completion tokens", async token =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DateTime hour = UsageRollupBuilder.Hour(DateTime.UtcNow.AddDays(-8));
                    await AddAsync(db.Driver, hour, 80, token).ConfigureAwait(false);
                    await db.Driver.UsageSnapshots.DeleteOlderThanAsync(hour.AddHours(1), token).ConfigureAwait(false);
                    await db.Driver.TokenUsage.CreateAsync(new TokenUsageRecord { TenantId = "ten_rollup", CaptainId = "cpt_rollup_a", Runtime = "ClaudeCode", Provider = "anthropic", HostId = "host_rollup", AccountKey = "account_rollup", MembershipId = "membership_fixture", Source = "mission", SourceId = "msn_deleted", InputTokens = 10, TotalTokens = 10, CreatedUtc = hour }, token).ConfigureAwait(false);
                    UsageQuery query = new UsageQuery { TenantId = "ten_rollup", Provider = "anthropic", FromUtc = hour, ToUtc = hour.AddHours(1), BucketMinutes = 60 };
                    UsageAggregationService service = new UsageAggregationService(db.Driver);
                    AssertEqual(1L, (await service.MissionsAsync(query, token).ConfigureAwait(false)).TotalRecords);
                    await db.Driver.TokenUsage.DeleteByFilterAsync(new TokenUsageQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false);
                    AssertEqual(0L, (await service.MissionsAsync(query, token).ConfigureAwait(false)).TotalRecords);
                    AssertEqual(80d, (await service.SeriesAsync(query, token).ConfigureAwait(false)).Series.Single().Points.Single().LastPercent!.Value);
                    query.Metric = "tokens";
                    AssertEqual(0, (await service.SeriesAsync(query, token).ConfigureAwait(false)).Series.Count);
                }),
                new TestCaseDescriptor(suite, "retention_cutoff_and_latest", "Retention preserves latest age, raw boundary and ninety-day rollup cutoff", async token =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DateTime now = UsageRollupBuilder.Hour(DateTime.UtcNow);
                    await AddAsync(db.Driver, now.AddDays(-91), 10, token).ConfigureAwait(false);
                    await AddAsync(db.Driver, now.AddDays(-90), 20, token).ConfigureAwait(false);
                    await AddAsync(db.Driver, now.AddDays(-7), 30, token).ConfigureAwait(false);
                    await db.Driver.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = "ten_rollup", HostId = "host_rollup", Runtime = AgentRuntimeEnum.ClaudeCode, AccountKey = "account_rollup", LastGoodSnapshotId = "original", LastGoodCapturedUtc = now.AddDays(-91) }, token).ConfigureAwait(false);
                    RetentionService retention = new RetentionService(db.Driver, new ArmadaSettings(), new LoggingModule(), () => now);
                    AssertEqual(2, await retention.PruneUsageSnapshotsAsync(token).ConfigureAwait(false));
                    AssertEqual(0, await retention.PruneUsageSnapshotsAsync(token).ConfigureAwait(false));
                    UsageHourlyRollup row = (await db.Driver.UsageRollups.EnumerateAsync(new UsageRollupQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false)).Objects.Single();
                    AssertEqual(now.AddDays(-90), row.HourUtc);
                    AssertEqual(now.AddDays(-91), (await db.Driver.UsageSnapshots.ReadLatestStateAsync("ten_rollup", "host_rollup", AgentRuntimeEnum.ClaudeCode, "account_rollup", token).ConfigureAwait(false))!.LastGoodCapturedUtc);
                    AssertEqual(1L, (await db.Driver.UsageSnapshots.EnumerateAsync(new UsageSnapshotQuery { TenantId = "ten_rollup" }, token).ConfigureAwait(false)).TotalRecords);
                })
            });
        }

        private static Task<UsageSnapshot> AddAsync(DatabaseDriver driver, DateTime time, double? percent, CancellationToken token) => driver.UsageSnapshots.CreateAsync(new UsageSnapshot
        {
            TenantId = "ten_rollup", HostId = "host_rollup", Runtime = AgentRuntimeEnum.ClaudeCode, Provider = "anthropic", AccountKey = "account_rollup", CapturedUtc = time,
            Status = percent.HasValue ? UsageSnapshotStatusEnum.Ok : UsageSnapshotStatusEnum.Failed,
            WindowsJson = percent.HasValue ? UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "weekly", UsedPercent = percent } }) : "[]"
        }, new List<string> { "cpt_rollup_a", "cpt_rollup_b" }, token);
    }
}
