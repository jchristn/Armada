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
    /// Turns an Architect's plan into slice missions and recovers interrupted fan-outs.
    /// </summary>
    internal sealed class ArchitectFanOutService
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly IGitService? _Git;
        private readonly string _Header;
        private readonly LoggingModule _Logging;
        private readonly ArmadaSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal ArchitectFanOutService(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Git = context.Git;
            _Header = context.Header;
            _Logging = context.Logging;
            _Settings = context.Settings;
        }

        #endregion

        #region Internal-Methods

        internal static void RetitleDependentChain(
            ArchitectFanOutWork work,
            Mission dependency,
            string parsedTitle,
            string parsedDescription)
        {
            List<Mission> directDependents = work.Missions
                .Where(m => m.DependsOnMissionId == dependency.Id)
                .OrderBy(m => m.CreatedUtc)
                .ToList();

            foreach (Mission dependent in directDependents)
            {
                string dependentPersona = PersonaCatalog.NormalizeName(dependent.Persona);
                if (String.IsNullOrEmpty(dependentPersona)) dependentPersona = dependent.Persona ?? PersonaCatalog.Worker;

                dependent.Persona = dependentPersona;
                dependent.Title = parsedTitle + " [" + dependentPersona + "]";
                dependent.Description = parsedDescription;
                dependent.BranchName = null;
                work.Changed(dependent);
                RetitleDependentChain(work, dependent, parsedTitle, parsedDescription);
            }
        }

        internal void ApplyArchitectMissionDependencies(
            Mission architectMission,
            List<ParsedArchitectMission> parsed,
            ArchitectFanOutWork work)
        {
            if (architectMission == null) throw new ArgumentNullException(nameof(architectMission));
            if (String.IsNullOrEmpty(architectMission.VoyageId)) return;
            if (parsed == null || parsed.Count == 0) return;

            // The plan was validated before the fan-out (RS-4.4), so every parent is an earlier slice within the depth limit.
            // An invalid plan stored before that check (met only by recovery) leaves its slices after the Architect.
            List<Mission> voyageMissions = work.Missions;
            bool hierarchyValid = ValidateArchitectHierarchy(parsed) == null;
            Dictionary<int, Mission> workerRootsByIndex = new Dictionary<int, Mission>();
            for (int i = 0; i < parsed.Count; i++)
            {
                Mission? workerRoot = FindArchitectSlice(voyageMissions, architectMission.Id, i);
                if (workerRoot != null) workerRootsByIndex[i + 1] = workerRoot;
            }

            Dictionary<int, int> depthByIndex = new Dictionary<int, int>();
            for (int i = 0; i < parsed.Count; i++)
            {
                if (!workerRootsByIndex.TryGetValue(i + 1, out Mission? workerRoot)) continue;
                // Idempotent: only a slice still waiting on the Architect is changed, so a recovery pass never touches one
                // that already started (H11).
                bool waitingOnArchitect = workerRoot.Status == MissionStatusEnum.Pending && workerRoot.DependsOnMissionId == architectMission.Id;
                if (!waitingOnArchitect) continue;
                ApplySliceOwnership(work, workerRoot, parsed[i]);

                if (!hierarchyValid || !parsed[i].DependsOnIndex.HasValue || !workerRootsByIndex.TryGetValue(parsed[i].DependsOnIndex!.Value, out Mission? parentRoot)) continue;
                int parentIndex = parsed[i].DependsOnIndex!.Value;
                int depth = (depthByIndex.TryGetValue(parentIndex, out int parentDepth) ? parentDepth : parentRoot.Depth) + 1;

                // D13/D16: the slice becomes a child of the parent slice's root. It starts once the parent's last stage
                // finishes (gate 2), so it no longer needs its own link to the Architect; the branch rule picks continue
                // for the first child and fork for later ones unless the plan says otherwise.
                workerRoot.ParentMissionId = parentRoot.Id;
                workerRoot.DependsOnMissionId = null;
                workerRoot.Depth = depth;
                workerRoot.BranchStrategy = parsed[i].BranchStrategy == "continue" ? MissionBranchStrategyEnum.ContinueParent
                    : parsed[i].BranchStrategy == "fork" ? MissionBranchStrategyEnum.ForkFromParent
                    : MissionBranchStrategyEnum.New;
                workerRoot.BranchName = null;
                work.Changed(workerRoot);
                depthByIndex[i + 1] = depth;
                _Logging.Debug(_Header + "architect made worker mission " + workerRoot.Id + " a child of " + parentRoot.Id + " (plan mission " + parentIndex + ")");
            }
        }

        internal static Mission FindTerminalPipelineStage(IEnumerable<Mission> voyageMissions, Mission root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            Mission current = root;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            while (!String.IsNullOrEmpty(current.Id) && visited.Add(current.Id))
            {
                Mission? next = voyageMissions
                    .Where(m => m.DependsOnMissionId == current.Id)
                    .OrderBy(m => m.CreatedUtc)
                    .FirstOrDefault();
                if (next == null) break;
                current = next;
            }

            return current;
        }

        internal async Task<bool> HasDependentPipelineStages(string? voyageId, string missionId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            return voyageMissions.Any(m => m.DependsOnMissionId == missionId);
        }

        /// <summary>
        /// Build the context a Worker slice gets about the Architect's whole plan: the overall objective and every
        /// planned mission, with its own part marked.
        /// </summary>
        internal async Task<string> BuildArchitectSliceContextAsync(Mission architectMission, List<ParsedArchitectMission> parsed, int index, CancellationToken token)
        {
            // Each Worker gets one slice of the plan; give it the overall objective and the shape of the whole plan so
            // it neither drifts from the goal nor redoes a sibling's part.
            StringBuilder sb = new StringBuilder();
            sb.Append("\n\n## Full plan reference\nArchitect mission: ").Append(architectMission.Id)
                .Append(". Retrieve its complete output with mission_status or GET /api/v1/missions/")
                .Append(architectMission.Id).Append(" when relevant context is missing; do only your assigned slice.\n");
            Voyage? voyage = String.IsNullOrEmpty(architectMission.VoyageId) ? null : await _Database.Voyages.ReadAsync(architectMission.VoyageId!, token).ConfigureAwait(false);
            string objective = voyage != null && !String.IsNullOrWhiteSpace(voyage.Description) ? voyage.Description! : (voyage?.Title ?? String.Empty);
            if (!String.IsNullOrWhiteSpace(objective))
            {
                const int maxObjective = 2000;
                string trimmed = objective.Trim();
                if (trimmed.Length > maxObjective) trimmed = trimmed.Substring(0, maxObjective) + "\n(truncated)";
                sb.Append("\n\n## Overall objective (context only; do only your part)\n").Append(trimmed);
            }

            if (parsed.Count > 1)
            {
                sb.Append("\n\n## The Architect's plan (your part is marked; other parts are done by other missions)\n");
                for (int i = 0; i < parsed.Count; i++)
                    sb.Append(i + 1).Append(". ").Append(parsed[i].Title).Append(i == index ? "  <- this mission" : String.Empty).Append('\n');
            }

            return sb.ToString();
        }

        internal List<ParsedArchitectMission> ParseArchitectOutput(Mission architectMission, ArchitectPlan? parsedPlan = null)
        {
            List<ParsedArchitectMission> results = new List<ParsedArchitectMission>();

            ArchitectPlan? plan = parsedPlan;
            if (plan == null && !ArchitectPlanParser.TryParse(StageSubmissionText.EffectiveOutput(architectMission), out plan, out _, _Settings.Concurrency.MaxHierarchyDepth)) plan = null;
            if (plan != null)
            {
                foreach (string warning in plan.Warnings)
                    _Logging.Warn(_Header + "architect plan of " + architectMission.Id + ": " + warning);
                foreach (ArchitectPlanMission planned in plan.Missions)
                {
                    ParsedArchitectMission parsed = new ParsedArchitectMission();
                    parsed.Title = planned.Title ?? String.Empty;
                    parsed.Description = ArchitectPlanParser.DescribeMission(plan, planned);
                    parsed.DependsOnIndex = planned.Parent;
                    parsed.WaitForOtherMissions = planned.WaitForOtherMissions;
                    parsed.WorkerEnvelope = planned.WorkerEnvelope;
                    parsed.OwnedPaths = (planned.OwnedPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    parsed.ReadsPaths = (planned.ReadsPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    parsed.BranchStrategy = planned.BranchStrategy;
                    results.Add(parsed);
                }

                return results;
            }

            // Only a structured plan block counts (the submit tool or an armada-plan fence). Legacy [ARMADA:MISSION]
            // markers were removed (D-027), and numbered narration is never inferred into missions: a plan without a
            // structured block gets a repair turn instead.
            return results;
        }

        // RS-4.4: the parsed plan, in any format, through the one hierarchy and ownership validator. Null when valid.
        internal string? ValidateArchitectHierarchy(List<ParsedArchitectMission> parsed)
        {
            List<PlanNode> nodes = parsed.Select(p => new PlanNode
            {
                Title = p.Title,
                Parent = p.DependsOnIndex,
                UnresolvedParent = !p.DependsOnIndex.HasValue && !String.IsNullOrWhiteSpace(p.DependsOnReference) ? p.DependsOnReference!.Trim() : null,
                BranchStrategy = p.BranchStrategy,
                OwnedPaths = p.OwnedPaths,
                WaitsForAll = p.WaitForOtherMissions
            }).ToList();
            PlanValidationResult result = MissionPlanValidator.Validate(nodes, _Settings.Concurrency.MaxHierarchyDepth, false);
            return result.IsValid ? null : "Architect plan rejected: " + result.Describe()
                + ". Remediation: give each mission an earlier mission's number as its parent, or none.";
        }

        internal async Task CleanupArchitectBranchAsync(Mission mission, Dock? dock, CancellationToken token)
        {
            if (_Git == null)
            {
                _Logging.Debug(_Header + "git service unavailable -- skipping architect branch cleanup for mission " + mission.Id);
                return;
            }

            string? branchName = mission.BranchName ?? dock?.BranchName;
            if (String.IsNullOrEmpty(branchName) || String.IsNullOrEmpty(mission.VesselId))
            {
                return;
            }

            Vessel? vessel = !String.IsNullOrEmpty(mission.TenantId)
                ? await _Database.Vessels.ReadAsync(mission.TenantId, mission.VesselId, token).ConfigureAwait(false)
                : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);

            // A Harbor-hosted dock's branch lives in the repository on that Harbor's host.
            DockHost? harborHost = _Context.StageCheckRunner.HarborHostFor(dock);
            IGitService git = harborHost != null ? harborHost.Git : _Git;
            string? repositoryPath = harborHost != null ? dock!.RepositoryPath : vessel?.LocalPath;
            string? workingDirectory = harborHost != null ? (dock!.CheckoutPath ?? dock.RepositoryPath) : vessel?.WorkingDirectory;

            if (vessel == null || String.IsNullOrEmpty(repositoryPath))
            {
                _Logging.Warn(_Header + "unable to clean architect branch " + branchName +
                    " for mission " + mission.Id + " because vessel metadata is incomplete");
                return;
            }

            BranchCleanupPolicyEnum cleanupPolicy = vessel.BranchCleanupPolicy ?? _Settings.BranchCleanupPolicy;
            if (cleanupPolicy == BranchCleanupPolicyEnum.None)
            {
                _Logging.Debug(_Header + "branch cleanup policy is None - retaining architect branch " + branchName + " after handoff");
                return;
            }

            try
            {
                await git.DeleteLocalBranchAsync(repositoryPath, branchName, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "deleted architect branch " + branchName + " from bare repo after successful handoff");
            }
            catch (Exception branchEx)
            {
                _Logging.Warn(_Header + "failed to delete architect branch " + branchName + " from bare repo: " + branchEx.Message);
            }

            if (cleanupPolicy == BranchCleanupPolicyEnum.LocalAndRemote)
            {
                if (String.IsNullOrEmpty(workingDirectory))
                {
                    _Logging.Warn(_Header + "cannot delete remote architect branch " + branchName +
                        " because vessel working directory is not configured");
                    return;
                }

                try
                {
                    await git.DeleteRemoteBranchAsync(workingDirectory, branchName, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "deleted remote architect branch " + branchName + " after successful handoff");
                }
                catch (Exception remoteBranchEx)
                {
                    _Logging.Warn(_Header + "failed to delete remote architect branch " + branchName + ": " + remoteBranchEx.Message);
                }
            }
        }

        /// <summary>
        /// Ensure parsed architect mission definitions are visible in the mission log even when
        /// the source was a diff snapshot or other non-stdout artifact.
        /// </summary>
        internal async Task ProjectArchitectMissionsToLogAsync(Mission architectMission, List<ParsedArchitectMission> parsed, CancellationToken token)
        {
            if (architectMission == null) throw new ArgumentNullException(nameof(architectMission));
            if (parsed == null || parsed.Count == 0) return;

            try
            {
                string missionLogDir = Path.Combine(_Settings.LogDirectory, "missions");
                Directory.CreateDirectory(missionLogDir);
                string logFilePath = Path.Combine(missionLogDir, architectMission.Id + ".log");

                string existing = File.Exists(logFilePath)
                    ? await File.ReadAllTextAsync(logFilePath, token).ConfigureAwait(false)
                    : String.Empty;

                if (existing.Contains("[Armada] Parsed architect mission definitions:"))
                {
                    return;
                }

                using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
                {
                    await writer.WriteLineAsync(String.Empty).ConfigureAwait(false);
                    await writer.WriteLineAsync("[Armada] Parsed architect mission definitions:").ConfigureAwait(false);
                    foreach (ParsedArchitectMission mission in parsed)
                    {
                        await writer.WriteLineAsync("[Armada] Planned mission: " + mission.Title).ConfigureAwait(false);
                        if (!String.IsNullOrEmpty(mission.DependsOnReference))
                        {
                            await writer.WriteLineAsync("Depends on: " + mission.DependsOnReference).ConfigureAwait(false);
                        }
                        if (!String.IsNullOrEmpty(mission.Description))
                        {
                            await writer.WriteLineAsync(mission.Description).ConfigureAwait(false);
                        }
                        await writer.WriteLineAsync(String.Empty).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not project architect mission definitions into log for " + architectMission.Id + ": " + ex.ToString());
            }
        }

        internal async Task<Mission> CreateArchitectSliceAsync(Mission architect, Mission template, ArchitectFanOutWork work, List<ParsedArchitectMission> parsed, int index, CancellationToken token)
        {
            Mission worker = new Mission(parsed[index].Title + " [Worker]", parsed[index].Description
                + await BuildArchitectSliceContextAsync(architect, parsed, index, token).ConfigureAwait(false));
            worker.ArchitectSourceMissionId = architect.Id;
            worker.ArchitectPlanIndex = index;
            worker.TenantId = architect.TenantId;
            worker.UserId = architect.UserId;
            worker.VoyageId = architect.VoyageId;
            worker.VesselId = architect.VesselId;
            worker.IsEvaluation = architect.IsEvaluation;
            worker.Persona = "Worker";
            worker.Mode = template.Mode;
            worker.WaitForVoyageWorkers = parsed[index].WaitForOtherMissions;
            worker.DependsOnMissionId = architect.Id;
            worker.RequiresReview = template.RequiresReview;
            worker.ReviewDenyAction = template.ReviewDenyAction;
            worker.BranchName = null;
            work.Create(worker);
            await CopyPlaybookSnapshotsAsync(work, template.Id, worker.Id, token).ConfigureAwait(false);

            await CloneDependentChainAsync(work, template, worker, parsed[index].Title, parsed[index].Description, token).ConfigureAwait(false);
            await _Context.WorkerEnvelopeService.ApplyArchitectWorkerEnvelopeAsync(architect, worker, parsed[index], work, token).ConfigureAwait(false);
            return worker;
        }

        /// <summary>
        /// Write a computed fan-out in one transaction together with the Architect's completion mark (RS-4.3): every slice,
        /// stage, rewiring and ownership change exists, or none does. A slice or the Architect whose status changed since
        /// it was read refuses the whole write; recovery computes the fan-out again from what is stored.
        /// </summary>
        internal async Task<bool> CommitArchitectFanOutAsync(Mission architect, ArchitectFanOutWork work, CancellationToken token)
        {
            CrashPoints.Hit(CrashPointNames.FanOutPlanned);
            Mission? current = await _Database.Missions.ReadAsync(architect.Id, token).ConfigureAwait(false);
            if (current == null) return false;
            List<MissionGuardedWrite> extra = new List<MissionGuardedWrite>();
            if (!current.FanOutCompletedUtc.HasValue)
            {
                current.FanOutCompletedUtc = DateTime.UtcNow;
                extra.Add(new MissionGuardedWrite(current, new[] { current.Status }));
            }

            // RS-6.9: the Architect's handoff artifact is written with the slices that read it.
            MissionWriteBatch fanOut = work.ToBatch(extra.ToArray());
            HandoffArtifact? artifact = await new HandoffArtifactService(_Database, _Logging).PrepareForHandoffAsync(architect, token).ConfigureAwait(false);
            if (artifact != null) fanOut.HandoffArtifacts.Add(artifact);
            if (!await _Database.Missions.WriteBatchAsync(fanOut, token).ConfigureAwait(false))
            {
                _Logging.Warn(_Header + "the fan-out of Architect mission " + architect.Id + " was not written: a mission it changes moved on meanwhile; recovery will compute it again");
                return false;
            }

            architect.FanOutCompletedUtc = current.FanOutCompletedUtc;
            CrashPoints.Hit(CrashPointNames.FanOutWritten);
            if (work.CreatedCount > 0)
                _Logging.Info(_Header + "architect mission " + architect.Id + " fanned out: " + work.CreatedCount + " mission(s) created");

            foreach (ArmadaEvent evt in work.Events)
            {
                try
                {
                    await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not record event " + evt.EventType + " for mission " + evt.MissionId + ": " + ex.Message);
                }
            }
            return true;
        }

        /// <summary>
        /// Finish an Architect fan-out that was not marked complete (H11). The fan-out is written in one transaction with its
        /// completion mark (RS-4.3), so an unmarked Architect normally has written nothing and the normal fan-out runs again.
        /// A partial fan-out written before that change is reconciled against the plan: a slice whose Worker (found by its
        /// plan index) is missing is created, a Worker missing its stage chain gets one, and dependencies are applied to
        /// slices that have not started, all in one transaction. Nothing that exists is created twice, and a slice that
        /// already started is never rewired.
        /// </summary>
        internal async Task<bool> TryRecoverArchitectFanOutAsync(Mission architect, CancellationToken token)
        {
            if (architect.FanOutCompletedUtc.HasValue) return false;
            if (_Service.IsCompletionInFlight(architect.Id)) return false;
            Voyage? voyage = await _Database.Voyages.ReadAsync(architect.VoyageId!, token).ConfigureAwait(false);
            if (voyage == null || voyage.Status == VoyageStatusEnum.Complete || voyage.Status == VoyageStatusEnum.Failed || voyage.Status == VoyageStatusEnum.Cancelled) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(architect.VoyageId!, token).ConfigureAwait(false);

            // Never started: the first slice was not written into the waiting Worker. Run the normal fan-out.
            if (voyageMissions.Any(m => m.DependsOnMissionId == architect.Id && m.Status == MissionStatusEnum.Pending
                && PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker) && !MissionHandoffService.IsPipelineHandoffPrepared(m, architect)))
            {
                _Logging.Warn(_Header + "re-driving the fan-out of Architect mission " + architect.Id + ", which an interruption left unstarted");
                return await _Context.MissionHandoffService.TryHandoffToNextStageAsync(architect, token).ConfigureAwait(false);
            }

            List<ParsedArchitectMission> parsed = ParseArchitectOutput(architect);
            if (parsed.Count == 0) return false;
            Mission? template = FindArchitectSlice(voyageMissions, architect.Id, 0);
            if (template == null) return false;

            ArchitectFanOutWork work = new ArchitectFanOutWork(voyageMissions);
            int repaired = 0;
            bool templateHasChain = voyageMissions.Any(m => m.DependsOnMissionId == template.Id);
            for (int i = 1; i < parsed.Count; i++)
            {
                Mission? worker = FindArchitectSlice(work.Missions, architect.Id, i);
                if (worker == null)
                {
                    await CreateArchitectSliceAsync(architect, template, work, parsed, i, token).ConfigureAwait(false);
                    repaired++;
                }
                else if (templateHasChain && !work.Missions.Any(m => m.DependsOnMissionId == worker.Id))
                {
                    await CloneDependentChainAsync(work, template, worker, parsed[i].Title, parsed[i].Description, token).ConfigureAwait(false);
                    repaired++;
                }
            }

            ApplyArchitectMissionDependencies(architect, parsed, work);
            if (!await CommitArchitectFanOutAsync(architect, work, token).ConfigureAwait(false)) return false;
            if (repaired > 0)
                _Logging.Warn(_Header + "finished the interrupted fan-out of Architect mission " + architect.Id + ": " + repaired + " slice(s) repaired");
            return repaired > 0;
        }

        #endregion

        #region Private-Methods

        private async Task CloneDependentChainAsync(
            ArchitectFanOutWork work,
            Mission templateMission,
            Mission newDependency,
            string parsedTitle,
            string parsedDescription,
            CancellationToken token)
        {
            List<Mission> directDependents = work.Missions
                .Where(m => m.DependsOnMissionId == templateMission.Id)
                .OrderBy(m => m.CreatedUtc)
                .ToList();

            foreach (Mission templateChild in directDependents)
            {
                string childPersona = PersonaCatalog.NormalizeName(templateChild.Persona);
                if (String.IsNullOrEmpty(childPersona)) childPersona = templateChild.Persona ?? PersonaCatalog.Worker;

                Mission clonedStage = new Mission(
                    parsedTitle + " [" + childPersona + "]",
                    parsedDescription);
                clonedStage.TenantId = templateChild.TenantId;
                clonedStage.UserId = templateChild.UserId;
                clonedStage.VoyageId = templateChild.VoyageId;
                clonedStage.VesselId = templateChild.VesselId;
                clonedStage.IsEvaluation = templateChild.IsEvaluation;
                clonedStage.WorkerEnvelope = templateChild.WorkerEnvelope;
                clonedStage.Persona = childPersona;
                clonedStage.Mode = templateChild.Mode;
                clonedStage.DependsOnMissionId = newDependency.Id;
                clonedStage.RequiresReview = templateChild.RequiresReview;
                clonedStage.ReviewDenyAction = templateChild.ReviewDenyAction;
                clonedStage.BranchName = null;
                work.Create(clonedStage);
                await CopyPlaybookSnapshotsAsync(work, templateChild.Id, clonedStage.Id, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "architect planned chained stage " + clonedStage.Id +
                    " (" + clonedStage.Persona + ") depending on " + newDependency.Id);
                await CloneDependentChainAsync(work, templateChild, clonedStage, parsedTitle, parsedDescription, token).ConfigureAwait(false);
            }
        }

        private static void ApplySliceOwnership(ArchitectFanOutWork work, Mission workerRoot, ParsedArchitectMission slice)
        {
            string? owned = slice.OwnedPaths.Count == 0 ? null : JsonSerializer.Serialize(slice.OwnedPaths);
            string? reads = slice.ReadsPaths.Count == 0 ? null : JsonSerializer.Serialize(slice.ReadsPaths);
            HashSet<string> chain = new HashSet<string>(StringComparer.Ordinal);
            for (Mission? member = workerRoot; member != null && chain.Add(member.Id);
                member = work.Missions.Where(m => m.DependsOnMissionId == member.Id).OrderBy(m => m.CreatedUtc).FirstOrDefault())
            {
                if (member.OwnedPathsJson == owned && member.ReadsPathsJson == reads) continue;
                member.OwnedPathsJson = owned;
                member.ReadsPathsJson = reads;
                work.Changed(member);
            }
        }

        // A slice or stage created by the fan-out carries the playbooks of the mission it is cloned from: the voyage's
        // selections are snapshotted per mission at dispatch, and a mission reads only its own snapshots (RS-2.8).
        private async Task CopyPlaybookSnapshotsAsync(ArchitectFanOutWork work, string fromMissionId, string toMissionId, CancellationToken token)
        {
            List<MissionPlaybookSnapshot> snapshots = work.PendingSnapshots(fromMissionId)
                ?? await _Database.Playbooks.GetMissionSnapshotsAsync(fromMissionId, token).ConfigureAwait(false);
            if (snapshots.Count == 0) return;

            // Content is copied; where a playbook was materialized is per mission and is resolved again at launch.
            List<MissionPlaybookSnapshot> copies = snapshots.Select(s => new MissionPlaybookSnapshot
            {
                PlaybookId = s.PlaybookId,
                FileName = s.FileName,
                Description = s.Description,
                Content = s.Content,
                DeliveryMode = s.DeliveryMode,
                SourceLastUpdateUtc = s.SourceLastUpdateUtc
            }).ToList();
            work.SetSnapshots(toMissionId, copies);
        }

        /// <summary>
        /// The Worker slice an Architect's plan entry created, found by its typed source and plan index (never by title,
        /// which a rename or a duplicate plan title would break). The newest live slice wins after a restarted fan-out.
        /// </summary>
        private static Mission? FindArchitectSlice(List<Mission> voyageMissions, string architectId, int planIndex)
        {
            return voyageMissions
                .Where(m => m.ArchitectPlanIndex == planIndex
                    && String.Equals(m.ArchitectSourceMissionId, architectId, StringComparison.Ordinal)
                    && PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker)
                    && m.Status != MissionStatusEnum.Cancelled)
                .OrderByDescending(m => m.CreatedUtc)
                .FirstOrDefault();
        }

        #endregion
    }
}
