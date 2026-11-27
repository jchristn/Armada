namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>Tenant-scoped account usage queries and capture receipts.</summary>
    public sealed class UsageClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal UsageClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        /// <summary>GetSnapshotsAsync: account usage contract.</summary>
        [DashboardExport("getUsageSnapshots")]
        public Task<EnumerationResult<UsageSnapshotView>?> GetSnapshotsAsync(UsageQuery? query = null, CancellationToken token = default) =>
            _Transport.GetAsync<EnumerationResult<UsageSnapshotView>>(UsagePath("snapshots", query), null, token);
        /// <summary>GetLatestAsync: account usage contract.</summary>
        [DashboardExport("getUsageLatest")]
        public Task<EnumerationResult<UsageSnapshotView>?> GetLatestAsync(UsageQuery? query = null, CancellationToken token = default) =>
            _Transport.GetAsync<EnumerationResult<UsageSnapshotView>>(UsagePath("latest", query), null, token);
        /// <summary>GetSeriesAsync: account usage contract.</summary>
        [DashboardExport("getUsageSeries")]
        public Task<UsageSeriesResult?> GetSeriesAsync(UsageQuery query, CancellationToken token = default) =>
            _Transport.GetAsync<UsageSeriesResult>(UsagePath("series", query), null, token);
        /// <summary>GetMissionsAsync: account usage contract.</summary>
        [DashboardExport("getUsageMissions")]
        public Task<UsageMissionResult?> GetMissionsAsync(UsageQuery query, CancellationToken token = default) =>
            _Transport.GetAsync<UsageMissionResult>(UsagePath("missions", query), null, token);
        /// <summary>CaptureCaptainAsync: account usage contract.</summary>
        [DashboardExport("captureCaptainUsage")]
        public Task<UsageCaptureResult?> CaptureCaptainAsync(string captainId, string? harborId = null, CancellationToken token = default) =>
            _Transport.PostAsync<UsageCaptureResult>("/api/v1/captains/" + Uri.EscapeDataString(captainId) + "/usage/capture", new { HarborId = harborId }, null, token);

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
