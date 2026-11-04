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
    using Armada.Core.Settings;

    /// <summary>Conservatively reconciles operational uncertainty from Disabled, legacy or incompletely measured executions.</summary>
    public sealed class UsageUntrackedExecutionReconciler
    {
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        /// <summary>Create maintenance over the durable account uncertainty ledger.</summary>
        public UsageUntrackedExecutionReconciler(DatabaseDriver database, ArmadaSettings settings)
        { _Database = database ?? throw new ArgumentNullException(nameof(database)); _Settings = settings ?? throw new ArgumentNullException(nameof(settings)); }

        /// <summary>Process one fair bounded page. Unverified identities stay uncertain rather than acquiring invented reservations.</summary>
        public async Task<int> ReconcileAsync(DateTime nowUtc, int batchSize = 100, CancellationToken token = default)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC clock is required.", nameof(nowUtc));
            UsageSelectionSettings policy = _Settings.EffectiveUsageSelection.Copy();
            List<UsageUntrackedExecution> rows = await _Database.CaptainSelections.ReadUntrackedExecutionsAsync(batchSize, token).ConfigureAwait(false);
            int changed = 0;
            foreach (UsageUntrackedExecution row in rows)
            {
                token.ThrowIfCancellationRequested();
                int version = row.Revision;
                bool running = await _Database.CaptainSelections.IsUntrackedExecutionRunningAsync(row.TenantId, row.MissionId, row.ExecutionKey, token).ConfigureAwait(false);
                if (row.State == UsageReservationStateEnum.Active && !running)
                {
                    if (await _Database.CaptainSelections.ReleaseStoppedUnstartedAsync(row.TenantId, row.ExecutionKey, token).ConfigureAwait(false))
                    { changed++; continue; }
                    row.State = UsageReservationStateEnum.Unsettled; row.ExecutionEndedUtc = nowUtc; row.TransitionReason = "UnreservedExecutionEnded";
                }
                else if (row.State != UsageReservationStateEnum.Active && row.ExecutionEndedUtc.HasValue)
                {
                    DateTime boundary = row.ExecutionEndedUtc.Value.AddSeconds(Math.Max(row.SettlementLagSeconds, policy.SettlementLagSeconds));
                    List<UsageSelectionAccountEvidence> evidence = await _Database.CaptainSelections.ReadAccountEvidenceAsync(row.TenantId, new[] { row.CaptainId }, token).ConfigureAwait(false);
                    UsageSelectionAccountEvidence? source = evidence.Count == 1 ? evidence[0] : null;
                    bool observed = row.AccountAttributionVerified && row.AccountKey != null && row.HostId != null && row.ConfigurationRevision != null && source != null
                        && source.AccountKey == row.AccountKey && source.HostId == row.HostId && source.Runtime.ToString() == row.Runtime
                        && source.ConfigurationRevision == row.ConfigurationRevision && source.ProbeStartedUtc >= boundary
                        && source.CapturedUtc >= source.ProbeStartedUtc && source.CapturedUtc <= nowUtc + UsageQuotaPolicy.MaximumFutureSkew && source.ProbeStartedUtc <= nowUtc
                        && nowUtc - source.CapturedUtc <= TimeSpan.FromSeconds(Math.Min(policy.MaximumFreshnessSeconds, UsageQuotaPolicy.FreshnessHorizon(source.EffectiveIntervalSeconds).TotalSeconds))
                        && source.Status == UsageSnapshotStatusEnum.Ok && source.Source != UsageCaptureSourceEnum.Derived && Enum.IsDefined(typeof(UsageCaptureSourceEnum), source.Source);
                    if (observed && source != null)
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(row.CaptainId, token).ConfigureAwait(false);
                        string? fingerprint = captain?.UsageCaptureMode == UsageCaptureModeEnum.Custom ? UsageAccountResolver.CommandFingerprint(captain.UsageCaptureCommand) : null;
                        string revision = UsageAccountResolver.ConfigurationRevision(source.HostId, source.Runtime, fingerprint);
                        string group = UsageAccountResolver.GroupKey(row.TenantId, source.HostId, source.Runtime, fingerprint);
                        List<UsageWindow> windows = UsageWindowsJson.Deserialize(source.WindowsJson);
                        bool valid = captain?.Runtime == source.Runtime && revision == row.ConfigurationRevision
                            && source.CaptureContextKey == UsageAccountResolver.CaptureContextKey(group, revision) && windows.Count > 0 && windows.All(w => w != null
                                && !String.IsNullOrWhiteSpace(w.Name) && UsageSelectionEvidenceSanitizer.Label(w.Name) == w.Name
                                && (!w.ResetsUtc.HasValue || w.ResetsUtc > nowUtc)
                                && (w.UsedPercent.HasValue ? Double.IsFinite(w.UsedPercent.Value) && w.UsedPercent >= 0 : w.Used >= 0 && w.Limit > 0 && !String.IsNullOrWhiteSpace(w.Unit)));
                        if (valid) { row.State = UsageReservationStateEnum.Settled; row.SettledBySnapshotId = source.SnapshotId; row.TransitionReason = "PostExecutionAccountCapture"; }
                    }
                    if (row.State == UsageReservationStateEnum.Unsettled && nowUtc >= row.ExecutionEndedUtc.Value.AddSeconds(row.MaximumUnsettledSeconds))
                    { row.State = UsageReservationStateEnum.Uncertain; row.TransitionReason = "UnreservedEvidenceUnavailable"; }
                }
                row.LastUpdateUtc = nowUtc;
                if (await _Database.CaptainSelections.UpdateUntrackedExecutionAsync(row, version, token).ConfigureAwait(false)) changed++;
            }
            return changed;
        }
    }
}
