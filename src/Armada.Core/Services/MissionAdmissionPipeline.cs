namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Decides whether a pending mission may be admitted now: safety, readiness, global and captain gates, and the typed blocker when it may not.
    /// </summary>
    internal sealed class MissionAdmissionPipeline
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly SemaphoreSlim _CapacityLock;
        private readonly DatabaseDriver _Database;
        private readonly MissionLaunchBackoff _LaunchBackoff;
        private readonly ArmadaSettings _Settings;
        private readonly UsageSelectionBlockerStore _UsageSelection;
        private readonly VesselAdmissionLockRegistry _VesselAdmissionLocks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionAdmissionPipeline(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _CapacityLock = context.CapacityLock;
            _Database = context.Database;
            _LaunchBackoff = context.LaunchBackoff;
            _Settings = context.Settings;
            _UsageSelection = context.UsageSelection;
            _VesselAdmissionLocks = context.VesselAdmissionLocks;
        }

        #endregion

        #region Internal-Methods

        internal async Task<MissionAssignmentBlocker?> GetAssignmentBlockerCoreAsync(Mission mission, CancellationToken token,
            Action<CaptainAdmissionSelection>? observeSelection = null)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Status != MissionStatusEnum.Pending) return null;

            MissionAssignmentBlocker blocker = new MissionAssignmentBlocker();

            Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.VesselMissing;
                blocker.Summary = String.IsNullOrEmpty(mission.VesselId)
                    ? "This mission has no vessel, so it cannot be assigned."
                    : "This mission's vessel no longer exists, so it cannot be assigned.";
                return blocker;
            }

            if (MissionCaptainRouter.UsesSharedLocalAndWorkingDirectory(vessel))
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.VesselMisconfigured;
                blocker.Summary = "Vessel " + vessel.Name + " uses the same directory for its local repository and working directory, so a dock cannot be provisioned. Change the vessel's working directory.";
                return blocker;
            }

            if (_LaunchBackoff.TryGet(mission.Id, out MissionLaunchFailure? launchFailure)) return MissionWaitCoordinator.LaunchFailingBlocker(launchFailure);

            ReadinessAdmissionOutcome readiness = await EvaluateReadinessAdmissionAsync(mission, token).ConfigureAwait(false);
            if (readiness.Blocker != null) return readiness.Blocker;

            MissionAssignmentBlocker? admission = await EvaluateSafetyAdmissionAsync(mission, vessel, token).ConfigureAwait(false);
            if (admission != null) return admission;
            MissionAssignmentBlocker? globalAdmission = await _Context.MissionCaptainRouter.EvaluateGlobalAdmissionAsync(token).ConfigureAwait(false);
            if (globalAdmission != null) return globalAdmission;

            if (_Settings.EffectiveUsageSelection.Mode != UsageSelectionModeEnum.Disabled
                && _UsageSelection.TryGetBlocker(mission.Id, out UsageSelectionTransientBlocker? usageFailure)
                && usageFailure.PolicyKey == _Context.MissionUsageSelector.UsagePolicyKey() && usageFailure.RetryUtc > DateTime.UtcNow) return usageFailure.Blocker;

            CaptainAdmissionSelection selection = await SelectCaptainAdmissionAsync(mission, token).ConfigureAwait(false);
            if (_Settings.EffectiveUsageSelection.Mode == UsageSelectionModeEnum.Enforce && selection.Visible.Count > 0 && !String.IsNullOrWhiteSpace(mission.TenantId))
            {
                try
                {
                    UsageSelectionSnapshot usage = await _Context.MissionUsageSelector.BuildUsageSelectionAsync(selection, _Settings.EffectiveUsageSelection.Copy(), token).ConfigureAwait(false);
                    if (usage.Evaluation.SelectedCaptainId == null)
                        return MissionUsageSelector.AddRecoveryAdvice(new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.NoEligibleCaptain,
                            SelectionReasonCode = usage.Evaluation.ReasonCode, Summary = "No captain currently satisfies the configured usage policy.",
                            SelectionNextReconsiderUtc = usage.Evaluation.Candidates.FirstOrDefault()?.NextReconsiderUtc }, mission, usage);
                    selection.Selected = selection.Visible.Single(c => c.Id == usage.Evaluation.SelectedCaptainId);
                }
                catch (System.Data.Common.DbException)
                { return new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, SelectionReasonCode = "SelectionPersistenceUnavailable", Summary = "Selection evidence is temporarily unavailable." }; }
            }
            observeSelection?.Invoke(selection);
            blocker = CaptainGateBlocker(mission, selection, out List<Captain> full);
            switch (blocker.Reason)
            {
                case MissionAssignmentBlockerReasonEnum.NoEligibleCaptain when !selection.CapacityExclusions.Values.Contains(blocker):
                    await _Context.MissionCaptainRouter.DescribeCaptainsAsync(blocker, selection.Visible, selection.Mission, token).ConfigureAwait(false);
                    break;
                case MissionAssignmentBlockerReasonEnum.NoIdleCaptain:
                case MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit:
                    await _Context.MissionCaptainRouter.DescribeCaptainsAsync(blocker, selection.Visible, selection.Mission, token).ConfigureAwait(false);
                    if (blocker.Reason == MissionAssignmentBlockerReasonEnum.NoIdleCaptain && blocker.CaptainCount == 1 && blocker.Captains.Count == 1)
                        blocker.Summary = "Waiting for a captain: " + (blocker.Captains[0].CaptainName ?? blocker.Captains[0].CaptainId) + " is " + blocker.Captains[0].Detail + ".";
                    break;
            }
            return blocker;
        }

        // The captain gate (routing, eligibility, captain capacity, placement) as one typed reason (RS-4.6). Dispatch
        // records it as is; the read-only assignment blocker adds per-captain detail on top.
        internal MissionAssignmentBlocker CaptainGateBlocker(Mission mission, CaptainAdmissionSelection selection, out List<Captain> full)
        {
            MissionAssignmentBlocker blocker = new MissionAssignmentBlocker();
            Mission routingView = selection.Mission;
            List<Captain> allCaptains = selection.Visible;
            full = new List<Captain>();
            if (allCaptains.Count == 0)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.NoCaptains;
                blocker.Summary = "Waiting for a captain: there are no captains. Create one to run missions.";
                blocker.Advice.Add(new AssignmentBlockerAdvice { Text = "Add an eligible captain to run this mission.", Action = AssignmentBlockerActionEnum.AddCaptain, TargetType = "captain" });
                return blocker;
            }
            if (selection.Selected != null)
            {
                // Placement and launch policy are dispatch-only gates; the last dispatch result is their answer.
                if (_Context.MissionWaitCoordinator.GetAdmissionResult(mission.Id)?.Blocker is MissionAssignmentBlocker placementWait
                    && placementWait.Reason == MissionAssignmentBlockerReasonEnum.PlacementWaiting)
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.PlacementWaiting;
                    blocker.Summary = placementWait.Summary;
                }
                else
                {
                    blocker.Reason = MissionAssignmentBlockerReasonEnum.AwaitingDispatch;
                    blocker.Summary = "A captain is free; the mission is assigned on the next dispatch cycle.";
                }
                return blocker;
            }
            if (selection.RoutingEligible.Count > 0 && selection.CapacityExclusions.Count == selection.RoutingEligible.Count)
            {
                MissionAssignmentBlocker capacity = selection.CapacityExclusions[selection.RoutingEligible[0].Id];
                foreach (AssignmentBlockerAdvice advice in selection.CapacityExclusions.Values.SelectMany(item => item.Advice)
                    .DistinctBy(item => new { item.Action, item.TargetId, item.Setting }).Take(MissionAssignmentBlocker.MaxListed).ToList())
                    if (!capacity.Advice.Contains(advice)) capacity.Advice.Add(advice);
                return capacity;
            }
            full = allCaptains.Where(c => c.State == CaptainStateEnum.Working && c.ActiveMissionCount > 0
                && c.CurrentSessionId == null && MissionCaptainRouter.CaptainIneligibilityReason(c, routingView, selection.Eligibility) == null).ToList();
            if (full.Count == 0 && selection.Available.Count > 0)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.NoEligibleCaptain;
                blocker.Summary = "Waiting for a captain: no available captain may take this mission"
                    + (!String.IsNullOrEmpty(routingView.Persona) ? " (persona " + routingView.Persona + ")" : "")
                    + (routingView.Tier.HasValue ? " (tier " + routingView.Tier.Value + " or higher)" : "")
                    + ". Check the persona's captain priorities and the captains' allowed personas and tiers."
                    + (selection.Eligibility.ExcludedAuthors.Count > 0 && selection.Available.Any(c => selection.Eligibility.ExcludedAuthors.Contains(c.Id))
                        ? " Available captains that ran an earlier stage of the reviewed work are excluded because ExcludeReviewAuthorsFromJudge is on." : "");
                if (selection.Eligibility.EnvelopeGate != null && !selection.Eligibility.EnvelopeGate.AnyCompatibleCaptain())
                    blocker.Summary = "Waiting for a captain: no captain meets the Worker eligibility envelope or may run the Worker persona. "
                        + WorkerEnvelopeRules.Describe(selection.Eligibility.EnvelopeGate.Envelope)
                        + " Give a captain a model profile that meets it, or change the vessel's envelope.";
                ApplyRoutingRuleBlocker(blocker, routingView, selection);
                return blocker;
            }
            blocker.Reason = full.Count > 0 ? MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit : MissionAssignmentBlockerReasonEnum.NoIdleCaptain;
            if (full.Count > 0)
            {
                blocker.SetBlockingMissions(full.SelectMany(c => c.ActiveMissionIds));
                AddCaptainCapacityAdvice(blocker, full);
            }
            List<DateTime> quarantineEnds = allCaptains
                .Where(c => c.State == CaptainStateEnum.Quarantined && c.QuarantineUntilUtc.HasValue)
                .Select(c => c.QuarantineUntilUtc!.Value)
                .ToList();
            if (allCaptains.All(c => c.State == CaptainStateEnum.Quarantined) && quarantineEnds.Count > 0) blocker.UntilUtc = quarantineEnds.Min();
            blocker.Summary = full.Count > 0
                ? "Eligible captains have no free mission slots: " + String.Join("; ", full.Take(MissionAssignmentBlocker.MaxListed)
                    .Select(c => c.Name + " " + c.ActiveMissionCount + " running / limit " + c.MaxConcurrency
                        + (c.ActiveMissionCount > c.MaxConcurrency ? " (limit below current usage; running work continues)" : ""))) + "."
                : "Waiting for a captain: every captain is busy.";
            return blocker;
        }

        internal async Task<MissionAssignmentBlocker?> EvaluateSafetyAdmissionAsync(Mission mission, Vessel vessel, CancellationToken token)
        {
            List<Mission> running = (await _Context.MissionCaptainRouter.ActiveMissionsOnVesselAsync(vessel.Id, token).ConfigureAwait(false))
                .Where(m => m.Id != mission.Id && (m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Verifying)).ToList();
            MissionAssignmentBlocker? safety = MissionAdmissionEvaluator.RunningLocks(mission, vessel, running,
                running.Where(_Context.MissionScopeGuard.IsBroadScope).ToList(), _Context.MissionScopeGuard.IsBroadScope(mission));
            return safety ?? await _Context.MissionCaptainRouter.EvaluateVesselAdmissionAsync(mission, vessel, token).ConfigureAwait(false);
        }

        internal static MissionAssignmentBlocker EndpointUnavailable(ModelEndpoint endpoint, string reason)
        {
            MissionAssignmentBlocker result = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.EndpointUnavailable,
                Summary = "Endpoint " + endpoint.Name + " is " + reason + ". Increasing concurrency will not make it available." };
            result.Advice.Add(new AssignmentBlockerAdvice { Text = "Review the endpoint's enabled state and health before retrying.",
                Action = AssignmentBlockerActionEnum.EditEndpoint, TargetType = "endpoint", TargetId = endpoint.Id });
            return result;
        }

        internal async Task<CaptainAdmissionSelection> SelectCaptainAdmissionAsync(Mission mission, CancellationToken token, bool recordEnvelope = false)
        {
            Mission view = mission.AdmissionView();
            if (!String.IsNullOrEmpty(view.TenantId))
                view.UsageRecovery = await _Database.CaptainSelections.ReadRecoveryAsync(view.TenantId, view.Id, token).ConfigureAwait(false);
            await _Context.MissionCaptainRouter.ResolvePreferredCaptainAsync(view, token, persist: false).ConfigureAwait(false);
            CaptainEligibilityContext context = await _Context.MissionCaptainRouter.BuildCaptainEligibilityContextAsync(view, token).ConfigureAwait(false);
            List<Captain> visible = (await _Context.MissionDispatchPass.PassCaptainsAsync(token).ConfigureAwait(false))
                .Where(c => MissionCaptainRouter.CaptainVisibleToMission(c, view)).ToList();
            foreach (Captain captain in visible) captain.MaxConcurrency = ConcurrencyClamp.CaptainLimit(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency);
            CaptainAdmissionSelection selection = new()
            {
                Mission = view, Eligibility = context, Visible = visible,
                Available = visible.Where(c => c.HasMissionCapacity).ToList()
            };
            WorkerEnvelopeGate? envelope = context.EnvelopeGate;
            selection.Selected = await _Context.MissionCaptainRouter.FindRoutedCaptainAsync(view, envelope, selection, token).ConfigureAwait(false);
            WorkerEnvelopeOutcomeEnum outcome = WorkerEnvelopeOutcomeEnum.Assigned;
            if (selection.Selected == null && envelope != null)
            {
                selection.Selected = envelope.SelectEscalation(view);
                if (selection.Selected != null) selection.RoutingBuckets.Add(new CaptainRoutingBucket { ReasonCode = "WorkerEnvelopeEscalation", Captains = new List<Captain> { selection.Selected } });
                outcome = selection.Selected != null ? WorkerEnvelopeOutcomeEnum.Escalated
                    : envelope.AnyCompatibleCaptain() ? WorkerEnvelopeOutcomeEnum.Waiting : WorkerEnvelopeOutcomeEnum.NoEligibleCaptain;
            }
            if (recordEnvelope && envelope != null)
                await _Context.WorkerEnvelopeService.RecordWorkerEnvelopeDecisionAsync(view, envelope, outcome, selection.Selected, token).ConfigureAwait(false);
            return selection;
        }

        // Readiness never claims capacity or mutates missions. Dispatch alone applies a failed-slice
        // transition; diagnostics use the exact same observation without side effects.
        internal async Task<ReadinessAdmissionOutcome> EvaluateReadinessAdmissionAsync(
            Mission mission, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
            {
                Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId, token).ConfigureAwait(false);
                MissionAssignmentBlocker? dependencyBlocker = MissionAdmissionEvaluator.Dependency(mission, dependency,
                    dependency != null && MissionHandoffService.IsPipelineHandoffPrepared(mission, dependency));
                if (dependencyBlocker != null) return new ReadinessAdmissionOutcome(dependencyBlocker);
            }

            HierarchyReadiness hierarchy = await _Context.MissionHierarchyService.EvaluateParentReadinessAsync(mission, token).ConfigureAwait(false);
            if (hierarchy.Blocker != null) return new ReadinessAdmissionOutcome(hierarchy.Blocker);

            IntegrationBarrierState barrier = await _Context.IntegrationBarrierService.EvaluateIntegrationBarrierAsync(mission, token).ConfigureAwait(false);
            if (barrier.FailedMission != null)
            {
                Mission failed = barrier.FailedMission;
                string transition = _Settings.BlockDependentsOfFailedStages ? "blocked" : "cancelled";
                MissionAssignmentBlocker blocker = new MissionAssignmentBlocker
                {
                    Reason = MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers,
                    Summary = "Mission '" + failed.Title + "' in another slice ended " + failed.Status
                        + "; this integration will not run without it and is " + transition
                        + " on the next dispatch pass. Restart that mission to continue."
                };
                blocker.SetBlockingMissions(new[] { failed.Id });
                return new ReadinessAdmissionOutcome(blocker, failed);
            }
            if (barrier.Waiting)
                return new ReadinessAdmissionOutcome(new MissionAssignmentBlocker
                {
                    Reason = MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers,
                    Summary = barrier.WaitingSummary ?? "Waiting for the voyage's other implementation pipelines, including reviews and checks, to settle."
                });
            return ReadinessAdmissionOutcome.Ready;
        }

        /// <inheritdoc />
        internal async Task<bool> TryAdmitVerificationResumeAsync(Mission mission, Captain captain, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return false;
            if (!String.IsNullOrEmpty(mission.VoyageId))
            {
                Voyage? voyage = await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                if (voyage?.Status == VoyageStatusEnum.Complete || voyage?.Status == VoyageStatusEnum.Cancelled || voyage?.Status == VoyageStatusEnum.Failed) return false;
            }
            TimeSpan timeout = TimeSpan.FromSeconds(_Settings.Concurrency.AdmissionLockTimeoutSeconds);
            using IDisposable? vesselLease = await _VesselAdmissionLocks.TryEnterAsync(vessel.Id, timeout, token).ConfigureAwait(false);
            if (vesselLease == null || await EvaluateSafetyAdmissionAsync(mission, vessel, token).ConfigureAwait(false) != null) return false;
            if (!await _CapacityLock.WaitAsync(timeout, token).ConfigureAwait(false)) return false;
            try
            {
                _Context.MissionDispatchPass.PassRevalidateUnderLock();
                if (await _Context.MissionCaptainRouter.EvaluateGlobalAdmissionAsync(token).ConfigureAwait(false) != null) return false;
                Captain? current = await _Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
                if (current == null || !current.HasMissionCapacity || !MissionCaptainRouter.CaptainVisibleToMission(current, mission)
                    || current.QuarantineUntilUtc > DateTime.UtcNow) return false;
                Mission routingView = mission.AdmissionView();
                if (!String.IsNullOrEmpty(mission.TenantId)) routingView.UsageRecovery = await _Database.CaptainSelections.ReadRecoveryAsync(mission.TenantId, mission.Id, token).ConfigureAwait(false);
                await _Context.MissionCaptainRouter.ResolvePreferredCaptainAsync(routingView, token, persist: false).ConfigureAwait(false);
                CaptainEligibilityContext eligibility = await _Context.MissionCaptainRouter.BuildCaptainEligibilityContextAsync(routingView, token).ConfigureAwait(false);
                if (MissionCaptainRouter.CaptainIneligibilityReason(current, routingView, eligibility) != null) return false;
                List<Captain> admitted = await _Context.MissionLauncher.ApplyCapacityLimitsAsync(new List<Captain> { current }, routingView, token).ConfigureAwait(false);
                if (admitted.Count == 0) return false;
                if (_Service.CanAssignMissionAsync != null && !await _Service.CanAssignMissionAsync(routingView, current).ConfigureAwait(false)) return false;
                if (_Settings.EffectiveUsageSelection.Mode == UsageSelectionModeEnum.Enforce && String.IsNullOrWhiteSpace(mission.TenantId)) return false;
                if (_Settings.EffectiveUsageSelection.Mode != UsageSelectionModeEnum.Disabled && !String.IsNullOrEmpty(mission.TenantId))
                {
                    CaptainAdmissionSelection repair = new CaptainAdmissionSelection { Mission = routingView, Eligibility = eligibility,
                        Visible = new List<Captain> { current }, Available = new List<Captain> { current }, Selected = current,
                        RoutingBuckets = new List<CaptainRoutingBucket> { new CaptainRoutingBucket { ReasonCode = "RepairCaptain", Captains = new List<Captain> { current } } } };
                    UsageSelectionSnapshot snapshot = await _Context.MissionUsageSelector.BuildUsageSelectionAsync(repair, _Settings.EffectiveUsageSelection.Copy(), token).ConfigureAwait(false);
                    if (snapshot.Settings.Mode == UsageSelectionModeEnum.Enforce && snapshot.Evaluation.SelectedCaptainId != current.Id) return false;
                    if (_Context.MissionUsageSelector.UsagePolicyKey() != System.Text.Json.JsonSerializer.Serialize(snapshot.Settings)) return false;
                    string? executionHost = mission.AssignedHarborId ?? _Service.UsageSelectionLocalHostId;
                    UsageSelectionCandidate? repairUsage = snapshot.Evaluation.Candidates.SingleOrDefault(c => c.Candidate.CaptainId == current.Id)?.Candidate;
                    if (snapshot.Settings.Mode == UsageSelectionModeEnum.Enforce && (String.IsNullOrWhiteSpace(executionHost)
                        || repairUsage?.IdentityVerified == true && repairUsage.ConfigurationMatches && snapshot.Evidence.TryGetValue(current.Id, out UsageSelectionAccountEvidence? observed) && observed.HostId != executionHost)) return false;
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, mission, CaptainSelectionTriggers.Repair, admitted: true);
                    records.AdmissionHostId = executionHost;
                    using CaptainSelectionStage admissionStage = CaptainSelectionTelemetry.Stage("transaction", mission.Id);
                    CaptainSelectionAdmissionReceipt receipt = snapshot.Settings.Mode == UsageSelectionModeEnum.Enforce
                        ? await _Database.CaptainSelections.TryAdmitEnforcedRepairAsync(records, mission.LastUpdateUtc, _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false)
                        : await _Database.CaptainSelections.TryAdmitObservedRepairAsync(records, mission.LastUpdateUtc, _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false);
                    MissionUsageSelector.RecordAdmissionTelemetry(admissionStage, snapshot, records, receipt);
                    if (receipt.Claimed) _Context.MissionUsageSelector.NotifySelectionChanged(mission, records.Decision.Id, CaptainSelectionOutcomes.Admitted);
                    return receipt.Claimed;
                }
                return await _Database.CaptainSelections.TryAdmitLegacyRepairAsync(current.Id, mission.Id, mission.LastUpdateUtc, mission.AssignedHarborId ?? _Service.UsageSelectionLocalHostId,
                    _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false);
            }
            finally { _Context.MissionDispatchPass.NoteClaimAttempt(); _CapacityLock.Release(); }
        }

        #endregion

        #region Private-Methods

        // RS-8.4, RS-8.7: when every available captain is excluded by the same hard routing rule, the blocker names it with a
        // selection reason code (context_too_small or judge_strength) and says what would let the mission run.
        private static void ApplyRoutingRuleBlocker(MissionAssignmentBlocker blocker, Mission mission, CaptainAdmissionSelection selection)
        {
            List<string?> codes = selection.Available.Select(c => MissionCaptainRouter.EvaluateCaptainIneligibility(c, mission, selection.Eligibility)?.Code).ToList();
            if (codes.Count == 0) return;
            if (codes.All(c => c == "JudgeStrength"))
            {
                blocker.SelectionReasonCode = JudgeStrengthRule.BlockerCode;
                blocker.Summary = "Waiting for a Judge: the pipeline's Judge strength rule needs a captain of tier " + selection.Eligibility.JudgeRequiredTier
                    + " or higher (the highest tier among the stages it reviews), and no available captain has it. A stronger captain takes it when"
                    + " one is free; set the pipeline's judgeStrength to ProceedAndFlag to run it on the best available Judge instead.";
            }
            else if (codes.All(c => c == "ContextTooSmall"))
            {
                blocker.SelectionReasonCode = MissionPromptFit.BlockerCode;
                blocker.Summary = "Waiting for a captain: no available captain's served context can hold this mission's prompt (description, output"
                    + " contract and handoff). Use a captain with a larger context, raise the endpoint's context length, or shorten the description.";
            }
        }

        private void AddCaptainCapacityAdvice(MissionAssignmentBlocker blocker, List<Captain> full)
        {
            blocker.Advice.Add(new AssignmentBlockerAdvice { Text = "Add another eligible captain to run work in parallel.",
                Action = AssignmentBlockerActionEnum.AddCaptain, TargetType = "captain" });
            foreach (Captain captain in full.Take(MissionAssignmentBlocker.MaxListed - 1))
            {
                if (captain.Runtime != AgentRuntimeEnum.Mux && captain.Runtime != AgentRuntimeEnum.ApiEndpoint) continue;
                int effective = Math.Min(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency);
                AssignmentBlockerAdvice advice = MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit,
                    captain.Name, captain.ActiveMissionCount, effective, AssignmentBlockerActionEnum.EditCaptain,
                    "captain", captain.Id, "MaxConcurrency", _Settings.Concurrency.MaxCaptainConcurrency).Advice[0];
                advice.Text = captain.Name + ": " + advice.Text;
                if (!advice.SuggestedValue.HasValue && effective == _Settings.Concurrency.MaxCaptainConcurrency)
                {
                    advice.Text = captain.Name + " is capped by the server's MaxCaptainConcurrency (" + effective + "). Update settings.json or add a captain.";
                    advice.Action = null;
                    advice.Setting = "Concurrency.MaxCaptainConcurrency";
                }
                blocker.Advice.Add(advice);
            }
        }

        #endregion
    }
}
