namespace Armada.Core.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>
    /// Computes the Harbor metrics series from recorded data. Pure: no database or clock access, so every series can be
    /// tested against seeded records.
    /// </summary>
    public static class HarborMetricsBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build the metrics for a window.
        /// </summary>
        /// <param name="input">Recorded data and the window.</param>
        /// <returns>The metrics.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the input or its Harbor is null.</exception>
        public static HarborMetrics Build(HarborMetricsInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Harbor == null) throw new ArgumentNullException(nameof(input) + ".Harbor");

            TimeBucketLayout layout = HarborMetricsRanges.Layout(input.Range, input.NowUtc);
            HarborMetrics metrics = new HarborMetrics
            {
                HarborId = input.Harbor.Id,
                HarborName = input.Harbor.Name,
                Range = HarborMetricsRanges.ToWireName(input.Range),
                FromUtc = layout.FromUtc,
                ToUtc = layout.ToUtc,
                BucketMinutes = (int)layout.BucketSize.TotalMinutes,
                BucketCount = layout.BucketCount,
                GeneratedUtc = input.NowUtc,
                ConnectionStatus = input.Harbor.ConnectionStatus
            };

            List<HarborJobRecord> jobs = input.Jobs ?? new List<HarborJobRecord>();
            metrics.Jobs = BuildJobs(layout, jobs);
            metrics.Slots = BuildSlots(layout, jobs, input.Harbor.MaxConcurrentJobs, input.NowUtc);
            metrics.LaunchSpeed = BuildLaunchSpeed(layout, jobs);
            metrics.Link = BuildLink(layout, input);
            metrics.Tokens = BuildTokens(layout, input.TokenRecords ?? new List<TokenUsageRecord>());
            return metrics;
        }

        /// <summary>
        /// Whether a job kind counts as a mission in the jobs series (everything else counts as interactive).
        /// </summary>
        /// <param name="kind">The job kind.</param>
        /// <returns>True for a mission.</returns>
        public static bool IsMission(HarborJobKindEnum kind)
        {
            return kind == HarborJobKindEnum.Mission;
        }

        /// <summary>
        /// Whether an outcome counts as finished in the jobs series (Succeeded and Stopped). Failed and Lost count as
        /// failed; Running counts as neither.
        /// </summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>True when finished.</returns>
        public static bool IsFinished(HarborJobOutcomeEnum outcome)
        {
            return outcome == HarborJobOutcomeEnum.Succeeded || outcome == HarborJobOutcomeEnum.Stopped;
        }

        /// <summary>
        /// Whether an outcome counts as failed in the jobs series (Failed and Lost).
        /// </summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>True when failed.</returns>
        public static bool IsFailed(HarborJobOutcomeEnum outcome)
        {
            return outcome == HarborJobOutcomeEnum.Failed || outcome == HarborJobOutcomeEnum.Lost;
        }

        /// <summary>
        /// Build the link-state timeline of a window from its events. A stretch after a close counts as reconnecting for
        /// <paramref name="reconnectGrace"/> and as down after that, until the next event.
        /// </summary>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="endUtc">Window end (now, or the end of the window when that is earlier).</param>
        /// <param name="before">The latest event before the window, or null.</param>
        /// <param name="events">The events in the window, in any order.</param>
        /// <param name="reconnectGrace">How long a closed link counts as reconnecting.</param>
        /// <returns>Contiguous segments covering [fromUtc, endUtc), oldest first.</returns>
        public static List<HarborLinkSegment> BuildTimeline(DateTime fromUtc, DateTime endUtc, HarborLinkEvent? before, List<HarborLinkEvent>? events, TimeSpan reconnectGrace)
        {
            List<HarborLinkSegment> segments = new List<HarborLinkSegment>();
            if (endUtc <= fromUtc) return segments;

            HarborLinkSegmentStateEnum state = HarborLinkSegmentStateEnum.Unknown;
            DateTime reconnectingSince = fromUtc;
            if (before != null)
            {
                state = ToSegmentState(before.EventType);
                reconnectingSince = before.OccurredUtc;
            }

            DateTime cursor = fromUtc;
            List<HarborLinkEvent> ordered = (events ?? new List<HarborLinkEvent>())
                .Where(e => e != null && e.OccurredUtc >= fromUtc && e.OccurredUtc < endUtc)
                .OrderBy(e => e.OccurredUtc)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

            foreach (HarborLinkEvent linkEvent in ordered)
            {
                Emit(segments, state, reconnectingSince, cursor, linkEvent.OccurredUtc, reconnectGrace);
                cursor = linkEvent.OccurredUtc;
                state = ToSegmentState(linkEvent.EventType);
                reconnectingSince = linkEvent.OccurredUtc;
            }

            Emit(segments, state, reconnectingSince, cursor, endUtc, reconnectGrace);
            return segments;
        }

        #endregion

        #region Private-Methods

        private static HarborJobMetrics BuildJobs(TimeBucketLayout layout, List<HarborJobRecord> jobs)
        {
            HarborJobMetrics result = new HarborJobMetrics();
            for (int i = 0; i < layout.BucketCount; i++)
                result.Buckets.Add(new HarborJobBucket { BucketStartUtc = layout.StartOf(i) });

            foreach (HarborJobRecord job in jobs)
            {
                if (job == null) continue;
                if (!job.EndedUtc.HasValue)
                {
                    result.Running++;
                    continue;
                }

                int index = layout.IndexOf(job.EndedUtc.Value);
                if (index < 0) continue;
                HarborJobBucket bucket = result.Buckets[index];
                bool mission = IsMission(job.Kind);
                if (IsFinished(job.Outcome))
                {
                    if (mission) { bucket.MissionsFinished++; result.MissionsFinished++; }
                    else { bucket.InteractiveFinished++; result.InteractiveFinished++; }
                }
                else if (IsFailed(job.Outcome))
                {
                    if (mission) { bucket.MissionsFailed++; result.MissionsFailed++; }
                    else { bucket.InteractiveFailed++; result.InteractiveFailed++; }
                }
            }

            return result;
        }

        private static HarborSlotMetrics BuildSlots(TimeBucketLayout layout, List<HarborJobRecord> jobs, int maxConcurrentJobs, DateTime nowUtc)
        {
            List<TimeInterval> intervals = new List<TimeInterval>();
            foreach (HarborJobRecord job in jobs)
            {
                if (job == null) continue;
                intervals.Add(new TimeInterval(job.StartedUtc ?? job.LaunchedUtc, job.EndedUtc));
            }

            List<ConcurrencyBucket> buckets = ConcurrencyProfile.Build(layout, intervals, nowUtc);
            HarborSlotMetrics result = new HarborSlotMetrics { MaxConcurrentJobs = maxConcurrentJobs, Buckets = buckets };

            double weighted = 0;
            double span = 0;
            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Peak > result.Peak) result.Peak = buckets[i].Peak;
                DateTime start = layout.StartOf(i);
                DateTime end = layout.EndOf(i) < nowUtc ? layout.EndOf(i) : nowUtc;
                if (end <= start) continue;
                double length = (end - start).TotalSeconds;
                weighted += buckets[i].Average * length;
                span += length;
            }

            result.Average = span > 0 ? Math.Round(weighted / span, 2) : 0;
            return result;
        }

        private static List<HarborLaunchSpeed> BuildLaunchSpeed(TimeBucketLayout layout, List<HarborJobRecord> jobs)
        {
            Dictionary<string, List<HarborJobRecord>> byRuntime = new Dictionary<string, List<HarborJobRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (HarborJobRecord job in jobs)
            {
                if (job == null || !job.EndedUtc.HasValue || layout.IndexOf(job.EndedUtc.Value) < 0) continue;
                string runtime = String.IsNullOrWhiteSpace(job.Runtime) ? "Unknown" : job.Runtime;
                if (!byRuntime.TryGetValue(runtime, out List<HarborJobRecord>? list))
                {
                    list = new List<HarborJobRecord>();
                    byRuntime[runtime] = list;
                }

                list.Add(job);
            }

            List<HarborLaunchSpeed> result = new List<HarborLaunchSpeed>();
            foreach (KeyValuePair<string, List<HarborJobRecord>> pair in byRuntime)
            {
                List<long> firstOutput = new List<long>();
                List<long> durations = new List<long>();
                List<List<long>> perBucket = new List<List<long>>();
                for (int i = 0; i < layout.BucketCount; i++) perBucket.Add(new List<long>());

                foreach (HarborJobRecord job in pair.Value)
                {
                    if (job.TimeToFirstOutputMs.HasValue && job.TimeToFirstOutputMs.Value >= 0)
                    {
                        firstOutput.Add(job.TimeToFirstOutputMs.Value);
                        perBucket[layout.IndexOf(job.EndedUtc!.Value)].Add(job.TimeToFirstOutputMs.Value);
                    }

                    if (job.DurationMs.HasValue && job.DurationMs.Value >= 0) durations.Add(job.DurationMs.Value);
                }

                HarborLaunchSpeed speed = new HarborLaunchSpeed
                {
                    Runtime = pair.Key,
                    JobCount = pair.Value.Count,
                    FirstOutputCount = firstOutput.Count,
                    FirstOutputMedianMs = TimeBucketMath.Median(firstOutput),
                    FirstOutputP95Ms = TimeBucketMath.Percentile(firstOutput, 95),
                    DurationCount = durations.Count,
                    DurationMedianMs = TimeBucketMath.Median(durations),
                    DurationP95Ms = TimeBucketMath.Percentile(durations, 95)
                };
                foreach (List<long> bucket in perBucket) speed.FirstOutputMedianMsByBucket.Add(TimeBucketMath.Median(bucket));
                result.Add(speed);
            }

            return result
                .OrderByDescending(s => s.JobCount)
                .ThenBy(s => s.Runtime, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HarborLinkMetrics BuildLink(TimeBucketLayout layout, HarborMetricsInput input)
        {
            HarborLinkMetrics result = new HarborLinkMetrics();
            DateTime end = input.NowUtc < layout.ToUtc ? input.NowUtc : layout.ToUtc;
            result.Segments = BuildTimeline(layout.FromUtc, end, input.EventBeforeWindow, input.Events, input.ReconnectGrace);

            double known = 0;
            double connected = 0;
            foreach (HarborLinkSegment segment in result.Segments)
            {
                if (segment.State == HarborLinkSegmentStateEnum.Unknown) continue;
                double length = (segment.EndUtc - segment.StartUtc).TotalMilliseconds;
                known += length;
                if (segment.State == HarborLinkSegmentStateEnum.Connected) connected += length;
            }

            result.ConnectedPercent = known > 0 ? Math.Round(connected * 100.0 / known, 1) : (double?)null;
            result.Disconnects = (input.Events ?? new List<HarborLinkEvent>())
                .Count(e => e != null && e.EventType == HarborLinkEventTypeEnum.Reconnecting && layout.IndexOf(e.OccurredUtc) >= 0);

            List<HarborRoundTripBucket> buckets = new List<HarborRoundTripBucket>();
            List<long> totals = new List<long>();
            for (int i = 0; i < layout.BucketCount; i++)
            {
                buckets.Add(new HarborRoundTripBucket { BucketStartUtc = layout.StartOf(i) });
                totals.Add(0);
            }

            List<long> sampleAverages = new List<long>();
            foreach (HarborLinkSample sample in input.Samples ?? new List<HarborLinkSample>())
            {
                if (sample == null) continue;
                int index = layout.IndexOf(sample.BucketStartUtc);
                if (index < 0) continue;
                HarborRoundTripBucket bucket = buckets[index];
                bucket.HeartbeatCount += sample.HeartbeatCount;
                if (sample.RoundTripCount <= 0) continue;
                bucket.SampleCount += sample.RoundTripCount;
                totals[index] += sample.RoundTripTotalMs;
                if (sample.RoundTripMaxMs.HasValue && (!bucket.MaxMs.HasValue || sample.RoundTripMaxMs.Value > bucket.MaxMs.Value))
                    bucket.MaxMs = sample.RoundTripMaxMs;
                sampleAverages.Add((long)Math.Round(sample.RoundTripTotalMs / (double)sample.RoundTripCount, MidpointRounding.AwayFromZero));
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].SampleCount > 0)
                    buckets[i].AverageMs = Math.Round(totals[i] / (double)buckets[i].SampleCount, 1);
            }

            result.RoundTrip = buckets;
            result.RoundTripMedianMs = TimeBucketMath.Median(sampleAverages);
            if (input.LatestSample != null)
            {
                result.ReconnectCount = input.LatestSample.ReconnectCount;
                result.LastReconnectUtc = input.LatestSample.LastReconnectUtc;
            }

            return result;
        }

        private static HarborTokenMetrics BuildTokens(TimeBucketLayout layout, List<TokenUsageRecord> records)
        {
            HarborTokenMetrics result = new HarborTokenMetrics();
            List<Dictionary<string, HarborTokenSeries>> perBucket = new List<Dictionary<string, HarborTokenSeries>>();
            for (int i = 0; i < layout.BucketCount; i++)
            {
                result.Buckets.Add(new HarborTokenBucket { BucketStartUtc = layout.StartOf(i) });
                perBucket.Add(new Dictionary<string, HarborTokenSeries>(StringComparer.OrdinalIgnoreCase));
            }

            Dictionary<string, HarborTokenSeries> overall = new Dictionary<string, HarborTokenSeries>(StringComparer.OrdinalIgnoreCase);
            foreach (TokenUsageRecord record in records)
            {
                if (record == null) continue;
                int index = layout.IndexOf(record.CreatedUtc);
                if (index < 0) continue;

                string runtime = String.IsNullOrWhiteSpace(record.Runtime) ? "unknown" : record.Runtime!;
                string model = String.IsNullOrWhiteSpace(record.Model) ? "unknown" : record.Model;
                string key = runtime + "\n" + model;

                result.RecordCount++;
                if (record.Estimated) result.EstimatedCount++;
                result.InputTokens += record.InputTokens;
                result.OutputTokens += record.OutputTokens;
                result.CachedTokens += record.CachedTokens;
                result.TotalTokens += record.TotalTokens;

                HarborTokenBucket bucket = result.Buckets[index];
                bucket.InputTokens += record.InputTokens;
                bucket.OutputTokens += record.OutputTokens;
                bucket.CachedTokens += record.CachedTokens;
                bucket.TotalTokens += record.TotalTokens;

                Accumulate(perBucket[index], key, runtime, model, record);
                Accumulate(overall, key, runtime, model, record);
            }

            for (int i = 0; i < layout.BucketCount; i++) result.Buckets[i].Series = Order(perBucket[i].Values);
            result.Series = Order(overall.Values);
            return result;
        }

        private static void Accumulate(Dictionary<string, HarborTokenSeries> map, string key, string runtime, string model, TokenUsageRecord record)
        {
            if (!map.TryGetValue(key, out HarborTokenSeries? series))
            {
                series = new HarborTokenSeries { Runtime = runtime, Model = model };
                map[key] = series;
            }

            series.InputTokens += record.InputTokens;
            series.OutputTokens += record.OutputTokens;
            series.CachedTokens += record.CachedTokens;
            series.TotalTokens += record.TotalTokens;
        }

        private static List<HarborTokenSeries> Order(IEnumerable<HarborTokenSeries> series)
        {
            return series
                .OrderByDescending(s => s.TotalTokens)
                .ThenBy(s => s.Runtime, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Model, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HarborLinkSegmentStateEnum ToSegmentState(HarborLinkEventTypeEnum type)
        {
            switch (type)
            {
                case HarborLinkEventTypeEnum.Connected: return HarborLinkSegmentStateEnum.Connected;
                case HarborLinkEventTypeEnum.Reconnecting: return HarborLinkSegmentStateEnum.Reconnecting;
                default: return HarborLinkSegmentStateEnum.Down;
            }
        }

        private static void Emit(List<HarborLinkSegment> segments, HarborLinkSegmentStateEnum state, DateTime reconnectingSince, DateTime start, DateTime end, TimeSpan grace)
        {
            if (end <= start) return;
            if (state == HarborLinkSegmentStateEnum.Reconnecting)
            {
                DateTime downAt = reconnectingSince.Add(grace);
                if (downAt <= start)
                {
                    Append(segments, HarborLinkSegmentStateEnum.Down, start, end);
                    return;
                }

                if (downAt < end)
                {
                    Append(segments, HarborLinkSegmentStateEnum.Reconnecting, start, downAt);
                    Append(segments, HarborLinkSegmentStateEnum.Down, downAt, end);
                    return;
                }
            }

            Append(segments, state, start, end);
        }

        private static void Append(List<HarborLinkSegment> segments, HarborLinkSegmentStateEnum state, DateTime start, DateTime end)
        {
            if (end <= start) return;
            if (segments.Count > 0)
            {
                HarborLinkSegment last = segments[segments.Count - 1];
                if (last.State == state && last.EndUtc == start)
                {
                    last.EndUtc = end;
                    return;
                }
            }

            segments.Add(new HarborLinkSegment { State = state, StartUtc = start, EndUtc = end });
        }

        #endregion
    }
}
