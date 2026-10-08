namespace Test.Shared.Suites.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Metrics.Charts;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The chart layer behind the Harbor app's and the TUI's Harbor charts (Armada.Core.Metrics.Charts), tested without
    /// Avalonia: axis scale, stacked bar rectangles, line runs with gaps, sparkline points, hit testing, text levels and
    /// downsampling, whole-number ticks, the series mapping from a metrics response (names, order, colors, values, the
    /// capacity line and the axis that tops out at it, tokens by type without counting cache reads twice), the link strip (clipping, worst state per column), tooltips, accessible summaries, and the
    /// value formats.
    /// </summary>
    public sealed class HarborChartsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Metrics.HarborCharts";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("nice_ceiling", "The value axis rounds up to 1, 2, or 5 times a power of ten, at least 1", () =>
            {
                AssertEqual(1.0, ChartGeometry.NiceCeiling(0), "zero");
                AssertEqual(1.0, ChartGeometry.NiceCeiling(0.4), "below one");
                AssertEqual(2.0, ChartGeometry.NiceCeiling(1.5), "1.5");
                AssertEqual(5.0, ChartGeometry.NiceCeiling(4), "4 slots");
                AssertEqual(10.0, ChartGeometry.NiceCeiling(6), "6");
                AssertEqual(10.0, ChartGeometry.NiceCeiling(10), "exactly 10");
                AssertEqual(200000.0, ChartGeometry.NiceCeiling(123456), "tokens");
            }));

            cases.Add(Case("whole_ticks", "Whole-number ticks run from zero to the maximum itself, prefer an even step, and do not crowd the top", () =>
            {
                AssertEqual("0,1,2,3,4", Join(ChartGeometry.WholeTicks(4, 4)), "4 slots, room for 4 gaps");
                AssertEqual("0,2,4", Join(ChartGeometry.WholeTicks(4, 2)), "4 slots, room for 2 gaps");
                AssertEqual("0,2,4,6,7", Join(ChartGeometry.WholeTicks(7, 4)), "7 has no even step; the top is still 7");
                AssertEqual("0,4,7", Join(ChartGeometry.WholeTicks(7, 2)), "7 in 2 gaps");
                AssertEqual("0,5,10", Join(ChartGeometry.WholeTicks(10, 4)), "a step of 5 divides 10");
                AssertEqual("0,4,8,12,16", Join(ChartGeometry.WholeTicks(16, 4)), "16");
                AssertEqual("0,3,6,9", Join(ChartGeometry.WholeTicks(9, 3)), "9 in 3 gaps");
                AssertEqual("0,1", Join(ChartGeometry.WholeTicks(1, 5)), "one slot");
                AssertEqual("0,1", Join(ChartGeometry.WholeTicks(0, 5)), "at least 1");
                AssertEqual("0,3", Join(ChartGeometry.WholeTicks(2.2, 0)), "rounded up; gaps below 1 count as 1");
            }));

            cases.Add(Case("stacked_bar_geometry", "Stacked bars: series stack bottom up in order, zeros draw nothing, bars leave a gap", () =>
            {
                BucketChartModel model = new BucketChartModel();
                model.BucketStartsUtc = new List<DateTime> { Utc(0), Utc(30), Utc(60) };
                model.Series.Add(new ChartSeriesData("a", "A", ChartColorEnum.Success, new double?[] { 2, 0, 5 }));
                model.Series.Add(new ChartSeriesData("b", "B", ChartColorEnum.Danger, new double?[] { 3, null, 5 }));
                AssertEqual(10.0, model.DataMax(), "tallest stack");
                AssertEqual(10.0, model.AxisMax(), "axis");
                List<ChartBarSegment> bars = ChartGeometry.StackedBars(model, 30, 100, 10);
                AssertEqual(4, bars.Count, "bucket 1 draws nothing");
                ChartBarSegment first = bars[0];
                AssertEqual(0, first.BucketIndex);
                AssertEqual(0, first.SeriesIndex);
                AssertEqual(1.0, first.X, "half the 2px gap");
                AssertEqual(8.0, first.Width, "slot 10 minus the gap");
                AssertEqual(80.0, first.Y, "2 of 10 from the bottom");
                AssertEqual(20.0, first.Height);
                ChartBarSegment second = bars[1];
                AssertEqual(1, second.SeriesIndex, "B on top of A");
                AssertEqual(50.0, second.Y, "3 more: the top of 5 of 10");
                AssertEqual(30.0, second.Height);
                ChartBarSegment last = bars[3];
                AssertEqual(2, last.BucketIndex);
                AssertEqual(0.0, last.Y, "a full stack reaches the top");
                AssertEqual(21.0, last.X);

                List<ChartBarSegment> narrow = ChartGeometry.StackedBars(model, 6, 100, 10);
                AssertEqual(2.0, narrow[0].Width, "no gap when a slot is under 4px");
                AssertEqual(0, ChartGeometry.StackedBars(new BucketChartModel(), 100, 100, 10).Count, "no buckets");
            }));

            cases.Add(Case("line_runs_and_hits", "Lines go through bucket centers, break at nulls, and hit testing finds the bucket", () =>
            {
                List<List<ChartPoint>> runs = ChartGeometry.LineRuns(new double?[] { 1, 2, null, 4 }, 40, 10, 4);
                AssertEqual(2, runs.Count, "a null breaks the line");
                AssertEqual(2, runs[0].Count);
                AssertEqual(5.0, runs[0][0].X, "center of bucket 0");
                AssertEqual(7.5, runs[0][0].Y, "1 of 4");
                AssertEqual(1, runs[1].Count, "a lone point");
                AssertEqual(3, runs[1][0].BucketIndex);
                AssertEqual(0.0, runs[1][0].Y, "the maximum is the top");
                AssertEqual(10.0, ChartGeometry.ValueToY(-3, 10, 4), "below zero clamps to the bottom");
                AssertEqual(0.0, ChartGeometry.ValueToY(9, 10, 4), "above the axis clamps to the top");

                AssertEqual(0, ChartGeometry.BucketAt(0, 40, 4), "left edge");
                AssertEqual(3, ChartGeometry.BucketAt(39.9, 40, 4), "right edge");
                AssertEqual(-1, ChartGeometry.BucketAt(40, 40, 4), "past the plot");
                AssertEqual(-1, ChartGeometry.BucketAt(-1, 40, 4), "before the plot");
                AssertEqual(-1, ChartGeometry.BucketAt(5, 40, 0), "no buckets");
            }));

            cases.Add(Case("sparkline_levels_downsample", "Sparklines scale between min and max; text levels keep zero apart; downsampling sums or keeps the peak", () =>
            {
                List<List<ChartPoint>> spark = ChartGeometry.SparklineRuns(new double?[] { 10, 20, 30 }, 100, 20);
                AssertEqual(1, spark.Count);
                AssertEqual(0.0, spark[0][0].X, "first point at the left");
                AssertEqual(100.0, spark[0][2].X, "last point at the right");
                AssertEqual(20.0, spark[0][0].Y, "lowest at the bottom");
                AssertEqual(0.0, spark[0][2].Y, "highest at the top");
                List<List<ChartPoint>> flat = ChartGeometry.SparklineRuns(new double?[] { 5, 5 }, 100, 20);
                AssertEqual(10.0, flat[0][0].Y, "a flat series sits mid-height");
                AssertEqual(0, ChartGeometry.SparklineRuns(new double?[] { null, null }, 100, 20).Count, "nothing to draw");

                List<int> levels = ChartGeometry.Levels(new double?[] { 0, 0.01, 2, 4, null, 9 }, 4, 9);
                AssertEqual("0,1,4,8,-1,8", String.Join(",", levels), "zero, the smallest value, half, the top, no value, clipped");
                AssertThrows<ArgumentOutOfRangeException>(() => ChartGeometry.Levels(new double?[] { 1 }, 1, 1), "levels below 2");

                List<double?> summed = ChartGeometry.Downsample(new double?[] { 1, 2, 3, null, null, null, 5 }, 3, false);
                AssertEqual(3, summed.Count, "7 values into 3 columns by threes");
                AssertEqual(6.0, summed[0]);
                AssertEqual(null, summed[1], "a group of nulls stays null");
                AssertEqual(5.0, summed[2]);
                List<double?> peaks = ChartGeometry.Downsample(new double?[] { 1, 4, 2, 3 }, 2, true);
                AssertEqual(4.0, peaks[0], "peak kept");
                AssertEqual(3.0, peaks[1]);
            }));

            cases.Add(Case("jobs_series_mapping", "Jobs: four series in dashboard order and colors, values by bucket, totals in the summary", () =>
            {
                HarborMetrics metrics = HarborMetricsFixture.Build();
                BucketChartModel jobs = HarborChartMapper.Jobs(metrics);
                AssertEqual(BucketChartKindEnum.StackedBar, jobs.Kind);
                AssertEqual(48, jobs.BucketCount, "24h buckets");
                AssertEqual(30, jobs.BucketMinutes);
                AssertEqual(metrics.FromUtc, jobs.BucketStartsUtc[0], "first bucket");
                AssertEqual(metrics.FromUtc.AddMinutes(30), jobs.BucketStartsUtc[1], "second bucket");
                AssertEqual("missionsFinished,missionsFailed,interactiveFinished,interactiveFailed", String.Join(",", jobs.Series.Select(s => s.Key)));
                AssertEqual("Success,Danger,Accent,Warning", String.Join(",", jobs.Series.Select(s => s.Color.ToString())));
                AssertEqual(2.0, jobs.Series[0].At(1), "missions finished in bucket 1");
                AssertEqual(1.0, jobs.Series[3].At(2), "interactive failed in bucket 2");
                AssertEqual(6.0, jobs.StackTotal(1), "bucket 1 total");
                AssertEqual(6.0, jobs.DataMax());
                AssertEqual(10.0, jobs.AxisMax());
                AssertEqual("Jobs over time, last 24 hours: Missions finished 3, Missions failed 1, Interactive finished 3, Interactive failed 1", jobs.Summary());

                string tip = jobs.Tooltip(1, TimeZoneInfo.Utc);
                string[] tipLines = tip.Split('\n');
                AssertEqual(6, tipLines.Length, "time, four series, total");
                AssertEqual("Wed Oct 7, 13:00 - 13:30", tipLines[0], "bucket 1 in UTC");
                AssertEqual("Missions finished: 2", tipLines[1]);
                AssertEqual("Total: 6", tipLines[5]);
                AssertEqual("", jobs.Tooltip(48, TimeZoneInfo.Utc), "out of range");
                AssertEqual("13:00", jobs.BucketLabel(1, TimeZoneInfo.Utc));
            }));

            cases.Add(Case("slots_and_round_trip_mapping", "Slots: peak and average against a dashed capacity line; round trip keeps gaps", () =>
            {
                HarborMetrics metrics = HarborMetricsFixture.Build();
                BucketChartModel slots = HarborChartMapper.Slots(metrics);
                AssertEqual(BucketChartKindEnum.Line, slots.Kind);
                AssertEqual("peak,average", String.Join(",", slots.Series.Select(s => s.Key)));
                AssertNotNull(slots.Reference, "capacity line");
                AssertEqual(4.0, slots.Reference!.Value);
                AssertEqual("Max slots (4)", slots.Reference.Label);
                AssertEqual(ChartColorEnum.Danger, slots.Reference.Color);
                AssertEqual(4.0, slots.DataMax(), "the capacity counts toward the axis");
                AssertEqual(4.0, slots.AxisCeiling!.Value, "the axis is pinned to the capacity");
                AssertEqual(4.0, slots.AxisMax(), "the top of the axis is MaxConcurrentJobs, not a rounded 5");
                AssertEqual("0,1,2,3,4", Join(slots.AxisTicks(4)), "whole-number ticks up to the capacity");
                AssertEqual("0,2,4", Join(slots.AxisTicks(2)), "fewer ticks on a short plot, still ending at 4");

                metrics.Slots.MaxConcurrentJobs = 7;
                BucketChartModel seven = HarborChartMapper.Slots(metrics);
                AssertEqual(7.0, seven.AxisMax(), "7 slots tops out at 7, not 10");
                AssertEqual(7.0, seven.AxisTicks(4).Last(), "the last tick is the capacity");

                metrics.Slots.MaxConcurrentJobs = 2;
                BucketChartModel over = HarborChartMapper.Slots(metrics);
                AssertEqual(3.0, over.AxisMax(), "a peak past the capacity (lowered mid-window) is not clipped");

                metrics.Slots.MaxConcurrentJobs = 0;
                BucketChartModel none = HarborChartMapper.Slots(metrics);
                AssertEqual(null, none.AxisCeiling, "no capacity: a nice axis");
                AssertEqual(5.0, none.AxisMax());
                AssertEqual("0,2.5,5", Join(none.AxisTicks(4)), "zero, half, and the top");
                metrics.Slots.MaxConcurrentJobs = 4;
                AssertEqual(3.0, slots.Series[0].Max(), "peak");
                AssertEqual(2.5, slots.Series[1].At(1), "average in bucket 1");
                AssertEqual("Slot usage, last 24 hours: Peak highest 3, Average highest 2.5; Max slots (4)", slots.Summary());
                AssertTrue(slots.Tooltip(1, TimeZoneInfo.Utc).EndsWith("\nMax slots (4)", StringComparison.Ordinal), "tooltip names the capacity");

                BucketChartModel rtt = HarborChartMapper.RoundTrip(metrics);
                AssertEqual(ChartValueFormatEnum.DurationMs, rtt.Format);
                AssertEqual(null, rtt.Series[0].At(2), "no heartbeat round trip in bucket 2");
                AssertTrue(rtt.Series[1].Dashed, "largest is dashed");
                AssertEqual(2, ChartGeometry.LineRuns(rtt.Series[0].Values, 480, 100, rtt.AxisMax()).Count, "the gap splits the line");

                // A short response (fewer buckets than BucketCount) is padded so series line up by index.
                metrics.Slots.Buckets.RemoveRange(10, metrics.Slots.Buckets.Count - 10);
                AssertEqual(48, HarborChartMapper.Slots(metrics).Series[0].Values.Count, "padded");
            }));

            cases.Add(Case("tokens_mapping", "Tokens: uncached input, cached input, and output stacked per bucket; bars add up to input plus output", () =>
            {
                HarborMetrics metrics = HarborMetricsFixture.Build();
                BucketChartModel tokens = HarborChartMapper.Tokens(metrics);
                AssertEqual("Tokens by type", tokens.Title);
                AssertEqual(BucketChartKindEnum.StackedBar, tokens.Kind);
                AssertEqual(ChartValueFormatEnum.Tokens, tokens.Format);
                AssertEqual("uncachedInput,cachedInput,output", String.Join(",", tokens.Series.Select(s => s.Key)));
                AssertEqual("Uncached input,Cached input,Output", String.Join(",", tokens.Series.Select(s => s.Name)));
                AssertEqual("Series1,Series2,Series3", String.Join(",", tokens.Series.Select(s => s.Color.ToString())));
                AssertEqual(600.0, tokens.Series[0].At(1), "input 1,300 minus 700 cached");
                AssertEqual(700.0, tokens.Series[1].At(1), "cached");
                AssertEqual(700.0, tokens.Series[2].At(1), "output");
                AssertEqual(2000.0, tokens.StackTotal(1), "the bar is the real total, cached counted once");
                AssertEqual((double)metrics.Tokens.Buckets[1].TotalTokens, tokens.StackTotal(1), "matches the bucket's TotalTokens");
                AssertEqual(2000.0, tokens.DataMax());
                AssertEqual(0.0, tokens.Series[0].At(0), "no tokens in bucket 0");
                AssertEqual(48, tokens.Series[2].Values.Count, "one value per bucket");

                string[] tip = tokens.Tooltip(1, TimeZoneInfo.Utc).Split('\n');
                AssertEqual("Uncached input: 600", tip[1]);
                AssertEqual("Cached input: 700", tip[2]);
                AssertEqual("Output: 700", tip[3]);
                AssertEqual("Total: 2K", tip[4]);
                AssertEqual("Tokens by type, last 24 hours: Uncached input 600, Cached input 700, Output 700", tokens.Summary());

                List<ChartBarSegment> bars = ChartGeometry.StackedBars(tokens, 480, 100, tokens.AxisMax());
                AssertEqual("0,1,2", String.Join(",", bars.Where(b => b.BucketIndex == 1).Select(b => b.SeriesIndex)), "uncached at the bottom, then cached, then output");

                // Runtime and model stay available as totals, most tokens first.
                List<HarborTokenSeries> rows = HarborChartMapper.TokenRows(metrics);
                AssertEqual("ClaudeCode/claude-opus,Codex/gpt-5", String.Join(",", rows.Select(r => r.Runtime + "/" + r.Model)));
            }));

            cases.Add(Case("token_type_split", "Token split: cached is part of input, never counted twice, never more than input, never negative", () =>
            {
                TokenTypeSplit split = TokenTypeSplit.From(1000, 500, 600);
                AssertEqual(400L, split.UncachedInput, "input minus cached");
                AssertEqual(600L, split.CachedInput);
                AssertEqual(500L, split.Output);
                AssertEqual(1500L, split.Total, "input plus output, the recorded total");

                TokenTypeSplit over = TokenTypeSplit.From(100, 50, 300);
                AssertEqual(0L, over.UncachedInput, "cached above input is capped");
                AssertEqual(100L, over.CachedInput);
                AssertEqual(150L, over.Total, "still input plus output");

                TokenTypeSplit negative = TokenTypeSplit.From(-5, -1, -9);
                AssertEqual(0L, negative.Total, "negatives count as zero");

                // A bucket where the cache reads exceed the input does not grow the bar past input plus output.
                HarborMetrics metrics = HarborMetricsFixture.Build();
                metrics.Tokens.Buckets[2].InputTokens = 100;
                metrics.Tokens.Buckets[2].OutputTokens = 40;
                metrics.Tokens.Buckets[2].CachedTokens = 250;
                metrics.Tokens.Buckets[2].TotalTokens = 140;
                BucketChartModel tokens = HarborChartMapper.Tokens(metrics);
                AssertEqual(140.0, tokens.StackTotal(2));
                AssertEqual(100.0, tokens.Series[1].At(2), "cached capped at input");
            }));

            cases.Add(Case("link_strip", "Link strip: segments clipped to the window, worst state per column, tooltips and summary", () =>
            {
                HarborMetrics metrics = HarborMetricsFixture.Build();
                StatusStripModel strip = HarborChartMapper.Link(metrics);
                AssertEqual(6, strip.Parts.Count);
                AssertEqual(0.0, strip.Parts[0].Start, "starts at the window start");
                double end = strip.Parts.Last().Start + strip.Parts.Last().Width;
                AssertTrue(end < 1.0, "the window extends past now, which has no state yet");
                AssertEqual(HarborLinkSegmentStateEnum.Down, strip.PartAt(0.55 * (end))!.State, "middle of the down stretch");
                AssertNull(strip.PartAt(0.999), "after now");

                List<HarborLinkSegmentStateEnum> columns = strip.StatesForColumns(20);
                AssertEqual(20, columns.Count);
                AssertEqual(HarborLinkSegmentStateEnum.Unknown, columns[0], "unknown at first");
                AssertTrue(columns.Contains(HarborLinkSegmentStateEnum.Down), "the down stretch shows");
                AssertTrue(columns.Contains(HarborLinkSegmentStateEnum.Reconnecting), "a short reconnect still shows (worst state wins)");
                AssertEqual(HarborLinkSegmentStateEnum.Connected, columns[19], "the last column holds the connected minutes up to now");
                AssertEqual(HarborLinkSegmentStateEnum.Unknown, strip.StatesForColumns(200)[199], "a column wholly after now has no state");

                StatusStripModel clipped = StatusStripModel.FromSegments(new List<HarborLinkSegment>
                {
                    new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Down, StartUtc = Utc(-60), EndUtc = Utc(30) },
                    new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Connected, StartUtc = Utc(30), EndUtc = Utc(120) }
                }, Utc(0), Utc(60));
                AssertEqual(2, clipped.Parts.Count);
                AssertEqual(Utc(0), clipped.Parts[0].StartUtc, "clipped at the start");
                AssertEqual(0.5, clipped.Parts[0].Width);
                AssertEqual(Utc(60), clipped.Parts[1].EndUtc, "clipped at the end");
                AssertEqual("Down\nThu Oct 8, 00:00 - 00:30\n30m 0s", StatusStripModel.Tooltip(clipped.Parts[0], TimeZoneInfo.Utc));
                clipped.Disconnects = 1;
                clipped.ConnectedPercent = 50;
                AssertEqual("Link health: connected 50% of the time, 1 disconnect; Connected 30m 0s, Down 30m 0s", clipped.Summary());
                AssertEqual("Link health: no link data", new StatusStripModel().Summary());

                foreach (HarborLinkSegmentStateEnum state in StatusStripModel.LegendStates) AssertTrue(StatusStripModel.StateLabel(state).Length > 0, "label for " + state);
                AssertEqual(4, StatusStripModel.LegendStates.Select(StatusStripModel.StateColor).Distinct().Count(), "a color per state");
            }));

            cases.Add(Case("launch_trend_and_empty", "Launch trend sparkline summary; an empty response has no data", () =>
            {
                HarborMetrics metrics = HarborMetricsFixture.Build();
                SparklineModel trend = HarborChartMapper.LaunchTrend(metrics.LaunchSpeed[0]);
                AssertEqual("First output trend for ClaudeCode: latest 3.9s, lowest 3.9s, highest 4.2s", trend.Summary());
                SparklineModel slots = HarborChartMapper.SlotTrend(metrics);
                AssertEqual(48, slots.Values.Count);
                AssertEqual(3.0, slots.Values[1]);
                AssertTrue(HarborChartMapper.HasAnyData(metrics), "fixture has data");

                HarborMetrics empty = new HarborMetrics { BucketCount = 3, BucketMinutes = 1, Range = "1h", FromUtc = Utc(0), ToUtc = Utc(3) };
                AssertFalse(HarborChartMapper.HasAnyData(empty), "nothing");
                BucketChartModel jobs = HarborChartMapper.Jobs(empty);
                AssertEqual(3, jobs.Series[0].Values.Count, "padded to the bucket count");
                AssertFalse(jobs.HasData);
                AssertEqual("Jobs over time, last hour: no data", jobs.Summary());
            }));

            cases.Add(Case("formats", "Durations, tokens, decimals, ranges, and bucket spans read like the dashboard", () =>
            {
                AssertEqual("850ms", ChartFormat.Duration(850));
                AssertEqual("4.2s", ChartFormat.Duration(4200));
                AssertEqual("3m 12s", ChartFormat.Duration(192000));
                AssertEqual("1h 5m", ChartFormat.Duration(3900000));
                AssertEqual("-", ChartFormat.Duration(null));
                AssertEqual("950", ChartFormat.Tokens(950));
                AssertEqual("1.5K", ChartFormat.Tokens(1500));
                AssertEqual("2.3M", ChartFormat.Tokens(2300000));
                AssertEqual("2.5", ChartFormat.Value(2.5, ChartValueFormatEnum.Decimal));
                AssertEqual("3", ChartFormat.Value(2.6, ChartValueFormatEnum.Count));
                AssertEqual("-", ChartFormat.Value(null, ChartValueFormatEnum.Count));
                AssertEqual("last hour", ChartFormat.RangeName("1h"));
                AssertEqual("last 7 days", ChartFormat.RangeName("7d"));
                AssertEqual("last 24 hours", ChartFormat.RangeName("bogus"));
                AssertEqual("Wed 21:00", ChartFormat.BucketLabel(Utc(-180), 180, TimeZoneInfo.Utc), "weekday for 3-hour buckets");
                AssertEqual("Wed Oct 7, 23:30 - Thu Oct 8, 00:00", ChartFormat.BucketSpan(Utc(-30), 30, TimeZoneInfo.Utc), "a span across midnight names both days");
            }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Charts", cases);
        }

        #endregion

        #region Private-Methods

        private static string Join(IEnumerable<double> values)
        {
            return String.Join(",", values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        private static DateTime Utc(int minutes)
        {
            return new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
