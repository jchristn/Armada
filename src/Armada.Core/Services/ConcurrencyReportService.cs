namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Per-entity concurrency reports read from missions and recorded wait intervals: a vessel's slots and waiting
    /// missions, and wait aggregates over a window for charts. Prometheus carries only fleet-wide series (section 8.3).
    /// </summary>
    public class ConcurrencyReportService
    {
        #region Private-Members

        private const int _MaxListed = 100;
        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="database"/> is null.</exception>
        public ConcurrencyReportService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>Build a vessel's concurrency report.</summary>
        /// <param name="vessel">The vessel, already authorized for the caller.</param>
        /// <param name="tenantId">Tenant scope of the caller, or null for a system administrator.</param>
        /// <param name="userId">When set, only this user's voyages and missions are listed; counts stay vessel-wide.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="vessel"/> is null.</exception>
        public async Task<VesselConcurrency> GetVesselConcurrencyAsync(Vessel vessel, string? tenantId, string? userId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            DateTime now = DateTime.UtcNow;
            VesselConcurrency report = new VesselConcurrency
            {
                VesselId = vessel.Id, Name = vessel.Name, ComputedUtc = now,
                MaxParallelVoyages = vessel.MaxParallelVoyages, MaxParallelMissionsPerVoyage = vessel.MaxParallelMissionsPerVoyage
            };

            List<MissionAdmissionSnapshot> missions = VoyageSlotPolicy.Runnable(
                await _Database.Missions.EnumerateAdmissionSnapshotsAsync(vessel.Id, token).ConfigureAwait(false));
            Dictionary<string, MissionAdmissionSnapshot> byId = missions.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            List<IGrouping<string, MissionAdmissionSnapshot>> admitted = missions.GroupBy(m => VoyageSlotPolicy.Group(m, byId))
                .Where(g => VoyageSlotPolicy.Admitted(g)).ToList();
            report.VoyageSlotsInUse = admitted.Count;
            report.RunningMissions = missions.Count(m => VoyageSlotPolicy.Running(m.Status));
            foreach (IGrouping<string, MissionAdmissionSnapshot> group in admitted
                .OrderByDescending(g => g.Count(m => VoyageSlotPolicy.Running(m.Status))).Take(_MaxListed))
            {
                VesselVoyageSlotUsage usage = new VesselVoyageSlotUsage
                {
                    RunningMissions = group.Count(m => VoyageSlotPolicy.Running(m.Status)),
                    PendingMissions = group.Count(m => m.Status == MissionStatusEnum.Pending)
                };
                string? voyageId = group.First().VoyageId;
                if (!String.IsNullOrEmpty(voyageId))
                {
                    Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId!, token).ConfigureAwait(false);
                    if (voyage == null || !Visible(voyage.TenantId, voyage.UserId, tenantId, userId)) continue;
                    usage.VoyageId = voyage.Id;
                    usage.Title = voyage.Title;
                }
                else
                {
                    MissionSummary? root = await _Database.Missions.ReadSummaryAsync(group.Key.Substring("mission:".Length), token).ConfigureAwait(false);
                    if (root == null || !Visible(root.TenantId, root.UserId, tenantId, userId)) continue;
                    usage.MissionId = root.Id;
                    usage.Title = root.Title;
                }
                report.HoldingVoyages.Add(usage);
            }

            List<MissionWaitInterval> open = await _Database.MissionWaitIntervals.EnumerateAsync(
                new MissionWaitIntervalQuery { TenantId = tenantId, VesselId = vessel.Id, OpenOnly = true }, token).ConfigureAwait(false);
            HashSet<string> pending = new HashSet<string>(missions.Where(m => m.Status == MissionStatusEnum.Pending).Select(m => m.Id), StringComparer.Ordinal);
            List<MissionWaitInterval> current = open.Where(i => pending.Contains(i.MissionId))
                .GroupBy(i => i.MissionId).Select(g => g.OrderByDescending(i => i.StartedUtc).First())
                .OrderBy(i => i.StartedUtc).ToList();
            report.WaitingCount = current.Count;
            foreach (MissionWaitInterval interval in current)
            {
                if (report.Waiting.Count >= _MaxListed) break;
                MissionSummary? mission = await _Database.Missions.ReadSummaryAsync(interval.MissionId, token).ConfigureAwait(false);
                if (mission == null || !Visible(mission.TenantId, mission.UserId, tenantId, userId)) continue;
                report.Waiting.Add(new WaitingMissionInfo
                {
                    MissionId = mission.Id, Title = mission.Title, VoyageId = mission.VoyageId, Gate = interval.Gate, Detail = interval.Detail,
                    WaitingSinceUtc = interval.StartedUtc, WaitedSeconds = Math.Max(0, (now - interval.StartedUtc).TotalSeconds)
                });
            }

            List<MissionWaitInterval> week = await _Database.MissionWaitIntervals.EnumerateAsync(
                new MissionWaitIntervalQuery { TenantId = tenantId, VesselId = vessel.Id, FromUtc = now.AddDays(-7), ToUtc = now }, token).ConfigureAwait(false);
            report.SlotWait24h = SlotWait(week, now.AddHours(-24), now, 24);
            report.SlotWait7d = SlotWait(week, now.AddDays(-7), now, 168);
            return report;
        }

        /// <summary>Aggregate recorded waits over a window for charts.</summary>
        /// <param name="fromUtc">Window start; defaults to 24 hours before <paramref name="toUtc"/>.</param>
        /// <param name="toUtc">Window end; defaults to now.</param>
        /// <param name="vesselId">Optional vessel filter.</param>
        /// <param name="tenantId">Tenant scope of the caller, or null for a system administrator.</param>
        /// <param name="captainId">Optional captain filter: waits of missions the captain went on to run, plus open waits of missions pinned to it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The aggregates.</returns>
        /// <exception cref="ArgumentException">When the window is empty, inverted or longer than 31 days.</exception>
        public async Task<ConcurrencyMetrics> GetMetricsAsync(DateTime? fromUtc, DateTime? toUtc, string? vesselId, string? tenantId, string? captainId = null, CancellationToken token = default)
        {
            DateTime to = (toUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime from = (fromUtc ?? to.AddHours(-24)).ToUniversalTime();
            if (from >= to) throw new ArgumentException("from must be earlier than to.", nameof(fromUtc));
            if (to - from > TimeSpan.FromDays(31)) throw new ArgumentException("The window can be at most 31 days.", nameof(fromUtc));

            MissionWaitIntervalQuery query = new MissionWaitIntervalQuery { TenantId = tenantId, VesselId = vesselId, CaptainId = captainId, FromUtc = from, ToUtc = to, MaxRows = 50000 };
            List<MissionWaitInterval> intervals = await _Database.MissionWaitIntervals.EnumerateAsync(query, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            int bucketSeconds = BucketSeconds(to - from);
            ConcurrencyMetrics metrics = new ConcurrencyMetrics
            {
                FromUtc = from, ToUtc = to, VesselId = vesselId, CaptainId = captainId, BucketSeconds = bucketSeconds,
                Truncated = intervals.Count >= query.MaxRows,
                SlotWait = SlotWait(intervals, from, to, (int)Math.Ceiling((to - from).TotalHours)),
                ByGate = Totals(intervals, from, to, now)
            };
            for (DateTime start = from; start < to; start = start.AddSeconds(bucketSeconds))
            {
                DateTime end = start.AddSeconds(bucketSeconds) < to ? start.AddSeconds(bucketSeconds) : to;
                List<MissionWaitInterval> inBucket = intervals.Where(i => Overlap(i, start, end, now) > 0).ToList();
                metrics.Buckets.Add(new ConcurrencyMetricsBucket
                {
                    StartUtc = start,
                    MissionsWaiting = inBucket.Select(i => i.MissionId).Distinct(StringComparer.Ordinal).Count(),
                    ByGate = Totals(inBucket, start, end, now)
                });
            }
            return metrics;
        }

        #endregion

        #region Private-Methods

        private static bool Visible(string? ownerTenant, string? ownerUser, string? tenantId, string? userId)
            => (tenantId == null || String.Equals(ownerTenant, tenantId, StringComparison.Ordinal))
                && (userId == null || String.Equals(ownerUser, userId, StringComparison.Ordinal));

        private static double Overlap(MissionWaitInterval interval, DateTime from, DateTime to, DateTime now)
        {
            DateTime start = interval.StartedUtc > from ? interval.StartedUtc : from;
            DateTime endRaw = interval.EndedUtc ?? now;
            DateTime end = endRaw < to ? endRaw : to;
            return Math.Max(0, (end - start).TotalSeconds);
        }

        private static SlotWaitStatistics SlotWait(List<MissionWaitInterval> intervals, DateTime from, DateTime to, int windowHours)
        {
            DateTime now = DateTime.UtcNow;
            List<double> perMission = intervals.Where(i => MissionWaitGates.IsCapacity(i.Gate))
                .GroupBy(i => i.MissionId, StringComparer.Ordinal)
                .Select(g => g.Sum(i => Overlap(i, from, to, now)))
                .Where(s => s > 0).OrderBy(s => s).ToList();
            SlotWaitStatistics stats = new SlotWaitStatistics { WindowHours = windowHours, Missions = perMission.Count };
            if (perMission.Count == 0) return stats;
            stats.P50Seconds = Math.Round(Percentile(perMission, 0.50), 1);
            stats.P95Seconds = Math.Round(Percentile(perMission, 0.95), 1);
            stats.MaxSeconds = Math.Round(perMission[perMission.Count - 1], 1);
            return stats;
        }

        // Nearest-rank percentile over an ascending list.
        private static double Percentile(List<double> sorted, double p)
            => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

        private static List<GateWaitTotal> Totals(List<MissionWaitInterval> intervals, DateTime from, DateTime to, DateTime now)
            => intervals.GroupBy(i => i.Gate)
                .Select(g => new GateWaitTotal
                {
                    Gate = g.Key,
                    Seconds = Math.Round(g.Sum(i => Overlap(i, from, to, now)), 1),
                    Missions = g.Select(i => i.MissionId).Distinct(StringComparer.Ordinal).Count()
                })
                .Where(t => t.Seconds > 0).OrderByDescending(t => t.Seconds).ToList();

        // About 48 buckets on a round width: 5 min, 15 min, 1 h, 3 h, 6 h or 1 day.
        private static int BucketSeconds(TimeSpan window)
        {
            int[] widths = { 300, 900, 3600, 10800, 21600, 86400 };
            foreach (int width in widths)
                if (window.TotalSeconds / width <= 48) return width;
            return widths[widths.Length - 1];
        }

        #endregion
    }
}
