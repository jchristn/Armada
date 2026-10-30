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
    /// Completion gates: boundary and scope checks, the Definition-of-Done gate, auto-land, the no-op check and the completion core.
    /// </summary>
    public partial class MissionService
    {
        #region Public-Methods

        /// <summary>
        /// Core completion logic, called under in-flight deduplication guard.
        /// </summary>
        /// <summary>
        /// If a completion looks like a no-op false-complete, re-dispatch the mission to a fresh captain
        /// (bounded by <see cref="ArmadaSettings.MaxNoOpRedispatchAttempts"/>) or, once exhausted, fail it
        /// so it surfaces in the operator inbox. Returns true when the completion was handled as a no-op
        /// (the caller must not proceed to landing).
        /// </summary>
        /// <summary>
        /// Run the pre-land dock-boundary scanner for the mission's vessel. On a finding, fail the mission
        /// with a redaction-safe reason (which surfaces it in the operator inbox) and return true so the
        /// caller does not land. Returns false when the vessel has no boundary rules or the dock is clean.
        /// </summary>
        private async Task<bool> TryFailMissionForBoundaryViolationAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return false;

            DockBoundaryPolicy policy = new DockBoundaryPolicy
            {
                SecretScanEnabled = vessel.SecretScanEnabled,
                ProtectedPathGlobs = vessel.ProtectedPathPatterns ?? new List<string>(),
                PrivateIdentifiers = vessel.PrivateIdentifierDenylist ?? new List<string>(),
            };
            if (!policy.HasAnyRule()) return false;

            DiffChangeSummary changes = await ResolveDiffChangeSummaryAsync(mission, vessel, dock, token).ConfigureAwait(false);
            List<BoundaryFinding> findings = DockBoundaryScanner.Scan(mission.DiffSnapshot, changes.ChangedPaths, policy);
            if (findings.Count == 0) return false;

            if (dock != null)
            {
                try { await ReclaimMissionDockAsync(dock.Id, token, "boundary violation fails the mission").ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "boundary reclaim error for mission " + mission.Id + ": " + ex.ToString()); }
            }

            MissionStatusEnum boundaryStatusBefore = mission.Status;
            string? boundaryCaptainId = mission.CaptainId;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = DockBoundaryScanner.Summarize(findings);
            mission.FailureKind = MissionFailureKindEnum.Boundary;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "mission " + mission.Id + " blocked by dock-boundary scanner (" + findings.Count + " finding(s))");
            await RecordFailureAsync(mission, boundaryStatusBefore, BlackBoxStepEnum.BoundaryViolation, dock, context =>
            {
                context.Vessel = vessel;
                context.CaptainId = boundaryCaptainId;
                // Private identifiers are never named (the finding's rule is the identifier itself).
                foreach (BoundaryFinding finding in findings)
                    context.BoundaryFindings.Add(finding.Kind + (finding.Kind == "private-identifier" ? "" : " " + finding.RuleId) + " " + finding.Path + (finding.Line.HasValue ? ":" + finding.Line.Value : ""));
            }, token).ConfigureAwait(false);
            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Size and path facts for a mission's change. Parses the persisted diff snapshot by its hunk ranges and,
        /// when the dock's worktree is still on disk, combines it with git's --name-status / --numstat output for
        /// the branch (union of paths, larger counts), so a missing or partial snapshot cannot hide a change.
        /// </summary>
        private async Task<DiffChangeSummary> ResolveDiffChangeSummaryAsync(Mission mission, Vessel vessel, Dock? dock, CancellationToken token)
        {
            DiffChangeSummary summary = DiffChangeSummary.FromUnifiedDiff(mission.DiffSnapshot);
            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            if (git == null || dock == null || String.IsNullOrEmpty(dock.WorktreePath) || !await DockDirectoryExistsAsync(harborHost, dock.WorktreePath!, token).ConfigureAwait(false))
                return summary;

            string baseBranch = String.IsNullOrEmpty(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch;
            try
            {
                IReadOnlyList<GitChangedFile> gitChanges = await git.GetBranchChangesAsync(dock.WorktreePath!, baseBranch, token).ConfigureAwait(false);
                return DiffChangeSummary.Combine(summary, DiffChangeSummary.FromGitChanges(gitChanges));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is TimeoutException)
            {
                _Logging.Warn(_Header + "could not list branch changes for mission " + mission.Id + " in " + dock.WorktreePath + ": " + ex.Message);
                return summary;
            }
        }

        /// <summary>
        /// Evaluate the vessel's auto-land predicate against a mission that would otherwise land. When the
        /// change violates the vessel's file/line/path rules, route the mission to Review and return true so
        /// the caller holds instead of landing. Returns false when auto-land is disabled or the change is
        /// within the rules.
        /// </summary>
        /// <summary>
        /// Run the vessel's in-dock Definition-of-Done gate for a mission about to land. On a classified
        /// failure (Compile/TestFail/Timeout/Infra), fail the mission with the classified reason, cancel
        /// dependent pipeline stages, and return true so the caller does not land. Returns false when the
        /// gate is disabled/unconfigured or the change passed.
        /// </summary>
        private async Task<bool> TryFailForDefinitionOfDoneAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (_DefinitionOfDone == null) return false;
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            if (dock == null || String.IsNullOrEmpty(dock.WorktreePath)) return false;

            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null || !vessel.DefinitionOfDoneEnabled) return false;

            DockHost? harborHost = HarborHostFor(dock);
            DefinitionOfDoneResult result = harborHost != null
                ? await _DefinitionOfDone.EvaluateOnHostAsync(vessel, dock.WorktreePath!, harborHost.Commands, token).ConfigureAwait(false)
                : await _DefinitionOfDone.EvaluateAsync(vessel, dock.WorktreePath!, token).ConfigureAwait(false);
            if (result.Passed) return false;

            MissionStatusEnum dodStatusBefore = mission.Status;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = "Definition-of-Done gate failed (" + result.Outcome + "): " + result.Detail;
            mission.FailureKind = MissionFailureClassifier.FromDefinitionOfDone(result.Outcome);
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "mission " + mission.Id + " failed the Definition-of-Done gate (" + result.Outcome + ")");
            await RecordFailureAsync(mission, dodStatusBefore, BlackBoxStepEnum.DefinitionOfDone, dock, context =>
            {
                context.Vessel = vessel;
                context.FailingCommand = vessel.DefinitionOfDoneBuildCommand ?? vessel.DefinitionOfDoneTestCommand;
                context.CommandOutput = result.Detail;
                context.Note = "Definition-of-Done outcome: " + result.Outcome + ".";
            }, token).ConfigureAwait(false);

            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null || String.IsNullOrEmpty(mission.VesselId)) return null;

            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId!, token).ConfigureAwait(false);
            if (vessel == null) return null;

            Dock? dock = null;
            if (!String.IsNullOrEmpty(mission.DockId))
                dock = !String.IsNullOrEmpty(mission.TenantId)
                    ? await _Database.Docks.ReadAsync(mission.TenantId, mission.DockId!, token).ConfigureAwait(false)
                    : await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);

            DiffChangeSummary changes = await ResolveDiffChangeSummaryAsync(mission, vessel, dock, token).ConfigureAwait(false);
            AutoLandPolicy policy = new AutoLandPolicy
            {
                Enabled = vessel.AutoLandEnabled,
                MaxFiles = vessel.AutoLandMaxFiles,
                MaxLines = vessel.AutoLandMaxLines,
                PathAllowGlobs = vessel.AutoLandPathAllowGlobs ?? new List<string>(),
                PathDenyGlobs = vessel.AutoLandPathDenyGlobs ?? new List<string>(),
            };

            return AutoLandPredicate.Evaluate(changes.FileCount, changes.ChangedLineCount, changes.ChangedPaths, policy);
        }

        private async Task<RunApprovalOptions?> ReadApprovalOptionsAsync(Mission mission, CancellationToken token)
        {
            Voyage? voyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
            return voyage?.ApprovalOptions;
        }

        private async Task<DateTime?> ReviewDeadlineAsync(Mission mission, CancellationToken token)
        {
            RunApprovalOptions? options = await ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false);
            return options != null ? DateTime.UtcNow.AddSeconds(options.TimeoutSeconds)
                : _Settings.ReviewTimeoutMinutes > 0 ? DateTime.UtcNow.AddMinutes(_Settings.ReviewTimeoutMinutes) : (DateTime?)null;
        }

        private async Task<bool> TryHoldForAutoLandAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            RunApprovalOptions? holdApproval = await ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false);
            if (holdApproval?.ApproveAll == true) return false;
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null || !vessel.AutoLandEnabled) return false;

            DiffChangeSummary changes = await ResolveDiffChangeSummaryAsync(mission, vessel, dock, token).ConfigureAwait(false);
            List<string> changedPaths = changes.ChangedPaths;
            int filesChanged = changes.FileCount;
            int linesChanged = changes.ChangedLineCount;

            AutoLandPolicy policy = new AutoLandPolicy
            {
                Enabled = true,
                MaxFiles = vessel.AutoLandMaxFiles,
                MaxLines = vessel.AutoLandMaxLines,
                PathAllowGlobs = vessel.AutoLandPathAllowGlobs ?? new List<string>(),
                PathDenyGlobs = vessel.AutoLandPathDenyGlobs ?? new List<string>(),
            };

            AutoLandDecision decision = AutoLandPredicate.Evaluate(filesChanged, linesChanged, changedPaths, policy);
            if (decision.Land) return false;

            mission.Status = MissionStatusEnum.Review;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.ReviewRequestedUtc = DateTime.UtcNow;
            mission.ReviewDeadlineUtc = await ReviewDeadlineAsync(mission, token).ConfigureAwait(false);
            if (holdApproval != null) mission.RequiresReview = true;
            mission.ReviewComment = "Auto-land held: " + (decision.HoldReason ?? "change exceeds auto-land rules");
            mission.ReviewedByUserId = null;
            mission.ReviewedUtc = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            OnReviewRequested?.Invoke(mission);
            _Logging.Info(_Header + "auto-land held mission " + mission.Id + " for review: " + (decision.HoldReason ?? "rules"));
            return true;
        }

        private async Task<bool> TryRejectNoOpCompletionAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            // A captain that reported BLOCKED and left nothing on its branch is not unfinished: re-dispatching it reruns the
            // same blocked analysis. Verifier stages keep their own BLOCKED meaning (back to the Worker).
            if (await TryFailCaptainBlockedAsync(mission, dock, token).ConfigureAwait(false)) return true;

            // A work stage that ended without a commit, a diff or its completion line stopped early (a model ending its
            // turn mid-task), however long it ran. Review and planning stages never commit, so they are exempt. The
            // repository decides, not the transcript: a missing or malformed completion line with nothing on the branch is
            // unfinished work, and one with work on the branch is repaired later (RecordWorkStageRepairsAsync). A Test
            // Engineer that reported a blocking defect ends with the blocked marker instead and is not unfinished.
            bool workStage = IsRepositoryWorkStage(mission.Persona)
                && !(PersonaCatalog.Matches(mission.Persona, PersonaCatalog.TestEngineer) && VerifierContract.IsBlocked(mission.AgentOutput));
            bool unfinished = workStage && dock != null
                && NoOpCompletionDetector.IsUnfinishedWork(mission.DiffSnapshot, mission.AgentOutput, TryReadDockStartCommit(dock, mission), mission.CommitHash);
            if (!unfinished && !NoOpCompletionDetector.IsNoOp(mission.DiffSnapshot, mission.AgentOutput, mission.TotalRuntimeMs))
                return false;

            // Release the dock/captain before re-dispatching or failing.
            if (dock != null)
            {
                try { await ReclaimMissionDockAsync(dock.Id, token, "no-op completion is re-dispatched or failed").ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "no-op reclaim error for mission " + mission.Id + ": " + ex.ToString()); }
            }

            int maxAttempts = _Settings.MaxNoOpRedispatchAttempts;
            if (mission.NoOpRedispatchAttempts < maxAttempts)
            {
                mission.NoOpRedispatchAttempts++;
                mission.Status = MissionStatusEnum.Pending;
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
                _Logging.Warn(_Header + (unfinished ? "unfinished completion (no commit, no diff, no completion line)" : "no-op completion") + " detected for mission " + mission.Id +
                    "; re-dispatching (attempt " + mission.NoOpRedispatchAttempts + "/" + maxAttempts + ")");

                if (!String.IsNullOrEmpty(mission.VesselId))
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                    if (vessel != null) await TryAssignAsync(mission, vessel, token).ConfigureAwait(false);
                }
                return true;
            }

            // Retries exhausted: fail and surface to the operator inbox (LandingFailed/Failed feed it).
            MissionStatusEnum noOpStatusBefore = mission.Status;
            string? noOpCaptainId = mission.CaptainId;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = unfinished
                ? (CaptainBlockedContract.HasResultLine(mission.AgentOutput) || CaptainBlockedContract.IsReported(mission.AgentOutput)
                    ? "Unfinished completion: the captain repeatedly ended its run without a commit or a diff, after "
                        + mission.NoOpRedispatchAttempts + " re-dispatch attempt(s), although its output carried a result or blocking line. Read its output: the brief may ask for a change that is impossible or already present."
                    : "Unfinished completion: the captain repeatedly ended its run without a commit, a diff or the completion line, after "
                        + mission.NoOpRedispatchAttempts + " re-dispatch attempt(s). The model is stopping mid-task; check its context window and output limits, or use a stronger captain.")
                : "No-op completion: the captain repeatedly completed with no changes after " +
                    mission.NoOpRedispatchAttempts + " re-dispatch attempt(s). Likely an impossible or mis-specified mission.";
            mission.FailureKind = MissionFailureKindEnum.NoOp;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "no-op completion for mission " + mission.Id +
                " exhausted re-dispatch attempts; failing to operator inbox");
            await RecordFailureAsync(mission, noOpStatusBefore, unfinished ? BlackBoxStepEnum.Unfinished : BlackBoxStepEnum.NoOp, dock, context =>
            {
                context.CaptainId = noOpCaptainId;
                context.Note = "Re-dispatch attempts used: " + mission.NoOpRedispatchAttempts + " of " + maxAttempts + ".";
            }, token).ConfigureAwait(false);
            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> TryFailCaptainBlockedAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (VerifierContract.Applies(mission.Persona)) return false;
            string? output = mission.AgentOutput;
            if (!CaptainBlockedContract.IsReported(output) || CaptainBlockedContract.HasCompletionOrVerdict(output)) return false;

            string? startCommit = dock != null ? TryReadDockStartCommit(dock, mission) : null;
            if (ContractRepair.HasNewWork(mission.DiffSnapshot, startCommit, mission.CommitHash)) return false;

            if (dock != null)
            {
                try { await ReclaimMissionDockAsync(dock.Id, token).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "blocked reclaim error for mission " + mission.Id + ": " + ex.ToString()); }
            }

            string? captainId = mission.CaptainId;
            MissionStatusEnum blockedStatusBefore = mission.Status;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = CaptainBlockedContract.FailureReason(output);
            mission.FailureKind = MissionFailureKindEnum.Blocked;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Warn(_Header + "captain reported BLOCKED for mission " + mission.Id + " with no work on its branch; failing to operator inbox without re-dispatch");
            await RecordFailureAsync(mission, blockedStatusBefore, BlackBoxStepEnum.CaptainBlocked, dock, context => context.CaptainId = captainId, token).ConfigureAwait(false);

            try
            {
                ArmadaEvent blockedEvent = new ArmadaEvent(CaptainBlockedContract.EventType, "Captain reported BLOCKED: " + mission.Title);
                blockedEvent.TenantId = mission.TenantId;
                blockedEvent.UserId = mission.UserId;
                blockedEvent.EntityType = "mission";
                blockedEvent.EntityId = mission.Id;
                blockedEvent.CaptainId = captainId;
                blockedEvent.MissionId = mission.Id;
                blockedEvent.VesselId = mission.VesselId;
                blockedEvent.VoyageId = mission.VoyageId;
                blockedEvent.Payload = JsonSerializer.Serialize(new MissionBlockedByCaptainPayload
                {
                    MissionId = mission.Id,
                    CaptainId = captainId,
                    Persona = mission.Persona,
                    Reason = CaptainBlockedContract.ExtractReason(output)
                });
                await _Database.Events.CreateAsync(blockedEvent, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record " + CaptainBlockedContract.EventType + " for mission " + mission.Id + ": " + ex.Message);
            }

            await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
            await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            return true;
        }

        private async Task HandleCompletionCoreAsync(Captain captain, string missionId, CancellationToken token, DateTime? expectedVersion = null)
        {
            Mission? mission = null;
            if (!String.IsNullOrEmpty(captain.TenantId))
            {
                mission = await _Database.Missions.ReadAsync(captain.TenantId, missionId, token).ConfigureAwait(false);
            }
            if (mission == null)
            {
                mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            }
            if (mission == null) return;
            if (expectedVersion.HasValue && (mission.LastUpdateUtc != expectedVersion.Value
                || mission.ProcessId.HasValue || (mission.Status != MissionStatusEnum.InProgress && mission.Status != MissionStatusEnum.Testing)))
            {
                _Logging.Info(_Header + "ignoring stale claimed completion for " + missionId);
                return;
            }

            // Idempotency guard: if the mission has already been processed (e.g. by a concurrent
            // health check or process exit handler), skip to avoid double-processing.
            if (mission.Status == MissionStatusEnum.WorkProduced ||
                mission.Status == MissionStatusEnum.Complete ||
                mission.Status == MissionStatusEnum.Failed ||
                mission.Status == MissionStatusEnum.Cancelled ||
                mission.Status == MissionStatusEnum.LandingFailed ||
                mission.Status == MissionStatusEnum.PullRequestOpen)
            {
                _Logging.Debug(_Header + "mission " + missionId + " already in post-work state " + mission.Status + " -- skipping completion handler");
                return;
            }

            // Completion callers must not bypass the last mechanical check, including orphan recovery and
            // a stale completion that arrives after a failed check has started an in-place resume.
            EnumerationResult<ArmadaEvent> checks = await _Database.Events.EnumerateAsync(new EnumerationQuery
            {
                MissionId = mission.Id,
                EventType = StageCheckEvents.EventType,
                PageSize = 1,
                Order = EnumerationOrderEnum.CreatedDescending
            }, token).ConfigureAwait(false);
            StageCheckResult? lastCheck = StageCheckEvents.Read(checks.Objects.FirstOrDefault());
            if (checks.Objects.Count > 0 && (lastCheck == null || lastCheck.IsBlocking))
            {
                _Logging.Warn(_Header + "mission " + mission.Id + " cannot complete while its stage check is unverified");
                return;
            }

            RunApprovalOptions? completionApproval = await ReadApprovalOptionsAsync(mission, token).ConfigureAwait(false);
            if (completionApproval?.ApproveAll == true) mission.RequiresReview = false;

            // Read-only modes (Audit/Research) produce a written report, not a commit. Their empty diff is a
            // successful outcome, so they skip no-op rejection and never attempt landing.
            bool isReadOnlyMission = MissionModeContract.IsReadOnly(mission.Mode);

            // Mark mission as work produced (agent finished, landing not yet attempted). Conditional on the status read
            // above, so exactly one completion wins and a concurrent handler that moved the mission (an interruption
            // re-dispatch, a cancel, a second completion) is never overwritten by this one.
            MissionStatusEnum observedStatus = mission.Status;
            DateTime observedVersion = mission.LastUpdateUtc;
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.ProcessId = null;
            mission.LastUpdateUtc = DateTime.UtcNow;
            if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, observedVersion, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false))
            {
                _Logging.Debug(_Header + "mission " + missionId + " left " + observedStatus + " before its completion was recorded -- skipping completion handler");
                return;
            }

            _Logging.Info(_Header + "mission " + mission.Id + " work produced by captain " + captain.Id);

            // Get dock for diff capture (prefer mission-level DockId, fall back to captain-level)
            Dock? dock = null;
            string? dockId = mission.DockId ?? captain.CurrentDockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dock = !String.IsNullOrEmpty(mission.TenantId)
                    ? await _Database.Docks.ReadAsync(mission.TenantId, dockId, token).ConfigureAwait(false)
                    : await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
            }

            if (dock != null && String.IsNullOrEmpty(mission.BranchName) && !String.IsNullOrEmpty(dock.BranchName))
            {
                mission.BranchName = dock.BranchName;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "backfilled branch " + dock.BranchName + " onto mission " + mission.Id +
                    " from dock " + dock.Id + " before pipeline handoff");
            }

            // A captain that edited files but never ran git commit (common for smaller local models) would otherwise
            // produce an empty diff, and the edits would be deleted with the dock. Commit them first.
            if (dock != null) await CommitLeftoverWorkAsync(mission, captain, dock, token).ConfigureAwait(false);
            bool leftoverCommitted = _LeftoverCommitted.TryRemove(mission.Id, out _);

            // Record the exact commit this stage produced. The next stage verifies its dock starts from it, and rework
            // stagnation detection compares it across rounds. Landing later replaces it with the landed commit.
            if (dock != null)
            {
                string? producedCommit = await ReadDockHeadAsync(dock, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(producedCommit) && !String.Equals(producedCommit, mission.CommitHash, StringComparison.Ordinal))
                {
                    mission.CommitHash = producedCommit;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                }

                // The branch, not the dock directory, is the durable copy of the work landing and repair rebuild from.
                await EnsureBranchHoldsCommitAsync(mission, dock, token).ConfigureAwait(false);
            }

            // Capture diff BEFORE pipeline handoff so the next stage gets the actual diff
            if (dock != null && OnCaptureDiff != null)
            {
                try
                {
                    await OnCaptureDiff.Invoke(mission, dock).ConfigureAwait(false);
                    // Re-read mission to get the persisted DiffSnapshot
                    Mission? refreshed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (refreshed != null && !String.IsNullOrEmpty(refreshed.DiffSnapshot))
                    {
                        mission.DiffSnapshot = refreshed.DiffSnapshot;
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error capturing diff for mission " + mission.Id + ": " + ex.ToString());
                }
            }

            bool failedForScopeViolation = false;
            if (dock != null)
            {
                failedForScopeViolation = await TryFailMissionForScopeViolationAsync(mission, dock, token).ConfigureAwait(false);
            }

            // Capture accumulated agent stdout output before pipeline handoff
            if (OnGetMissionOutput != null)
            {
                string? agentOutput = OnGetMissionOutput(mission.Id);
                ChatTurnTelemetryRecorder? reported = OnTakeMissionTelemetry?.Invoke(mission.Id);
                if (!String.IsNullOrEmpty(agentOutput))
                {
                    mission.AgentOutput = agentOutput;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    // The runtime's own usage report (mux contract 2 run_completed, a Claude Code / Codex / OpenCode / pi
                    // usage event), read from its raw output as it arrived; without one (an older lifecycle handler),
                    // from the collected output; otherwise both counts are estimated from text length and flagged.
                    if (reported == null)
                    {
                        reported = new ChatTurnTelemetryRecorder();
                        foreach (string line in agentOutput.Split('\n'))
                            reported.ObserveLine(captain.Runtime, line.TrimEnd('\r'));
                    }

                    await TokenUsageCapture.CaptureAsync(
                        _Database, _Logging, "mission",
                        model: reported.Model ?? captain.Model,
                        runtime: captain.Runtime.ToString(),
                        tenantId: mission.TenantId,
                        userId: null,
                        vesselId: String.IsNullOrEmpty(mission.VesselId) ? null : mission.VesselId,
                        captainId: captain.Id,
                        sourceId: mission.Id,
                        inputTokens: reported.InputTokens,
                        outputTokens: reported.OutputTokens,
                        cachedTokens: reported.CachedTokens,
                        inputText: mission.Description,
                        outputText: agentOutput,
                        token: token,
                        harborId: mission.AssignedHarborId).ConfigureAwait(false);
                }
            }

            // Reject a no-op false-complete (fast exit, empty diff, trivial output): re-dispatch to a
            // fresh captain up to a bounded number of times, then fail to the operator inbox. Skipped
            // when the mission already failed scope validation.
            if (!failedForScopeViolation && !isReadOnlyMission &&
                await TryRejectNoOpCompletionAsync(mission, dock, token).ConfigureAwait(false))
            {
                await ReleaseCaptainAfterCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                return;
            }

            // Pre-land dock-boundary scan: secrets, protected paths, private-identifier leaks. A finding
            // blocks landing and surfaces the mission (with a redaction-safe reason) in the operator inbox.
            if (!failedForScopeViolation &&
                await TryFailMissionForBoundaryViolationAsync(mission, dock, token).ConfigureAwait(false))
            {
                await ReleaseCaptainAfterCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
                return;
            }

            // The work stage left work on its branch: a missing or malformed completion line, or a missing commit, is
            // repaired from the repository and recorded.
            if (!failedForScopeViolation && !isReadOnlyMission)
                await RecordWorkStageRepairsAsync(mission, captain, dock, leftoverCommitted, token).ConfigureAwait(false);

            string? judgeBlockReason = null;
            if (!failedForScopeViolation && PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge))
            {
                // Only the structured [ARMADA:VERDICT] protocol line counts; a missing or conflicting verdict is no
                // verdict, which blocks landing. A single unambiguous verdict in a nearby non-canonical form is repaired
                // (the canonical line is appended below the original output); anything ambiguous stays no verdict.
                JudgeVerdictEnum verdict = JudgeVerdictParser.Parse(mission.AgentOutput);
                if (verdict == JudgeVerdictEnum.None)
                    verdict = await TryRepairJudgeVerdictAsync(mission, captain, token).ConfigureAwait(false);
                string? verdictFailureReason = null;
                bool rejectedPass = false;

                // A PASS must be substantiated (three lenses + real narrative). A rejected PASS is not
                // silently re-run: it is downgraded to a blocking verdict and the mission fails terminally
                // with an explicit reason so an operator sees it rather than a quiet re-dispatch.
                if (verdict == JudgeVerdictEnum.Pass && !TryValidateJudgePassOutput(mission.AgentOutput, out verdictFailureReason))
                {
                    verdict = JudgeVerdictEnum.NeedsRevision;
                    rejectedPass = true;
                }

                // A specification conflict is not a defect a Worker can fix, so it never consumes the rework budget. The
                // stage goes to a person, who approves the work as is, restarts the Judge, or stops the pipeline; nothing is
                // waived automatically. A SPEC_CONFLICT without the quoted criterion is a contract violation (repaired below).
                if (verdict == JudgeVerdictEnum.SpecConflict)
                {
                    string? conflict = JudgeContract.ExtractSpecConflict(mission.AgentOutput);
                    if (conflict != null)
                    {
                        mission.RequiresReview = true;
                        judgeBlockReason = "Judge reported a specification conflict (a person must decide; no criterion was waived): " + conflict;
                        _Logging.Warn(_Header + "judge mission " + mission.Id + " reported a specification conflict; sending it to review");
                    }
                    else
                    {
                        verdict = JudgeVerdictEnum.None;
                    }
                }

                if (verdict != JudgeVerdictEnum.Pass && verdict != JudgeVerdictEnum.SpecConflict)
                {
                    // To block, the Judge must exhibit a concrete affected case. A block without one is a
                    // contract violation: the mission still fails terminally, but the reason makes clear the
                    // Judge did not substantiate the block rather than the work being definitively wrong.
                    bool isBlockingVerdict = verdict == JudgeVerdictEnum.Fail || verdict == JudgeVerdictEnum.NeedsRevision;
                    bool exhibitsAffectedCase = JudgeContract.ExhibitsAffectedCase(mission.AgentOutput);

                    string blockingReason;
                    if (rejectedPass)
                    {
                        blockingReason = "Judge PASS rejected (" + (verdictFailureReason ?? "unsubstantiated approval") + ")";
                    }
                    else if (isBlockingVerdict && !exhibitsAffectedCase)
                    {
                        blockingReason = "Judge verdict: " + (verdict == JudgeVerdictEnum.Fail ? "FAIL" : "NEEDS_REVISION") +
                            " but did not exhibit a concrete affected case; a block must cite a real affected file, line, or scenario";
                    }
                    else
                    {
                        blockingReason = verdict switch
                        {
                            JudgeVerdictEnum.Fail => "Judge verdict: FAIL",
                            JudgeVerdictEnum.NeedsRevision => "Judge verdict: NEEDS_REVISION",
                            _ => "Judge mission did not emit an explicit PASS verdict"
                        };
                    }

                    string? reworkUnavailableReason = null;
                    if (!mission.RequiresReview && !rejectedPass && isBlockingVerdict && exhibitsAffectedCase
                        && await TryScheduleReworkAsync(mission, token, reason => reworkUnavailableReason = reason).ConfigureAwait(false))
                    {
                        // The Judge asked for concrete changes: the work goes back to a Worker on the same branch with
                        // the review, then to a new Judge. The rest of the pipeline now waits on that Judge.
                        _Logging.Info(_Header + "judge mission " + mission.Id + " asked for revisions (" + blockingReason + "); sent back for rework");
                    }
                    else if (mission.RequiresReview)
                    {
                        // The stage has a review gate: a person decides. Approving overrides the Judge and lands the
                        // work; denying with FailPipeline stops the pipeline, RetryStage runs the Judge again.
                        judgeBlockReason = blockingReason;
                        _Logging.Warn(_Header + "judge mission " + mission.Id + " blocked landing, sent to review: " + blockingReason);
                    }
                    else if ((rejectedPass || !isBlockingVerdict || !exhibitsAffectedCase)
                        && await TryScheduleOutputRepairAsync(mission, blockingReason, JudgeContract.OutputContract(), token).ConfigureAwait(false))
                    {
                        // The Judge broke its output contract (no verdict, conflicting verdicts, an unsubstantiated
                        // PASS, or a block without an affected case). That is a formatting failure, not a review
                        // outcome: the same Judge runs again with the exact problem quoted before anything fails.
                        await FinishOutputRepairScheduledAsync(mission, captain, dock, token).ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        if (rejectedPass || !isBlockingVerdict || !exhibitsAffectedCase)
                            blockingReason = AppendRepairHistory(blockingReason, mission);
                        mission.Status = MissionStatusEnum.Failed;
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        mission.FailureReason = blockingReason + (reworkUnavailableReason == null ? String.Empty
                            : "\n" + reworkUnavailableReason + "\nRemediation: Read this Judge's affected-case report and correct the identified code or configuration defects. Dispatch a follow-up mission using the preserved work branch and an independent Judge; keep strict selection unless you explicitly choose a different captain. Rejected work has not been approved for landing.");
                        mission.FailureKind = MissionFailureKindEnum.JudgeRejected;
                        await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                        _Logging.Warn(_Header + "judge mission " + mission.Id + " blocked landing: " + blockingReason);
                        await RecordFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.JudgeRejected, dock, context =>
                        {
                            context.Captain = captain;
                            context.Note = "Judge verdict: " + verdict + (rejectedPass ? " (PASS rejected)" : "") + (exhibitsAffectedCase ? "; affected case exhibited." : "; no affected case exhibited.");
                        }, token).ConfigureAwait(false);
                    }
                }
            }

            // A verifier stage that found a product defect it may not fix sends the work back to the Worker now, as a Judge's
            // NEEDS_REVISION does, instead of passing a known blocker down the chain (HF-25).
            if (!failedForScopeViolation && mission.Status == MissionStatusEnum.WorkProduced
                && VerifierContract.Applies(mission.Persona) && VerifierContract.IsBlocked(mission.AgentOutput))
            {
                string? reworkUnavailableReason = null;
                if (!JudgeContract.ExhibitsAffectedCase(mission.AgentOutput))
                {
                    string problem = mission.Persona + " reported " + VerifierContract.BlockedMarker + " without a concrete affected case; a block must cite a real affected file, line, or scenario";
                    if (await TryScheduleOutputRepairAsync(mission, problem, VerifierContract.BlockingInstruction().Trim(), token).ConfigureAwait(false))
                    {
                        await FinishOutputRepairScheduledAsync(mission, captain, dock, token).ConfigureAwait(false);
                        return;
                    }
                    reworkUnavailableReason = problem;
                }
                else if (!mission.RequiresReview
                    && await TryScheduleReworkAsync(mission, token, reason => reworkUnavailableReason = reason).ConfigureAwait(false))
                {
                    _Logging.Info(_Header + mission.Persona + " mission " + mission.Id + " reported a blocking defect; sent back to the Worker for rework");
                }
                else
                {
                    mission.Status = MissionStatusEnum.Failed;
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission.FailureReason = mission.Persona + " reported a blocking defect (see its Affected Case)."
                        + (reworkUnavailableReason == null ? String.Empty : "\n" + reworkUnavailableReason)
                        + "\nRemediation: read the stage's affected case, correct the product code on the preserved branch, then restart the pipeline from the Worker. Unverified work has not been approved for landing.";
                    mission.FailureKind = MissionFailureKindEnum.JudgeRejected;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    _Logging.Warn(_Header + mission.Persona + " mission " + mission.Id + " reported a blocking defect and no rework could be scheduled");
                    await RecordFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.JudgeRejected, dock, context =>
                    {
                        context.Captain = captain;
                        context.Note = mission.Persona + " reported " + VerifierContract.BlockedMarker + "; no rework could be scheduled.";
                    }, token).ConfigureAwait(false);
                }
            }

            bool hasDependentPipelineStages = await HasDependentPipelineStages(mission.VoyageId, mission.Id, token).ConfigureAwait(false);
            bool awaitingManualReview = false;
            if (!failedForScopeViolation &&
                mission.Status == MissionStatusEnum.WorkProduced &&
                mission.RequiresReview)
            {
                mission.Status = MissionStatusEnum.Review;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.ReviewRequestedUtc = DateTime.UtcNow;
                mission.ReviewDeadlineUtc = await ReviewDeadlineAsync(mission, token).ConfigureAwait(false);
                mission.ReviewComment = judgeBlockReason != null
                    ? "Judge blocked landing: " + judgeBlockReason + ". Approve to land the work anyway, or deny: FailPipeline stops the pipeline, RetryStage runs the Judge again."
                    : null;
                mission.ReviewedByUserId = null;
                mission.ReviewedUtc = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                OnReviewRequested?.Invoke(mission);
                awaitingManualReview = true;
            }

            // Pipeline handoff: if missions in the same voyage depend on this one, prepare them
            bool preparedDownstreamStages = false;
            if (!failedForScopeViolation && !awaitingManualReview)
            {
                preparedDownstreamStages = await TryHandoffToNextStageAsync(mission, token).ConfigureAwait(false);
            }

            Mission? missionAfterHandoff = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            if (missionAfterHandoff != null)
            {
                mission = missionAfterHandoff;
            }

            if (mission.Status == MissionStatusEnum.Failed ||
                mission.Status == MissionStatusEnum.Cancelled ||
                mission.Status == MissionStatusEnum.LandingFailed)
            {
                await CancelDependentPipelineStagesAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
            }

            await EmitMissionOutcomeTelemetryAsync(mission, captain, token).ConfigureAwait(false);

            bool shouldAttemptLanding =
                !awaitingManualReview &&
                !preparedDownstreamStages &&
                !hasDependentPipelineStages &&
                !isReadOnlyMission &&
                (mission.Status == MissionStatusEnum.WorkProduced ||
                mission.Status == MissionStatusEnum.PullRequestOpen);

            // Read-only missions (Audit/Research) that are not held for review or feeding a pipeline stage
            // complete directly: there is nothing to land, and an empty diff is the expected outcome.
            if (isReadOnlyMission &&
                !awaitingManualReview &&
                !preparedDownstreamStages &&
                !hasDependentPipelineStages &&
                mission.Status == MissionStatusEnum.WorkProduced)
            {
                mission.Status = MissionStatusEnum.Complete;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                await UpdateVoyageTerminalStatusAsync(mission.VoyageId, token).ConfigureAwait(false);
                _Logging.Info(_Header + "read-only " + mission.Mode + " mission " + mission.Id +
                    " completed without landing (no commit expected)");
            }

            // In-dock Definition-of-Done gate: run the vessel's build + unit tests inside the mission's own
            // checkout before acceptance. A classified failure (Compile/TestFail/Timeout/Infra) blocks
            // landing and fails the mission so it surfaces (and can be recovered).
            if (shouldAttemptLanding && await TryFailForDefinitionOfDoneAsync(mission, dock, token).ConfigureAwait(false))
            {
                shouldAttemptLanding = false;
            }

            // Per-vessel auto-land predicate: a change that is too large or touches denied/out-of-scope
            // paths holds for review instead of landing unattended.
            if (shouldAttemptLanding && await TryHoldForAutoLandAsync(mission, dock, token).ConfigureAwait(false))
            {
                shouldAttemptLanding = false;
                awaitingManualReview = true;
            }

            if (!shouldAttemptLanding)
            {
                _Logging.Debug(_Header + "skipping landing for mission " + mission.Id +
                    " because it is not a terminal landed stage yet (status: " + mission.Status + ")");
            }

            // Invoke OnMissionComplete synchronously (Phase A: push branch, create PR, or enqueue).
            // Captain stays in Working state until the handoff completes, preventing the captain
            // from being reassigned while git operations are still in progress.
            if (shouldAttemptLanding && dock != null && OnMissionComplete != null)
            {
                _Logging.Debug(_Header + "executing synchronous landing handoff for mission " + mission.Id);
                try
                {
                    await OnMissionComplete.Invoke(mission, dock).ConfigureAwait(false);
                    // The landing handler can return after logging an enqueue error. Require durable
                    // evidence of the MergeQueue handoff rather than treating that return as success.
                    Vessel? landingVessel = String.IsNullOrEmpty(mission.VesselId) ? null
                        : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                    Voyage? landingVoyage = String.IsNullOrEmpty(mission.VoyageId) ? null
                        : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                    if ((landingVoyage?.LandingMode ?? landingVessel?.LandingMode ?? _Settings.LandingMode ?? LandingModeEnum.MergeAndPush) == LandingModeEnum.MergeQueue)
                    {
                        EnumerationResult<MergeEntry> entries = await _Database.MergeEntries.EnumerateAsync(
                            new EnumerationQuery { MissionId = mission.Id, PageSize = 1 }, token).ConfigureAwait(false);
                        if (entries.Objects.Count == 0)
                            throw new InvalidOperationException("Landing handoff returned without recording a merge queue entry.");
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error in mission complete handler for " + mission.Id + ": " + ex.ToString());
                    // Persist the failed handoff instead of leaving an idempotently skipped WorkProduced mission.
                    Mission? stranded = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (stranded?.Status == MissionStatusEnum.WorkProduced)
                    {
                        DateTime version = stranded.LastUpdateUtc;
                        stranded.Status = MissionStatusEnum.LandingFailed;
                        stranded.FailureKind = LandingFailureClassifier.Classify(ex);
                        stranded.FailureReason = "Landing handoff failed: " + ex.Message;
                        stranded.LastUpdateUtc = DateTime.UtcNow;
                        if (await _Database.Missions.TryUpdateIfUnchangedAsync(stranded, version,
                            new[] { MissionStatusEnum.WorkProduced }, token).ConfigureAwait(false))
                        {
                            mission = stranded;
                            bool queueHandoffMissing = ex is InvalidOperationException && ex.Message.Contains("merge queue entry", StringComparison.Ordinal);
                            BlackBoxStepEnum strandedStep = queueHandoffMissing ? BlackBoxStepEnum.MissingMergeHandoff
                                : BlackBoxSiteFacts.LandingStepOf(ex, stranded.FailureKind == MissionFailureKindEnum.LandingConflict ? BlackBoxStepEnum.TargetIntegration : BlackBoxStepEnum.Unknown);
                            await RecordFailureAsync(stranded, MissionStatusEnum.WorkProduced, strandedStep, dock, context =>
                            {
                                context.Captain = captain;
                                BlackBoxSiteFacts.ApplyException(context, ex);
                            }, token).ConfigureAwait(false);
                        }
                    }
                }
            }

            // The handler may persist a new instance rather than mutate the supplied mission.
            mission = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false) ?? mission;
            // Retain worktrees needed by retry_landing and manual review.
            string? completionDockId = dock?.Id;
            bool cleanupArchitectBranch =
                preparedDownstreamStages &&
                PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect);
            bool retainDockForReview =
                awaitingManualReview &&
                !hasDependentPipelineStages &&
                !String.IsNullOrEmpty(completionDockId);

            if (!String.IsNullOrEmpty(completionDockId) && !retainDockForReview
                && mission.Status != MissionStatusEnum.LandingFailed
                && !(mission.Status == MissionStatusEnum.WorkProduced
                    && !hasDependentPipelineStages && !preparedDownstreamStages))
            {
                // The dock lease (DockService) keeps a worktree that still has unlanded work. This path only forces the
                // cases it decided on: a pipeline handoff moves the branch to the next stage (which needs it checked out
                // in its own dock), and an opened pull request has already published the branch.
                string? completionForceReason = hasDependentPipelineStages || preparedDownstreamStages
                    ? "pipeline handoff: the next stage takes over branch " + (mission.BranchName ?? dock?.BranchName)
                    : mission.Status == MissionStatusEnum.PullRequestOpen ? "pull request opened: the branch is published" : null;
                try
                {
                    await _Docks.ReclaimAsync(completionDockId, token: token, forceReason: completionForceReason, caller: "MissionCompletion").ConfigureAwait(false);
                }
                catch (Exception reclaimEx)
                {
                    _Logging.Warn(_Header + "error reclaiming dock " + completionDockId + " after mission " + mission.Id + ": " + reclaimEx.Message);
                }
            }

            if (cleanupArchitectBranch)
            {
                await CleanupArchitectBranchAsync(mission, dock, token).ConfigureAwait(false);
            }

            MissionOutcomeSignalDefinition missionOutcomeSignal = BuildMissionOutcomeSignal(mission);
            Signal signal = new Signal(missionOutcomeSignal.Type, missionOutcomeSignal.Payload);
            signal.FromCaptainId = captain.Id;
            await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            // Release the captain to idle only AFTER the handoff and dock reclaim are done,
            // and only if the captain is still assigned to this mission. Orphan recovery can
            // finalize an older mission using a captain record that has already moved on.
            await ReleaseCaptainAfterCompletionAsync(captain, mission.Id).ConfigureAwait(false);

            await DispatchPendingMissionsAsync(token).ConfigureAwait(false);
        }

        #endregion
    }
}
