namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Regression cases for historical resolution, failure gaps and nullable attribution.</summary>
    public sealed class UsageAggregationContractSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "E2E.UsageAggregation";
            return new TestSuiteDescriptor(suite, "Usage aggregation boundaries", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "hourly_rollup_does_not_cross_bucket", "Hourly aggregates cannot invent sub-hour precision at offset bucket boundaries", async token =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    string tenant = "ten_rollup_" + Guid.NewGuid().ToString("N");
                    DateTime hour = DateTime.UtcNow.Date.AddDays(-10).AddHours(2);
                    await db.UsageRollups.UpsertAsync(new UsageHourlyRollup { TenantId = tenant, HostId = "host", AccountKey = "account", HourUtc = hour, WindowName = "daily", SampleCount = 3, LastPercent = 30, MeanPercent = 20, MinPercent = 10, MaxPercent = 30, LastObservedUtc = hour.AddMinutes(55) }, token).ConfigureAwait(false);
                    UsageAggregationService service = new UsageAggregationService(db);
                    UsageSeriesResult aligned = await service.SeriesAsync(new UsageQuery { TenantId = tenant, FromUtc = hour, ToUtc = hour.AddHours(2), BucketMinutes = 60 }, token).ConfigureAwait(false);
                    AssertEqual(1, aligned.Series.Count); AssertEqual(3, aligned.Series.Single().Points.Single().ObservationCount);
                    UsageSeriesResult offset = await service.SeriesAsync(new UsageQuery { TenantId = tenant, FromUtc = hour.AddMinutes(-30), ToUtc = hour.AddMinutes(90), BucketMinutes = 60 }, token).ConfigureAwait(false);
                    AssertEqual(0, offset.Series.Count, "full hour spans two offset buckets and cannot be allocated exactly");
                    AssertNotNull(offset.Coverage.Warning, "resolution loss is explicit");
                }),
                new TestCaseDescriptor(suite, "failed_observation_breaks_named_window", "A failed account observation inserts a null point in its previously observed named window", async token =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    string tenant = "ten_gap_" + Guid.NewGuid().ToString("N");
                    DateTime from = DateTime.UtcNow.Date;
                    await UsageTestData.SnapshotAsync(db, tenant, from, "account", new List<string>()).ConfigureAwait(false);
                    await db.UsageSnapshots.CreateAsync(new UsageSnapshot { TenantId = tenant, HostId = "host", AccountKey = "account", Provider = "anthropic", CapturedUtc = from.AddMinutes(5), Status = UsageSnapshotStatusEnum.Failed, ErrorCode = "ProbeTimeout" }, new List<string>(), token).ConfigureAwait(false);
                    await UsageTestData.SnapshotAsync(db, tenant, from.AddMinutes(10), "account", new List<string>()).ConfigureAwait(false);
                    UsageSeriesResult result = await new UsageAggregationService(db).SeriesAsync(new UsageQuery { TenantId = tenant, FromUtc = from, ToUtc = from.AddMinutes(15) }, token).ConfigureAwait(false);
                    UsageSeries series = result.Series.Single(); AssertEqual("five_hour", series.Window); AssertEqual(3, series.Points.Count);
                    AssertNull(series.Points[1].LastPercent, "failure must not carry old low utilization"); AssertEqual("Failed", series.Points[1].Status);
                    AssertEqual(62d, series.Points[2].LastPercent!.Value);
                }),
                new TestCaseDescriptor(suite, "partial_legacy_attribution_stays_unknown", "An account key without captured membership stays in the unattributed series", async token =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    string tenant = "ten_legacy_" + Guid.NewGuid().ToString("N");
                    DateTime from = DateTime.UtcNow.AddHours(-1);
                    await UsageTestData.TokenAsync(db, tenant, "captain", from, "unverified-account", null, "mission", "missing", 12).ConfigureAwait(false);
                    UsageSeriesResult result = await new UsageAggregationService(db).SeriesAsync(new UsageQuery { TenantId = tenant, FromUtc = from, ToUtc = from.AddHours(1), Metric = "tokens" }, token).ConfigureAwait(false);
                    AssertNull(result.Series.Single().AccountKey, "do not present partial attribution as an owned account"); AssertEqual(1L, result.Coverage.UnattributedRecords);
                    AssertEqual(12L, result.Series.Single().Points.Single().TotalTokens);
                })
            });
        }
    }
}
