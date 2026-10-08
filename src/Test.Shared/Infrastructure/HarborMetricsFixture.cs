namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Metrics;
    using Armada.Core.Models;

    /// <summary>
    /// A deterministic Harbor metrics response for chart, feed, and TUI tests: every series has known values (jobs in
    /// buckets 1 and 2, a peak of 3 slots of 4, a round-trip gap in bucket 2, a down stretch and a reconnect, two runtimes,
    /// and two token series with cache reads inside their input).
    /// </summary>
    public static class HarborMetricsFixture
    {
        #region Public-Members

        /// <summary>
        /// The fixed time the fixture is built around (UTC).
        /// </summary>
        public static readonly DateTime NowUtc = new DateTime(2026, 10, 8, 12, 10, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build metrics for a range.
        /// </summary>
        /// <param name="harborId">Harbor ID.</param>
        /// <param name="range">1h, 24h, or 7d.</param>
        /// <returns>Metrics.</returns>
        public static HarborMetrics Build(string harborId = "hbr_1", string range = "24h")
        {
            HarborMetricsRanges.TryParse(range, out HarborMetricsRangeEnum parsed);
            TimeBucketLayout layout = HarborMetricsRanges.Layout(parsed, NowUtc);
            HarborMetrics m = new HarborMetrics();
            m.HarborId = harborId;
            m.HarborName = "build-box";
            m.Range = HarborMetricsRanges.ToWireName(parsed);
            m.FromUtc = layout.FromUtc;
            m.ToUtc = layout.ToUtc;
            m.BucketMinutes = (int)layout.BucketSize.TotalMinutes;
            m.BucketCount = layout.BucketCount;
            m.GeneratedUtc = NowUtc;
            m.ConnectionStatus = HarborConnectionStatusEnum.Connected;
            m.Slots.MaxConcurrentJobs = 4;
            for (int i = 0; i < m.BucketCount; i++)
            {
                DateTime start = layout.FromUtc.AddMinutes((double)i * m.BucketMinutes);
                HarborJobBucket jobs = new HarborJobBucket { BucketStartUtc = start };
                if (i == 1)
                {
                    jobs.MissionsFinished = 2;
                    jobs.MissionsFailed = 1;
                    jobs.InteractiveFinished = 3;
                }

                if (i == 2)
                {
                    jobs.MissionsFinished = 1;
                    jobs.InteractiveFailed = 1;
                }

                m.Jobs.Buckets.Add(jobs);
                int peak = i == 1 ? 3 : i == 2 ? 2 : 0;
                m.Slots.Buckets.Add(new ConcurrencyBucket { BucketStartUtc = start, Peak = peak, Average = peak == 0 ? 0 : peak - 0.5 });
                HarborRoundTripBucket rt = new HarborRoundTripBucket { BucketStartUtc = start };
                if (i != 2)
                {
                    rt.HeartbeatCount = 2;
                    rt.SampleCount = 2;
                    rt.AverageMs = 20 + i % 5;
                    rt.MaxMs = 40 + i % 5;
                }

                m.Link.RoundTrip.Add(rt);
                HarborTokenBucket tokens = new HarborTokenBucket { BucketStartUtc = start };
                if (i == 1)
                {
                    tokens.Series.Add(new HarborTokenSeries { Runtime = "ClaudeCode", Model = "claude-opus", InputTokens = 1000, OutputTokens = 500, CachedTokens = 600, TotalTokens = 1500 });
                    tokens.Series.Add(new HarborTokenSeries { Runtime = "Codex", Model = "gpt-5", InputTokens = 300, OutputTokens = 200, CachedTokens = 100, TotalTokens = 500 });
                    tokens.InputTokens = 1300;
                    tokens.OutputTokens = 700;
                    tokens.CachedTokens = 700;
                    tokens.TotalTokens = 2000;
                }

                m.Tokens.Buckets.Add(tokens);
            }

            m.Jobs.MissionsFinished = 3;
            m.Jobs.MissionsFailed = 1;
            m.Jobs.InteractiveFinished = 3;
            m.Jobs.InteractiveFailed = 1;
            m.Jobs.Running = 1;
            m.Slots.Peak = 3;
            m.Slots.Average = 0.25;

            // Unknown for the first tenth, connected, down for a tenth in the middle, connected, a short reconnect, connected.
            TimeSpan span = NowUtc - layout.FromUtc;
            DateTime a = layout.FromUtc + TimeSpan.FromTicks(span.Ticks / 10);
            DateTime b = layout.FromUtc + TimeSpan.FromTicks(span.Ticks / 2);
            DateTime c = layout.FromUtc + TimeSpan.FromTicks(span.Ticks * 6 / 10);
            DateTime d = layout.FromUtc + TimeSpan.FromTicks(span.Ticks * 9 / 10);
            DateTime e = d + TimeSpan.FromTicks(span.Ticks / 100);
            m.Link.Segments.Add(new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Unknown, StartUtc = layout.FromUtc, EndUtc = a });
            m.Link.Segments.Add(new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Connected, StartUtc = a, EndUtc = b });
            m.Link.Segments.Add(new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Down, StartUtc = b, EndUtc = c });
            m.Link.Segments.Add(new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Connected, StartUtc = c, EndUtc = d });
            m.Link.Segments.Add(new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Reconnecting, StartUtc = d, EndUtc = e });
            m.Link.Segments.Add(new HarborLinkSegment { State = HarborLinkSegmentStateEnum.Connected, StartUtc = e, EndUtc = NowUtc });
            m.Link.ConnectedPercent = 87.8;
            m.Link.Disconnects = 2;
            m.Link.ReconnectCount = 2;
            m.Link.LastReconnectUtc = e;
            m.Link.RoundTripMedianMs = 22;

            HarborLaunchSpeed claude = new HarborLaunchSpeed { Runtime = "ClaudeCode", JobCount = 4, FirstOutputCount = 4, FirstOutputMedianMs = 4200, FirstOutputP95Ms = 9800, DurationCount = 4, DurationMedianMs = 312000, DurationP95Ms = 1180000 };
            HarborLaunchSpeed codex = new HarborLaunchSpeed { Runtime = "Codex", JobCount = 2, FirstOutputCount = 1, FirstOutputMedianMs = 850, FirstOutputP95Ms = 850, DurationCount = 2, DurationMedianMs = 61000, DurationP95Ms = 64000 };
            for (int i = 0; i < m.BucketCount; i++)
            {
                claude.FirstOutputMedianMsByBucket.Add(i == 1 ? 4200 : i == 2 ? 3900 : (long?)null);
                codex.FirstOutputMedianMsByBucket.Add(i == 1 ? 850 : (long?)null);
            }

            m.LaunchSpeed.Add(claude);
            m.LaunchSpeed.Add(codex);
            m.Tokens.Series = new List<HarborTokenSeries>
            {
                new HarborTokenSeries { Runtime = "ClaudeCode", Model = "claude-opus", InputTokens = 1000, OutputTokens = 500, CachedTokens = 600, TotalTokens = 1500 },
                new HarborTokenSeries { Runtime = "Codex", Model = "gpt-5", InputTokens = 300, OutputTokens = 200, CachedTokens = 100, TotalTokens = 500 }
            };
            m.Tokens.InputTokens = 1300;
            m.Tokens.OutputTokens = 700;
            m.Tokens.CachedTokens = 700;
            m.Tokens.TotalTokens = 2000;
            m.Tokens.RecordCount = 4;
            m.Tokens.EstimatedCount = 1;
            return m;
        }

        /// <summary>
        /// The metrics as the Admiral's JSON.
        /// </summary>
        /// <param name="harborId">Harbor ID.</param>
        /// <param name="range">1h, 24h, or 7d.</param>
        /// <returns>JSON.</returns>
        public static string Json(string harborId = "hbr_1", string range = "24h")
        {
            return JsonHelper.Serialize(Build(harborId, range));
        }

        #endregion
    }
}
