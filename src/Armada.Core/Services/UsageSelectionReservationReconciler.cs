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

    /// <summary>Bounded, restart-safe reconciliation of usage estimates against current attempt and capture evidence.</summary>
    public sealed class UsageSelectionReservationReconciler
    {
        private readonly DatabaseDriver _Database;

        /// <summary>Create server-side reconciliation over the persistent ledger.</summary>
        public UsageSelectionReservationReconciler(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>Process an oldest-checked page. Unproven settlement remains debt; timestamps alone never imply rollover.</summary>
        public async Task<int> ReconcileAsync(DateTime nowUtc, int batchSize = 100, CancellationToken token = default)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC clock is required.", nameof(nowUtc));
            List<UsageBudgetReservation> rows = await _Database.CaptainSelections.ReadReconciliationBatchAsync(batchSize, token).ConfigureAwait(false);
            int changed = 0;
            foreach (UsageBudgetReservation row in rows)
            {
                token.ThrowIfCancellationRequested();
                // A different/new unrecorded attempt must not keep an older attempt's estimate active.
                CaptainSelectionDecision? current = await _Database.CaptainSelections.ReadCurrentDecisionAsync(row.TenantId, row.MissionId, token: token).ConfigureAwait(false);
                Mission? mission = await _Database.Missions.ReadAsync(row.MissionId, token).ConfigureAwait(false);
                CaptainSelectionDecision? decision = await _Database.CaptainSelections.ReadDecisionAsync(row.TenantId, row.DecisionId, token: token).ConfigureAwait(false);
                CaptainSelectionPolicyRevision? policy = decision == null ? null : await _Database.CaptainSelections.ReadPolicyAsync(row.TenantId, decision.PolicyRevisionId, token).ConfigureAwait(false);
                if (policy == null)
                {
                    await TouchAsync(row, nowUtc, token).ConfigureAwait(false);
                    continue;
                }
                bool running = mission?.TenantId == row.TenantId && current?.Id == row.DecisionId
                    && (mission.Status == MissionStatusEnum.Assigned || mission.Status == MissionStatusEnum.InProgress);
                if (row.State == UsageReservationStateEnum.Active && !running)
                {
                    if (await _Database.CaptainSelections.ReleaseStoppedUnstartedAsync(row.TenantId, row.ExecutionAttemptId, token).ConfigureAwait(false))
                    { changed++; continue; }
                    if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                        UsageReservationStateEnum.Unsettled, nowUtc, "AttemptNoLongerExecuting", token: token).ConfigureAwait(false)) changed++;
                    continue;
                }
                if (row.State != UsageReservationStateEnum.Active && row.ExecutionEndedUtc.HasValue)
                {
                    DateTime eligible = row.ExecutionEndedUtc.Value.AddSeconds(policy.SettlementLagSeconds);
                    List<UsageSelectionAccountEvidence> evidence = await _Database.CaptainSelections.ReadAccountEvidenceAsync(row.TenantId, new[] { row.CaptainId }, token).ConfigureAwait(false);
                    // Reassignment, credential/config changes or ambiguous identity require operator-visible uncertainty,
                    // never settlement against a different account that happens to have the same captain name.
                    UsageSelectionAccountEvidence? account = evidence.Count == 1 ? evidence[0] : null;
                    bool provesExecution = account != null && account.MembershipId == row.MembershipId && account.HostId == row.HostId
                        && account.Runtime.ToString() == row.Runtime && account.AccountKey == row.AccountKey
                        && account.Status == UsageSnapshotStatusEnum.Ok && account.Source != UsageCaptureSourceEnum.Derived
                        && account.CapturedUtc >= eligible && account.ProbeStartedUtc >= eligible && account.ProbeStartedUtc <= account.CapturedUtc && account.ProbeStartedUtc <= nowUtc
                        && account.CapturedUtc <= nowUtc + UsageQuotaPolicy.MaximumFutureSkew
                        && nowUtc - account.CapturedUtc <= TimeSpan.FromSeconds(Math.Min(policy.MaximumFreshnessSeconds,
                            UsageQuotaPolicy.FreshnessHorizon(account.EffectiveIntervalSeconds).TotalSeconds));
                    if (provesExecution && account != null)
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(row.CaptainId, token).ConfigureAwait(false);
                        string? custom = captain?.UsageCaptureMode == UsageCaptureModeEnum.Custom ? account.ConfigurationRevision : null;
                        string? fingerprint = captain?.UsageCaptureMode == UsageCaptureModeEnum.Custom ? UsageAccountResolver.CommandFingerprint(captain.UsageCaptureCommand) : null;
                        string revision = UsageAccountResolver.ConfigurationRevision(account.HostId, account.Runtime, fingerprint);
                        string group = UsageAccountResolver.GroupKey(row.TenantId, account.HostId, account.Runtime, fingerprint);
                        bool sameConfiguration = captain != null && captain.Runtime == account.Runtime && account.ConfigurationRevision == revision
                            && account.CaptureContextKey == UsageAccountResolver.CaptureContextKey(group, revision);
                        List<UsageWindow> matches = UsageWindowsJson.Deserialize(account.WindowsJson)
                            .Where(w => w != null && UsageBudgetIdentity.WindowKey(w, custom) == row.WindowKey).ToList();
                        UsageWindow? window = matches.Count == 1 ? matches[0] : null;
                        bool valid = window != null && (window.UsedPercent.HasValue
                            ? Double.IsFinite(window.UsedPercent.Value) && window.UsedPercent >= 0
                            : window.Used >= 0 && window.Limit > 0 && !String.IsNullOrWhiteSpace(window.Unit));
                        if (sameConfiguration && valid && (!window!.ResetsUtc.HasValue || window.ResetsUtc > nowUtc))
                        {
                            if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                                UsageReservationStateEnum.Settled, nowUtc, "PostExecutionCapture", account.SnapshotId, token).ConfigureAwait(false)) changed++;
                            continue;
                        }
                    }
                    if (row.State == UsageReservationStateEnum.Unsettled && nowUtc >= row.ExecutionEndedUtc.Value.AddSeconds(policy.MaximumUnsettledSeconds))
                    {
                        if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                            UsageReservationStateEnum.Uncertain, nowUtc, "PostExecutionEvidenceUnavailable", token: token).ConfigureAwait(false)) changed++;
                        continue;
                    }
                }
                await TouchAsync(row, nowUtc, token).ConfigureAwait(false);
            }
            return changed;
        }

        private Task<bool> TouchAsync(UsageBudgetReservation row, DateTime nowUtc, CancellationToken token)
            => _Database.CaptainSelections.TouchReservationAsync(row.TenantId, row.Id, row.Revision, nowUtc, token);
    }
}
