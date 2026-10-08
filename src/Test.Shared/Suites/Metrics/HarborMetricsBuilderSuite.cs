namespace Test.Shared.Suites.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Metrics;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Every Harbor metrics series built from seeded records (HarborMetricsBuilder): jobs over time split into missions
    /// and other launches, slot usage, launch speed per runtime, the link timeline with the reconnect grace, round-trip
    /// buckets, and token usage by runtime and model.
    /// </summary>
    public sealed class HarborMetricsBuilderSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Metrics.HarborMetricsBuilder";
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

            cases.Add(Case("window_shape", "The response carries the window, buckets, Harbor, and every series with one entry per bucket", TestTags.Positive, () =>
            {
                HarborMetrics metrics = HarborMetricsBuilder.Build(SeededInput());
                AssertEqual("hbr_metrics", metrics.HarborId);
                AssertEqual("1h", metrics.Range);
                AssertEqual(1, metrics.BucketMinutes);
                AssertEqual(60, metrics.BucketCount);
                AssertEqual(At(13, 38), metrics.FromUtc, "from");
                AssertEqual(At(14, 38), metrics.ToUtc, "to");
                AssertEqual(60, metrics.Jobs.Buckets.Count, "jobs buckets");
                AssertEqual(60, metrics.Slots.Buckets.Count, "slot buckets");
                AssertEqual(60, metrics.Link.RoundTrip.Count, "round-trip buckets");
                AssertEqual(60, metrics.Tokens.Buckets.Count, "token buckets");
                AssertEqual(At(13, 38), metrics.Jobs.Buckets[0].BucketStartUtc, "first bucket start");
            }));

            cases.Add(Case("jobs_over_time", "Jobs count in the bucket they ended in; stopped counts as finished and lost as failed", TestTags.Positive, () =>
            {
                HarborJobMetrics jobs = HarborMetricsBuilder.Build(SeededInput()).Jobs;
                HarborJobBucket ten = jobs.Buckets[Index(14, 10)];
                AssertEqual(1, ten.MissionsFinished, "mission finished at 14:10");
                AssertEqual(1, ten.MissionsFailed, "mission failed at 14:10");
                AssertEqual(1, jobs.Buckets[Index(14, 21)].InteractiveFinished, "stopped Ask turn counts as finished");
                AssertEqual(1, jobs.Buckets[Index(14, 30)].InteractiveFailed, "lost planning turn counts as failed");
                AssertEqual(1, jobs.MissionsFinished);
                AssertEqual(1, jobs.MissionsFailed);
                AssertEqual(1, jobs.InteractiveFinished);
                AssertEqual(1, jobs.InteractiveFailed);
                AssertEqual(1, jobs.Running, "the open mission");
            }));

            cases.Add(Case("slot_usage", "Slot usage peaks where jobs overlap and reports the Harbor's capacity", TestTags.Positive, () =>
            {
                HarborSlotMetrics slots = HarborMetricsBuilder.Build(SeededInput()).Slots;
                AssertEqual(3, slots.MaxConcurrentJobs, "capacity");
                AssertEqual(2, slots.Peak, "two missions overlapped");
                AssertEqual(2, slots.Buckets[Index(14, 7)].Peak, "overlap at 14:07");
                AssertEqual(2.0, slots.Buckets[Index(14, 7)].Average, "both running all minute");
                AssertEqual(0, slots.Buckets[Index(13, 40)].Peak, "idle before the jobs");
                AssertEqual(1, slots.Buckets[Index(14, 37)].Peak, "the open mission runs now");
                AssertTrue(slots.Average > 0, "a window average");
            }));

            cases.Add(Case("launch_speed", "Launch speed has median and p95 per runtime over the jobs that ended, with a per-bucket sparkline", TestTags.Positive, () =>
            {
                List<HarborLaunchSpeed> speed = HarborMetricsBuilder.Build(SeededInput()).LaunchSpeed;
                AssertEqual(2, speed.Count, "two runtimes");
                HarborLaunchSpeed claude = speed[0];
                AssertEqual("ClaudeCode", claude.Runtime, "ties order by name");
                AssertEqual(2, claude.JobCount, "the running job does not count");
                AssertEqual((long?)4000, claude.FirstOutputMedianMs);
                AssertEqual((long?)4900, claude.FirstOutputP95Ms);
                AssertEqual((long?)484500, claude.DurationMedianMs);
                AssertEqual(60, claude.FirstOutputMedianMsByBucket.Count, "one sparkline point per bucket");
                AssertEqual((long?)4000, claude.FirstOutputMedianMsByBucket[Index(14, 10)], "sparkline point where they ended");
                AssertNull(claude.FirstOutputMedianMsByBucket[Index(14, 11)], "no point where none ended");

                HarborLaunchSpeed codex = speed[1];
                AssertEqual(2, codex.JobCount);
                AssertEqual(1, codex.FirstOutputCount, "the lost job reported no first output");
                AssertEqual(1, codex.DurationCount, "the lost job reported no duration");
                AssertEqual((long?)70000, codex.DurationP95Ms);
            }));

            cases.Add(Case("link_timeline", "The link timeline joins events into connected, reconnecting, and down stretches", TestTags.Positive, () =>
            {
                HarborLinkMetrics link = HarborMetricsBuilder.Build(SeededInput()).Link;
                List<HarborLinkSegment> segments = link.Segments;
                AssertEqual(6, segments.Count, "segments");
                AssertSegment(segments[0], HarborLinkSegmentStateEnum.Connected, At(13, 38), At(14, 0));
                AssertSegment(segments[1], HarborLinkSegmentStateEnum.Reconnecting, At(14, 0), At(14, 0, 30));
                AssertSegment(segments[2], HarborLinkSegmentStateEnum.Connected, At(14, 0, 30), At(14, 10));
                AssertSegment(segments[3], HarborLinkSegmentStateEnum.Reconnecting, At(14, 10), At(14, 10, 45));
                AssertSegment(segments[4], HarborLinkSegmentStateEnum.Down, At(14, 10, 45), At(14, 12));
                AssertSegment(segments[5], HarborLinkSegmentStateEnum.Connected, At(14, 12), _Now);
                AssertEqual(2, link.Disconnects, "two link closes");
                AssertEqual((double?)95.8, link.ConnectedPercent, "3410 of 3560 seconds");
                AssertEqual((int?)2, link.ReconnectCount, "from the latest sample");
                AssertEqual((DateTime?)At(14, 12), link.LastReconnectUtc);
            }));

            cases.Add(Case("link_grace_and_unknown", "A close older than the grace reads as down; no history reads as unknown", TestTags.Negative, () =>
            {
                HarborLinkEvent before = new HarborLinkEvent { HarborId = "h", EventType = HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = At(13, 30) };
                List<HarborLinkEvent> events = new List<HarborLinkEvent>
                {
                    new HarborLinkEvent { HarborId = "h", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = At(14, 0) },
                    new HarborLinkEvent { HarborId = "h", EventType = HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = At(14, 30) }
                };
                List<HarborLinkSegment> segments = HarborMetricsBuilder.BuildTimeline(At(13, 38), _Now, before, events, TimeSpan.FromSeconds(45));
                AssertEqual(4, segments.Count);
                AssertSegment(segments[0], HarborLinkSegmentStateEnum.Down, At(13, 38), At(14, 0));
                AssertSegment(segments[1], HarborLinkSegmentStateEnum.Connected, At(14, 0), At(14, 30));
                AssertSegment(segments[2], HarborLinkSegmentStateEnum.Reconnecting, At(14, 30), At(14, 30, 45));
                AssertSegment(segments[3], HarborLinkSegmentStateEnum.Down, At(14, 30, 45), _Now);

                List<HarborLinkSegment> none = HarborMetricsBuilder.BuildTimeline(At(13, 38), _Now, null, new List<HarborLinkEvent>(), TimeSpan.FromSeconds(45));
                AssertEqual(1, none.Count);
                AssertSegment(none[0], HarborLinkSegmentStateEnum.Unknown, At(13, 38), _Now);

                HarborMetricsInput empty = new HarborMetricsInput { Harbor = new Harbor { Id = "hbr_empty", Name = "Empty" }, Range = HarborMetricsRangeEnum.OneHour, NowUtc = _Now };
                HarborMetrics metrics = HarborMetricsBuilder.Build(empty);
                AssertNull(metrics.Link.ConnectedPercent, "no known state");
                AssertNull(metrics.Link.RoundTripMedianMs, "no round trips");
                AssertEqual(0, metrics.LaunchSpeed.Count, "no runtimes");
                AssertEqual(0L, metrics.Tokens.TotalTokens, "no tokens");
            }));

            cases.Add(Case("round_trip_buckets", "Round-trip buckets average the reported times and keep the largest", TestTags.Positive, () =>
            {
                HarborLinkMetrics link = HarborMetricsBuilder.Build(SeededInput()).Link;
                HarborRoundTripBucket first = link.RoundTrip[Index(14, 0)];
                AssertEqual(4, first.HeartbeatCount);
                AssertEqual(3, first.SampleCount);
                AssertEqual((double?)100.0, first.AverageMs);
                AssertEqual((long?)150, first.MaxMs);
                AssertEqual((double?)25.0, link.RoundTrip[Index(14, 1)].AverageMs);
                AssertNull(link.RoundTrip[Index(14, 2)].AverageMs, "no samples");
                AssertEqual((long?)63, link.RoundTripMedianMs, "median of 100 and 25");
            }));

            cases.Add(Case("token_usage", "Token usage aggregates per bucket and per runtime and model, most tokens first", TestTags.Positive, () =>
            {
                HarborTokenMetrics tokens = HarborMetricsBuilder.Build(SeededInput()).Tokens;
                AssertEqual(4, tokens.RecordCount);
                AssertEqual(1, tokens.EstimatedCount);
                AssertEqual(1395L, tokens.TotalTokens);
                AssertEqual(3, tokens.Series.Count);
                AssertEqual("Codex", tokens.Series[0].Runtime);
                AssertEqual("gpt-5", tokens.Series[0].Model);
                AssertEqual(1200L, tokens.Series[0].TotalTokens);
                AssertEqual("ClaudeCode", tokens.Series[1].Runtime);
                AssertEqual(165L, tokens.Series[1].TotalTokens, "same runtime and model merge");
                AssertEqual("unknown", tokens.Series[2].Runtime, "missing runtime");
                AssertEqual("unknown", tokens.Series[2].Model, "missing model");

                HarborTokenBucket two = tokens.Buckets[Index(14, 2)];
                AssertEqual(165L, two.TotalTokens);
                AssertEqual(110L, two.InputTokens);
                AssertEqual(1, two.Series.Count);
                AssertEqual(2, tokens.Buckets[Index(14, 20)].Series.Count);
            }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Metrics Builder", cases);
        }

        #endregion

        #region Private-Methods

        private static DateTime At(int hour, int minute, int second = 0)
        {
            return new DateTime(2026, 10, 8, hour, minute, second, DateTimeKind.Utc);
        }

        private static int Index(int hour, int minute)
        {
            return (int)(At(hour, minute) - At(13, 38)).TotalMinutes;
        }

        private static void AssertSegment(HarborLinkSegment segment, HarborLinkSegmentStateEnum state, DateTime start, DateTime end)
        {
            AssertEqual(state, segment.State, "segment state at " + start.ToString("HH:mm:ss"));
            AssertEqual(start, segment.StartUtc, "segment start");
            AssertEqual(end, segment.EndUtc, "segment end");
        }

        private static HarborJobRecord Job(HarborJobKindEnum kind, string runtime, DateTime launched, DateTime? started, DateTime? ended, HarborJobOutcomeEnum outcome, long? ttft, long? duration)
        {
            return new HarborJobRecord
            {
                JobId = Guid.NewGuid().ToString("N"),
                HarborId = "hbr_metrics",
                Kind = kind,
                Runtime = runtime,
                LaunchedUtc = launched,
                StartedUtc = started,
                EndedUtc = ended,
                Outcome = outcome,
                TimeToFirstOutputMs = ttft,
                DurationMs = duration
            };
        }

        private static HarborMetricsInput SeededInput()
        {
            HarborMetricsInput input = new HarborMetricsInput
            {
                Harbor = new Harbor { Id = "hbr_metrics", Name = "Rig", MaxConcurrentJobs = 3, ConnectionStatus = HarborConnectionStatusEnum.Connected },
                Range = HarborMetricsRangeEnum.OneHour,
                NowUtc = _Now,
                ReconnectGrace = TimeSpan.FromSeconds(45)
            };

            input.Jobs.Add(Job(HarborJobKindEnum.Mission, "ClaudeCode", At(14, 0), At(14, 0, 5), At(14, 10, 30), HarborJobOutcomeEnum.Succeeded, 3000, 630000));
            input.Jobs.Add(Job(HarborJobKindEnum.Mission, "ClaudeCode", At(14, 5), At(14, 5, 1), At(14, 10, 40), HarborJobOutcomeEnum.Failed, 5000, 339000));
            input.Jobs.Add(Job(HarborJobKindEnum.AskTurn, "Codex", At(14, 20), At(14, 20, 1), At(14, 21, 10), HarborJobOutcomeEnum.Stopped, 1000, 70000));
            input.Jobs.Add(Job(HarborJobKindEnum.Planning, "Codex", At(14, 30), null, At(14, 30, 2), HarborJobOutcomeEnum.Lost, null, null));
            input.Jobs.Add(Job(HarborJobKindEnum.Mission, "ClaudeCode", At(14, 35), At(14, 35, 2), null, HarborJobOutcomeEnum.Running, null, null));

            input.EventBeforeWindow = new HarborLinkEvent { HarborId = "hbr_metrics", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = At(13, 0) };
            input.Events.Add(new HarborLinkEvent { HarborId = "hbr_metrics", EventType = HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = At(14, 0) });
            input.Events.Add(new HarborLinkEvent { HarborId = "hbr_metrics", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = At(14, 0, 30) });
            input.Events.Add(new HarborLinkEvent { HarborId = "hbr_metrics", EventType = HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = At(14, 10) });
            input.Events.Add(new HarborLinkEvent { HarborId = "hbr_metrics", EventType = HarborLinkEventTypeEnum.Disconnected, OccurredUtc = At(14, 10, 45) });
            input.Events.Add(new HarborLinkEvent { HarborId = "hbr_metrics", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = At(14, 12) });

            input.Samples.Add(new HarborLinkSample { HarborId = "hbr_metrics", BucketStartUtc = At(14, 0), HeartbeatCount = 4, RoundTripCount = 3, RoundTripTotalMs = 300, RoundTripMaxMs = 150 });
            input.Samples.Add(new HarborLinkSample { HarborId = "hbr_metrics", BucketStartUtc = At(14, 1), HeartbeatCount = 2, RoundTripCount = 2, RoundTripTotalMs = 50, RoundTripMaxMs = 30 });
            input.Samples.Add(new HarborLinkSample { HarborId = "hbr_metrics", BucketStartUtc = At(13, 0), HeartbeatCount = 4, RoundTripCount = 4, RoundTripTotalMs = 4000, RoundTripMaxMs = 2000 });
            input.LatestSample = new HarborLinkSample { HarborId = "hbr_metrics", BucketStartUtc = At(14, 36), ReconnectCount = 2, LastReconnectUtc = At(14, 12) };

            input.TokenRecords.Add(new TokenUsageRecord { Runtime = "ClaudeCode", Model = "sonnet", InputTokens = 100, OutputTokens = 50, TotalTokens = 150, CreatedUtc = At(14, 2, 10) });
            input.TokenRecords.Add(new TokenUsageRecord { Runtime = "ClaudeCode", Model = "sonnet", InputTokens = 10, OutputTokens = 5, TotalTokens = 15, Estimated = true, CreatedUtc = At(14, 2, 40) });
            input.TokenRecords.Add(new TokenUsageRecord { Runtime = "Codex", Model = "gpt-5", InputTokens = 1000, OutputTokens = 200, TotalTokens = 1200, CreatedUtc = At(14, 20, 5) });
            input.TokenRecords.Add(new TokenUsageRecord { Runtime = null, Model = "", InputTokens = 20, OutputTokens = 10, TotalTokens = 30, CreatedUtc = At(14, 20, 6) });
            return input;
        }

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
