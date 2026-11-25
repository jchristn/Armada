namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The one path that creates a voyage and its missions (RELIABILITY_SIMPLICITY_PLAN.md RS-4.1). REST, MCP, WebSocket,
    /// planning sessions, fleet actions, and battery runs all call <see cref="CreateAsync"/>. It owns validation, caller
    /// scope, pipeline resolution (a standard voyage is a pipeline with one stage), playbooks, the mission hierarchy,
    /// owned paths, the dispatch advisor, and objective linking.
    /// </summary>
    public sealed class VoyageCreationService
    {
        #region Public-Members

        /// <summary>
        /// Consulted just before each dispatched mission is created (overlap warnings, brief lint), or null. It never
        /// blocks a dispatch.
        /// </summary>
        public IMissionDispatchAdvisor? DispatchAdvisor { get; set; } = null;

        /// <summary>
        /// When true, an admitted mission's dock and agent launch run in the background.
        /// </summary>
        public bool LaunchInBackground { get; set; } = false;

        /// <summary>
        /// Objective service used to link a voyage to its objective, or null for a private instance.
        /// </summary>
        public ObjectiveService? Objectives { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[VoyageCreationService] ";
        private readonly LoggingModule _Logging;
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IMissionService _Missions;
        private readonly IPlaybookService _Playbooks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="missions">Mission service (assignment).</param>
        /// <param name="playbooks">Playbook service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public VoyageCreationService(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings, IMissionService missions, IPlaybookService playbooks)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Missions = missions ?? throw new ArgumentNullException(nameof(missions));
            _Playbooks = playbooks ?? throw new ArgumentNullException(nameof(playbooks));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a voyage and its missions, then try to assign each first stage.
        /// </summary>
        /// <param name="caller">Caller, or null when Armada itself dispatches (fleet actions, battery runs, planning). A
        /// caller must be able to see the vessel and objective; the voyage belongs to the caller.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created voyage, with any overlap and brief lint warnings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the request or its title is missing.</exception>
        /// <exception cref="ArgumentException">Thrown when the request is invalid (no vessel or no missions without a bare
        /// voyage, a bad mission tree, a limit exceeded, approval options the caller may not set).</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel, objective, or a named pipeline is not found or
        /// not visible to the caller (<see cref="PipelineNotFoundException"/> for a pipeline).</exception>
        public async Task<Voyage> CreateAsync(AuthContext? caller, VoyageCreationRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrEmpty(request.Title)) throw new ArgumentNullException(nameof(request.Title));
            List<MissionDescription> missions = request.Missions ?? new List<MissionDescription>();
            List<SelectedPlaybook> playbooks = request.SelectedPlaybooks ?? new List<SelectedPlaybook>();
            CollectionLimits.RequireRequestItems(missions, "Missions");
            CollectionLimits.RequireConfigItems(playbooks, "SelectedPlaybooks");
            CollectionLimits.RequireConfigItems(request.CaptainOverrides, "CaptainOverrides");
            if (caller != null) request.ApprovalOptions?.ValidateFor(caller);

            DispatchValidationResult validation = await ValidateAsync(request.ObjectiveId, request.PipelineId, request.PipelineName,
                request.VesselId, missions.Count, request.AllowBareVoyage, token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                string message = validation.Message ?? "Invalid dispatch request";
                if (validation.Error == DispatchValidationErrorEnum.PipelineNotFound)
                    throw new PipelineNotFoundException(request.PipelineId ?? request.PipelineName ?? String.Empty);
                if (validation.Error == DispatchValidationErrorEnum.ObjectiveNotFound) throw new KeyNotFoundException(message);
                throw new ArgumentException(message, nameof(request));
            }

            if (caller != null && !String.IsNullOrWhiteSpace(request.ObjectiveId)
                && await ObjectiveService().ReadAsync(caller, request.ObjectiveId!, token).ConfigureAwait(false) == null)
                throw new KeyNotFoundException("Objective not found: " + request.ObjectiveId);

            Vessel? vessel = null;
            if (!String.IsNullOrWhiteSpace(request.VesselId))
            {
                vessel = await ReadVesselAsync(caller, request.VesselId!, token).ConfigureAwait(false);
                if (vessel == null) throw new KeyNotFoundException("Vessel not found: " + request.VesselId);
            }

            Voyage voyage = validation.IsBareVoyage
                ? await CreateBareAsync(caller, request, vessel, playbooks, token).ConfigureAwait(false)
                : await CreateWithMissionsAsync(caller, request, vessel!, missions, playbooks, validation.ResolvedPipelineId, token).ConfigureAwait(false);

            if (!String.IsNullOrWhiteSpace(request.ObjectiveId))
            {
                AuthContext linker = caller ?? AuthContext.Authenticated(voyage.TenantId ?? Constants.DefaultTenantId, voyage.UserId ?? Constants.DefaultUserId, true, true, "System");
                await ObjectiveService().LinkVoyageAsync(linker, request.ObjectiveId!, voyage.Id, token).ConfigureAwait(false);
            }

            return voyage;
        }

        /// <summary>
        /// Check a dispatch request without creating anything: objective existence, pipeline by id or name, and whether
        /// it is a bare voyage.
        /// </summary>
        /// <param name="objectiveId">Objective to link, or null.</param>
        /// <param name="pipelineId">Pipeline id (a name is accepted too), or null.</param>
        /// <param name="pipelineName">Pipeline name, or null.</param>
        /// <param name="vesselId">Vessel id, or null.</param>
        /// <param name="missionCount">Number of missions.</param>
        /// <param name="allowBareVoyage">Whether no vessel or no missions is a bare voyage rather than an error.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, with the resolved pipeline id.</returns>
        public async Task<DispatchValidationResult> ValidateAsync(
            string? objectiveId,
            string? pipelineId,
            string? pipelineName,
            string? vesselId,
            int missionCount,
            bool allowBareVoyage,
            CancellationToken token = default)
        {
            CollectionLimits.RequireCount(missionCount, "Missions", CollectionLimits.MaxRequestItems);

            if (!String.IsNullOrWhiteSpace(objectiveId))
            {
                Objective? objective = await _Database.Objectives.ReadAsync(objectiveId!, token).ConfigureAwait(false);
                if (objective == null)
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.ObjectiveNotFound, "Objective not found: " + objectiveId);
            }

            // A named pipeline that does not exist is an error; the vessel and fleet defaults apply only when none was named.
            string? resolvedPipelineId = String.IsNullOrWhiteSpace(pipelineId) ? null : pipelineId!.Trim();
            if (resolvedPipelineId != null)
            {
                Pipeline? explicitPipeline = await _Database.Pipelines.ReadAsync(resolvedPipelineId, token).ConfigureAwait(false)
                    ?? await _Database.Pipelines.ReadByNameAsync(resolvedPipelineId, token).ConfigureAwait(false);
                if (explicitPipeline == null)
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.PipelineNotFound, "Pipeline not found: " + resolvedPipelineId);
                resolvedPipelineId = explicitPipeline.Id;
            }
            else if (!String.IsNullOrWhiteSpace(pipelineName))
            {
                Pipeline? namedPipeline = await _Database.Pipelines.ReadByNameAsync(pipelineName!, token).ConfigureAwait(false);
                if (namedPipeline == null)
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.PipelineNotFound, "Pipeline not found: " + pipelineName);
                resolvedPipelineId = namedPipeline.Id;
            }

            bool isBareVoyage = String.IsNullOrWhiteSpace(vesselId) || missionCount == 0;
            if (isBareVoyage && !allowBareVoyage)
            {
                if (String.IsNullOrWhiteSpace(vesselId))
                    return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.MissingVessel, "A vessel id is required to dispatch a voyage.");
                return DispatchValidationResult.Invalid(DispatchValidationErrorEnum.NoMissions, "At least one mission is required to dispatch a voyage.");
            }

            return DispatchValidationResult.Valid(resolvedPipelineId, isBareVoyage);
        }

        #endregion

        #region Internal-Methods

        internal async Task<MissionDispatchAdvice?> AdviseAsync(Mission mission, Vessel vessel, CancellationToken token)
        {
            IMissionDispatchAdvisor? advisor = DispatchAdvisor;
            if (advisor == null) return null;
            try
            {
                return await advisor.AdviseAsync(mission, vessel, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "dispatch advisor failed for mission \"" + mission.Title + "\" on vessel " + vessel.Id + " (dispatch continues): " + ex.Message);
                return null;
            }
        }

        internal static void ApplyAdvice(Mission mission, MissionDispatchAdvice? advice, List<MissionOverlapWarning>? voyageWarnings, List<BriefLintFinding>? voyageLint)
        {
            if (advice == null) return;
            foreach (MissionOverlapWarning w in advice.OverlapWarnings) w.MissionId = mission.Id;
            mission.OverlapWarnings = advice.OverlapWarnings.Count > 0 ? advice.OverlapWarnings.Take(BlackBoxInsightLimits.MaxOverlapWarnings).ToList() : null;
            mission.BriefLintWarnings = advice.BriefLintWarnings.Count > 0 ? advice.BriefLintWarnings.Take(BlackBoxInsightLimits.MaxLintFindings).ToList() : null;
            if (voyageWarnings != null) voyageWarnings.AddRange(advice.OverlapWarnings);
            if (voyageLint != null) voyageLint.AddRange(advice.BriefLintWarnings);
        }

        internal async Task PersistMissionPlaybooksAsync(Mission mission, List<SelectedPlaybook>? selections, CancellationToken token)
        {
            mission.SelectedPlaybooks = ClonePlaybooks(selections);
            if (mission.SelectedPlaybooks.Count == 0 || String.IsNullOrEmpty(mission.TenantId))
            {
                mission.PlaybookSnapshots = new List<MissionPlaybookSnapshot>();
                return;
            }

            List<MissionPlaybookSnapshot> snapshots = await _Playbooks.CreateSnapshotsAsync(mission.TenantId, mission.SelectedPlaybooks, token).ConfigureAwait(false);
            mission.PlaybookSnapshots = snapshots;
            await _Database.Playbooks.SetMissionSnapshotsAsync(mission.Id, snapshots, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private ObjectiveService ObjectiveService()
        {
            ObjectiveService? objectives = Objectives;
            if (objectives == null)
            {
                objectives = new ObjectiveService(_Database, _Logging);
                Objectives = objectives;
            }

            return objectives;
        }

        private async Task<Vessel?> ReadVesselAsync(AuthContext? caller, string id, CancellationToken token)
        {
            if (caller == null || caller.IsAdmin) return await _Database.Vessels.ReadAsync(id, token).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await _Database.Vessels.ReadAsync(caller.TenantId!, id, token).ConfigureAwait(false);
            return await _Database.Vessels.ReadAsync(caller.TenantId!, caller.UserId!, id, token).ConfigureAwait(false);
        }

        private async Task<Voyage> CreateBareAsync(AuthContext? caller, VoyageCreationRequest request, Vessel? vessel, List<SelectedPlaybook> playbooks, CancellationToken token)
        {
            string tenantId = vessel?.TenantId ?? caller?.TenantId ?? Constants.DefaultTenantId;
            if (playbooks.Count > 0) await _Playbooks.ResolveSelectionsAsync(tenantId, playbooks, token).ConfigureAwait(false);

            Voyage voyage = NewVoyage(caller, request, tenantId, vessel?.UserId);
            voyage = await _Database.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            await PersistVoyagePlaybooksAsync(voyage, playbooks, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created bare voyage " + voyage.Id + ": " + voyage.Title);
            return voyage;
        }

        private async Task<Voyage> CreateWithMissionsAsync(
            AuthContext? caller,
            VoyageCreationRequest request,
            Vessel vessel,
            List<MissionDescription> missions,
            List<SelectedPlaybook> playbooks,
            string? pipelineId,
            CancellationToken token)
        {
            foreach (string captainId in missions.Select(m => m.RequestedCaptainId)
                .Concat(request.CaptainOverrides.Select(o => o.CaptainId))
                .Where(id => !String.IsNullOrEmpty(id)).Select(id => id!).Distinct())
            {
                Captain? requested = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
                if (requested != null) CaptainEligibility.RequireActive(requested);
            }

            // Resolve Key/ParentKey links before anything is written, so a bad tree creates nothing.
            MissionHierarchyPlan hierarchy = MissionHierarchyPlan.Resolve(missions, _Settings.Concurrency.MaxHierarchyDepth);
            if (playbooks.Count > 0 && !String.IsNullOrEmpty(vessel.TenantId))
                await _Playbooks.ResolveSelectionsAsync(vessel.TenantId, playbooks, token).ConfigureAwait(false);

            // Pipeline: explicit, else vessel default, else fleet default, else one Worker stage. When no pipeline is named
            // and every mission names its own persona (a Mission fleet action), each runs as one stage with that persona.
            bool personaDispatch = String.IsNullOrWhiteSpace(pipelineId) && missions.All(md => !String.IsNullOrWhiteSpace(md.Persona));
            Pipeline? pipeline = personaDispatch ? null : await ResolvePipelineAsync(pipelineId, vessel, token).ConfigureAwait(false);
            List<PipelineStage?> stages = pipeline == null ? new List<PipelineStage?>() : pipeline.Stages.OrderBy(s => s.Order).Select(s => (PipelineStage?)s).ToList();

            // A standard voyage is a pipeline with one Worker stage (or none): one mission per description, titled as
            // given, whose persona a description may override. Otherwise every description becomes a chain of stages.
            bool staged = stages.Count > 1 || (stages.Count == 1 && !PersonaCatalog.Matches(stages[0]!.PersonaName, PersonaCatalog.Worker));
            if (!staged && stages.Count == 0) stages.Add(null);

            Voyage voyage = NewVoyage(caller, request, vessel.TenantId, vessel.UserId);
            voyage = await _Database.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            CrashPoints.Hit(CrashPointNames.VoyageCreated);
            await PersistVoyagePlaybooksAsync(voyage, playbooks, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created voyage " + voyage.Id + ": " + voyage.Title + (pipeline != null ? " (pipeline: " + pipeline.Name + ")" : ""));

            Dictionary<int, Mission> roots = new Dictionary<int, Mission>();
            List<MissionOverlapWarning> voyageWarnings = new List<MissionOverlapWarning>();
            List<BriefLintFinding> voyageLint = new List<BriefLintFinding>();
            for (int index = 0; index < missions.Count; index++)
            {
                MissionDescription md = missions[index];
                string baseTitle = md.Title.Length > 60 ? md.Title.Substring(0, 60).TrimEnd() + "..." : md.Title;
                string? previousMissionId = null;
                foreach (PipelineStage? stage in stages)
                {
                    bool first = previousMissionId == null;
                    Mission mission;
                    if (staged)
                    {
                        string persona = PersonaCatalog.NormalizeName(stage!.PersonaName);
                        if (String.IsNullOrEmpty(persona)) persona = stage.PersonaName;
                        mission = new Mission("[" + persona + "] " + baseTitle, md.Description);
                        mission.Persona = persona;
                        mission.DependsOnMissionId = previousMissionId;
                    }
                    else
                    {
                        mission = new Mission(md.Title, md.Description);
                        if (stage != null) mission.Persona = stage.PersonaName;
                        if (!String.IsNullOrWhiteSpace(md.Persona)) mission.Persona = md.Persona!.Trim();
                    }

                    // Every stage carries its logical mission's parent so the tree shows it; only the root waits on it.
                    ApplyHierarchy(mission, md, hierarchy, roots, index);
                    mission.TenantId = voyage.TenantId;
                    mission.UserId = voyage.UserId;
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vessel.Id;
                    mission.IsEvaluation = voyage.IsEvaluation;
                    mission.Mode = md.Mode;
                    mission.Tier = md.Tier;
                    if (stage != null)
                    {
                        mission.RequiresReview = stage.RequiresReview;
                        mission.ReviewDenyAction = stage.ReviewDenyAction;
                    }

                    // A requested captain applies to the first stage only; later stages are routed by their persona.
                    if (first) mission.RequestedCaptainId = md.RequestedCaptainId;

                    MissionDispatchAdvice? advice = first ? await AdviseAsync(mission, vessel, token).ConfigureAwait(false) : null;
                    mission = await _Database.Missions.CreateAsync(mission, token).ConfigureAwait(false);
                    CrashPoints.Hit(CrashPointNames.VoyageMissionCreated);
                    if (first) roots[index] = mission;
                    ApplyAdvice(mission, advice, voyageWarnings, voyageLint);
                    await PersistMissionPlaybooksAsync(mission, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "created mission " + mission.Id + ": " + mission.Title
                        + (staged ? " (persona " + mission.Persona + (first ? "" : ", depends on " + previousMissionId) + ")" : ""));

                    // Later stages wait for their dependency; a first stage chained after an overlapping mission waits too.
                    if (first)
                    {
                        await _Missions.TryAssignAsync(mission, vessel, LaunchInBackground, token).ConfigureAwait(false);
                        CrashPoints.Hit(CrashPointNames.VoyageMissionAssigned);
                    }

                    previousMissionId = mission.Id;
                }
            }

            // InProgress only once a mission was admitted.
            List<Mission> created = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
            if (created.Any(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress))
                voyage.Status = VoyageStatusEnum.InProgress;
            voyage.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
            voyage.OverlapWarnings = voyageWarnings.Count > 0 ? voyageWarnings.Take(BlackBoxInsightLimits.MaxOverlapWarnings).ToList() : null;
            voyage.BriefLintWarnings = voyageLint.Count > 0 ? voyageLint.Take(BlackBoxInsightLimits.MaxLintFindings).ToList() : null;
            return voyage;
        }

        private static Voyage NewVoyage(AuthContext? caller, VoyageCreationRequest request, string? tenantId, string? vesselOwnerId)
        {
            // The voyage belongs to the caller; Armada's own dispatches belong to the vessel's owner.
            Voyage voyage = new Voyage(request.Title, request.Description);
            voyage.TenantId = tenantId;
            voyage.UserId = caller?.UserId ?? vesselOwnerId;
            voyage.Status = VoyageStatusEnum.Open;
            voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(request.CaptainOverrides);
            voyage.ApprovalOptions = request.ApprovalOptions?.Snapshot();
            voyage.LandingMode = request.LandingMode;
            voyage.IsEvaluation = request.IsEvaluation;
            voyage.SourcePlanningSessionId = request.SourcePlanningSessionId;
            voyage.SourcePlanningMessageId = request.SourcePlanningMessageId;
            return voyage;
        }

        private async Task PersistVoyagePlaybooksAsync(Voyage voyage, List<SelectedPlaybook> playbooks, CancellationToken token)
        {
            voyage.SelectedPlaybooks = ClonePlaybooks(playbooks);
            if (voyage.SelectedPlaybooks.Count > 0)
                await _Database.Playbooks.SetVoyageSelectionsAsync(voyage.Id, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
        }

        private static List<SelectedPlaybook> ClonePlaybooks(List<SelectedPlaybook>? selections)
        {
            if (selections == null || selections.Count == 0) return new List<SelectedPlaybook>();
            return selections.Select(s => new SelectedPlaybook { PlaybookId = s.PlaybookId, DeliveryMode = s.DeliveryMode }).ToList();
        }

        private static void ApplyHierarchy(Mission mission, MissionDescription md, MissionHierarchyPlan hierarchy, Dictionary<int, Mission> roots, int index)
        {
            List<string> owned = (md.OwnedPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            if (owned.Count > 0) mission.OwnedPathsJson = System.Text.Json.JsonSerializer.Serialize(owned);
            int parentIndex = hierarchy.ParentOf(index);
            if (parentIndex < 0) return;
            mission.ParentMissionId = roots[parentIndex].Id;
            mission.Depth = hierarchy.DepthOf(index);
            if (md.BranchStrategy.HasValue && md.BranchStrategy.Value != MissionBranchStrategyEnum.New) mission.BranchStrategy = md.BranchStrategy.Value;
        }

        private async Task<Pipeline?> ResolvePipelineAsync(string? pipelineId, Vessel vessel, CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(pipelineId))
            {
                string reference = pipelineId!.Trim();
                Pipeline? named = await _Database.Pipelines.ReadAsync(reference, token).ConfigureAwait(false)
                    ?? await _Database.Pipelines.ReadByNameAsync(reference, token).ConfigureAwait(false);
                return named ?? throw new PipelineNotFoundException(reference);
            }

            if (!String.IsNullOrEmpty(vessel.DefaultPipelineId))
            {
                Pipeline? vesselPipeline = await _Database.Pipelines.ReadAsync(vessel.DefaultPipelineId, token).ConfigureAwait(false);
                if (vesselPipeline != null) return vesselPipeline;
                _Logging.Warn(_Header + "vessel " + vessel.Id + " references missing pipeline " + vessel.DefaultPipelineId + ", clearing");
                vessel.DefaultPipelineId = null;
                await _Database.Vessels.UpdateAsync(vessel, token).ConfigureAwait(false);
            }

            if (!String.IsNullOrEmpty(vessel.FleetId))
            {
                Fleet? fleet = await _Database.Fleets.ReadAsync(vessel.FleetId, token).ConfigureAwait(false);
                if (fleet != null && !String.IsNullOrEmpty(fleet.DefaultPipelineId))
                {
                    Pipeline? fleetPipeline = await _Database.Pipelines.ReadAsync(fleet.DefaultPipelineId, token).ConfigureAwait(false);
                    if (fleetPipeline != null) return fleetPipeline;
                    _Logging.Warn(_Header + "fleet " + fleet.Id + " references missing pipeline " + fleet.DefaultPipelineId + ", clearing");
                    fleet.DefaultPipelineId = null;
                    await _Database.Fleets.UpdateAsync(fleet, token).ConfigureAwait(false);
                }
            }

            return null;
        }

        #endregion
    }
}
