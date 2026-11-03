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
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, UsageSelectionTransientBlocker> _UsageSelectionBlockers =
            new System.Collections.Concurrent.ConcurrentDictionary<string, UsageSelectionTransientBlocker>(StringComparer.Ordinal);

        private string UsagePolicyKey() => System.Text.Json.JsonSerializer.Serialize(_Settings.EffectiveUsageSelection);

        private bool UsageRetryPending(Mission mission)
        {
            return _UsageSelectionBlockers.TryGetValue(mission.Id, out UsageSelectionTransientBlocker? failure)
                && failure.PolicyKey == UsagePolicyKey() && failure.RetryUtc > DateTime.UtcNow;
        }

        private void UsageWait(Mission mission, string code, string summary)
        {
            if (_UsageSelectionBlockers.Count >= _MaxEnvelopeDecisionsTracked)
            {
                string? oldest = _UsageSelectionBlockers.Keys.FirstOrDefault();
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
            if (mission.Status != MissionStatusEnum.Pending) readiness = "MissionNotPending";
            else if (String.IsNullOrEmpty(mission.TenantId)) readiness = "MissionScopeUnavailable";
            else
            {
                Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel == null) readiness = "VesselMissing";
                else if (UsesSharedLocalAndWorkingDirectory(vessel)) readiness = "VesselMisconfigured";
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
            preview.EvaluatedUtc = snapshot.EvaluatedUtc; preview.ActualCaptainId = snapshot.ActualCaptainId;
            preview.HypotheticalCaptainId = snapshot.Evaluation.SelectedCaptainId; preview.ReasonCode = snapshot.Evaluation.ReasonCode;
            List<CaptainSelectionCandidateView> rows = snapshot.Evaluation.Candidates.Select((result, index) => new CaptainSelectionCandidateView
            {
                CaptainId = result.Candidate.CaptainId, CaptainName = UsageSelectionEvidenceSanitizer.Label(snapshot.Captains[result.Candidate.CaptainId].Name)!,
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
                    ExecutionAttemptId = decision.ExecutionAttemptId, EventKind = "ExecutionStarted",
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
                    ExecutionAttemptId = decision.ExecutionAttemptId, EventKind = "ActualModelReported", ActualModel = sanitized,
                    ReportingSource = "RuntimeReport", OccurredUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException)
            {
                _Logging.Warn(_Header + "selection model evidence could not be recorded for mission " + mission.Id);
            }
        }

        private async Task<UsageSelectionSnapshot> BuildUsageSelectionAsync(CaptainAdmissionSelection selection, UsageSelectionSettings settings, CancellationToken token)
        {
            UsageSelectionSnapshot snapshot = new UsageSelectionSnapshot { Settings = settings, EvaluatedUtc = DateTime.UtcNow,
                ActualCaptainId = selection.Selected?.Id, Captains = selection.Visible.ToDictionary(c => c.Id, StringComparer.Ordinal) };
            if (selection.Visible.Count > settings.MaximumCandidates)
            {
                snapshot.Evaluation = new UsageSelectionEvaluation { CandidateLimitExceeded = true, ReasonCode = "CandidateLimitExceeded" };
                return snapshot;
            }
            List<string> ids = selection.Visible.Select(c => c.Id).ToList();
            List<UsageSelectionAccountEvidence> evidence = new List<UsageSelectionAccountEvidence>();
            List<UsageSelectionBudgetTotals> totals = new List<UsageSelectionBudgetTotals>();
            for (int offset = 0; offset < ids.Count; offset += 1000)
            {
                List<string> batch = ids.Skip(offset).Take(1000).ToList();
                evidence.AddRange(await _Database.CaptainSelections.ReadAccountEvidenceAsync(selection.Mission.TenantId!, batch, token).ConfigureAwait(false));
                totals.AddRange(await _Database.CaptainSelections.ReadBudgetTotalsAsync(selection.Mission.TenantId!, batch, token).ConfigureAwait(false));
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
                    Capacity = captain.MaxConcurrency, OccupiedSlots = captain.ActiveMissionCount, LastAssignedUtc = captain.LastUpdateUtc,
                    RoutingBucket = buckets.TryGetValue(captain.Id, out int bucket) ? bucket : Int32.MaxValue,
                    FirstAvailableRank = ranks.TryGetValue(captain.Id, out int rank) ? rank : Int32.MaxValue,
                    HasAuthoritativeQuotaHold = captain.QuarantineUntilUtc > snapshot.EvaluatedUtc && UsageQuotaHold.IsQuotaOwned(captain.QuarantineReason) };
                CaptainEligibilityFailure? failure = EvaluateCaptainIneligibility(captain, selection.Mission, selection.Eligibility);
                candidate.IneligibilityReason = failure?.Code;
                if (selection.CapacityExclusions.TryGetValue(captain.Id, out MissionAssignmentBlocker? capacity)) candidate.IneligibilityReason = capacity.Reason.ToString();
                if (candidate.IneligibilityReason == null && !buckets.ContainsKey(captain.Id)) candidate.IneligibilityReason = "RoutingPreferenceExcluded";
                if (snapshot.Evidence.TryGetValue(captain.Id, out UsageSelectionAccountEvidence? account))
                {
                    string? fingerprint = captain.UsageCaptureMode == UsageCaptureModeEnum.Custom ? UsageAccountResolver.CommandFingerprint(captain.UsageCaptureCommand) : null;
                    string revision = UsageAccountResolver.ConfigurationRevision(account.HostId, captain.Runtime, fingerprint);
                    string groupKey = UsageAccountResolver.GroupKey(selection.Mission.TenantId!, account.HostId, captain.Runtime, fingerprint);
                    candidate.Provider = account.Provider;
                    candidate.IdentityVerified = account.Runtime == captain.Runtime && captain.UsageCaptureActive && _Settings.UsageCapture.EffectiveEnabled;
                    candidate.ConfigurationMatches = account.ConfigurationRevision == revision && account.CaptureContextKey == UsageAccountResolver.CaptureContextKey(groupKey, revision)
                        && (String.IsNullOrEmpty(captain.UsageCaptureHarborId) || captain.UsageCaptureHarborId == account.HostId);
                    candidate.CapturedUtc = account.CapturedUtc; candidate.Status = account.Status; candidate.Source = account.Source;
                    candidate.EffectiveCaptureIntervalSeconds = account.EffectiveIntervalSeconds;
                    List<UsageSelectionBudgetTotals> accountTotals = totals.Where(t => t.HostId == account.HostId && t.Runtime == account.Runtime.ToString() && t.AccountKey == account.AccountKey)
                        .DistinctBy(t => t.WindowKey + ":" + t.Generation).ToList();
                    candidate.HasUncertainDebt = accountTotals.Any(t => t.HasUncertainDebt);
                    foreach (UsageWindow window in UsageWindowsJson.Deserialize(account.WindowsJson))
                    {
                        if (window == null) { candidate.Windows.Add(new UsageSelectionWindowEvidence()); continue; }
                        string key = UsageBudgetIdentity.WindowKey(window, captain.UsageCaptureMode == UsageCaptureModeEnum.Custom ? account.ConfigurationRevision : null);
                        candidate.Windows.Add(new UsageSelectionWindowEvidence { Window = window,
                            ScopeApplies = String.IsNullOrEmpty(window.Scope) || String.Equals(window.Scope, "account", StringComparison.OrdinalIgnoreCase),
                            GenerationConfirmed = !String.IsNullOrWhiteSpace(window.Name)
                                && UsageSelectionEvidenceSanitizer.Label(window.Name) == window.Name
                                && UsageSelectionEvidenceSanitizer.Label(window.Model) == window.Model
                                && UsageSelectionEvidenceSanitizer.Label(window.Scope) == window.Scope
                                && UsageSelectionEvidenceSanitizer.Label(window.Unit) == window.Unit,
                            ActiveReservationPercent = accountTotals.Where(t => t.WindowKey == key).Sum(t => t.ActivePercent),
                            UnsettledPercent = accountTotals.Where(t => t.WindowKey == key).Sum(t => t.UnsettledPercent) });
                    }
                    foreach (IGrouping<string, UsageSelectionWindowEvidence> duplicate in candidate.Windows.GroupBy(w => UsageBudgetIdentity.WindowKey(w.Window)).Where(g => g.Count() > 1))
                        foreach (UsageSelectionWindowEvidence window in duplicate) window.GenerationConfirmed = false;
                }
                inputs.Add(candidate);
            }
            snapshot.Evaluation = UsageSelectionPolicy.Evaluate(inputs, settings, snapshot.EvaluatedUtc);
            return snapshot;
        }
    }
}
