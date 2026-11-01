namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>Tenant-scoped account usage queries and capture receipts.</summary>
    public partial class ArmadaClient
    {
        /// <summary>GetUsageSnapshotsAsync: account usage contract.</summary>
        public Task<EnumerationResult<UsageSnapshotView>?> GetUsageSnapshotsAsync(UsageQuery? query = null, CancellationToken token = default) =>
            GetAsync<EnumerationResult<UsageSnapshotView>>(UsagePath("snapshots", query), null, token);
        /// <summary>GetUsageLatestAsync: account usage contract.</summary>
        public Task<EnumerationResult<UsageSnapshotView>?> GetUsageLatestAsync(UsageQuery? query = null, CancellationToken token = default) =>
            GetAsync<EnumerationResult<UsageSnapshotView>>(UsagePath("latest", query), null, token);
        /// <summary>GetUsageSeriesAsync: account usage contract.</summary>
        public Task<UsageSeriesResult?> GetUsageSeriesAsync(UsageQuery query, CancellationToken token = default) =>
            GetAsync<UsageSeriesResult>(UsagePath("series", query), null, token);
        /// <summary>GetUsageMissionsAsync: account usage contract.</summary>
        public Task<UsageMissionResult?> GetUsageMissionsAsync(UsageQuery query, CancellationToken token = default) =>
            GetAsync<UsageMissionResult>(UsagePath("missions", query), null, token);
        /// <summary>CaptureCaptainUsageAsync: account usage contract.</summary>
        public Task<UsageCaptureResult?> CaptureCaptainUsageAsync(string captainId, string? harborId = null, CancellationToken token = default) =>
            PostAsync<UsageCaptureResult>("/api/v1/captains/" + Uri.EscapeDataString(captainId) + "/usage/capture", new { HarborId = harborId }, null, token);

        private static string UsagePath(string resource, UsageQuery? query)
        {
            if (query == null) return "/api/v1/usage/" + resource;
            List<string> parts = new List<string>();
            foreach (System.Reflection.PropertyInfo property in typeof(UsageQuery).GetProperties())
            {
                object? value = property.GetValue(query);
                if (value == null) continue;
                string name = property.Name == "FromUtc" ? "from" : property.Name == "ToUtc" ? "to" : Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
                string text = value is List<string> ids ? String.Join(",", ids) : value is DateTime date ? date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture)!;
                parts.Add(name + "=" + Uri.EscapeDataString(text));
            }
            return "/api/v1/usage/" + resource + "?" + String.Join("&", parts);
        }
    }
}
