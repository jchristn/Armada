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
    using Armada.Core.Services;
    using Armada.Core.Settings;

    internal sealed partial class CaptainSelectionMethods
    {
        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitEnforcedAsync(CaptainSelectionRecordSet records, string? dockId = null, int ceiling = 256, CancellationToken token = default)
            => AdmitSelectionAsync(records, dockId, ceiling, null, UsageSelectionModeEnum.Enforce, token);

        /// <inheritdoc />
        public Task<CaptainSelectionAdmissionReceipt> TryAdmitEnforcedRepairAsync(CaptainSelectionRecordSet records, DateTime verificationVersion, int ceiling = 256, CancellationToken token = default)
            => AdmitSelectionAsync(records, null, ceiling, verificationVersion, UsageSelectionModeEnum.Enforce, token);

        internal static async Task<bool> ValidateEnforcementAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionRecordSet records, int ceiling, CancellationToken token)
        {
            if (!records.AdmissionMissionVersionUtc.HasValue || !records.AdmissionCaptainVersionUtc.HasValue || String.IsNullOrWhiteSpace(records.AdmissionHostId))
                throw new ArgumentException("Enforcement requires mission and captain version preconditions.", nameof(records));
            CaptainSelectionDecision decision = records.Decision;
            UsageSelectionSettings policy = records.Policy.ToSettings();
            policy.Validate();
            CaptainSelectionCandidateRecord? recorded = records.Candidates.SingleOrDefault(c => c.CaptainId == decision.SelectedCaptainId);
            if (recorded == null || !recorded.Eligible || decision.Outcome != "Admitted" || records.Policy.Mode != UsageSelectionModeEnum.Enforce)
                throw new ArgumentException("Enforced admission requires an eligible selected candidate and matching policy.", nameof(records));
            UsageSelectionRecovery? recovery = await ReadRecoveryAsync(connection, transaction, decision.TenantId, decision.MissionId, token).ConfigureAwait(false);
            if ((recovery?.Revision ?? 0) != records.AdmissionRecoveryRevision) return false;
            if (recovery != null && records.Events.Count(e => e.EventKind == "RecoveryAuthorizationApplied" && e.ReportingSource == recovery.Id) != 1) return false;
            if (recovery == null && records.Events.Any(e => e.EventKind == "RecoveryAuthorizationApplied")) return false;
            DateTime now = DateTime.UtcNow;
            Captain? captain;
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM captains WHERE tenant_id=@tenant AND id=@captain;"))
            {
                Add(command, "@tenant", decision.TenantId); Add(command, "@captain", recorded.CaptainId);
                using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return false;
                if (ParseUtc(reader["last_update_utc"]) != records.AdmissionCaptainVersionUtc.Value) return false;
                captain = new Captain { Id = recorded.CaptainId, TenantId = decision.TenantId,
                    Runtime = Enum.Parse<AgentRuntimeEnum>(Convert.ToString(reader["runtime"], CultureInfo.InvariantCulture)!),
                    UserId = reader["user_id"] == DBNull.Value ? null : reader["user_id"].ToString(),
                    Model = reader["model"] == DBNull.Value ? null : reader["model"].ToString(),
                    MaxConcurrency = Math.Min(ceiling, Convert.ToInt32(reader["max_concurrency"], CultureInfo.InvariantCulture)),
                    ActiveMissionCount = Convert.ToInt32(reader["active_mission_count"], CultureInfo.InvariantCulture),
                    UsageCaptureEnabled = reader["usage_capture_enabled"] == DBNull.Value ? null : Convert.ToBoolean(reader["usage_capture_enabled"], CultureInfo.InvariantCulture),
                    UsageCaptureMode = Enum.Parse<UsageCaptureModeEnum>(reader["usage_capture_mode"].ToString()!),
                    UsageCaptureHarborId = reader["usage_capture_harbor_id"] == DBNull.Value ? null : reader["usage_capture_harbor_id"].ToString(),
                    QuarantineUntilUtc = reader["quarantine_until_utc"] == DBNull.Value ? null : ParseUtc(reader["quarantine_until_utc"]),
                    QuarantineReason = reader["quarantine_reason"] == DBNull.Value ? null : reader["quarantine_reason"].ToString() };
                if (captain.UsageCaptureMode == UsageCaptureModeEnum.Custom)
                    captain.UsageCaptureCommand = reader["usage_capture_command"] == DBNull.Value ? null : reader["usage_capture_command"].ToString();
            }
            if (captain.Model != records.AdmissionCaptainModel || captain.UserId != recorded.UserId || captain.QuarantineUntilUtc > now) return false;
            string[] ids = new[] { captain.Id };
            List<UsageSelectionAccountEvidence> before = await ReadAccountEvidenceAsync(connection, transaction, decision.TenantId, ids, token).ConfigureAwait(false);
            // Lock the same account rows updated by capture commits. Distinct captains on a shared account
            // serialize here, before reading reservations. ReadCommitted prevents MySQL snapshot reads
            // from hiding a reservation committed while this transaction waited for the account row.
            foreach (UsageSelectionAccountEvidence account in before.OrderBy(a => a.HostId, StringComparer.Ordinal).ThenBy(a => a.Runtime).ThenBy(a => a.AccountKey, StringComparer.Ordinal))
            {
                using DbCommand locked = Command(connection, transaction, "UPDATE usage_latest_state SET last_good_snapshot_id=last_good_snapshot_id WHERE tenant_id=@tenant AND host_id=@host AND runtime=@runtime AND account_key=@account;");
                Add(locked, "@tenant", decision.TenantId); Add(locked, "@host", account.HostId); Add(locked, "@runtime", account.Runtime.ToString()); Add(locked, "@account", account.AccountKey);
                await locked.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            List<UsageSelectionAccountEvidence> after = await ReadAccountEvidenceAsync(connection, transaction, decision.TenantId, ids, token).ConfigureAwait(false);
            if (!before.Select(a => a.MembershipId).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(after.Select(a => a.MembershipId).OrderBy(id => id, StringComparer.Ordinal))) return false;
            UsageSelectionAccountEvidence? source = after.Count == 1 ? after[0] : null;
            if (source?.MembershipId != recorded.MembershipId || source?.HostId != recorded.HostId || source?.AccountKey != recorded.AccountKey) return false;
            List<UsageSelectionBudgetTotals> totals = await ReadBudgetTotalsAsync(connection, transaction, decision.TenantId, ids, token).ConfigureAwait(false);
            UsageSelectionCandidate input = new UsageSelectionCandidate { CaptainId = captain.Id, Model = captain.Model, Capacity = captain.MaxConcurrency,
                OccupiedSlots = captain.ActiveMissionCount, HasAuthoritativeQuotaHold = captain.QuarantineUntilUtc > now && UsageQuotaHold.IsQuotaOwned(captain.QuarantineReason) };
            if (source != null) UsageSelectionEvidenceBuilder.Apply(input, captain, source, totals, decision.TenantId, records.AdmissionCaptureEnabled);
            if (input.IdentityVerified && input.ConfigurationMatches && source?.HostId != records.AdmissionHostId) return false;
            UsageSelectionCandidateResult actual = UsageSelectionPolicy.Evaluate(new[] { input }, policy, now, recovery?.ReserveException == true).Candidates.Single();
            if (!actual.CanSelect || actual.Band != recorded.Band || actual.ReasonCode != recorded.PrimaryReasonCode || UsageSelectionNumbers.Headroom(actual.AvailablePercent) != recorded.AvailablePercent) return false;
            List<CaptainSelectionWindowRecord> windows = records.Windows.Where(w => w.CandidateId == recorded.Id).ToList();
            if (windows.Count != input.Windows.Count) return false;
            List<string> expectedReservations = new List<string>();
            foreach (UsageSelectionWindowEvidence evidence in input.Windows)
            {
                string key = UsageBudgetIdentity.WindowKey(evidence.Window, captain.UsageCaptureMode == UsageCaptureModeEnum.Custom ? source?.ConfigurationRevision : null);
                CaptainSelectionWindowRecord? original = windows.SingleOrDefault(w => w.WindowKey == key);
                if (original == null || original.SnapshotId != source?.SnapshotId || original.CapturedUtc != source?.CapturedUtc
                    || original.ActiveReservationPercent != evidence.ActiveReservationPercent || original.UnsettledPercent != evidence.UnsettledPercent) return false;
                if (UsageSelectionEvidenceBuilder.CanReserve(input, evidence, policy, now)) expectedReservations.Add(key);
            }
            if (!expectedReservations.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(records.Reservations.Select(r => r.WindowKey).OrderBy(key => key, StringComparer.Ordinal))) return false;
            return records.Reservations.All(row => row.CaptainId == captain.Id && row.MembershipId == source?.MembershipId && row.HostId == source?.HostId
                && row.Runtime == source?.Runtime.ToString() && row.AccountKey == source?.AccountKey && row.Generation == UsageBudgetIdentity.ContinuousGeneration
                && row.ReservedPercent == policy.PerAttemptReservationPercent && row.State == UsageReservationStateEnum.Active && row.ExecutionStartedUtc == null);
        }
    }
}
