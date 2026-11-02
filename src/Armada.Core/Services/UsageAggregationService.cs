namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Tenant-scoped aggregation of persisted observations and completion-time token records.</summary>
    public class UsageAggregationService
    {
        private readonly DatabaseDriver _Database;
        private readonly UsageCaptureSettings _Settings;

        /// <summary>Construct with database and retention policy.</summary>
        public UsageAggregationService(DatabaseDriver database, UsageCaptureSettings? settings = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? new UsageCaptureSettings();
        }

        /// <summary>Validate required bounds, pagination and public chart options.</summary>
        public static void Validate(UsageQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (String.IsNullOrEmpty(query.TenantId)) throw new ArgumentException("A tenant scope is required.");
            if (!query.FromUtc.HasValue || !query.ToUtc.HasValue) throw new ArgumentException("from and to are required.");
            if (query.FromUtc.Value.Kind != DateTimeKind.Utc || query.ToUtc.Value.Kind != DateTimeKind.Utc) throw new ArgumentException("Bounds must be UTC.");
            if (query.FromUtc >= query.ToUtc || query.ToUtc.Value - query.FromUtc.Value > TimeSpan.FromDays(90)) throw new ArgumentException("Range must be increasing and at most 90 days.");
            if (query.PageNumber < 1 || query.PageSize < 1 || query.PageSize > 100) throw new ArgumentException("pageSize must be 1..100 and pageNumber positive.");
        }

        /// <summary>Read bounded account limits or deduplicated recorded token consumption.</summary>
        public async Task<UsageSeriesResult> SeriesAsync(UsageQuery query, CancellationToken token = default)
        {
            using UsageTelemetryOperation telemetry = UsageTelemetry.Start("query");
            Validate(query);
            if (query.Metric != "limits" && query.Metric != "tokens") throw new ArgumentException("metric must be limits or tokens.");
            if (query.GroupBy != "account" && query.GroupBy != "provider") throw new ArgumentException("groupBy must be account or provider.");
            if (query.BucketMinutes != 5 && query.BucketMinutes != 60 && query.BucketMinutes != 360) throw new ArgumentException("Allowed bucketMinutes: 5, 60, 360.");
            if (Math.Ceiling((query.ToUtc!.Value - query.FromUtc!.Value).TotalMinutes / query.BucketMinutes) > 576) throw new ArgumentException("At most 576 points per series; choose a larger bucketMinutes.");
            if (query.Metric == "limits" && query.BucketMinutes == 5 && query.FromUtc < DateTime.UtcNow.AddDays(-_Settings.RawSnapshotRetentionDays)) throw new ArgumentException("Raw retention exceeded; allowed bucketMinutes: 60, 360.");
            UsageSeriesResult result = new UsageSeriesResult { FromUtc = query.FromUtc.Value, ToUtc = query.ToUtc.Value, BucketMinutes = query.BucketMinutes, GroupBy = query.GroupBy, DrilldownFilters = query };
            if (query.Metric == "tokens")
            {
                List<TokenUsageRecord> records = await TokensAsync(query, result.Coverage, token).ConfigureAwait(false);
                foreach (IGrouping<string, TokenUsageRecord> group in records.GroupBy(r => Key(r.Provider ?? r.Runtime, query.GroupBy == "account" ? CapturedAccount(r) : null, query.GroupBy == "account" ? r.HostId : null, query.GroupBy == "account" ? r.Runtime : null, r.CostCurrency)))
                {
                    TokenUsageRecord first = group.First();
                    UsageSeries series = new UsageSeries { Provider = first.Provider ?? first.Runtime, AccountKey = query.GroupBy == "account" ? CapturedAccount(first) : null, HostId = query.GroupBy == "account" ? first.HostId : null, Runtime = query.GroupBy == "account" ? first.Runtime : null, CostCurrency = first.CostCurrency };
                    foreach (IGrouping<DateTime, TokenUsageRecord> bucket in group.GroupBy(r => Bucket(query, r.CreatedUtc)).OrderBy(g => g.Key))
                    {
                        UsageSeriesPoint point = Point(query, bucket.Key);
                        point.InputTokens = bucket.Sum(r => r.InputTokens); point.OutputTokens = bucket.Sum(r => r.OutputTokens); point.CachedTokens = bucket.Sum(r => r.CachedTokens); point.TotalTokens = bucket.Sum(r => r.TotalTokens);
                        point.ReportedCost = bucket.Any(r => r.ReportedCost.HasValue && r.CostCurrency != null) ? bucket.Where(r => r.CostCurrency != null).Sum(r => r.ReportedCost ?? 0) : null;
                        point.CostCurrency = first.CostCurrency; point.Estimated = bucket.Any(r => r.Estimated); point.ObservationCount = bucket.Count(); series.Points.Add(point);
                    }
                    result.Series.Add(series);
                }
            }
            else await LimitsAsync(query, result, token).ConfigureAwait(false);
            result.Series = result.Series.OrderBy(s => Key(s.Provider, s.HostId, s.AccountKey, s.Runtime, s.Window, s.Model, s.CostCurrency), StringComparer.Ordinal).ToList();
            result.Coverage.SeriesTruncated = result.Series.Count > 20;
            result.Series = result.Series.Take(20).ToList();
            telemetry.Complete();
            return result;
        }

        /// <summary>Read mission-source tokens grouped by completion attribution, with safe tenant-scoped metadata.</summary>
        public async Task<UsageMissionResult> MissionsAsync(UsageQuery query, CancellationToken token = default)
        {
            using UsageTelemetryOperation telemetry = UsageTelemetry.Start("query");
            Validate(query);
            if (String.IsNullOrWhiteSpace(query.Provider)) throw new ArgumentException("provider is required.");
            if (!new[] { "totalTokens", "inputTokens", "outputTokens", "cachedTokens", "name", "completion", "cost" }.Contains(query.SortBy)) throw new ArgumentException("Invalid sortBy.");
            if (query.SortDirection != "ascending" && query.SortDirection != "descending") throw new ArgumentException("Invalid sortDirection.");
            UsageMissionResult result = new UsageMissionResult { PageNumber = query.PageNumber, PageSize = query.PageSize };
            List<TokenUsageRecord> records = await TokensAsync(query, result.Coverage, token).ConfigureAwait(false);
            List<UsageMissionRow> rows = new List<UsageMissionRow>();
            foreach (IGrouping<string, TokenUsageRecord> group in records.Where(r => r.Source == "mission" && !String.IsNullOrEmpty(r.SourceId)).GroupBy(r => Key(r.SourceId, r.CaptainId, r.MembershipId, CapturedAccount(r), r.HostId, r.CostCurrency)))
            {
                TokenUsageRecord first = group.First();
                Mission? mission = await _Database.Missions.ReadAsync(query.TenantId!, first.SourceId!, token).ConfigureAwait(false);
                Vessel? vessel = mission?.VesselId == null ? null : await _Database.Vessels.ReadAsync(query.TenantId!, mission.VesselId, token).ConfigureAwait(false);
                Captain? captain = first.CaptainId == null ? null : await _Database.Captains.ReadAsync(query.TenantId!, first.CaptainId, token).ConfigureAwait(false);
                rows.Add(new UsageMissionRow { MissionId = first.SourceId!, Title = mission?.Title, Status = mission?.Status.ToString(), MissionUrl = mission == null ? null : "/missions/" + mission.Id, VesselId = mission?.VesselId, VesselName = vessel?.Name, CaptainId = first.CaptainId, CaptainName = captain?.Name, MembershipId = first.MembershipId, AccountKey = CapturedAccount(first), HostId = first.HostId, CompletedUtc = group.Max(r => r.CreatedUtc), InputTokens = group.Sum(r => r.InputTokens), OutputTokens = group.Sum(r => r.OutputTokens), CachedTokens = group.Sum(r => r.CachedTokens), TotalTokens = group.Sum(r => r.TotalTokens), ReportedCost = group.Any(r => r.ReportedCost.HasValue && r.CostCurrency != null) ? group.Where(r => r.CostCurrency != null).Sum(r => r.ReportedCost ?? 0) : null, CostCurrency = first.CostCurrency, Estimated = group.Any(r => r.Estimated), CostCoverage = (double)group.Count(r => r.ReportedCost.HasValue && r.CostCurrency != null) / group.Count() });
            }
            Func<UsageMissionRow, IComparable?> selector = query.SortBy switch { "name" => r => r.Title, "completion" => r => r.CompletedUtc, "cost" => r => r.ReportedCost, "inputTokens" => r => r.InputTokens, "outputTokens" => r => r.OutputTokens, "cachedTokens" => r => r.CachedTokens, _ => r => r.TotalTokens };
            IOrderedEnumerable<UsageMissionRow> ordered = query.SortDirection == "ascending" ? rows.OrderBy(selector) : rows.OrderByDescending(selector);
            result.TotalRecords = rows.Count; result.TotalPages = (int)Math.Ceiling((double)rows.Count / query.PageSize);
            result.Objects = ordered.ThenBy(r => r.MissionId, StringComparer.Ordinal).ThenBy(r => r.CaptainId, StringComparer.Ordinal).ThenBy(r => r.MembershipId, StringComparer.Ordinal).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
            telemetry.Complete();
            return result;
        }

        private async Task<List<TokenUsageRecord>> TokensAsync(UsageQuery query, UsageCoverage coverage, CancellationToken token)
        {
            List<TokenUsageRecord> records = await _Database.TokenUsage.EnumerateForSummaryAsync(new TokenUsageQuery { TenantId = query.TenantId, CaptainId = query.CaptainId, HarborId = query.HarborId, Runtime = query.Runtime?.ToString(), Model = query.Model, FromUtc = query.FromUtc, ToUtc = query.ToUtc }, token).ConfigureAwait(false);
            records = records.Where(r => r.TenantId == query.TenantId && r.CreatedUtc >= query.FromUtc && r.CreatedUtc < query.ToUtc).GroupBy(r => r.Id).Select(g => g.First()).Where(r => query.Provider == null || String.Equals(r.Provider ?? r.Runtime, query.Provider, StringComparison.OrdinalIgnoreCase)).Where(r => query.HostId == null || r.HostId == query.HostId).ToList();
            coverage.UnattributedRecords = records.Count(r => r.MembershipId == null || r.AccountKey == null);
            coverage.ExcludedUnattributedRecords = query.AccountKey == null ? 0 : coverage.UnattributedRecords;
            if (query.AccountKey != null) records = records.Where(r => r.MembershipId != null && r.AccountKey == query.AccountKey).ToList();
            coverage.MissionRecords = records.Count(r => r.Source == "mission"); coverage.NonMissionRecords = records.Count(r => r.Source != "mission"); coverage.KnownCostRecords = records.Count(r => r.ReportedCost.HasValue && r.CostCurrency != null);
            if (coverage.UnattributedRecords > 0) coverage.Warning = "Legacy records without captured membership are correlation only; account-specific views exclude them.";
            if (query.Window != null) coverage.Warning = (coverage.Warning == null ? String.Empty : coverage.Warning + " ") + "Window identifies quota context only; completed token records are not allocated to quota windows.";
            return records;
        }

        private static string? CapturedAccount(TokenUsageRecord record) => String.IsNullOrEmpty(record.MembershipId) ? null : record.AccountKey;
        private static string Key(params string?[] values) => JsonSerializer.Serialize(values);
        private static DateTime Bucket(UsageQuery query, DateTime time) => query.FromUtc!.Value.AddMinutes(Math.Floor((time - query.FromUtc.Value).TotalMinutes / query.BucketMinutes) * query.BucketMinutes);
        private static UsageSeriesPoint Point(UsageQuery query, DateTime start) => new UsageSeriesPoint { BucketStartUtc = start, BucketEndUtc = start.AddMinutes(query.BucketMinutes) < query.ToUtc!.Value ? start.AddMinutes(query.BucketMinutes) : query.ToUtc.Value };

        private async Task LimitsAsync(UsageQuery query, UsageSeriesResult result, CancellationToken token)
        {
            Dictionary<string, UsageSeries> seriesByKey = new Dictionary<string, UsageSeries>();
            Dictionary<UsageSeriesPoint, double> weights = new Dictionary<UsageSeriesPoint, double>();
            List<UsageSnapshot> raw = new List<UsageSnapshot>();
            UsageSnapshotQuery snapshots = new UsageSnapshotQuery { TenantId = query.TenantId, CaptainId = query.CaptainId, HarborId = query.HarborId, Runtime = query.Runtime, Provider = query.Provider, AccountKey = query.AccountKey, FromUtc = query.FromUtc, ToUtc = query.ToUtc, Status = query.Status, PageSize = 1000 };
            while (true)
            {
                EnumerationResult<UsageSnapshot> page = await _Database.UsageSnapshots.EnumerateAsync(snapshots, token).ConfigureAwait(false);
                raw.AddRange(page.Objects);
                if (snapshots.PageNumber >= page.TotalPages || page.Objects.Count == 0) break;
                snapshots.PageNumber++;
            }
            raw = raw.Where(s => s.TenantId == query.TenantId && s.CapturedUtc >= query.FromUtc && s.CapturedUtc < query.ToUtc && (query.HostId == null || s.HostId == query.HostId)).GroupBy(s => s.Id).Select(g => g.First()).ToList();
            foreach (UsageSnapshot snapshot in raw.OrderBy(s => s.CapturedUtc).ThenBy(s => s.Id, StringComparer.Ordinal))
            {
                List<UsageWindow> windows;
                try { windows = JsonSerializer.Deserialize<List<UsageWindow>>(snapshot.WindowsJson ?? "[]") ?? new List<UsageWindow>(); }
                catch (JsonException) { windows = new List<UsageWindow>(); }
                if (snapshot.Status != UsageSnapshotStatusEnum.Ok && snapshot.Status != UsageSnapshotStatusEnum.Estimated)
                {
                    // A failed account observation breaks each previously observed window line.
                    windows = seriesByKey.Values.Where(s => s.Provider == snapshot.Provider && s.AccountKey == snapshot.AccountKey && s.HostId == snapshot.HostId && s.Runtime == snapshot.Runtime.ToString())
                        .Select(s => new UsageWindow { Name = s.Window ?? String.Empty, Model = s.Model }).ToList();
                    if (windows.Count == 0 && query.BucketMinutes >= 60)
                    {
                        EnumerationResult<UsageHourlyRollup> known = await _Database.UsageRollups.EnumerateAsync(new UsageRollupQuery
                        {
                            TenantId = query.TenantId, HostId = snapshot.HostId, Runtime = snapshot.Runtime,
                            Provider = snapshot.Provider, AccountKey = snapshot.AccountKey,
                            FromHourUtc = UsageRollupBuilder.Hour(snapshot.CapturedUtc), ToHourUtc = UsageRollupBuilder.Hour(snapshot.CapturedUtc).AddHours(1), PageSize = 1000
                        }, token).ConfigureAwait(false);
                        windows = known.Objects.Where(row => row.Provider == snapshot.Provider && row.RawPruned)
                            .Select(row => new UsageWindow { Name = row.WindowName, Model = row.Model.Length == 0 ? null : row.Model }).ToList();
                    }
                }
                if (windows.Count == 0 && query.Window == null && query.Model == null) windows.Add(new UsageWindow());
                foreach (UsageWindow window in windows)
                {
                    if (window == null || (query.Window != null && window.Name != query.Window) || (query.Model != null && window.Model != query.Model)) continue;
                    UsageSeries series = GetSeries(seriesByKey, snapshot.Provider, snapshot.AccountKey, snapshot.HostId, snapshot.Runtime.ToString(), window.Name, window.Model);
                    UsageSeriesPoint point = GetPoint(series, query, snapshot.CapturedUtc);
                    Merge(point, weights, window.UsedPercent, window.UsedPercent, window.UsedPercent, window.UsedPercent, 1, snapshot.CapturedUtc, window.ResetsUtc, snapshot.Status.ToString(), snapshot.Status == UsageSnapshotStatusEnum.Estimated);
                }
            }
            if (query.BucketMinutes >= 60)
            {
                UsageRollupQuery rollups = new UsageRollupQuery { TenantId = query.TenantId, HostId = query.HostId, Runtime = query.Runtime, Provider = query.Provider, AccountKey = query.AccountKey, WindowName = query.Window, Model = query.Model, FromHourUtc = UsageRollupBuilder.Hour(query.FromUtc!.Value), ToHourUtc = query.ToUtc, PageSize = 1000 };
                while (true)
                {
                    EnumerationResult<UsageHourlyRollup> page = await _Database.UsageRollups.EnumerateAsync(rollups, token).ConfigureAwait(false);
                    foreach (UsageHourlyRollup rollup in page.Objects)
                    {
                        // A rollup is usable only for a complete hour inside the requested interval.
                        if (rollup.TenantId != query.TenantId) continue;
                        if (rollup.HourUtc < query.FromUtc || rollup.HourUtc.AddHours(1) > query.ToUtc)
                        {
                            result.Coverage.Warning = "Partial UTC hours are excluded from retained history; align bounds to UTC hours for complete coverage.";
                            continue;
                        }
                        if (rollup.HourUtc.AddHours(1) > Point(query, Bucket(query, rollup.HourUtc)).BucketEndUtc)
                        {
                            result.Coverage.Warning = "Hourly observations crossing a requested bucket boundary are excluded; align bounds to UTC hours for complete coverage.";
                            continue;
                        }
                        if (query.HarborId != null)
                        {
                            result.Coverage.Warning = "Hourly rollups lack historical Harbor attribution; matching raw observations only are shown.";
                            continue;
                        }
                        if (query.CaptainId != null)
                        {
                            UsageMembershipQuery membershipQuery = new UsageMembershipQuery { TenantId = query.TenantId, CaptainId = query.CaptainId, HostId = rollup.HostId, Runtime = rollup.Runtime, AccountKey = rollup.AccountKey, PageSize = 1000 };
                            bool member = false;
                            while (true)
                            {
                                EnumerationResult<UsageAccountMembership> memberships = await _Database.UsageMemberships.EnumerateAsync(membershipQuery, token).ConfigureAwait(false);
                                member = memberships.Objects.Any(m => m.TenantId == query.TenantId && m.EffectiveFromUtc <= rollup.HourUtc && (!m.EffectiveToUtc.HasValue || m.EffectiveToUtc >= rollup.HourUtc.AddHours(1)));
                                if (member || membershipQuery.PageNumber >= memberships.TotalPages || memberships.Objects.Count == 0) break;
                                membershipQuery.PageNumber++;
                            }
                            if (!member) continue;
                        }
                        // Retention writes rollups before deleting raw observations. Never count both.
                        if (!rollup.RawPruned && raw.Any(s => s.HostId == rollup.HostId && s.Runtime == rollup.Runtime && s.AccountKey == rollup.AccountKey && s.Provider == rollup.Provider && s.CapturedUtc >= rollup.HourUtc && s.CapturedUtc < rollup.HourUtc.AddHours(1))) continue;
                        UsageSeries series = GetSeries(seriesByKey, rollup.Provider, rollup.AccountKey, rollup.HostId, rollup.Runtime.ToString(), rollup.WindowName, rollup.Model.Length == 0 ? null : rollup.Model);
                        UsageSeriesPoint point = GetPoint(series, query, rollup.HourUtc);
                        Merge(point, weights, rollup.LastPercent, rollup.MinPercent, rollup.MaxPercent, rollup.MeanPercent, rollup.SampleCount, rollup.LastObservedUtc ?? rollup.HourUtc, rollup.LastResetUtc, rollup.LastStatus ?? (rollup.FailedCount > 0 ? "Failed" : rollup.AuthMissingCount > 0 ? "AuthMissing" : rollup.UnsupportedCount > 0 ? "Unsupported" : rollup.EstimatedCount > 0 ? "Estimated" : "Ok"), rollup.EstimatedCount > 0, rollup.RawPruned ? rollup.PercentSampleCount : null);
                        result.Resolution = "hourly";
                    }
                    if (rollups.PageNumber >= page.TotalPages || page.Objects.Count == 0) break;
                    rollups.PageNumber++;
                }
            }
            foreach (UsageSeries series in seriesByKey.Values) series.Points = series.Points.OrderBy(p => p.BucketStartUtc).ToList();
            result.Series = seriesByKey.Values.ToList();
        }

        private static UsageSeries GetSeries(Dictionary<string, UsageSeries> seriesByKey, string? provider, string? account, string host, string runtime, string window, string? model)
        {
            string key = Key(provider, account, host, runtime, window, model);
            if (!seriesByKey.TryGetValue(key, out UsageSeries? series))
            {
                series = new UsageSeries { Provider = provider, AccountKey = account, HostId = host, Runtime = runtime, Window = window, Model = model };
                seriesByKey.Add(key, series);
            }
            return series;
        }

        private static UsageSeriesPoint GetPoint(UsageSeries series, UsageQuery query, DateTime observed)
        {
            DateTime start = Bucket(query, observed);
            UsageSeriesPoint? point = series.Points.FirstOrDefault(p => p.BucketStartUtc == start);
            if (point == null) { point = Point(query, start); series.Points.Add(point); }
            return point;
        }

        private static void Merge(UsageSeriesPoint point, Dictionary<UsageSeriesPoint, double> weights, double? last, double? min, double? max, double? mean, int count, DateTime observed, DateTime? reset, string status, bool estimated, int? percentCount = null)
        {
            point.ObservationCount += count;
            if (min.HasValue) point.MinPercent = point.MinPercent.HasValue ? Math.Min(point.MinPercent.Value, min.Value) : min;
            if (max.HasValue) point.MaxPercent = point.MaxPercent.HasValue ? Math.Max(point.MaxPercent.Value, max.Value) : max;
            int numericCount = percentCount ?? count;
            if (mean.HasValue && numericCount > 0)
            {
                double previousWeight = weights.TryGetValue(point, out double weight) ? weight : 0;
                point.MeanPercent = ((point.MeanPercent ?? 0) * previousWeight + mean.Value * numericCount) / (previousWeight + numericCount);
                weights[point] = previousWeight + numericCount;
            }
            if (!point.ObservedUtc.HasValue || observed >= point.ObservedUtc)
            {
                point.LastPercent = last; point.LastResetUtc = reset; point.ObservedUtc = observed; point.Status = status;
                point.ObservationAgeSeconds = Math.Max(0, (DateTime.UtcNow - observed).TotalSeconds);
            }
            point.Estimated |= estimated;
        }
    }
}
