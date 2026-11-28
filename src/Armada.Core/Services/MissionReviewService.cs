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
    /// Approves, denies, and restarts missions under review.
    /// </summary>
    internal sealed class MissionReviewService
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly IDockService _Docks;
        private readonly string _Header;
        private readonly LoggingModule _Logging;

        // Serializes review decisions so an approve and a deny of the same stage cannot both apply.
        private readonly SemaphoreSlim _ReviewDecisionGate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionReviewService(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Docks = context.Docks;
            _Header = context.Header;
            _Logging = context.Logging;
        }

        #endregion

        #region Internal-Methods

        internal async Task ReclaimMissionDockAsync(string dockId, CancellationToken token, string? forceReason = null,
            [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            if (String.IsNullOrEmpty(dockId)) return;

            try
            {
                await _Docks.ReclaimAsync(dockId, token: token, forceReason: forceReason, caller: caller).ConfigureAwait(false);
            }
            catch (Exception reclaimEx)
            {
                _Logging.Warn(_Header + "error reclaiming dock " + dockId + ": " + reclaimEx.Message);
            }
        }

        /// <inheritdoc />
        internal async Task<Mission> ApproveReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default)
        {
            await _ReviewDecisionGate.WaitAsync(token).ConfigureAwait(false);
            try { return await ApproveReviewCoreAsync(missionId, reviewedByUserId, comment, conditional, token).ConfigureAwait(false); }
            finally { _ReviewDecisionGate.Release(); }
        }

        /// <inheritdoc />
        internal async Task<Mission> RestartAsync(Mission mission, string? title = null, string? description = null, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Status != MissionStatusEnum.Pending && mission.Status != MissionStatusEnum.Failed
                && mission.Status != MissionStatusEnum.Cancelled && mission.Status != MissionStatusEnum.LandingFailed)
                throw new InvalidOperationException("Only Pending, Failed, LandingFailed, or Cancelled missions can be restarted (current: " + mission.Status + ").");

            // A verifier that blocked on a product defect cannot correct product code: restarting it would only re-run
            // verification against the same commit and block again. Send the work back to its Worker instead, as the
            // failure's remediation says, with the operator's description as guidance for that Worker.
            if (IsBlockedVerifierAwaitingWorker(mission))
                return await RestartBlockedVerifierFromWorkerAsync(mission, description, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(title)) mission.Title = title!;
            if (!String.IsNullOrEmpty(description)) mission.Description = description!;

            // A Pending mission has nothing to reset: apply the new instructions and try to start it now.
            if (mission.Status == MissionStatusEnum.Pending)
            {
                mission.LastUpdateUtc = DateTime.UtcNow;
                mission = await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                _Logging.Info(_Header + "restart requested for pending mission " + mission.Id + "; attempting dispatch");
                return await TryDispatchRestartedMissionAsync(mission, token).ConfigureAwait(false);
            }

            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge))
            {
                string? judgeOutput = StageSubmissionText.EffectiveOutput(mission);
                JudgeVerdictEnum previousVerdict = JudgeVerdictParser.Parse(judgeOutput);
                if ((previousVerdict == JudgeVerdictEnum.Fail || previousVerdict == JudgeVerdictEnum.NeedsRevision)
                    && !String.IsNullOrWhiteSpace(judgeOutput))
                {
                    string priorFindings = judgeOutput!.Trim();
                    const int maxFindings = 12000;
                    if (priorFindings.Length > maxFindings)
                        priorFindings = priorFindings.Substring(0, maxFindings / 2)
                            + "\n(previous review excerpt truncated)\n"
                            + priorFindings.Substring(priorFindings.Length - maxFindings / 2);
                    string? earlierFindings = MissionNotes.Get(mission, MissionNoteKindEnum.PriorJudgeFindings);
                    if (earlierFindings == null || !earlierFindings.Contains(priorFindings, StringComparison.Ordinal))
                        MissionNotes.Set(mission, MissionNoteKindEnum.PriorJudgeFindings, (earlierFindings == null ? "" : earlierFindings + "\n\n")
                            + "## Previous Judge findings (historical; recheck on restart)\n"
                            + "Re-evaluate every finding below against the current branch and current requirements. "
                            + "Report which are resolved, superseded, or still reproducible, and check the full mission scope. "
                            + "This is historical evidence, not the current verdict or a reason to skip independent validation.\n\n"
                            + priorFindings);
                }
            }

            // A later pipeline stage builds on the earlier stages' commits: keep their branch. A first stage starts
            // over on a fresh branch.
            string? keptBranch = null;
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
            {
                Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId!, token).ConfigureAwait(false);
                keptBranch = dependency?.BranchName ?? mission.BranchName;
            }

            MissionStatusEnum restartFrom = mission.Status;
            ResetForRestart(mission, keptBranch);
            if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { restartFrom }, MissionStatusEnum.Pending, null, token).ConfigureAwait(false)) return mission;

            List<string> restoredIds = new List<string>();
            if (!String.IsNullOrEmpty(mission.VoyageId))
                restoredIds.AddRange(await ReopenVoyageAndRestoreDependentsAsync(mission, new HashSet<string>(StringComparer.Ordinal) { mission.Id }, token).ConfigureAwait(false));

            // D18: restarting a parent brings back the descendants that were cancelled only because it failed.
            Mission root = mission;
            for (int hops = 0; !String.IsNullOrEmpty(root.DependsOnMissionId) && hops < 32; hops++)
            {
                Mission? upstream = await _Database.Missions.ReadAsync(root.DependsOnMissionId!, token).ConfigureAwait(false);
                if (upstream == null) break;
                root = upstream;
            }
            restoredIds.AddRange(await _Context.MissionHierarchyService.RestoreDescendantsCancelledForParentAsync(root, token).ConfigureAwait(false));

            _Logging.Info(_Header + "restarted mission " + mission.Id + (keptBranch != null ? " on branch " + keptBranch : ""));
            Mission restarted = await TryDispatchRestartedMissionAsync(mission, token).ConfigureAwait(false);
            restarted.RestoredMissionIds = restoredIds.Distinct(StringComparer.Ordinal).Where(id => id != restarted.Id).Take(CollectionLimits.MaxLinkedIds).ToList();
            return restarted;
        }

        /// <inheritdoc />
        internal async Task<Mission> DenyReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default)
        {
            await _ReviewDecisionGate.WaitAsync(token).ConfigureAwait(false);
            try { return await DenyReviewCoreAsync(missionId, reviewedByUserId, comment, actionOverride, token).ConfigureAwait(false); }
            finally { _ReviewDecisionGate.Release(); }
        }

        #endregion

        #region Private-Methods

        private async Task<Mission> RequireReviewMissionAsync(string missionId, CancellationToken token)
        {
            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null) throw new KeyNotFoundException("Mission not found: " + missionId);
            if (mission.Status != MissionStatusEnum.Review || !mission.RequiresReview)
            {
                throw new InvalidOperationException("Mission " + missionId + " is not waiting for an explicit review decision.");
            }

            return mission;
        }

        private async Task<Dock?> ReadMissionDockAsync(Mission mission, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (String.IsNullOrEmpty(mission.DockId)) return null;

            return !String.IsNullOrEmpty(mission.TenantId)
                ? await _Database.Docks.ReadAsync(mission.TenantId, mission.DockId, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
        }

        private static string? NormalizeReviewComment(string? comment)
        {
            if (String.IsNullOrWhiteSpace(comment)) return null;
            return comment.Trim();
        }

        private static string BuildReviewDeniedFailureReason(string comment)
        {
            return "Review denied: " + comment;
        }

        private static string ReviewFeedbackNote(string reviewComment)
        {
            return "---\n\n## Review Feedback\n" + reviewComment.Trim() + "\n\nAddress this feedback and continue the mission on the existing branch.";
        }

        private static string ReviewerGuidanceNote(string reviewComment)
        {
            return "---\n\n## Reviewer Guidance (from prior stage approval)\n" + reviewComment.Trim()
                + "\n\nThe previous stage was conditionally approved with the guidance above. Take it into account as you carry out this stage.";
        }

        /// <summary>
        /// After a conditional approval, fold the reviewer's guidance into the pending next-stage mission(s)
        /// that depend on the just-approved mission, before they dispatch.
        /// </summary>
        private async Task ApplyConditionalFeedbackToNextStagesAsync(Mission mission, string reviewComment, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VoyageId)) return;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
            foreach (Mission next in voyageMissions)
            {
                if (next.DependsOnMissionId != mission.Id) continue;
                if (next.Status != MissionStatusEnum.Pending) continue;

                MissionNotes.Set(next, MissionNoteKindEnum.ReviewerGuidance, ReviewerGuidanceNote(reviewComment));
                next.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(next, token).ConfigureAwait(false);
            }
        }

        private async Task<Mission> ApproveReviewCoreAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission mission = await RequireReviewMissionAsync(missionId, token).ConfigureAwait(false);
            if (reviewedByUserId != null && mission.ReviewDeadlineUtc <= DateTime.UtcNow && await _Context.StageCompletionHandler.ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false) != null)
                throw new InvalidOperationException("The approval timer has expired; its configured default answer applies.");
            bool hasDependentPipelineStages = await _Context.ArchitectFanOutService.HasDependentPipelineStages(mission.VoyageId, mission.Id, token).ConfigureAwait(false);

            mission.ReviewComment = NormalizeReviewComment(comment);
            mission.ReviewedByUserId = reviewedByUserId;
            mission.ReviewedUtc = DateTime.UtcNow;

            if (hasDependentPipelineStages)
            {
                mission.FailureReason = null;
                mission.FailureKind = null;
                if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Review }, MissionStatusEnum.Complete, null, token).ConfigureAwait(false)) return mission;


                await _Context.MissionHandoffService.TryHandoffToNextStageAsync(mission, token).ConfigureAwait(false);

                // A handoff can fail the approved stage (for example an Architect whose plan cannot be parsed). Then
                // the following stages can never start: cancel them and settle the voyage instead of leaving them
                // Pending forever.
                Mission? afterHandoff = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                if (afterHandoff != null && afterHandoff.Status == MissionStatusEnum.Failed)
                {
                    await _Context.MissionHandoffService.CancelDependentPipelineStagesAsync(afterHandoff, token).ConfigureAwait(false);
                    await _Context.MissionHandoffService.UpdateVoyageTerminalStatusAsync(afterHandoff.VoyageId, token).ConfigureAwait(false);
                    return afterHandoff;
                }

                // Conditionally Approve: fold the reviewer's guidance into the freshly-prepared next stage(s)
                // before they dispatch, so the next captain is required to take that feedback into account.
                if (conditional && !String.IsNullOrWhiteSpace(mission.ReviewComment))
                {
                    await ApplyConditionalFeedbackToNextStagesAsync(mission, mission.ReviewComment!, token).ConfigureAwait(false);
                }

                _Context.MissionWaitCoordinator.RequestDispatch();
                await _Context.MissionHandoffService.UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);

                Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                return refreshed ?? mission;
            }

            Dock? dock = await ReadMissionDockAsync(mission, token).ConfigureAwait(false);
            mission.FailureReason = null;
            mission.FailureKind = null;
            if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Review }, MissionStatusEnum.WorkProduced, null, token).ConfigureAwait(false)) return mission;

            if (dock == null)
            {
                mission.FailureReason = "Review approved but the mission dock was unavailable for landing.";
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.CompletedUtc = DateTime.UtcNow;
                if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.WorkProduced }, MissionStatusEnum.LandingFailed, null, token).ConfigureAwait(false)) return mission;
                await _Context.MissionFailureCapture.RecordFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.ReviewApprovedDockMissing, null, context =>
                {
                    context.GitFacts = new BlackBoxGitFacts { WorktreeExisted = false };
                    context.Note = "The review was approved but dock " + (mission.DockId ?? "(none)") + " could not be read.";
                }, token).ConfigureAwait(false);
                await _Context.MissionHandoffService.UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                return mission;
            }

            if (_Service.OnMissionComplete != null)
            {
                await _Service.OnMissionComplete.Invoke(mission, dock).ConfigureAwait(false);
            }

            // Unlanded work keeps its dock (the lease refuses the reclaim) so retry_landing can land it; an opened pull
            // request has published the branch, so its dock may go.
            Mission? afterApproval = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            await ReclaimMissionDockAsync(dock.Id, token,
                afterApproval?.Status == MissionStatusEnum.PullRequestOpen ? "pull request opened: the branch is published" : null).ConfigureAwait(false);
            _Context.MissionWaitCoordinator.RequestDispatch();
            await _Context.MissionHandoffService.UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);

            Mission? landed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            return landed ?? mission;
        }

        private async Task<List<string>> ReopenVoyageAndRestoreDependentsAsync(Mission mission, HashSet<string> seedIds, CancellationToken token)
        {
            Voyage? voyage = await _Database.Voyages.ReadAsync(mission.VoyageId!, token).ConfigureAwait(false);
            if (voyage != null && (voyage.Status == VoyageStatusEnum.Failed || voyage.Status == VoyageStatusEnum.Cancelled || voyage.Status == VoyageStatusEnum.Complete))
            {
                voyage.Status = VoyageStatusEnum.InProgress;
                voyage.CompletedUtc = null;
                voyage.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
                _Logging.Info(_Header + "reopened voyage " + voyage.Id + " for restarted mission " + mission.Id);
            }

            // Restore the stages cancelled because this one failed (the chain hanging off it), and a final integration
            // slice stopped because of it, which keeps its branch: its own dependency was not restarted.
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId!, token).ConfigureAwait(false);
            HashSet<string> chain = new HashSet<string>(seedIds, StringComparer.Ordinal);
            foreach (Mission integration in voyageMissions.Where(m => (m.Status == MissionStatusEnum.Cancelled || m.Status == MissionStatusEnum.Blocked)
                && m.FailureKind == MissionFailureKindEnum.DependencyFailed && m.FailureReason != null
                && m.FailureReason.StartsWith(IntegrationBarrierService._IntegrationBlockedPrefix, StringComparison.Ordinal)
                && m.FailureReason.Contains("(" + mission.Id + ")", StringComparison.Ordinal)).ToList())
            {
                MissionStatusEnum integrationFrom = integration.Status;
                ResetForRestart(integration, integration.BranchName);
                MissionNotes.Remove(integration, MissionNoteKindEnum.IntegrationMerge);
                if (!await _Service.Transitions.TryTransitionAsync(integration, new[] { integrationFrom }, MissionStatusEnum.Pending, null, token).ConfigureAwait(false)) continue;
                chain.Add(integration.Id);
                _Logging.Info(_Header + "restored integration mission " + integration.Id + " with restarted mission " + mission.Id);
            }
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Mission candidate in voyageMissions)
                {
                    if (chain.Contains(candidate.Id) || String.IsNullOrEmpty(candidate.DependsOnMissionId) || !chain.Contains(candidate.DependsOnMissionId!)) continue;
                    if ((candidate.Status != MissionStatusEnum.Cancelled && candidate.Status != MissionStatusEnum.Blocked)
                        || candidate.FailureKind != MissionFailureKindEnum.DependencyFailed) continue;
                    chain.Add(candidate.Id);
                    MissionStatusEnum candidateFrom = candidate.Status;
                    ResetForRestart(candidate, null);
                    await _Service.Transitions.TryTransitionAsync(candidate, new[] { candidateFrom }, MissionStatusEnum.Pending, null, token).ConfigureAwait(false);
                    grew = true;
                }
            }
            return chain.Where(id => !seedIds.Contains(id)).ToList();
        }

        private static bool IsBlockedVerifierAwaitingWorker(Mission mission)
        {
            return mission.Status == MissionStatusEnum.Failed
                && mission.FailureKind == MissionFailureKindEnum.JudgeRejected
                && VerifierContract.Applies(mission.Persona)
                && VerifierContract.IsBlocked(StageSubmissionText.EffectiveOutput(mission))
                && !String.IsNullOrEmpty(mission.VoyageId)
                && !String.IsNullOrEmpty(mission.DependsOnMissionId);
        }

        private async Task<Mission> RestartBlockedVerifierFromWorkerAsync(Mission verifier, string? guidance, CancellationToken token)
        {
            string? unavailable = null;
            string? nextReviewerId = null;
            bool scheduled = await _Context.MissionReworkService.TryScheduleReworkAsync(verifier, token, reason => unavailable = reason,
                operatorRestart: true, onReviewerCreated: id => nextReviewerId = id, operatorGuidance: guidance).ConfigureAwait(false);
            if (!scheduled || nextReviewerId == null)
                throw new InvalidOperationException("The blocked " + verifier.Persona + " stage " + verifier.Id + " cannot be sent back to its Worker: "
                    + (unavailable ?? "no rework chain was written") + " Restarting the " + verifier.Persona + " alone would re-verify the unchanged product code.");

            // The blocked stage has done its job (it found the defect), exactly as when automatic rework was available:
            // it stays as produced work and the new chain hangs off it.
            verifier.FailureReason = null;
            verifier.FailureKind = null;
            if (!await _Service.Transitions.TryTransitionAsync(verifier, new[] { MissionStatusEnum.Failed }, MissionStatusEnum.WorkProduced, null, token).ConfigureAwait(false)) return verifier;

            // The stages cancelled after the blocked stage now follow the new verifier: restore them with it.
            await ReopenVoyageAndRestoreDependentsAsync(verifier, new HashSet<string>(StringComparer.Ordinal) { verifier.Id, nextReviewerId }, token).ConfigureAwait(false);

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(verifier.VoyageId!, token).ConfigureAwait(false);
            Mission? rework = voyageMissions.Where(m => m.DependsOnMissionId == verifier.Id && PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker)
                && m.Status == MissionStatusEnum.Pending).OrderByDescending(m => m.CreatedUtc).FirstOrDefault();
            if (rework == null)
                throw new InvalidOperationException("The rework Worker for blocked " + verifier.Persona + " stage " + verifier.Id + " was not found after scheduling.");

            _Logging.Info(_Header + "restart of blocked " + verifier.Persona + " mission " + verifier.Id + " sent the work back to Worker mission "
                + rework.Id + " (rework round " + rework.ReworkRound + ", re-verification " + nextReviewerId + ")");
            return await TryDispatchRestartedMissionAsync(rework, token).ConfigureAwait(false);
        }

        private async Task<Mission> TryDispatchRestartedMissionAsync(Mission mission, CancellationToken token)
        {
            // Normal dispatch rules apply (voyage, pipeline dependency, vessel locks, requested captain). When no
            // captain can take it the mission stays Pending and the regular dispatcher picks it up later.
            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                try
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId!, token).ConfigureAwait(false);
                    if (vessel != null) await _Context.MissionLauncher.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "restarted mission " + mission.Id + " stays Pending: immediate dispatch failed: " + ex.Message);
                }
            }

            Mission? latest = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            return latest ?? mission;
        }

        private static void ResetForRestart(Mission mission, string? branchName)
        {
            // A restarted Architect fans out again, and that new fan-out must be recoverable too.
            mission.FanOutCompletedUtc = null;
            mission.CaptainId = null;
            mission.BranchName = branchName;
            mission.PrUrl = null;
            mission.CommitHash = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.DiffSnapshot = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.ReviewComment = null;
            mission.ReviewedByUserId = null;
            mission.ReviewedUtc = null;
            mission.ReviewRequestedUtc = null;
            mission.ReviewDeadlineUtc = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
        }

        private async Task<Mission> DenyReviewCoreAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission mission = await RequireReviewMissionAsync(missionId, token).ConfigureAwait(false);
            if (reviewedByUserId != null && mission.ReviewDeadlineUtc <= DateTime.UtcNow && await _Context.StageCompletionHandler.ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false) != null)
                throw new InvalidOperationException("The approval timer has expired; its configured default answer applies.");
            Dock? dock = await ReadMissionDockAsync(mission, token).ConfigureAwait(false);
            string reviewComment = NormalizeReviewComment(comment) ?? "Review denied. Rework is required before this stage can continue.";

            // "More Work Required" and "Deny" surface as an explicit action from the reviewer; fall back to the
            // mission's configured deny action when the caller does not specify one.
            ReviewDenyActionEnum effectiveAction = actionOverride ?? mission.ReviewDenyAction;

            mission.ReviewComment = reviewComment;
            mission.ReviewedByUserId = reviewedByUserId;
            mission.ReviewedUtc = DateTime.UtcNow;

            if (dock != null)
            {
                // Rework and retries take the branch into a new dock, so a denied review always releases this one.
                await ReclaimMissionDockAsync(dock.Id, token, "review denied").ConfigureAwait(false);
            }

            if (effectiveAction == ReviewDenyActionEnum.FailPipeline)
            {
                // The approval timer's default answer arrives without a reviewer once the deadline has passed.
                bool timedOut = reviewedByUserId == null && mission.ReviewDeadlineUtc.HasValue && mission.ReviewDeadlineUtc.Value <= DateTime.UtcNow;
                MissionStatusEnum deniedStatusBefore = mission.Status;
                mission.FailureReason = BuildReviewDeniedFailureReason(reviewComment);
                mission.FailureKind = MissionFailureKindEnum.ReviewDenied;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.ProcessId = null;
                mission.DockId = null;
                if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { deniedStatusBefore }, MissionStatusEnum.Failed, null, token).ConfigureAwait(false)) return mission;
                await _Context.MissionFailureCapture.RecordFailureAsync(mission, deniedStatusBefore, timedOut ? BlackBoxStepEnum.ReviewTimeout : BlackBoxStepEnum.ReviewDenied, dock, context =>
                {
                    context.Note = timedOut ? "The approval timer expired and its default answer denied the review." : "Denied by " + (reviewedByUserId ?? "a reviewer") + ".";
                }, token).ConfigureAwait(false);

                await _Context.MissionHandoffService.CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
                await _Context.MissionHandoffService.UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                return mission;
            }

            MissionStatusEnum reworkFrom = mission.Status;
            MissionNotes.Set(mission, MissionNoteKindEnum.ReviewFeedback, ReviewFeedbackNote(reviewComment));
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.PrUrl = null;
            mission.CommitHash = null;
            mission.DiffSnapshot = null;
            mission.AgentOutput = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.TotalRuntimeMs = null;
            if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { reworkFrom }, MissionStatusEnum.Pending, null, token).ConfigureAwait(false)) return mission;


            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel != null)
                {
                    await _Context.MissionLauncher.TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                    Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (refreshed != null) return refreshed;
                }
            }

            return mission;
        }

        #endregion
    }
}
