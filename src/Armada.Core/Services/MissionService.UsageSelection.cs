namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    public partial class MissionService
    {
        /// <summary>Stable Admiral credential-host identity. Required for local usage enforcement; the server supplies its protected usage identity.</summary>
        public string? UsageSelectionLocalHostId { get; set; }

        // Mission ID to the execution attempt whose reservation its pending no-op redispatch takes over.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _NoOpRedispatchCarry =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, UsageSelectionTransientBlocker> _UsageSelectionBlockers =
            new System.Collections.Concurrent.ConcurrentDictionary<string, UsageSelectionTransientBlocker>(StringComparer.Ordinal);

        /// <inheritdoc />
        public void ReconsiderCaptainSelection(Mission mission)
        {
            ArgumentNullException.ThrowIfNull(mission);
            _UsageSelectionBlockers.TryRemove(mission.Id, out _);
            NotifySelectionChanged(mission, null, CaptainSelectionOutcomes.RecoveryAuthorized);
        }

        // A mission pinned to a captain that cannot run now says what an administrator can authorize: fallback to another
        // captain (when others were excluded only for needing that permission) or a soft-reserve exception for the pinned one.
        private static MissionAssignmentBlocker AddRecoveryAdvice(MissionAssignmentBlocker blocker, Mission mission, UsageSelectionSnapshot usage)
        {
            if (usage.Settings.Mode != UsageSelectionModeEnum.Enforce || String.IsNullOrEmpty(mission.RequestedCaptainId)) return blocker;
            if (mission.UsageRecovery?.AllowFallback != true && usage.Evaluation.Candidates.Any(c => c.ReasonCode == UsageSelectionReasonCodes.FallbackPermissionRequired))
                blocker.Advice.Add(new AssignmentBlockerAdvice { Text = "An administrator can allow fallback to another eligible captain in this mission's captain-selection recovery permissions.",
                    Action = AssignmentBlockerActionEnum.OpenMission, TargetType = "mission", TargetId = mission.Id });
            UsageSelectionCandidateResult? pinned = usage.Evaluation.Candidates.FirstOrDefault(c => c.Candidate.CaptainId == mission.RequestedCaptainId);
            if (mission.UsageRecovery?.ReserveException != true && pinned?.ReasonCode == UsageSelectionReasonCodes.BelowReserve)
                blocker.Advice.Add(new AssignmentBlockerAdvice { Text = "An administrator can grant the requested captain a soft-reserve exception in this mission's captain-selection recovery permissions.",
                    Action = AssignmentBlockerActionEnum.OpenMission, TargetType = "mission", TargetId = mission.Id });
            return blocker;
        }

        private void NotifySelectionChanged(Mission mission, string? decisionId, string outcome)
        {
            if (OnCaptainSelectionChanged == null || String.IsNullOrEmpty(mission.TenantId)) return;
            try
            {
                OnCaptainSelectionChanged(new CaptainSelectionChange { TenantId = mission.TenantId, MissionId = mission.Id,
                    VoyageId = mission.VoyageId, DecisionId = decisionId, Outcome = outcome });
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "selection change notification failed for mission " + mission.Id + ": " + ex.Message);
            }
        }

        private string UsagePolicyKey() => System.Text.Json.JsonSerializer.Serialize(_Settings.EffectiveUsageSelection);

        private bool UsageRetryPending(Mission mission)
        {
            return _UsageSelectionBlockers.TryGetValue(mission.Id, out UsageSelectionTransientBlocker? failure)
                && failure.PolicyKey == UsagePolicyKey() && failure.RetryUtc > DateTime.UtcNow;
        }

        private void UsageWait(Mission mission, string code, string summary)
        {
            NoteWait(mission, MissionWaitGateEnum.CaptainSlot, code);
            if (_UsageSelectionBlockers.Count >= _MaxEnvelopeDecisionsTracked)
            {
                // Evict the blocker whose retry is due soonest; it is the least valuable to keep.
                string? oldest = _UsageSelectionBlockers.OrderBy(pair => pair.Value.RetryUtc).Select(pair => pair.Key).FirstOrDefault();
                if (oldest != null) _UsageSelectionBlockers.TryRemove(oldest, out _);
            }
            _UsageSelectionBlockers[mission.Id] = new UsageSelectionTransientBlocker
            {
                PolicyKey = UsagePolicyKey(), RetryUtc = DateTime.UtcNow.AddSeconds(_Settings.EffectiveUsageSelection.UnknownRetrySeconds),
                Blocker = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.NoEligibleCaptain,
                    SelectionReasonCode = code, Summary = summary }
            };
        }

        /// <summary>Read-only current-state selection preview using shared readiness, eligibility and routing rules.</summary>
        /// <param name="mission">An already-authorized mission; its tenant/user scope bounds candidate evidence.</param>
        /// <param name="pageNumber">One-based comparison page.</param>
        /// <param name="pageSize">Comparison rows, 1..1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Authorized candidate explanations without account keys or writes.</returns>
        public async Task<CaptainSelectionPreview> PreviewCaptainSelectionAsync(Mission mission, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mission);
            if (pageNumber < 1 || pageSize < 1 || pageSize > 1000 || (long)(pageNumber - 1) * pageSize > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be positive and page size between 1 and 1000.");
            UsageSelectionSettings settings = _Settings.EffectiveUsageSelection.Copy();
            CaptainSelectionPreview preview = new CaptainSelectionPreview { MissionId = mission.Id, Mode = settings.Mode,
                Strategy = settings.Strategy, AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion, EvaluatedUtc = DateTime.UtcNow };
            string? readiness = null;
            if (mission.Status != MissionStatusEnum.Pending) readiness = UsageSelectionReasonCodes.MissionNotPending;
            else if (String.IsNullOrEmpty(mission.TenantId)) readiness = UsageSelectionReasonCodes.MissionScopeUnavailable;
            else if (_LaunchFailures.TryGetValue(mission.Id, out MissionLaunchFailure? backoff) && DateTime.UtcNow < backoff.NextAttemptUtc
                && !await HasUntriedIdleCaptainAsync(mission, backoff, token).ConfigureAwait(false)) readiness = UsageSelectionReasonCodes.LaunchRetryPending;
            else if (!String.IsNullOrWhiteSpace(mission.VoyageId))
            {
                Voyage? voyage = await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                if (voyage?.Status == VoyageStatusEnum.Complete || voyage?.Status == VoyageStatusEnum.Cancelled || voyage?.Status == VoyageStatusEnum.Failed)
                    readiness = UsageSelectionReasonCodes.VoyageNotRunnable;
            }
            if (readiness == null)
            {
                Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel == null) readiness = UsageSelectionReasonCodes.VesselMissing;
                else if (UsesSharedLocalAndWorkingDirectory(vessel)) readiness = UsageSelectionReasonCodes.VesselMisconfigured;
                else
                {
                    MissionAssignmentBlocker? blocker = (await EvaluateReadinessAdmissionAsync(mission, token).ConfigureAwait(false)).Blocker
                        ?? await EvaluateSafetyAdmissionAsync(mission, vessel, token).ConfigureAwait(false)
                        ?? await EvaluateGlobalAdmissionAsync(token).ConfigureAwait(false);
                    readiness = blocker?.Reason.ToString();
                }
            }
            if (readiness != null)
            {
                preview.ReasonCode = readiness;
                preview.Candidates = EnumerationResult<CaptainSelectionCandidateView>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, new List<CaptainSelectionCandidateView>(), 0);
                return preview;
            }
            CaptainAdmissionSelection selection = await SelectCaptainAdmissionAsync(mission, token).ConfigureAwait(false);
            UsageSelectionSnapshot snapshot = await BuildUsageSelectionAsync(selection, settings, token).ConfigureAwait(false);
            if (settings.Mode == UsageSelectionModeEnum.Enforce) snapshot.ActualCaptainId = snapshot.Evaluation.SelectedCaptainId;
            bool placementWaiting = snapshot.ActualCaptainId != null && CanAssignMissionAsync != null
                && !await CanAssignMissionAsync(selection.Mission, snapshot.Captains[snapshot.ActualCaptainId]).ConfigureAwait(false);
            if (placementWaiting) snapshot.ActualCaptainId = null;
            preview.EvaluatedUtc = snapshot.EvaluatedUtc; preview.ActualCaptainId = snapshot.ActualCaptainId;
            preview.HypotheticalCaptainId = snapshot.Evaluation.SelectedCaptainId; preview.ReasonCode = placementWaiting ? UsageSelectionReasonCodes.PlacementWaiting : snapshot.Evaluation.ReasonCode;
            List<CaptainSelectionCandidateView> rows = snapshot.Evaluation.Candidates.Select((result, index) => new CaptainSelectionCandidateView
            {
                CaptainId = result.Candidate.CaptainId, CaptainAvailable = true, CaptainName = UsageSelectionEvidenceSanitizer.Label(snapshot.Captains[result.Candidate.CaptainId].Name)!,
                Model = UsageSelectionEvidenceSanitizer.Label(result.Candidate.Model), Band = result.Band, ReasonCode = result.ReasonCode, CanSelect = result.CanSelect,
                AvailablePercent = result.AvailablePercent, CapturedUtc = result.Candidate.CapturedUtc,
                NextReconsiderUtc = result.NextReconsiderUtc, Rank = index + 1,
                ActualSelection = result.Candidate.CaptainId == snapshot.ActualCaptainId,
                HypotheticalSelection = result.Candidate.CaptainId == snapshot.Evaluation.SelectedCaptainId
            }).ToList();
            preview.Candidates = EnumerationResult<CaptainSelectionCandidateView>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize },
                rows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), rows.Count);
            return preview;
        }

        private async Task<bool> RecordUsageSelectionWaitAsync(UsageSelectionSnapshot snapshot, Mission mission, CancellationToken token, string? reason = null)
        {
            try
            {
                CaptainSelectionRecordSet waiting = UsageSelectionRecordBuilder.Build(snapshot, mission, CaptainSelectionOutcomes.Waiting, admitted: false);
                if (reason != null)
                {
                    waiting.Decision.PrimaryReasonCode = reason;
                    waiting.Decision.EvidenceFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(waiting.Decision.EvidenceFingerprint + ":" + reason))).ToLowerInvariant();
                }
                EnumerationResult<CaptainSelectionDecision> history = await _Database.CaptainSelections.EnumerateByMissionAsync(mission.TenantId!, mission.Id, mission.UserId, pageSize: 1, token: token).ConfigureAwait(false);
                CaptainSelectionDecision? previous = history.Objects.FirstOrDefault();
                if (previous == null || previous.EvidenceFingerprint != waiting.Decision.EvidenceFingerprint || previous.Outcome != CaptainSelectionOutcomes.Waiting)
                {
                    waiting.Decision.PreviousDecisionId = previous?.Id;
                    CaptainSelectionDecision stored = await _Database.CaptainSelections.CreateDecisionAsync(waiting, token).ConfigureAwait(false);
                    CaptainSelectionTelemetry.Decision(snapshot.Settings.Mode, CaptainSelectionOutcomes.Waiting, waiting.Decision.Trigger);
                    CaptainSelectionTelemetry.Wait(snapshot.Settings.Mode, stored.PrimaryReasonCode);
                    NotifySelectionChanged(mission, stored.Id, CaptainSelectionOutcomes.Waiting);
                    await RecordSelectionWaitingEventAsync(mission, stored, token).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException || Armada.Core.Database.TransientDatabaseError.IsTransient(ex))
            {
                CaptainSelectionTelemetry.PersistenceFailure("History");
                UsageWait(mission, UsageSelectionReasonCodes.SelectionPersistenceUnavailable, "Selection history could not be recorded. Armada will retry without starting a process.");
                return false;
            }
        }

        /// <summary>Event type recorded when a mission enters a materially new captain-selection waiting state.</summary>
        public const string SelectionWaitingEventType = "mission.selection_waiting";

        // Material waiting states are already deduplicated by fingerprint, so this records one event per state change.
        private async Task RecordSelectionWaitingEventAsync(Mission mission, CaptainSelectionDecision decision, CancellationToken token)
        {
            try
            {
                ArmadaEvent evt = new ArmadaEvent(SelectionWaitingEventType, "Waiting for captain selection: " + UsageSelectionText.ForReason(decision.PrimaryReasonCode));
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                evt.Payload = System.Text.Json.JsonSerializer.Serialize(new CaptainSelectionWaitingEvent
                {
                    DecisionId = decision.Id, Mode = decision.Mode, ReasonCode = decision.PrimaryReasonCode, NextReconsiderUtc = decision.NextReconsiderUtc
                });
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record the selection waiting event for mission " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task<bool> OwnsSelectionCleanupAsync(Mission mission)
        {
            if (String.IsNullOrEmpty(mission.TenantId) || String.IsNullOrEmpty(mission.UsageAdmissionExecutionKey)) return true;
            try
            {
                string? current = await _Database.CaptainSelections.ReadUsageExecutionKeyAsync(mission.TenantId, mission.Id, CancellationToken.None).ConfigureAwait(false);
                if (current == mission.UsageAdmissionExecutionKey) return true;
                _Logging.Info(_Header + "ignoring late assignment cleanup for a superseded execution of mission " + mission.Id);
                return false;
            }
            catch (System.Data.Common.DbException) { return true; }
        }

        private async Task ReleaseSelectionIfUnstartedAsync(Mission mission, Captain captain)
        {
            if (String.IsNullOrEmpty(mission.UsageAdmissionExecutionKey)) return;
            try { await _Database.CaptainSelections.ReleaseUnstartedAsync(mission.TenantId!, mission.Id, captain.Id, mission.UsageAdmissionExecutionKey, CancellationToken.None).ConfigureAwait(false); }
            catch (System.Data.Common.DbException) { _Logging.Warn(_Header + "unstarted usage release could not be persisted for mission " + mission.Id + "; retaining conservative debt"); }
        }

        private async Task RecordSelectionExecutionStartedAsync(Mission mission, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(mission.TenantId)) return;
            try
            {
                CaptainSelectionDecision? decision = await _Database.CaptainSelections.ReadCurrentDecisionAsync(mission.TenantId, mission.Id, token: token).ConfigureAwait(false);
                if (decision == null) return;
                await _Database.CaptainSelections.AppendExecutionEventAsync(new CaptainSelectionExecutionEvent
                {
                    TenantId = decision.TenantId, UserId = decision.UserId, DecisionId = decision.Id,
                    ExecutionAttemptId = decision.ExecutionAttemptId, EventKind = CaptainSelectionEventKinds.ExecutionStarted,
                    ReportingSource = "MissionLifecycle", OccurredUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
            }
            catch (Exception)
            { _Logging.Warn(_Header + "selection execution evidence could not be recorded for mission " + mission.Id); }
        }

        private async Task RecordSelectionModelReportAsync(Mission mission, string? reportedModel, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(mission.TenantId) || String.IsNullOrWhiteSpace(reportedModel)) return;
            string? sanitized = UsageSelectionEvidenceSanitizer.Label(reportedModel);
            // A redacted/truncated value is not a model identity. Never substitute the configured model.
            if (sanitized != reportedModel) return;
            try
            {
                CaptainSelectionDecision? decision = await _Database.CaptainSelections.ReadCurrentDecisionAsync(mission.TenantId, mission.Id, token: token).ConfigureAwait(false);
                if (decision == null) return;
                await _Database.CaptainSelections.AppendExecutionEventAsync(new CaptainSelectionExecutionEvent
                {
                    TenantId = decision.TenantId, UserId = decision.UserId, DecisionId = decision.Id,
                    ExecutionAttemptId = decision.ExecutionAttemptId, EventKind = CaptainSelectionEventKinds.ActualModelReported, ActualModel = sanitized,
                    ReportingSource = "RuntimeReport", OccurredUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException)
            {
                _Logging.Warn(_Header + "selection model evidence could not be recorded for mission " + mission.Id);
            }
        }

        // Admission outcome labels: Admitted, Replayed (already committed) or Conflict (slot, revision or evidence changed).
        private static void RecordAdmissionTelemetry(CaptainSelectionStage stage, UsageSelectionSnapshot snapshot, CaptainSelectionRecordSet records, CaptainSelectionAdmissionReceipt receipt)
        {
            string outcome = receipt.Claimed ? CaptainSelectionOutcomes.Admitted : receipt.AlreadyCommitted ? "Replayed" : "Conflict";
            stage.Complete(outcome);
            if (!receipt.Claimed) return;
            CaptainSelectionTelemetry.Decision(snapshot.Settings.Mode, CaptainSelectionOutcomes.Admitted, records.Decision.Trigger);
            if (snapshot.Settings.Mode == UsageSelectionModeEnum.Observe && !String.Equals(records.Decision.HypotheticalCaptainId, snapshot.ActualCaptainId, StringComparison.Ordinal))
                CaptainSelectionTelemetry.ObserveDivergence(snapshot.Settings.Strategy);
        }

        private async Task<UsageSelectionSnapshot> BuildUsageSelectionAsync(CaptainAdmissionSelection selection, UsageSelectionSettings settings, CancellationToken token)
        {
            UsageSelectionSnapshot snapshot = new UsageSelectionSnapshot { Settings = settings, Recovery = selection.Mission.UsageRecovery, EvaluatedUtc = DateTime.UtcNow,
                ActualCaptainId = selection.Selected?.Id, CaptureEnabled = _Settings.UsageCapture.EffectiveEnabled, Captains = selection.Visible.ToDictionary(c => c.Id, StringComparer.Ordinal),
                CarriedExecutionAttemptId = _NoOpRedispatchCarry.TryGetValue(selection.Mission.Id, out string? carried) ? carried : null };
            if (selection.Visible.Count > settings.MaximumCandidates)
            {
                snapshot.Evaluation = new UsageSelectionEvaluation { CandidateLimitExceeded = true, ReasonCode = UsageSelectionReasonCodes.CandidateLimitExceeded };
                return snapshot;
            }
            List<string> ids = selection.Visible.Select(c => c.Id).ToList();
            List<UsageSelectionAccountEvidence> evidence = new List<UsageSelectionAccountEvidence>();
            List<UsageSelectionBudgetTotals> totals = new List<UsageSelectionBudgetTotals>();
            Dictionary<string, DateTime> lastAssigned = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            using (CaptainSelectionStage evidenceStage = CaptainSelectionTelemetry.Stage("evidence", selection.Mission.Id))
            {
                for (int offset = 0; offset < ids.Count; offset += 1000)
                {
                    List<string> batch = ids.Skip(offset).Take(1000).ToList();
                    evidence.AddRange(await _Database.CaptainSelections.ReadAccountEvidenceAsync(selection.Mission.TenantId!, batch, token).ConfigureAwait(false));
                    totals.AddRange(await _Database.CaptainSelections.ReadBudgetTotalsAsync(selection.Mission.TenantId!, batch, token, snapshot.CarriedExecutionAttemptId).ConfigureAwait(false));
                    foreach (KeyValuePair<string, DateTime> row in await _Database.CaptainSelections.ReadLastAssignedAsync(selection.Mission.TenantId!, batch, token).ConfigureAwait(false))
                        lastAssigned[row.Key] = row.Value;
                }
                evidenceStage.Complete();
            }
            foreach (IGrouping<string, UsageSelectionAccountEvidence> group in evidence.GroupBy(row => row.CaptainId, StringComparer.Ordinal))
                if (group.Count() == 1) snapshot.Evidence[group.Key] = group.Single();
            Dictionary<string, int> buckets = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            int order = 0;
            for (int bucket = 0; bucket < selection.RoutingBuckets.Count; bucket++)
                foreach (Captain captain in selection.RoutingBuckets[bucket].Captains)
                { buckets[captain.Id] = bucket; ranks[captain.Id] = order++; }
            List<UsageSelectionCandidate> inputs = new List<UsageSelectionCandidate>();
            foreach (Captain captain in selection.Visible)
            {
                UsageSelectionCandidate candidate = new UsageSelectionCandidate { CaptainId = captain.Id, Model = captain.Model,
                    Capacity = captain.MaxConcurrency, OccupiedSlots = captain.ActiveMissionCount, LastAssignedUtc = lastAssigned.TryGetValue(captain.Id, out DateTime assignedUtc) ? assignedUtc : null,
                    RoutingBucket = buckets.TryGetValue(captain.Id, out int bucket) ? bucket : Int32.MaxValue,
                    FirstAvailableRank = ranks.TryGetValue(captain.Id, out int rank) ? rank : Int32.MaxValue,
                    HasAuthoritativeQuotaHold = captain.QuarantineUntilUtc > snapshot.EvaluatedUtc && UsageQuotaHold.IsQuotaOwned(captain.QuarantineReason) };
                CaptainEligibilityFailure? failure = EvaluateCaptainIneligibility(captain, selection.Mission, selection.Eligibility);
                candidate.IneligibilityReason = failure?.Code;
                if (selection.CapacityExclusions.TryGetValue(captain.Id, out MissionAssignmentBlocker? capacity)) candidate.IneligibilityReason = capacity.Reason.ToString();
                if (candidate.IneligibilityReason == null && !buckets.ContainsKey(captain.Id)) candidate.IneligibilityReason = UsageSelectionReasonCodes.RoutingPreferenceExcluded;
                if (snapshot.Evidence.TryGetValue(captain.Id, out UsageSelectionAccountEvidence? account))
                {
                    UsageSelectionEvidenceBuilder.Apply(candidate, captain, account, totals, selection.Mission.TenantId!, _Settings.UsageCapture.EffectiveEnabled);
                }
                inputs.Add(candidate);
            }
            using (CaptainSelectionStage evaluation = CaptainSelectionTelemetry.Stage("evaluation", selection.Mission.Id))
            {
                snapshot.Evaluation = UsageSelectionPolicy.Evaluate(inputs, settings, snapshot.EvaluatedUtc, snapshot.Recovery?.ReserveException == true);
                evaluation.Complete(snapshot.Evaluation.SelectedCaptainId == null ? CaptainSelectionOutcomes.Waiting : CaptainSelectionOutcomes.Selected);
            }
            return snapshot;
        }
    }
}
