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
    /// Pipeline handoff and recovery: preparing the next stage, re-driving dangling handoffs, settling the stages behind
    /// a failed one, cascading cancellation and settling the voyage.
    /// </summary>
    public partial class MissionService
    {
        // Produced missions whose refused handoff was already warned about, so each warns once.
        private readonly MissionFlagSet _RefusedHandoffWarnings = new MissionFlagSet();

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
                    && (JudgeVerdictParser.Parse(StageSubmissionText.EffectiveOutput(produced)) != JudgeVerdictEnum.Pass || !TryValidateJudgePassOutput(StageSubmissionText.EffectiveOutput(produced), out _)))
                {
                    // Recovery runs every health cycle; say this once per mission, not every few seconds.
                    if (_RefusedHandoffWarnings.TrySetFirst(produced.Id))
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
                // RS-6.2: the plan is read once, from a submitted plan when there is one, and handed to the fan-out.
                ArchitectPlanParseResult planResult = ArchitectPlanParser.Parse(StageSubmissionText.EffectiveOutput(completedMission), _Settings.Concurrency.MaxHierarchyDepth);
                if (planResult.Status == ArchitectPlanParseStatusEnum.NoPlanFound
                    && await TryRepairArchitectPlanAsync(completedMission, token).ConfigureAwait(false))
                    planResult = ArchitectPlanParser.Parse(StageSubmissionText.EffectiveOutput(completedMission), _Settings.Concurrency.MaxHierarchyDepth);

                string? planError = planResult.Error;
                List<ParsedArchitectMission> parsed = planError == null ? ParseArchitectOutput(completedMission, planResult.Plan) : new List<ParsedArchitectMission>();
                planError ??= ValidateArchitectHierarchy(parsed);

                // RS-6.4: a plan that exists but breaks the contract (invalid JSON, a rejected field, a bad hierarchy) first
                // gets one format-only turn that restates it with the parser's errors. A missing plan is never restated
                // (narration is not inferred into missions), and an oversized plan must be split, not reformatted.
                if (planError != null && planResult.Status != ArchitectPlanParseStatusEnum.NoPlanFound
                    && planResult.Status != ArchitectPlanParseStatusEnum.TooLarge
                    && await TryFormatRepairArchitectPlanAsync(completedMission, null, planError,
                        planResult.Errors.Count > 0 ? planResult.Errors : new List<string> { planError }, token).ConfigureAwait(false))
                {
                    planResult = ArchitectPlanParser.Parse(completedMission.AgentOutput, _Settings.Concurrency.MaxHierarchyDepth);
                    planError = planResult.Error;
                    parsed = planError == null ? ParseArchitectOutput(completedMission) : new List<ParsedArchitectMission>();
                    planError ??= ValidateArchitectHierarchy(parsed);
                }

                if (planError != null)
                {
                    if (await TryScheduleOutputRepairAsync(completedMission, planError, token).ConfigureAwait(false))
                        return false;

                    MissionStatusEnum planStatusBefore = completedMission.Status;
                    completedMission.FailureReason = AppendRepairHistory(planError, completedMission);
                    completedMission.FailureKind = MissionFailureKindEnum.InvalidOutput;
                    completedMission.CompletedUtc = DateTime.UtcNow;
                    if (!await Transitions.TryTransitionAsync(completedMission, new[] { planStatusBefore }, MissionStatusEnum.Failed, null, token).ConfigureAwait(false)) return false;
                    await RecordFailureAsync(completedMission, planStatusBefore, BlackBoxStepEnum.PlanParse, null, context =>
                        context.Note = "Architect plan error: " + planError, token).ConfigureAwait(false);
                    return false;
                }
                if (parsed.Count > 0)
                {
                    await ProjectArchitectMissionsToLogAsync(completedMission, parsed, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "architect produced " + parsed.Count + " mission definitions");

                    // RS-4.3: the whole fan-out (the first slice written into the waiting Worker, every further slice
                    // with its stage chain, ownership, envelopes and the plan's dependencies) is computed in memory and
                    // written in one transaction with the Architect's completion mark.
                    ArchitectFanOutWork work = new ArchitectFanOutWork(voyageMissions);
                    foreach (Mission nextMission in dependentMissions)
                    {
                        if (!PersonaCatalog.Matches(nextMission.Persona, PersonaCatalog.Worker)) continue;
                        ParsedArchitectMission first = parsed[0];
                        nextMission.Title = first.Title + " [Worker]";
                        nextMission.ArchitectSourceMissionId = completedMission.Id;
                        nextMission.ArchitectPlanIndex = 0;
                        nextMission.Description = first.Description + await BuildArchitectSliceContextAsync(completedMission, parsed, 0, token).ConfigureAwait(false);
                        nextMission.WaitForVoyageWorkers = first.WaitForOtherMissions;
                        nextMission.BranchName = null;
                        work.Changed(nextMission);
                        RetitleDependentChain(work, nextMission, first.Title, first.Description);
                        await ApplyArchitectWorkerEnvelopeAsync(completedMission, nextMission, first, work, token).ConfigureAwait(false);

                        // The Worker slot and its stages are the template for every further slice.
                        for (int i = 1; i < parsed.Count; i++)
                            await CreateArchitectSliceAsync(completedMission, nextMission, work, parsed, i, token).ConfigureAwait(false);
                    }

                    ApplyArchitectMissionDependencies(completedMission, parsed, work);
                    await CommitArchitectFanOutAsync(completedMission, work, token).ConfigureAwait(false);
                    return true; // Architect special handling complete, skip normal handoff
                }

                bool hadArchitectMarkers = !String.IsNullOrEmpty(completedMission.AgentOutput) &&
                    completedMission.AgentOutput.Contains("[ARMADA:MISSION]", StringComparison.Ordinal);

                string failureReason = hadArchitectMarkers && !_Settings.AcceptLegacyMissionMarkers
                    ? "Architect used [ARMADA:MISSION] markers, which this Admiral no longer accepts (AcceptLegacyMissionMarkers is false); emit one ```" + ArchitectPlanParser.FenceInfo + " plan block instead"
                    : hadArchitectMarkers
                    ? "Architect produced no valid [ARMADA:MISSION] definitions in output"
                    : "Architect produced no ```" + ArchitectPlanParser.FenceInfo + " plan block and no [ARMADA:MISSION] markers in output";

                // An absent plan is a format failure, not a judgment about the work: run the Architect again with the
                // exact contract before failing the voyage. Narration is never inferred into missions.
                if (await TryScheduleOutputRepairAsync(completedMission, failureReason, token).ConfigureAwait(false))
                    return false;

                failureReason = AppendRepairHistory(failureReason, completedMission);

                _Logging.Warn(_Header + "architect mission " + completedMission.Id +
                    " produced no valid mission definitions -- marking as failed");
                MissionStatusEnum markersStatusBefore = completedMission.Status;
                completedMission.FailureReason = failureReason;
                completedMission.FailureKind = MissionFailureKindEnum.InvalidOutput;
                completedMission.CompletedUtc = DateTime.UtcNow;
                if (!await Transitions.TryTransitionAsync(completedMission, new[] { markersStatusBefore }, MissionStatusEnum.Failed, null, token).ConfigureAwait(false)) return false;
                await RecordFailureAsync(completedMission, markersStatusBefore, BlackBoxStepEnum.PlanParse, null, context =>
                    context.Note = hadArchitectMarkers ? "The output had [ARMADA:MISSION] markers but none was valid." : "The output had no plan block and no mission markers.", token).ConfigureAwait(false);
                return false;
            }

            // Stage-lag hardening: the just-completed stage's dock may have committed on a detached HEAD,
            // leaving its produced commit off the shared branch ref. The next stage reuses that same branch,
            // so safely advance the branch before handing off. An explicit refusal fails the handoff;
            // transport exceptions retain the existing best-effort logging behavior.
            if (!await HardenStageBranchAsync(completedMission, token).ConfigureAwait(false)) return false;

            // RS-4.3: every dependent stage is prepared in one transaction, each only while it is still Pending, so a
            // restart never leaves some stages on the branch and others without it.
            MissionWriteBatch batch = new MissionWriteBatch();
            foreach (Mission nextMission in dependentMissions)
            {
                // The persona template supplies role instructions; keep the persisted handoff concise.
                MissionNotes.Set(nextMission, MissionNoteKindEnum.PipelineHandoff, PipelineHandoffContext.Build(completedMission));
                nextMission.BranchName = completedMission.BranchName;
                nextMission.LastUpdateUtc = DateTime.UtcNow;
                batch.Updated.Add(new MissionGuardedWrite(nextMission, new[] { MissionStatusEnum.Pending }));
            }

            // RS-6.9: the completed stage's handoff artifact is created once, here, in the transaction that prepares the
            // stages reading it, so none of them can launch before it exists.
            HandoffArtifact? handoffArtifact = await new HandoffArtifactService(_Database, _Logging).PrepareForHandoffAsync(completedMission, token).ConfigureAwait(false);
            if (handoffArtifact != null) batch.HandoffArtifacts.Add(handoffArtifact);

            CrashPoints.Hit(CrashPointNames.HandoffPlanned);
            if (!await _Database.Missions.WriteBatchAsync(batch, token).ConfigureAwait(false))
            {
                _Logging.Warn(_Header + "pipeline handoff from mission " + completedMission.Id + " was not written: a dependent stage left Pending meanwhile; recovery will prepare the rest");
                return false;
            }
            CrashPoints.Hit(CrashPointNames.HandoffWritten);

            foreach (Mission nextMission in dependentMissions)
                _Logging.Debug(_Header + "pipeline handoff: prepared mission " + nextMission.Id +
                    " (" + nextMission.Persona + ") with context from " + completedMission.Id +
                    " (" + completedMission.Persona + ")");

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
                    MissionStatusEnum hardenStatusBefore = completedMission.Status;
                    completedMission.CommitHash = headCommit;
                    completedMission.FailureKind = MissionFailureKindEnum.LandingConflict;
                    completedMission.FailureReason = "Pipeline handoff could not safely advance branch " + completedMission.BranchName
                        + " to stage commit " + headCommit + ". No downstream stage was dispatched. "
                        + "Remediation: inspect the branch and saved stage commit; integrate any divergent work without resetting newer commits, then restart the failed stage. Check Harbor/git availability if the update itself failed.";
                    completedMission.CompletedUtc = DateTime.UtcNow;
                    await Transitions.TryTransitionAsync(completedMission, new[] { hardenStatusBefore }, MissionStatusEnum.Failed, null, token).ConfigureAwait(false);
                    await RecordFailureAsync(completedMission, hardenStatusBefore, BlackBoxStepEnum.StageBranchHarden, dock, context =>
                    {
                        context.GitFacts = new BlackBoxGitFacts { BranchTip = headCommit, GitOperation = "update-ref refs/heads/" + completedMission.BranchName, WorktreeExisted = true };
                    }, token).ConfigureAwait(false);
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
                MissionStatusEnum dependentStatusBefore = dependent.Status;
                dependent.FailureReason = "Blocked by failed dependency " + failedMission.Id
                    + (blockThis ? ". Restart that mission to continue, or cancel the voyage." : String.Empty);
                dependent.FailureKind = MissionFailureKindEnum.DependencyFailed;
                dependent.CompletedUtc = blockThis ? null : DateTime.UtcNow;
                if (!await Transitions.TryTransitionAsync(dependent, new[] { dependentStatusBefore }, blockThis ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled, null, token).ConfigureAwait(false)) continue;
                _Logging.Info(_Header + (blockThis ? "blocked" : "cancelled") + " dependent mission " + dependent.Id +
                    " because upstream mission " + failedMission.Id + " ended in " + failedMission.Status);
                await RecordFailureAsync(dependent, dependentStatusBefore, BlackBoxStepEnum.DependencyCancelled, null, context =>
                {
                    context.Note = (blockThis ? "Non-terminal: held as Blocked" : "Cancelled") + " because upstream mission " + failedMission.Id + " ended "
                        + failedMission.Status + (failedMission.LastBlackBoxEntryId != null ? " (entry " + failedMission.LastBlackBoxEntryId + ")" : "") + ".";
                }, token).ConfigureAwait(false);
                await CancelDependentPipelineStagesAsync(dependent, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Settle a voyage whose missions have all finished; does nothing while any is still running or completing, or
        /// when the voyage is already settled.
        /// </summary>
        /// <param name="voyageId">Voyage id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ReevaluateVoyageAsync(string voyageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) return;
            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null || voyage.Status == VoyageStatusEnum.Complete || voyage.Status == VoyageStatusEnum.Failed || voyage.Status == VoyageStatusEnum.Cancelled) return;
            await UpdateVoyageTerminalStatusAsync(voyageId, token).ConfigureAwait(false);
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

            await VoyageSettlement.ApplyAsync(_Database, voyage, settled.Value, missions, token, BlackBox, _Logging).ConfigureAwait(false);
            _Logging.Info(_Header + "voyage " + voyage.Id + " reached terminal status " + voyage.Status + " during mission completion");
        }

        #endregion
    }
}
