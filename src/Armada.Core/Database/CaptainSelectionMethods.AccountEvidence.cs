namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<List<UsageSelectionBudgetTotals>> ReadBudgetTotalsAsync(string tenantId, IReadOnlyList<string> captainIds, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); ArgumentNullException.ThrowIfNull(captainIds);
            if (captainIds.Count > 1000) throw new ArgumentException("Budget evidence is read in batches of at most 1000 captains.", nameof(captainIds));
            if (captainIds.Count == 0) return Task.FromResult(new List<UsageSelectionBudgetTotals>());
            return RunAsync(async connection =>
            {
                string[] names = Enumerable.Range(0, captainIds.Count).Select(i => "@captain" + i).ToArray();
                using DbCommand command = Command(connection, null, @"SELECT r.host_id,r.runtime,r.account_key,r.window_key,r.generation_key,
                    SUM(CASE WHEN r.state='Active' THEN r.reserved_percent ELSE 0 END) AS active_percent,
                    SUM(CASE WHEN r.state='Unsettled' THEN r.reserved_percent ELSE 0 END) AS unsettled_percent,
                    MAX(CASE WHEN r.state='Uncertain' THEN 1 ELSE 0 END) AS uncertain
                    FROM usage_budget_reservations r WHERE r.tenant_id=@tenant AND r.state IN ('Active','Unsettled','Uncertain')
                    AND EXISTS (SELECT 1 FROM usage_account_memberships m WHERE m.tenant_id=r.tenant_id AND m.host_id=r.host_id AND m.runtime=r.runtime AND m.account_key=r.account_key AND m.effective_to_utc IS NULL AND m.captain_id IN ("
                    + String.Join(",", names) + ")) GROUP BY r.host_id,r.runtime,r.account_key,r.window_key,r.generation_key;");
                Add(command, "@tenant", tenantId);
                for (int index = 0; index < names.Length; index++) Add(command, names[index], captainIds[index]);
                List<UsageSelectionBudgetTotals> rows = new List<UsageSelectionBudgetTotals>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(new UsageSelectionBudgetTotals
                {
                    HostId = reader["host_id"].ToString()!, Runtime = reader["runtime"].ToString()!, AccountKey = reader["account_key"].ToString()!,
                    WindowKey = reader["window_key"].ToString()!, Generation = reader["generation_key"].ToString()!,
                    ActivePercent = Convert.ToDouble(reader["active_percent"], CultureInfo.InvariantCulture),
                    UnsettledPercent = Convert.ToDouble(reader["unsettled_percent"], CultureInfo.InvariantCulture),
                    HasUncertainDebt = Convert.ToInt32(reader["uncertain"], CultureInfo.InvariantCulture) != 0
                });
                return rows;
            }, token);
        }

        /// <inheritdoc />
        public Task<List<UsageSelectionAccountEvidence>> ReadAccountEvidenceAsync(string tenantId, IReadOnlyList<string> captainIds, CancellationToken token = default)
        {
            Required(tenantId, nameof(tenantId)); ArgumentNullException.ThrowIfNull(captainIds);
            if (captainIds.Count > 1000) throw new ArgumentException("Account evidence is read in batches of at most 1000 captains.", nameof(captainIds));
            if (captainIds.Count == 0) return Task.FromResult(new List<UsageSelectionAccountEvidence>());
            foreach (string id in captainIds) Required(id, nameof(captainIds));
            return RunAsync(async connection =>
            {
                string[] names = Enumerable.Range(0, captainIds.Count).Select(i => "@captain" + i).ToArray();
                using DbCommand command = Command(connection, null, @"SELECT m.id AS membership_id,m.captain_id,m.host_id,m.runtime,m.account_key,m.provider,m.configuration_revision,m.effective_from_utc,
                    l.capture_context_key,l.last_good_snapshot_id,l.last_good_captured_utc,l.last_good_status,l.effective_interval_seconds,
                    s.source,s.windows_json,s.probe_started_utc
                    FROM usage_account_memberships m
                    LEFT JOIN usage_latest_state l ON l.tenant_id=m.tenant_id AND l.host_id=m.host_id AND l.runtime=m.runtime AND l.account_key=m.account_key AND l.expired_utc IS NULL
                    LEFT JOIN usage_snapshots s ON s.id=l.last_good_snapshot_id AND s.tenant_id=m.tenant_id AND s.host_id=m.host_id AND s.runtime=m.runtime AND s.account_key=m.account_key
                    WHERE m.tenant_id=@tenant AND m.effective_to_utc IS NULL AND m.captain_id IN (" + String.Join(",", names) + ") ORDER BY m.captain_id,m.effective_from_utc DESC,m.id DESC;");
                Add(command, "@tenant", tenantId);
                for (int index = 0; index < names.Length; index++) Add(command, names[index], captainIds[index]);
                List<UsageSelectionAccountEvidence> rows = new List<UsageSelectionAccountEvidence>();
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                string? Text(string name) => reader[name] == DBNull.Value ? null : Convert.ToString(reader[name], CultureInfo.InvariantCulture);
                DateTime? Date(string name) => reader[name] == DBNull.Value ? null : reader[name] is DateTime value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : ParseUtc(reader[name]);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    rows.Add(new UsageSelectionAccountEvidence
                    {
                        CaptainId = Text("captain_id")!, MembershipId = Text("membership_id")!, HostId = Text("host_id")!,
                        Runtime = Enum.Parse<AgentRuntimeEnum>(Text("runtime")!, false), AccountKey = Text("account_key")!, Provider = Text("provider"),
                        ConfigurationRevision = Text("configuration_revision"), CaptureContextKey = Text("capture_context_key"),
                        SnapshotId = Text("last_good_snapshot_id"), CapturedUtc = Date("last_good_captured_utc"), ProbeStartedUtc = Date("probe_started_utc"),
                        EffectiveFromUtc = Date("effective_from_utc")!.Value,
                        Status = Enum.TryParse(Text("last_good_status"), out UsageSnapshotStatusEnum status) ? status : UsageSnapshotStatusEnum.Failed,
                        Source = Enum.TryParse(Text("source"), out UsageCaptureSourceEnum source) ? source : UsageCaptureSourceEnum.Derived,
                        WindowsJson = Text("windows_json"),
                        EffectiveIntervalSeconds = reader["effective_interval_seconds"] == DBNull.Value ? 60 : Convert.ToInt32(reader["effective_interval_seconds"], CultureInfo.InvariantCulture)
                    });
                }
                return rows;
            }, token);
        }
    }
}
