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

        /// <summary>
        /// Process an oldest-checked page. Unproven settlement remains debt. Rollover is never inferred from timestamps alone:
        /// only a fixed-generation reservation whose execution ended before the reset closes, and only after a fresh capture
        /// that began after the reset reports a newer generation of the same window for the same account and configuration.
        /// </summary>
        public async Task<int> ReconcileAsync(DateTime nowUtc, int batchSize = 100, CancellationToken token = default)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC clock is required.", nameof(nowUtc));
            using CaptainSelectionStage stage = CaptainSelectionTelemetry.Stage("reconcile");
            List<UsageBudgetReservation> rows = await _Database.CaptainSelections.ReadReconciliationBatchAsync(batchSize, token).ConfigureAwait(false);
            Dictionary<string, int> outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
            // Rows in one batch often share a policy revision, captain and account; read each once per pass.
            Dictionary<string, CaptainSelectionPolicyRevision?> policies = new Dictionary<string, CaptainSelectionPolicyRevision?>(StringComparer.Ordinal);
            Dictionary<string, List<UsageSelectionAccountEvidence>> evidenceByCaptain = new Dictionary<string, List<UsageSelectionAccountEvidence>>(StringComparer.Ordinal);
            Dictionary<string, Captain?> captains = new Dictionary<string, Captain?>(StringComparer.Ordinal);
            int changed = 0;
            foreach (UsageBudgetReservation row in rows)
            {
                token.ThrowIfCancellationRequested();
                // A different/new unrecorded attempt must not keep an older attempt's estimate active.
                CaptainSelectionDecision? current = await _Database.CaptainSelections.ReadCurrentDecisionAsync(row.TenantId, row.MissionId, token: token).ConfigureAwait(false);
                Mission? mission = await _Database.Missions.ReadAsync(row.MissionId, token).ConfigureAwait(false);
                CaptainSelectionDecision? decision = await _Database.CaptainSelections.ReadDecisionAsync(row.TenantId, row.DecisionId, token: token).ConfigureAwait(false);
                CaptainSelectionPolicyRevision? policy = null;
                if (decision != null)
                {
                    string policyKey = row.TenantId + "\n" + decision.PolicyRevisionId;
                    if (!policies.TryGetValue(policyKey, out policy))
                    {
                        policy = await _Database.CaptainSelections.ReadPolicyAsync(row.TenantId, decision.PolicyRevisionId, token).ConfigureAwait(false);
                        policies[policyKey] = policy;
                    }
                }
                if (policy == null)
                {
                    await TouchAsync(row, nowUtc, token).ConfigureAwait(false);
                    Count(outcomes, "Unchanged");
                    continue;
                }
                bool running = mission?.TenantId == row.TenantId && current?.Id == row.DecisionId
                    && (mission.Status == MissionStatusEnum.Assigned || mission.Status == MissionStatusEnum.InProgress);
                if (row.State == UsageReservationStateEnum.Active && !running)
                {
                    if (await _Database.CaptainSelections.ReleaseStoppedUnstartedAsync(row.TenantId, row.ExecutionAttemptId, token).ConfigureAwait(false))
                    { changed++; Count(outcomes, "Released"); continue; }
                    if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                        UsageReservationStateEnum.Unsettled, nowUtc, "AttemptNoLongerExecuting", token: token).ConfigureAwait(false)) { changed++; Count(outcomes, "Unsettled"); }
                    continue;
                }
                if (row.State != UsageReservationStateEnum.Active && row.ExecutionEndedUtc.HasValue)
                {
                    DateTime eligible = row.ExecutionEndedUtc.Value.AddSeconds(policy.SettlementLagSeconds);
                    string captainKey = row.TenantId + "\n" + row.CaptainId;
                    if (!evidenceByCaptain.TryGetValue(captainKey, out List<UsageSelectionAccountEvidence>? evidence))
                    {
                        evidence = await _Database.CaptainSelections.ReadAccountEvidenceAsync(row.TenantId, new[] { row.CaptainId }, token).ConfigureAwait(false);
                        evidenceByCaptain[captainKey] = evidence;
                    }
                    // Reassignment, credential/config changes or ambiguous identity require operator-visible uncertainty,
                    // never settlement against a different account that happens to have the same captain name.
                    UsageSelectionAccountEvidence? account = evidence.Count == 1 ? evidence[0] : null;
                    bool identityProven = account != null && account.MembershipId == row.MembershipId && account.HostId == row.HostId
                        && account.Runtime.ToString() == row.Runtime && account.AccountKey == row.AccountKey
                        && account.Status == UsageSnapshotStatusEnum.Ok && account.Source != UsageCaptureSourceEnum.Derived
                        && account.ProbeStartedUtc <= account.CapturedUtc + UsageQuotaPolicy.CaptureTimestampResolution && account.ProbeStartedUtc <= nowUtc
                        && account.CapturedUtc <= nowUtc + UsageQuotaPolicy.MaximumFutureSkew
                        && nowUtc - account.CapturedUtc <= TimeSpan.FromSeconds(Math.Min(policy.MaximumFreshnessSeconds,
                            UsageQuotaPolicy.FreshnessHorizon(account.EffectiveIntervalSeconds).TotalSeconds));
                    bool provesExecution = identityProven && account!.CapturedUtc >= eligible && account.ProbeStartedUtc >= eligible;
                    // A fixed window that reset after this execution ended discards everything it consumed. A capture that began
                    // after the reset and reports a newer generation of the same window proves it, so the old debt closes.
                    bool fixedReset = UsageBudgetIdentity.TryReadFixedReset(row.Generation, out DateTime resetUtc)
                        && nowUtc >= resetUtc && row.ExecutionEndedUtc.Value <= resetUtc;
                    bool provesReset = identityProven && fixedReset && account!.ProbeStartedUtc >= resetUtc;
                    if ((provesExecution || provesReset) && account != null)
                    {
                        if (!captains.TryGetValue(row.CaptainId, out Captain? captain))
                        {
                            captain = await _Database.Captains.ReadAsync(row.CaptainId, token).ConfigureAwait(false);
                            captains[row.CaptainId] = captain;
                        }
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
                            if (provesExecution)
                            {
                                if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                                    UsageReservationStateEnum.Settled, nowUtc, "PostExecutionCapture", account.SnapshotId, token).ConfigureAwait(false)) { changed++; Count(outcomes, "Settled"); }
                                continue;
                            }
                            if (UsageBudgetIdentity.TryReadFixedReset(UsageBudgetIdentity.Generation(window), out DateTime nextReset) && nextReset > resetUtc)
                            {
                                if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                                    UsageReservationStateEnum.Settled, nowUtc, "WindowReset", account.SnapshotId, token).ConfigureAwait(false)) { changed++; Count(outcomes, "WindowReset"); }
                                continue;
                            }
                        }
                    }
                    if (row.State == UsageReservationStateEnum.Unsettled && nowUtc >= row.ExecutionEndedUtc.Value.AddSeconds(policy.MaximumUnsettledSeconds))
                    {
                        if (await _Database.CaptainSelections.TryTransitionReservationAsync(row.TenantId, row.Id, row.Revision, row.State,
                            UsageReservationStateEnum.Uncertain, nowUtc, "PostExecutionEvidenceUnavailable", token: token).ConfigureAwait(false)) { changed++; Count(outcomes, "Uncertain"); }
                        continue;
                    }
                }
                await TouchAsync(row, nowUtc, token).ConfigureAwait(false);
                Count(outcomes, "Unchanged");
            }
            foreach (KeyValuePair<string, int> outcome in outcomes) CaptainSelectionTelemetry.Reconciled(outcome.Key, outcome.Value);
            CaptainSelectionTelemetry.Reservations(await _Database.CaptainSelections.CountReservationsByStateAsync(token).ConfigureAwait(false));
            stage.Complete(changed > 0 ? "Changed" : "Unchanged");
            return changed;
        }

        private static void Count(Dictionary<string, int> outcomes, string outcome)
            => outcomes[outcome] = outcomes.TryGetValue(outcome, out int count) ? count + 1 : 1;

        private Task<bool> TouchAsync(UsageBudgetReservation row, DateTime nowUtc, CancellationToken token)
            => _Database.CaptainSelections.TouchReservationAsync(row.TenantId, row.Id, row.Revision, nowUtc, token);
    }
}
