namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Creates structured immutable decision evidence from one scoped selection snapshot.</summary>
    internal static class UsageSelectionRecordBuilder
    {
        internal static CaptainSelectionRecordSet Build(UsageSelectionSnapshot snapshot, Mission mission, string trigger, bool admitted)
        {
            CaptainSelectionRecordSet records = new CaptainSelectionRecordSet
            {
                Policy = CaptainSelectionPolicyRevision.FromSettings(mission.TenantId!, snapshot.Settings)
            };
            records.AdmissionMissionVersionUtc = mission.LastUpdateUtc;
            records.AdmissionCaptureEnabled = snapshot.CaptureEnabled;
            if (snapshot.ActualCaptainId != null && snapshot.Captains.TryGetValue(snapshot.ActualCaptainId, out Captain? selectedVersion))
            {
                records.AdmissionCaptainVersionUtc = selectedVersion.LastUpdateUtc;
                records.AdmissionCaptainModel = selectedVersion.Model;
            }
            records.AdmissionRecoveryRevision = snapshot.Recovery?.Revision ?? 0;
            CaptainSelectionDecision decision = records.Decision;
            decision.TenantId = mission.TenantId!; decision.UserId = mission.UserId; decision.MissionId = mission.Id; decision.VoyageId = mission.VoyageId;
            decision.ExecutionAttemptId = decision.Id; decision.Trigger = trigger; decision.Mode = snapshot.Settings.Mode;
            decision.Outcome = admitted ? CaptainSelectionOutcomes.Admitted : CaptainSelectionOutcomes.Waiting; decision.SelectedCaptainId = admitted ? snapshot.ActualCaptainId : null;
            if (decision.SelectedCaptainId != null && snapshot.Captains.TryGetValue(decision.SelectedCaptainId, out Captain? actual))
            {
                decision.SelectedModel = Label(actual.Model, 1024);
                if (snapshot.Evidence.TryGetValue(actual.Id, out UsageSelectionAccountEvidence? source)) decision.SelectedProvider = Label(source.Provider, 512);
            }
            decision.HypotheticalCaptainId = snapshot.Settings.Mode == UsageSelectionModeEnum.Observe ? snapshot.Evaluation.SelectedCaptainId : null;
            decision.HypotheticalOutcome = snapshot.Settings.Mode != UsageSelectionModeEnum.Observe ? null : snapshot.Evaluation.SelectedCaptainId == null ? CaptainSelectionOutcomes.Waiting : CaptainSelectionOutcomes.Selected;
            decision.AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion; decision.PolicyRevisionId = records.Policy.Id;
            decision.EvaluatedUtc = snapshot.EvaluatedUtc; decision.PrimaryReasonCode = snapshot.Evaluation.ReasonCode;
            decision.CandidateCount = snapshot.Evaluation.Candidates.Count;
            decision.NextReconsiderUtc = snapshot.Evaluation.SelectedCaptainId == null ? snapshot.Evaluation.Candidates.FirstOrDefault()?.NextReconsiderUtc : null;
            if (snapshot.Recovery != null)
                records.Events.Add(new CaptainSelectionExecutionEvent { TenantId = mission.TenantId!, UserId = mission.UserId, DecisionId = decision.Id,
                    ExecutionAttemptId = decision.ExecutionAttemptId, EventKind = CaptainSelectionEventKinds.RecoveryAuthorizationApplied, ReportingSource = snapshot.Recovery.Id,
                    OccurredUtc = snapshot.EvaluatedUtc, FailureClassification = snapshot.Recovery.ReserveException ? "SoftReserveException" : snapshot.Recovery.AllowFallback ? "FallbackPermission" : "RecoveryPermissionsRevoked" });
            int rank = 0;
            foreach (UsageSelectionCandidateResult result in snapshot.Evaluation.Candidates)
            {
                UsageSelectionCandidate input = result.Candidate;
                Captain captain = snapshot.Captains[input.CaptainId];
                snapshot.Evidence.TryGetValue(captain.Id, out UsageSelectionAccountEvidence? source);
                CaptainSelectionCandidateRecord candidate = new CaptainSelectionCandidateRecord
                {
                    TenantId = mission.TenantId!, UserId = captain.UserId, DecisionId = decision.Id, CaptainId = captain.Id,
                    Provider = Label(input.Provider, 512), ConfiguredModel = Label(input.Model, 1024), CaptainName = Label(captain.Name, 1024), RoutingBucket = input.RoutingBucket, Rank = ++rank,
                    Band = result.Band, NextReconsiderUtc = result.NextReconsiderUtc, PrimaryReasonCode = result.ReasonCode, Eligible = input.IneligibilityReason == null,
                    OccupiedSlots = input.OccupiedSlots, Capacity = input.Capacity, LastAssignedUtc = input.LastAssignedUtc,
                    MembershipId = source?.MembershipId, HostId = source?.HostId, Runtime = source?.Runtime.ToString(), AccountKey = source?.AccountKey,
                    AvailablePercent = UsageSelectionNumbers.Headroom(result.AvailablePercent), EvidenceCapturedUtc = input.CapturedUtc
                };
                records.Candidates.Add(candidate);
                records.Reasons.Add(new CaptainSelectionReason { TenantId = mission.TenantId!, UserId = captain.UserId,
                    DecisionId = decision.Id, CandidateId = candidate.Id, ReasonCode = result.ReasonCode,
                    RequiredPercent = snapshot.Settings.ReservePercent + snapshot.Settings.PerAttemptReservationPercent,
                    AvailablePercent = UsageSelectionNumbers.Headroom(result.AvailablePercent) });
                foreach (UsageSelectionWindowEvidence item in input.Windows)
                {
                    UsageWindow window = item.Window;
                    bool applicable = item.ScopeApplies && item.WindowIdentityConfirmed
                        && (String.IsNullOrWhiteSpace(window.Model) || String.Equals(window.Model.Trim(), input.Model?.Trim(), StringComparison.OrdinalIgnoreCase));
                    string key = UsageBudgetIdentity.WindowKey(window, captain.UsageCaptureMode == UsageCaptureModeEnum.Custom ? source?.ConfigurationRevision : null);
                    records.Windows.Add(new CaptainSelectionWindowRecord
                    {
                        TenantId = mission.TenantId!, UserId = captain.UserId, DecisionId = decision.Id, CandidateId = candidate.Id,
                        SnapshotId = source?.SnapshotId, CapturedUtc = source?.CapturedUtc, ProbeStartedUtc = source?.ProbeStartedUtc,
                        Source = source?.Source.ToString(), Status = source?.Status.ToString(), ConfigurationRevision = source?.ConfigurationRevision,
                        WindowKey = key, WindowName = Label(window.Name, 1024), Model = Label(window.Model, 1024), Scope = Label(window.Scope, 1024),
                        Generation = UsageBudgetIdentity.Generation(window), UsedPercent = Finite(window.UsedPercent).HasValue ? UsageSelectionNumbers.Up(Math.Min(100, window.UsedPercent!.Value)) : null, Used = window.Used, Limit = window.Limit,
                        Unit = Label(window.Unit, 1024), ResetsUtc = window.ResetsUtc, Applicable = applicable,
                        NormalizationCode = Label(window.Name, 1024) != window.Name || Label(window.Model, 1024) != window.Model || Label(window.Scope, 1024) != window.Scope || Label(window.Unit, 1024) != window.Unit || window.UsedPercent > 100 || (Finite(window.UsedPercent).HasValue && UsageSelectionNumbers.Up(Math.Min(100, window.UsedPercent!.Value)) != window.UsedPercent) ? "SourceValuesNormalized" : null,
                        ActiveReservationPercent = Finite(item.ActiveReservationPercent) ?? 0,
                        UnsettledPercent = Finite(item.UnsettledPercent) ?? 0, ReservePercent = snapshot.Settings.ReservePercent
                    });
                    // Observe tracks actual execution, including launches its hypothetical policy would defer.
                    // Unknown/invalid evidence is recorded without fabricating a budget reservation.
                    if (admitted && captain.Id == snapshot.ActualCaptainId && source != null
                        && UsageSelectionEvidenceBuilder.CanReserve(input, item, snapshot.Settings, snapshot.EvaluatedUtc))
                        records.Reservations.Add(new UsageBudgetReservation { TenantId = mission.TenantId!, UserId = mission.UserId,
                            DecisionId = decision.Id, MissionId = mission.Id, ExecutionAttemptId = decision.ExecutionAttemptId, CaptainId = captain.Id,
                            MembershipId = source.MembershipId, HostId = source.HostId, Runtime = source.Runtime.ToString(), AccountKey = source.AccountKey,
                            WindowKey = key, Generation = UsageBudgetIdentity.Generation(window),
                            ReservedPercent = snapshot.Settings.PerAttemptReservationPercent });
                }
            }
            // Exclude capture IDs/times and raw percentages that did not change a decision band or ranking.
            // Actual admissions still get an independent attempt/decision every time.
            string fingerprint = JsonSerializer.Serialize(new object?[] { records.Policy.Fingerprint, snapshot.Recovery?.Id, snapshot.ActualCaptainId,
                snapshot.Evaluation.SelectedCaptainId, snapshot.Evaluation.ReasonCode,
                snapshot.Evaluation.Candidates.Select(r => new object?[] { r.Candidate.CaptainId, r.Candidate.RoutingBucket, r.Band, r.ReasonCode,
                    r.Candidate.OccupiedSlots, r.Candidate.Capacity, r.Candidate.CooldownUntilUtc,
                    r.ReasonCode == "BelowReserve" || r.ReasonCode == "AccountQuotaExhausted" || r.ReasonCode == "AccountCooldown" ? r.NextReconsiderUtc : null,
                    snapshot.Evidence.TryGetValue(r.Candidate.CaptainId, out UsageSelectionAccountEvidence? source) ? source.MembershipId : null,
                    r.Candidate.Windows.Select(w => new object?[] { UsageBudgetIdentity.WindowKey(w.Window), w.WindowIdentityConfirmed, w.ScopeApplies,
                        Finite(w.ActiveReservationPercent), Finite(w.UnsettledPercent) }).ToArray() }).ToArray() });
            decision.EvidenceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))).ToLowerInvariant();
            return records;
        }

        private static string? Label(string? value, int length) => UsageSelectionEvidenceSanitizer.Label(value, length);

        private static double? Finite(double? number) => number.HasValue && Double.IsFinite(number.Value) && number.Value >= 0 ? number : null;
    }
}
