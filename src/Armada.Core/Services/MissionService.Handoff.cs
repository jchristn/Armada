namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Pipeline handoff and recovery: preparing the next stage, re-driving dangling handoffs, settling the stages behind
    /// a failed one, cascading cancellation and settling the voyage.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        /// <inheritdoc />
        public async Task<int> SettleStagesBehindFailedDependenciesAsync(CancellationToken token = default)
        {
            List<Mission> pending = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false);
            HashSet<string> settled = new HashSet<string>(StringComparer.Ordinal);
            foreach (Mission stage in pending.Where(m => !String.IsNullOrEmpty(m.DependsOnMissionId) && !String.IsNullOrEmpty(m.VoyageId)))
            {
                if (settled.Contains(stage.DependsOnMissionId!)) continue;
                Mission? dependency = await _Database.Missions.ReadAsync(stage.DependsOnMissionId!, token).ConfigureAwait(false);
                if (dependency == null) continue;
                if (dependency.Status != MissionStatusEnum.Failed && dependency.Status != MissionStatusEnum.LandingFailed && dependency.Status != MissionStatusEnum.Cancelled) continue;
                settled.Add(dependency.Id);
                _Logging.Warn(_Header + "mission " + dependency.Id + " ended " + dependency.Status + " without settling the stages after it; settling them now");
                await CancelDependentPipelineStagesAsync(dependency, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(dependency.VoyageId, token).ConfigureAwait(false);
            }
            return settled.Count;
        }

        /// <summary>
        /// Recover pipeline handoffs that dangled: a mission reached WorkProduced but its Pending downstream stage was never
        /// prepared (branch and context not copied), so it could never be dispatched. Re-drives the handoff for any such
        /// mission. An Architect is reconciled against its own plan instead (an interrupted fan-out is finished without
        /// creating anything twice). Returns the number re-driven.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count of missions whose handoff was re-driven.</returns>
        public async Task<int> RecoverDanglingHandoffsAsync(CancellationToken token = default)
        {
            int redriven = 0;
            List<Mission> workProduced = await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.WorkProduced, token).ConfigureAwait(false);

            foreach (Mission produced in workProduced)
            {
                if (String.IsNullOrEmpty(produced.VoyageId)) continue;
                if (PersonaCatalog.Matches(produced.Persona, PersonaCatalog.Architect))
                {
                    try
                    {
                        if (await TryRecoverArchitectFanOutAsync(produced, token).ConfigureAwait(false)) redriven++;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                    {
                        _Logging.Warn(_Header + "error recovering the fan-out of Architect mission " + produced.Id + ": " + ex.ToString());
                    }
                    continue;
                }

                // A completion still being processed prepares its own handoff; re-driving it here would run the handoff twice.
                if (IsCompletionInFlight(produced.Id)) continue;

                // Recovery never bypasses the gates completion applies: a Judge's work moves on only with a substantiated PASS.
                if (PersonaCatalog.Matches(produced.Persona, PersonaCatalog.Judge)
                    && (JudgeVerdictParser.Parse(produced.AgentOutput) != JudgeVerdictEnum.Pass || !TryValidateJudgePassOutput(produced.AgentOutput, out _)))
                {
                    // Recovery runs every health cycle; say this once per mission, not every few seconds.
                    if (_RefusedHandoffWarnings.TryAdd(produced.Id, 0))
                        _Logging.Warn(_Header + "not re-driving handoff for Judge mission " + produced.Id + ": its recorded output is not a substantiated PASS");
                    continue;
                }

                List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(produced.VoyageId, token).ConfigureAwait(false);
                List<Mission> pendingDependents = voyageMissions
                    .Where(m => m.DependsOnMissionId == produced.Id && m.Status == MissionStatusEnum.Pending)
                    .ToList();
                if (pendingDependents.Count == 0) continue;

                bool anyUnprepared = pendingDependents.Any(dep => !IsPipelineHandoffPrepared(dep, produced));
                if (!anyUnprepared) continue;

                _Logging.Warn(_Header + "re-driving dangling pipeline handoff for WorkProduced mission " + produced.Id +
                    " (" + pendingDependents.Count + " pending dependent(s) not prepared)");
                try
                {
                    await TryHandoffToNextStageAsync(produced, token).ConfigureAwait(false);
                    Mission? afterHandoff = await _Database.Missions.ReadAsync(produced.Id, token).ConfigureAwait(false);
                    if (afterHandoff?.Status == MissionStatusEnum.Failed)
                    {
                        await CancelDependentPipelineStagesAsync(afterHandoff, token).ConfigureAwait(false);
                        await UpdateVoyageTerminalStatusAsync(afterHandoff.VoyageId, token).ConfigureAwait(false);
                    }
                    redriven++;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error re-driving handoff for mission " + produced.Id + ": " + ex.ToString());
                }
            }

            return redriven;
        }

        private const string _ArchitectFanOutCompleteMarker = "<!-- armada:fanout-complete -->";

        private async Task<bool> TryHandoffToNextStageAsync(Mission completedMission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(completedMission.VoyageId)) return false;

            // Find missions that depend on this completed mission
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(completedMission.VoyageId, token).ConfigureAwait(false);
            List<Mission> dependentMissions = voyageMissions.Where(m =>
                m.DependsOnMissionId == completedMission.Id &&
                m.Status == MissionStatusEnum.Pending).ToList();

            if (dependentMissions.Count == 0) return false;

            // Special handling for Architect stage: parse output into new missions
            if (PersonaCatalog.Matches(completedMission.Persona, PersonaCatalog.Architect))
            {
                // A single valid plan outside a closed armada-plan fence is re-fenced (appended below the original output)
                // instead of being sent back for a format repair; two plan blocks or an invalid plan are never repaired.
                if (!ArchitectPlanParser.TryParse(completedMission.AgentOutput, out _, out string? canonicalError) && canonicalError == null)
                    await TryRepairArchitectPlanAsync(completedMission, token).ConfigureAwait(false);

                ArchitectPlanParser.TryParse(completedMission.AgentOutput, out _, out string? planError);
                if (planError != null)
                {
                    if (await TryScheduleOutputRepairAsync(completedMission, planError, ArchitectPlanParser.FormatInstructions(), token).ConfigureAwait(false))
                        return false;

                    completedMission.FailureReason = AppendRepairHistory(planError, completedMission);
                    completedMission.Status = MissionStatusEnum.Failed;
                    completedMission.FailureKind = MissionFailureKindEnum.InvalidOutput;
                    completedMission.CompletedUtc = DateTime.UtcNow;
                    completedMission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(completedMission, token).ConfigureAwait(false);
                    return false;
                }
                List<ParsedArchitectMission> parsed = ParseArchitectOutput(completedMission);
                if (parsed.Count > 0)
                {
                    await ProjectArchitectMissionsToLogAsync(completedMission, parsed, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "architect produced " + parsed.Count + " mission definitions");

                    foreach (Mission nextMission in dependentMissions)
                    {
                if (PersonaCatalog.Matches(nextMission.Persona, PersonaCatalog.Worker))
                {
                    // Update the first parsed mission into this existing Worker mission slot
                    ParsedArchitectMission first = parsed[0];
                    nextMission.Title = first.Title + " [Worker]";
                    nextMission.Description = ArchitectHandoffMarker + "\n" + first.Description + await BuildArchitectSliceContextAsync(completedMission, parsed, 0, token).ConfigureAwait(false);
                    nextMission.WaitForVoyageWorkers = first.WaitForOtherMissions;
                    nextMission.BranchName = null;
                    nextMission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(nextMission, token).ConfigureAwait(false);
                    await RetitleDependentChainAsync(voyageMissions, nextMission, first.Title, first.Description, token).ConfigureAwait(false);

                    // Find what depends on this worker mission (Judge, Test Engineer stages)
                    // Create additional worker missions for remaining parsed items
                    for (int i = 1; i < parsed.Count; i++)
                        await CreateArchitectSliceAsync(completedMission, nextMission, voyageMissions, parsed, i, token).ConfigureAwait(false);
                }
            }

                    await ApplyArchitectMissionDependenciesAsync(completedMission, parsed, token).ConfigureAwait(false);
                    await MarkArchitectFanOutCompleteAsync(completedMission, token).ConfigureAwait(false);
                    return true; // Architect special handling complete, skip normal handoff
                }

                bool hadArchitectMarkers = !String.IsNullOrEmpty(completedMission.AgentOutput) &&
                    completedMission.AgentOutput.Contains("[ARMADA:MISSION]", StringComparison.Ordinal);

                string failureReason = hadArchitectMarkers
                    ? "Architect produced no valid [ARMADA:MISSION] definitions in output"
                    : "Architect produced no ```" + ArchitectPlanParser.FenceInfo + " plan block and no [ARMADA:MISSION] markers in output";

                // An absent plan is a format failure, not a judgment about the work: run the Architect again with the
                // exact contract before failing the voyage. Narration is never inferred into missions.
                if (await TryScheduleOutputRepairAsync(completedMission, failureReason, ArchitectPlanParser.FormatInstructions(), token).ConfigureAwait(false))
                    return false;

                failureReason = AppendRepairHistory(failureReason, completedMission);

                _Logging.Warn(_Header + "architect mission " + completedMission.Id +
                    " produced no valid mission definitions -- marking as failed");
                completedMission.Status = MissionStatusEnum.Failed;
                completedMission.FailureReason = failureReason;
                completedMission.FailureKind = MissionFailureKindEnum.InvalidOutput;
                completedMission.CompletedUtc = DateTime.UtcNow;
                completedMission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(completedMission, token).ConfigureAwait(false);
                return false;
            }

            // Stage-lag hardening: the just-completed stage's dock may have committed on a detached HEAD,
            // leaving its produced commit off the shared branch ref. The next stage reuses that same branch,
            // so safely advance the branch before handing off. An explicit refusal fails the handoff;
            // transport exceptions retain the existing best-effort logging behavior.
            if (!await HardenStageBranchAsync(completedMission, token).ConfigureAwait(false)) return false;

            foreach (Mission nextMission in dependentMissions)
            {
                // The persona template supplies role instructions; keep the persisted handoff concise.
                nextMission.Description = StripPipelineHandoff(nextMission.Description)
                    + PipelineHandoffContext.Build(completedMission);
                nextMission.BranchName = completedMission.BranchName;
                nextMission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(nextMission, token).ConfigureAwait(false);

                _Logging.Debug(_Header + "pipeline handoff: prepared mission " + nextMission.Id +
                    " (" + nextMission.Persona + ") with context from " + completedMission.Id +
                    " (" + completedMission.Persona + ")");

            }

            return true;
        }

        /// <summary>
        /// Force-advance the shared pipeline branch to the just-completed stage's produced commit. A prior
        /// stage's dock can end on a detached HEAD (its commit is not on the branch ref), so the next stage --
        /// which reuses the same branch -- would otherwise check out stale code. Resolve the dock's live HEAD
        /// and lift it onto the branch ref without rewinding newer work. A refused update blocks downstream
        /// dispatch with a recoverable commit reference and remediation. Transport exceptions retain the existing
        /// best-effort behavior.
        /// </summary>
        private async Task<bool> HardenStageBranchAsync(Mission completedMission, CancellationToken token)
        {
            if (_Git == null) return true;
            if (String.IsNullOrEmpty(completedMission.BranchName) || String.IsNullOrEmpty(completedMission.DockId)) return true;

            Dock? dock = !String.IsNullOrEmpty(completedMission.TenantId)
                ? await _Database.Docks.ReadAsync(completedMission.TenantId, completedMission.DockId!, token).ConfigureAwait(false)
                : await _Database.Docks.ReadAsync(completedMission.DockId!, token).ConfigureAwait(false);
            DockHost? harborHost = HarborHostFor(dock);
            IGitService git = harborHost != null ? harborHost.Git : _Git;
            if (dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !await DockDirectoryExistsAsync(harborHost, dock.WorktreePath!, token).ConfigureAwait(false)) return true;

            try
            {
                string? headCommit = await git.GetHeadCommitHashAsync(dock.WorktreePath!, token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(headCommit)) return true;

                // This also prevents rewinds when talking to an older Harbor implementation.
                if (await git.IsAncestorAsync(dock.WorktreePath!, headCommit!, completedMission.BranchName!, token).ConfigureAwait(false) == true) return true;
                bool advanced = await git.ForceAdvanceBranchAsync(dock.WorktreePath!, completedMission.BranchName!, headCommit!, token).ConfigureAwait(false);
                if (!advanced)
                {
                    completedMission.Status = MissionStatusEnum.Failed;
                    completedMission.CommitHash = headCommit;
                    completedMission.FailureKind = MissionFailureKindEnum.LandingConflict;
                    completedMission.FailureReason = "Pipeline handoff could not safely advance branch " + completedMission.BranchName
                        + " to stage commit " + headCommit + ". No downstream stage was dispatched. "
                        + "Remediation: inspect the branch and saved stage commit; integrate any divergent work without resetting newer commits, then restart the failed stage. Check Harbor/git availability if the update itself failed.";
                    completedMission.CompletedUtc = DateTime.UtcNow;
                    completedMission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(completedMission, token).ConfigureAwait(false);
                    return false;
                }
                if (advanced)
                {
                    _Logging.Debug(_Header + "stage-lag hardening: advanced branch " + completedMission.BranchName +
                        " to " + headCommit + " from dock " + dock.Id + " before handoff from mission " + completedMission.Id);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "stage-lag hardening failed for mission " + completedMission.Id + ": " + ex.ToString());
            }
            return true;
        }

        private async Task CancelDependentPipelineStagesAsync(Mission failedMission, CancellationToken token)
        {
            if (failedMission == null) throw new ArgumentNullException(nameof(failedMission));
            if (String.IsNullOrEmpty(failedMission.VoyageId)) return;

            // A failed slice also stops the voyage's final integration, which would otherwise run without its work.
            if (failedMission.Status == MissionStatusEnum.Failed || failedMission.Status == MissionStatusEnum.LandingFailed)
                await CancelIntegrationsBlockedByFailureAsync(failedMission, token).ConfigureAwait(false);

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(failedMission.VoyageId, token).ConfigureAwait(false);
            List<Mission> directDependents = voyageMissions.Where(m =>
                m.DependsOnMissionId == failedMission.Id &&
                (m.Status == MissionStatusEnum.Pending ||
                 m.Status == MissionStatusEnum.Assigned ||
                 m.Status == MissionStatusEnum.InProgress ||
                 m.Status == MissionStatusEnum.Testing ||
                 m.Status == MissionStatusEnum.Review)).ToList();

            bool block = _Settings.BlockDependentsOfFailedStages;
            foreach (Mission dependent in directDependents)
            {
                // Blocking applies to stages still waiting; one that somehow started is cancelled either way.
                bool blockThis = block && dependent.Status == MissionStatusEnum.Pending;
                dependent.Status = blockThis ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled;
                dependent.FailureReason = "Blocked by failed dependency " + failedMission.Id
                    + (blockThis ? ". Restart that mission to continue, or cancel the voyage." : String.Empty);
                dependent.FailureKind = MissionFailureKindEnum.DependencyFailed;
                dependent.CompletedUtc = blockThis ? null : DateTime.UtcNow;
                dependent.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(dependent, token).ConfigureAwait(false);
                _Logging.Info(_Header + (blockThis ? "blocked" : "cancelled") + " dependent mission " + dependent.Id +
                    " because upstream mission " + failedMission.Id + " ended in " + failedMission.Status);
                await CancelDependentPipelineStagesAsync(dependent, token).ConfigureAwait(false);
            }
        }

        private async Task UpdateVoyageTerminalStatusAsync(string? voyageId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return;

            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return;

            List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            VoyageStatusEnum? settled = await VoyageSettlement.DecideAsync(missions, IsCompletionInFlight,
                () => MergeQueueMissionActivity.GetMissionIdsWithActiveEntriesAsync(_Database, token)).ConfigureAwait(false);
            if (settled == null) return;

            await VoyageSettlement.ApplyAsync(_Database, voyage, settled.Value, missions, token, _Logging.Warn).ConfigureAwait(false);
            _Logging.Info(_Header + "voyage " + voyage.Id + " reached terminal status " + voyage.Status + " during mission completion");
        }

        #endregion
    }
}
