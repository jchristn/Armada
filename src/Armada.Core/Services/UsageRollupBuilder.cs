namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Builds weighted hourly history from raw rows consumed by one atomic retention batch.</summary>
    public static class UsageRollupBuilder
    {
        /// <summary>UTC start of an observation's hour.</summary>
        public static DateTime Hour(DateTime time) => new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0, DateTimeKind.Utc);

        /// <summary>Tenant/account/provider/hour key with unambiguous component boundaries.</summary>
        public static string GroupKey(UsageSnapshot row) => JsonSerializer.Serialize(new object?[] { row.TenantId, row.HostId, row.Runtime, row.Provider, row.AccountKey, Hour(row.CapturedUtc) });

        /// <summary>Merge a batch into retained aggregates. Call only inside the transaction deleting the same raw rows.</summary>
        /// <param name="snapshots">One account and closed UTC hour, ordered by time and ID.</param>
        /// <param name="existing">Previously committed, pruned contributions for that account/hour.</param>
        /// <returns>Replacement aggregates; failed observations break the last-value line without destroying prior statistics.</returns>
        public static List<UsageHourlyRollup> Merge(List<UsageSnapshot> snapshots, List<UsageHourlyRollup> existing)
        {
            if (snapshots.Count == 0) return existing;
            UsageSnapshot first = snapshots[0];
            Dictionary<string, UsageHourlyRollup> rows = existing.ToDictionary(row => WindowKey(row.WindowName, row.Model));
            Dictionary<string, List<UsageWindow>> decoded = new Dictionary<string, List<UsageWindow>>();
            foreach (UsageSnapshot snapshot in snapshots)
            {
                List<UsageWindow> windows;
                try { windows = JsonSerializer.Deserialize<List<UsageWindow>>(snapshot.WindowsJson ?? "[]") ?? new List<UsageWindow>(); }
                catch (JsonException) { windows = new List<UsageWindow>(); }
                windows = windows.Where(window => window != null).GroupBy(window => WindowKey(window.Name, window.Model)).Select(group => group.Last()).ToList();
                decoded[snapshot.Id] = windows;
                foreach (UsageWindow window in windows)
                {
                    string key = WindowKey(window.Name, window.Model);
                    if (!rows.ContainsKey(key)) rows[key] = new UsageHourlyRollup
                    {
                        TenantId = first.TenantId, HostId = first.HostId, Runtime = first.Runtime,
                        Provider = first.Provider, AccountKey = first.AccountKey!, WindowName = window.Name,
                        Model = window.Model ?? String.Empty, HourUtc = Hour(first.CapturedUtc)
                    };
                }
            }
            if (rows.Count == 0) rows[WindowKey(String.Empty, null)] = new UsageHourlyRollup
            {
                TenantId = first.TenantId, HostId = first.HostId, Runtime = first.Runtime,
                Provider = first.Provider, AccountKey = first.AccountKey!, HourUtc = Hour(first.CapturedUtc)
            };
            foreach (UsageSnapshot snapshot in snapshots.OrderBy(row => row.CapturedUtc).ThenBy(row => row.Id, StringComparer.Ordinal))
            {
                bool successful = snapshot.Status == UsageSnapshotStatusEnum.Ok || snapshot.Status == UsageSnapshotStatusEnum.Estimated;
                foreach (UsageHourlyRollup row in rows.Values)
                {
                    UsageWindow? window = decoded[snapshot.Id].FirstOrDefault(value => WindowKey(value.Name, value.Model) == WindowKey(row.WindowName, row.Model));
                    if (successful && window == null && row.WindowName.Length > 0) continue;
                    row.SampleCount++;
                    switch (snapshot.Status)
                    {
                        case UsageSnapshotStatusEnum.Ok: row.OkCount++; break;
                        case UsageSnapshotStatusEnum.Estimated: row.EstimatedCount++; break;
                        case UsageSnapshotStatusEnum.Failed: row.FailedCount++; break;
                        case UsageSnapshotStatusEnum.AuthMissing: row.AuthMissingCount++; break;
                        case UsageSnapshotStatusEnum.Unsupported: row.UnsupportedCount++; break;
                    }
                    double? percent = successful ? window?.UsedPercent : null;
                    if (percent.HasValue && Double.IsFinite(percent.Value))
                    {
                        row.MeanPercent = ((row.MeanPercent ?? 0) * row.PercentSampleCount + percent.Value) / (row.PercentSampleCount + 1);
                        row.PercentSampleCount++;
                        row.MinPercent = row.MinPercent.HasValue ? Math.Min(row.MinPercent.Value, percent.Value) : percent;
                        row.MaxPercent = row.MaxPercent.HasValue ? Math.Max(row.MaxPercent.Value, percent.Value) : percent;
                    }
                    row.FirstObservedUtc = !row.FirstObservedUtc.HasValue || snapshot.CapturedUtc < row.FirstObservedUtc ? snapshot.CapturedUtc : row.FirstObservedUtc;
                    if (!row.LastObservedUtc.HasValue || snapshot.CapturedUtc >= row.LastObservedUtc)
                    {
                        row.LastPercent = percent; row.LastUsed = successful ? window?.Used : null;
                        row.LastLimit = successful ? window?.Limit : null; row.LastResetUtc = window?.ResetsUtc;
                        row.LastObservedUtc = snapshot.CapturedUtc; row.LastStatus = snapshot.Status.ToString();
                    }
                    // Instant samples establish no continuous coverage duration. Never infer an interval or interpolate gaps.
                    row.CoverageSeconds = null;
                    row.RawPruned = true;
                    row.LastUpdateUtc = DateTime.UtcNow;
                }
            }
            return rows.Values.ToList();
        }

        private static string WindowKey(string name, string? model) => JsonSerializer.Serialize(new[] { name, model ?? String.Empty });
    }
}
