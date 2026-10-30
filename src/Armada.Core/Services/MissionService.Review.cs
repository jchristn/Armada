namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Review gates and restart: approving or denying a stage held for review, and restarting a mission with the stages that wait on it.
    /// </summary>
    public partial class MissionService
    {
        #region Public-Methods

        /// <inheritdoc />
        public async Task<Mission> ApproveReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default)
        {
            await _ReviewDecisionGate.WaitAsync(token).ConfigureAwait(false);
            try { return await ApproveReviewCoreAsync(missionId, reviewedByUserId, comment, conditional, token).ConfigureAwait(false); }
            finally { _ReviewDecisionGate.Release(); }
        }

        private async Task<Mission> ApproveReviewCoreAsync(string missionId, string? reviewedByUserId, string? comment = null, bool conditional = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission mission = await RequireReviewMissionAsync(missionId, token).ConfigureAwait(false);
            if (reviewedByUserId != null && mission.ReviewDeadlineUtc <= DateTime.UtcNow && await ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false) != null)
                throw new InvalidOperationException("The approval timer has expired; its configured default answer applies.");
            bool hasDependentPipelineStages = await HasDependentPipelineStages(mission.VoyageId, mission.Id, token).ConfigureAwait(false);

            mission.ReviewComment = NormalizeReviewComment(comment);
            mission.ReviewedByUserId = reviewedByUserId;
            mission.ReviewedUtc = DateTime.UtcNow;

            if (hasDependentPipelineStages)
            {
                mission.Status = MissionStatusEnum.Complete;
                mission.FailureReason = null;
                mission.FailureKind = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                await TryHandoffToNextStageAsync(mission, token).ConfigureAwait(false);

                // A handoff can fail the approved stage (for example an Architect whose plan cannot be parsed). Then
                // the following stages can never start: cancel them and settle the voyage instead of leaving them
                // Pending forever.
                Mission? afterHandoff = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                if (afterHandoff != null && afterHandoff.Status == MissionStatusEnum.Failed)
                {
                    await CancelDependentPipelineStagesAsync(afterHandoff, token).ConfigureAwait(false);
                    await UpdateVoyageTerminalStatusAsync(afterHandoff.VoyageId, token).ConfigureAwait(false);
                    return afterHandoff;
                }

                // Conditionally Approve: fold the reviewer's guidance into the freshly-prepared next stage(s)
                // before they dispatch, so the next captain is required to take that feedback into account.
                if (conditional && !String.IsNullOrWhiteSpace(mission.ReviewComment))
                {
                    await ApplyConditionalFeedbackToNextStagesAsync(mission, mission.ReviewComment!, token).ConfigureAwait(false);
                }

                await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);

                Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                return refreshed ?? mission;
            }

            Dock? dock = await ReadMissionDockAsync(mission, token).ConfigureAwait(false);
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            if (dock == null)
            {
                mission.Status = MissionStatusEnum.LandingFailed;
                mission.FailureReason = "Review approved but the mission dock was unavailable for landing.";
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                await RecordFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.ReviewApprovedDockMissing, null, context =>
                {
                    context.GitFacts = new BlackBoxGitFacts { WorktreeExisted = false };
                    context.Note = "The review was approved but dock " + (mission.DockId ?? "(none)") + " could not be read.";
                }, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                return mission;
            }

            if (OnMissionComplete != null)
            {
                await OnMissionComplete.Invoke(mission, dock).ConfigureAwait(false);
            }

            // Unlanded work keeps its dock (the lease refuses the reclaim) so retry_landing can land it; an opened pull
            // request has published the branch, so its dock may go.
            Mission? afterApproval = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            await ReclaimMissionDockAsync(dock.Id, token,
                afterApproval?.Status == MissionStatusEnum.PullRequestOpen ? "pull request opened: the branch is published" : null).ConfigureAwait(false);
            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);

            Mission? landed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            return landed ?? mission;
        }

        /// <inheritdoc />
        public async Task<Mission> RestartAsync(Mission mission, string? title = null, string? description = null, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Status != MissionStatusEnum.Pending && mission.Status != MissionStatusEnum.Failed
                && mission.Status != MissionStatusEnum.Cancelled && mission.Status != MissionStatusEnum.LandingFailed)
                throw new InvalidOperationException("Only Pending, Failed, LandingFailed, or Cancelled missions can be restarted (current: " + mission.Status + ").");

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
                JudgeVerdictEnum previousVerdict = JudgeVerdictParser.Parse(mission.AgentOutput);
                if ((previousVerdict == JudgeVerdictEnum.Fail || previousVerdict == JudgeVerdictEnum.NeedsRevision)
                    && !String.IsNullOrWhiteSpace(mission.AgentOutput))
                {
                    string priorFindings = mission.AgentOutput.Trim();
                    const int maxFindings = 12000;
                    if (priorFindings.Length > maxFindings)
                        priorFindings = priorFindings.Substring(0, maxFindings / 2)
                            + "\n(previous review excerpt truncated)\n"
                            + priorFindings.Substring(priorFindings.Length - maxFindings / 2);
                    if (!(mission.Description ?? String.Empty).Contains(priorFindings, StringComparison.Ordinal))
                        mission.Description = (mission.Description ?? String.Empty)
                            + "\n\n## Previous Judge findings (historical; recheck on restart)\n"
                            + "Re-evaluate every finding below against the current branch and current requirements. "
                            + "Report which are resolved, superseded, or still reproducible, and check the full mission scope. "
                            + "This is historical evidence, not the current verdict or a reason to skip independent validation.\n\n"
                            + priorFindings;
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

            ResetForRestart(mission, keptBranch);
            mission = await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(mission.VoyageId))
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
                HashSet<string> chain = new HashSet<string>(StringComparer.Ordinal) { mission.Id };
                foreach (Mission integration in voyageMissions.Where(m => (m.Status == MissionStatusEnum.Cancelled || m.Status == MissionStatusEnum.Blocked)
                    && m.FailureKind == MissionFailureKindEnum.DependencyFailed && m.FailureReason != null
                    && m.FailureReason.StartsWith(_IntegrationBlockedPrefix, StringComparison.Ordinal)
                    && m.FailureReason.Contains("(" + mission.Id + ")", StringComparison.Ordinal)).ToList())
                {
                    ResetForRestart(integration, integration.BranchName);
                    integration.Description = RemoveIntegrationMergeSection(integration.Description);
                    await _Database.Missions.UpdateAsync(integration, token).ConfigureAwait(false);
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
                        ResetForRestart(candidate, null);
                        await _Database.Missions.UpdateAsync(candidate, token).ConfigureAwait(false);
                        grew = true;
                    }
                }
            }

            _Logging.Info(_Header + "restarted mission " + mission.Id + (keptBranch != null ? " on branch " + keptBranch : ""));
            return await TryDispatchRestartedMissionAsync(mission, token).ConfigureAwait(false);
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
                    if (vessel != null) await TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
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
            if (mission.Description != null && mission.Description.Contains(_ArchitectFanOutCompleteMarker, StringComparison.Ordinal))
                mission.Description = mission.Description.Replace(_ArchitectFanOutCompleteMarker, String.Empty).TrimEnd();
            mission.Status = MissionStatusEnum.Pending;
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

        /// <inheritdoc />
        public async Task<Mission> DenyReviewAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default)
        {
            await _ReviewDecisionGate.WaitAsync(token).ConfigureAwait(false);
            try { return await DenyReviewCoreAsync(missionId, reviewedByUserId, comment, actionOverride, token).ConfigureAwait(false); }
            finally { _ReviewDecisionGate.Release(); }
        }

        private async Task<Mission> DenyReviewCoreAsync(string missionId, string? reviewedByUserId, string? comment = null, ReviewDenyActionEnum? actionOverride = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission mission = await RequireReviewMissionAsync(missionId, token).ConfigureAwait(false);
            if (reviewedByUserId != null && mission.ReviewDeadlineUtc <= DateTime.UtcNow && await ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false) != null)
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
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureReason = BuildReviewDeniedFailureReason(reviewComment);
                mission.FailureKind = MissionFailureKindEnum.ReviewDenied;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.ProcessId = null;
                mission.DockId = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                await RecordFailureAsync(mission, deniedStatusBefore, timedOut ? BlackBoxStepEnum.ReviewTimeout : BlackBoxStepEnum.ReviewDenied, dock, context =>
                {
                    context.Note = timedOut ? "The approval timer expired and its default answer denied the review." : "Denied by " + (reviewedByUserId ?? "a reviewer") + ".";
                }, token).ConfigureAwait(false);

                await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                return mission;
            }

            mission.Status = MissionStatusEnum.Pending;
            mission.Description = ApplyReviewFeedback(mission.Description, reviewComment);
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
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel != null)
                {
                    await TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                    Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (refreshed != null) return refreshed;
                }
            }

            return mission;
        }

        #endregion
    }
}
