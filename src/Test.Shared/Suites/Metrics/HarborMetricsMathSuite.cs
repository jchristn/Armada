namespace Test.Shared.Suites.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Metrics;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The shared bucketing math behind the Harbor metrics (Armada.Core.Metrics): range parsing and bucket sizes, window
    /// alignment on the UTC epoch grid, bucket indexing at the edges, percentiles, and per-bucket concurrency (peak and
    /// time-weighted average, back-to-back jobs, open jobs, and the partly elapsed current bucket).
    /// </summary>
    public sealed class HarborMetricsMathSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Metrics.HarborMetricsMath";
        private static readonly DateTime _Now = new DateTime(2026, 10, 8, 14, 37, 20, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("ranges_parse_and_size", "1h, 24h, and 7d parse case-insensitively with 60, 48, and 56 buckets; unknown ranges are refused", TestTags.Positive, () =>
            {
                AssertTrue(HarborMetricsRanges.TryParse("1H", out HarborMetricsRangeEnum hour), "1H parses");
                AssertEqual(HarborMetricsRangeEnum.OneHour, hour);
                AssertTrue(HarborMetricsRanges.TryParse(null, out HarborMetricsRangeEnum fallback), "null means the default");
                AssertEqual(HarborMetricsRangeEnum.OneDay, fallback);
                AssertTrue(HarborMetricsRanges.TryParse("7d", out HarborMetricsRangeEnum week), "7d parses");
                AssertEqual("7d", HarborMetricsRanges.ToWireName(week));
                AssertFalse(HarborMetricsRanges.TryParse("2h", out HarborMetricsRangeEnum _), "2h is refused");

                AssertEqual(60, HarborMetricsRanges.Layout(HarborMetricsRangeEnum.OneHour, _Now).BucketCount, "1h buckets");
                AssertEqual(48, HarborMetricsRanges.Layout(HarborMetricsRangeEnum.OneDay, _Now).BucketCount, "24h buckets");
                AssertEqual(56, HarborMetricsRanges.Layout(HarborMetricsRangeEnum.SevenDays, _Now).BucketCount, "7d buckets");
                AssertEqual(TimeSpan.FromHours(3), HarborMetricsRanges.BucketSize(HarborMetricsRangeEnum.SevenDays), "7d bucket size");
            }));

            cases.Add(Case("window_aligns_to_epoch_grid", "The window ends at the end of the bucket holding now and starts on the grid", TestTags.Positive, () =>
            {
                TimeBucketLayout day = HarborMetricsRanges.Layout(HarborMetricsRangeEnum.OneDay, _Now);
                AssertEqual(new DateTime(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc), day.ToUtc, "24h window ends at the next half hour");
                AssertEqual(new DateTime(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc), day.FromUtc, "24h window starts 48 buckets earlier");
                AssertEqual(47, day.IndexOf(_Now), "now is in the last bucket");

                TimeBucketLayout week = HarborMetricsRanges.Layout(HarborMetricsRangeEnum.SevenDays, _Now);
                AssertEqual(new DateTime(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc), week.ToUtc, "7d window ends on a 3-hour boundary");
                AssertEqual(0, week.FromUtc.Hour % 3, "7d window starts on a 3-hour boundary");

                AssertEqual(new DateTime(2026, 10, 8, 14, 30, 0, DateTimeKind.Utc), TimeBucketMath.Floor(_Now, TimeSpan.FromMinutes(30)), "floor to 30 minutes");
            }));

            cases.Add(Case("index_edges", "A bucket includes its start and excludes its end; times outside the window are -1", TestTags.Negative, () =>
            {
                TimeBucketLayout hour = HarborMetricsRanges.Layout(HarborMetricsRangeEnum.OneHour, _Now);
                AssertEqual(0, hour.IndexOf(hour.FromUtc), "the window start is bucket 0");
                AssertEqual(1, hour.IndexOf(hour.FromUtc.AddMinutes(1)), "a bucket end is the next bucket");
                AssertEqual(-1, hour.IndexOf(hour.FromUtc.AddTicks(-1)), "before the window");
                AssertEqual(-1, hour.IndexOf(hour.ToUtc), "the window end is outside");
                AssertEqual(hour.StartOf(5).AddMinutes(1), hour.EndOf(5), "bucket end");
                AssertEqual(60, hour.Starts().Count, "every start");
                AssertThrows<ArgumentOutOfRangeException>(() => hour.StartOf(60), "past the last bucket");
                AssertThrows<ArgumentOutOfRangeException>(() => new TimeBucketLayout(_Now, TimeSpan.Zero, 3), "zero bucket");
            }));

            cases.Add(Case("percentiles", "Median and p95 interpolate between ranks; an empty set has none", TestTags.Positive, () =>
            {
                AssertNull(TimeBucketMath.Median(new List<long>()), "empty");
                AssertEqual((long?)7, TimeBucketMath.Median(new List<long> { 7 }), "single value");
                AssertEqual((long?)250, TimeBucketMath.Median(new List<long> { 400, 100, 300, 200 }), "even count interpolates");
                AssertEqual((long?)300, TimeBucketMath.Median(new List<long> { 500, 100, 300 }), "odd count");
                List<long> hundred = new List<long>();
                for (long i = 1; i <= 100; i++) hundred.Add(i * 10);
                AssertEqual((long?)951, TimeBucketMath.Percentile(hundred, 95), "p95 of 10..1000");
                AssertEqual((long?)1000, TimeBucketMath.Percentile(hundred, 100), "p100 is the max");
                AssertThrows<ArgumentOutOfRangeException>(() => TimeBucketMath.Percentile(hundred, 101), "over 100");
            }));

            cases.Add(Case("concurrency_peak_and_average", "Concurrency counts overlapping jobs, not back-to-back ones, and averages over time", TestTags.Positive, () =>
            {
                DateTime from = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
                TimeBucketLayout layout = new TimeBucketLayout(from, TimeSpan.FromMinutes(10), 3);
                List<TimeInterval> intervals = new List<TimeInterval>
                {
                    new TimeInterval(from, from.AddMinutes(5)),
                    new TimeInterval(from.AddMinutes(5), from.AddMinutes(10)),
                    new TimeInterval(from.AddMinutes(12), from.AddMinutes(14)),
                    new TimeInterval(from.AddMinutes(13), from.AddMinutes(18)),
                    new TimeInterval(from.AddMinutes(-30), from.AddMinutes(-20))
                };

                List<ConcurrencyBucket> buckets = ConcurrencyProfile.Build(layout, intervals, from.AddHours(1));
                AssertEqual(3, buckets.Count, "one entry per bucket");
                AssertEqual(1, buckets[0].Peak, "back-to-back jobs do not overlap");
                AssertEqual(1.0, buckets[0].Average, "one job all the time");
                AssertEqual(2, buckets[1].Peak, "overlap in the second bucket");
                AssertEqual(0.7, buckets[1].Average, "(2 + 5) job-minutes over 10 minutes");
                AssertEqual(0, buckets[2].Peak, "idle bucket");
                AssertEqual(0.0, buckets[2].Average, "idle average");
            }));

            cases.Add(Case("concurrency_open_job_and_current_bucket", "An open job runs until now, and the current bucket averages only its elapsed part", TestTags.Negative, () =>
            {
                DateTime from = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);
                TimeBucketLayout layout = new TimeBucketLayout(from, TimeSpan.FromMinutes(10), 2);
                DateTime now = from.AddMinutes(14);
                List<TimeInterval> intervals = new List<TimeInterval> { new TimeInterval(from.AddMinutes(8), null) };

                List<ConcurrencyBucket> buckets = ConcurrencyProfile.Build(layout, intervals, now);
                AssertEqual(1, buckets[0].Peak, "started in the first bucket");
                AssertEqual(0.2, buckets[0].Average, "2 of 10 minutes");
                AssertEqual(1, buckets[1].Peak, "still running");
                AssertEqual(1.0, buckets[1].Average, "running for all 4 elapsed minutes");
            }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Metrics Math", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
