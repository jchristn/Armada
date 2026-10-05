namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Event-backed objective records that capture scoped work before and alongside execution.
    /// </summary>
    public class ObjectiveService
    {
        /// <summary>
        /// Optional callback invoked whenever an objective changes.
        /// </summary>
        public Action<Objective>? OnObjectiveChanged { get; set; }

        private readonly DatabaseDriver _Database;
        private readonly SemaphoreSlim _BackfillLock = new SemaphoreSlim(1, 1);
        private bool _BackfillCompleted = false;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ObjectiveService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Enumerate objectives visible to the caller.
        /// </summary>
        public async Task<EnumerationResult<Objective>> EnumerateAsync(
            AuthContext auth,
            ObjectiveQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) throw new ArgumentNullException(nameof(query));

            await EnsureBackfilledAsync(token).ConfigureAwait(false);
            List<Objective> objectives = await ReadAllObjectivesAsync(auth, token).ConfigureAwait(false);
            IEnumerable<Objective> filtered = objectives;

            if (!String.IsNullOrWhiteSpace(query.Owner))
                filtered = filtered.Where(item => ContainsIgnoreCase(item.Owner, query.Owner));
            if (!String.IsNullOrWhiteSpace(query.Category))
                filtered = filtered.Where(item => ContainsIgnoreCase(item.Category, query.Category));
            if (!String.IsNullOrWhiteSpace(query.ParentObjectiveId))
                filtered = filtered.Where(item => String.Equals(item.ParentObjectiveId, query.ParentObjectiveId, StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.VesselId))
                filtered = filtered.Where(item => item.VesselIds.Contains(query.VesselId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.FleetId))
                filtered = filtered.Where(item => item.FleetIds.Contains(query.FleetId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.PlanningSessionId))
                filtered = filtered.Where(item => item.PlanningSessionIds.Contains(query.PlanningSessionId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.VoyageId))
                filtered = filtered.Where(item => item.VoyageIds.Contains(query.VoyageId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.MissionId))
                filtered = filtered.Where(item => item.MissionIds.Contains(query.MissionId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.CheckRunId))
                filtered = filtered.Where(item => item.CheckRunIds.Contains(query.CheckRunId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.ReleaseId))
                filtered = filtered.Where(item => item.ReleaseIds.Contains(query.ReleaseId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.DeploymentId))
                filtered = filtered.Where(item => item.DeploymentIds.Contains(query.DeploymentId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.IncidentId))
                filtered = filtered.Where(item => item.IncidentIds.Contains(query.IncidentId, StringComparer.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.Tag))
                filtered = filtered.Where(item => item.Tags.Contains(query.Tag, StringComparer.OrdinalIgnoreCase));
            if (query.Status.HasValue)
                filtered = filtered.Where(item => item.Status == query.Status.Value);
            if (query.BacklogState.HasValue)
                filtered = filtered.Where(item => item.BacklogState == query.BacklogState.Value);
            if (query.Kind.HasValue)
                filtered = filtered.Where(item => item.Kind == query.Kind.Value);
            if (query.Priority.HasValue)
                filtered = filtered.Where(item => item.Priority == query.Priority.Value);
            if (query.Effort.HasValue)
                filtered = filtered.Where(item => item.Effort == query.Effort.Value);
            if (!String.IsNullOrWhiteSpace(query.TargetVersion))
                filtered = filtered.Where(item => String.Equals(item.TargetVersion, query.TargetVersion, StringComparison.OrdinalIgnoreCase));
            if (query.FromUtc.HasValue)
                filtered = filtered.Where(item => item.CreatedUtc >= query.FromUtc.Value.ToUniversalTime());
            if (query.ToUtc.HasValue)
                filtered = filtered.Where(item => item.CreatedUtc <= query.ToUtc.Value.ToUniversalTime());

            string? search = Normalize(query.Search);
            if (!String.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(item =>
                    ContainsIgnoreCase(item.Title, search)
                    || ContainsIgnoreCase(item.Description, search)
                    || ContainsIgnoreCase(item.Owner, search)
                    || ContainsIgnoreCase(item.Category, search)
                    || ContainsIgnoreCase(item.TargetVersion, search)
                    || ContainsIgnoreCase(item.RefinementSummary, search)
                    || ContainsIgnoreCase(item.SourceProvider, search)
                    || ContainsIgnoreCase(item.SourceType, search)
                    || ContainsIgnoreCase(item.SourceId, search)
                    || ContainsIgnoreCase(item.SourceUrl, search)
                    || item.Tags.Any(tag => ContainsIgnoreCase(tag, search))
                    || item.AcceptanceCriteria.Any(criteria => ContainsIgnoreCase(criteria, search))
                    || item.NonGoals.Any(nonGoal => ContainsIgnoreCase(nonGoal, search))
                    || item.RolloutConstraints.Any(constraint => ContainsIgnoreCase(constraint, search))
                    || item.EvidenceLinks.Any(link => ContainsIgnoreCase(link, search))
                    || item.BlockedByObjectiveIds.Any(blockedBy => ContainsIgnoreCase(blockedBy, search)));
            }

            List<Objective> ordered = filtered
                .OrderByDescending(item => item.LastUpdateUtc)
                .ThenByDescending(item => item.Id, StringComparer.Ordinal)
                .ToList();

            int pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 500);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;
            List<Objective> page = ordered.Skip(offset).Take(pageSize).ToList();

            return new EnumerationResult<Objective>
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = ordered.Count,
                TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)ordered.Count / pageSize) : 0,
                Objects = page,
                TotalMs = 0
            };
        }

        /// <summary>
        /// Read one objective.
        /// </summary>
        public async Task<Objective?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            await EnsureBackfilledAsync(token).ConfigureAwait(false);
            Objective? objective = await ReadObjectiveRowAsync(auth, id, token).ConfigureAwait(false);
            if (objective != null)
                return objective;

            Objective? restored = await TryRehydrateObjectiveFromSnapshotsAsync(auth, id, token).ConfigureAwait(false);
            if (restored == null)
                return null;

            return await ReadObjectiveRowAsync(auth, id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create an objective.
        /// </summary>
        public async Task<Objective> CreateAsync(AuthContext auth, ObjectiveUpsertRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            await EnsureBackfilledAsync(token).ConfigureAwait(false);

            Objective objective = new Objective
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.UserId,
                Title = NormalizeRequired(request.Title, nameof(request.Title)),
                Description = Normalize(request.Description),
                Status = request.Status ?? ObjectiveStatusEnum.Draft,
                Kind = request.Kind ?? ObjectiveKindEnum.Feature,
                Category = Normalize(request.Category),
                Priority = request.Priority ?? ObjectivePriorityEnum.P2,
                Rank = await ResolveRankAsync(auth, request.Rank, token).ConfigureAwait(false),
                BacklogState = request.BacklogState ?? ObjectiveBacklogStateEnum.Inbox,
                Effort = request.Effort ?? ObjectiveEffortEnum.M,
                Owner = Normalize(request.Owner),
                TargetVersion = Normalize(request.TargetVersion),
                DueUtc = request.DueUtc?.ToUniversalTime(),
                ParentObjectiveId = Normalize(request.ParentObjectiveId),
                BlockedByObjectiveIds = DistinctNormalized(request.BlockedByObjectiveIds),
                RefinementSummary = Normalize(request.RefinementSummary),
                SuggestedPipelineId = Normalize(request.SuggestedPipelineId),
                SuggestedPlaybooks = DistinctPlaybooks(request.SuggestedPlaybooks),
                Tags = DistinctNormalized(request.Tags),
                AcceptanceCriteria = DistinctNormalized(request.AcceptanceCriteria),
                NonGoals = DistinctNormalized(request.NonGoals),
                RolloutConstraints = DistinctNormalized(request.RolloutConstraints),
                EvidenceLinks = DistinctNormalized(request.EvidenceLinks),
                FleetIds = DistinctNormalized(request.FleetIds),
                VesselIds = DistinctNormalized(request.VesselIds),
                PlanningSessionIds = DistinctNormalized(request.PlanningSessionIds),
                RefinementSessionIds = DistinctNormalized(request.RefinementSessionIds),
                VoyageIds = DistinctNormalized(request.VoyageIds),
                MissionIds = DistinctNormalized(request.MissionIds),
                CheckRunIds = DistinctNormalized(request.CheckRunIds),
                ReleaseIds = DistinctNormalized(request.ReleaseIds),
                DeploymentIds = DistinctNormalized(request.DeploymentIds),
                IncidentIds = DistinctNormalized(request.IncidentIds),
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            SanitizeObjective(objective);
            await ValidateLinksAsync(auth, objective, token).ConfigureAwait(false);
            ApplyLifecycleTimestamps(objective);
            await PersistObjectiveAsync(auth, objective, token).ConfigureAwait(false);
            OnObjectiveChanged?.Invoke(objective);
            return objective;
        }

        /// <summary>
        /// Update an objective.
        /// </summary>
        public async Task<Objective> UpdateAsync(AuthContext auth, string id, ObjectiveUpsertRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));
            await EnsureBackfilledAsync(token).ConfigureAwait(false);

            Objective objective = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");

            objective.Title = Normalize(request.Title) ?? objective.Title;
            objective.Description = request.Description != null ? Normalize(request.Description) : objective.Description;
            objective.Status = request.Status ?? objective.Status;
            objective.Kind = request.Kind ?? objective.Kind;
            if (request.Category != null) objective.Category = Normalize(request.Category);
            objective.Priority = request.Priority ?? objective.Priority;
            if (request.Rank.HasValue) objective.Rank = request.Rank.Value;
            objective.BacklogState = request.BacklogState ?? objective.BacklogState;
            objective.Effort = request.Effort ?? objective.Effort;
            objective.Owner = request.Owner != null ? Normalize(request.Owner) : objective.Owner;
            if (request.TargetVersion != null) objective.TargetVersion = Normalize(request.TargetVersion);
            if (request.DueUtc.HasValue) objective.DueUtc = request.DueUtc.Value.ToUniversalTime();
            if (request.ParentObjectiveId != null) objective.ParentObjectiveId = Normalize(request.ParentObjectiveId);
            if (request.BlockedByObjectiveIds != null) objective.BlockedByObjectiveIds = DistinctNormalized(request.BlockedByObjectiveIds);
            if (request.RefinementSummary != null) objective.RefinementSummary = Normalize(request.RefinementSummary);
            if (request.SuggestedPipelineId != null) objective.SuggestedPipelineId = Normalize(request.SuggestedPipelineId);
            if (request.SuggestedPlaybooks != null) objective.SuggestedPlaybooks = DistinctPlaybooks(request.SuggestedPlaybooks);
            if (request.Tags != null) objective.Tags = DistinctNormalized(request.Tags);
            if (request.AcceptanceCriteria != null) objective.AcceptanceCriteria = DistinctNormalized(request.AcceptanceCriteria);
            if (request.NonGoals != null) objective.NonGoals = DistinctNormalized(request.NonGoals);
            if (request.RolloutConstraints != null) objective.RolloutConstraints = DistinctNormalized(request.RolloutConstraints);
            if (request.EvidenceLinks != null) objective.EvidenceLinks = DistinctNormalized(request.EvidenceLinks);
            if (request.FleetIds != null) objective.FleetIds = DistinctNormalized(request.FleetIds);
            if (request.VesselIds != null) objective.VesselIds = DistinctNormalized(request.VesselIds);
            if (request.PlanningSessionIds != null) objective.PlanningSessionIds = DistinctNormalized(request.PlanningSessionIds);
            if (request.RefinementSessionIds != null) objective.RefinementSessionIds = DistinctNormalized(request.RefinementSessionIds);
            if (request.VoyageIds != null) objective.VoyageIds = DistinctNormalized(request.VoyageIds);
            if (request.MissionIds != null) objective.MissionIds = DistinctNormalized(request.MissionIds);
            if (request.CheckRunIds != null) objective.CheckRunIds = DistinctNormalized(request.CheckRunIds);
            if (request.ReleaseIds != null) objective.ReleaseIds = DistinctNormalized(request.ReleaseIds);
            if (request.DeploymentIds != null) objective.DeploymentIds = DistinctNormalized(request.DeploymentIds);
            if (request.IncidentIds != null) objective.IncidentIds = DistinctNormalized(request.IncidentIds);
            objective.LastUpdateUtc = DateTime.UtcNow;

            SanitizeObjective(objective);
            await ValidateLinksAsync(auth, objective, token).ConfigureAwait(false);
            ApplyLifecycleTimestamps(objective);
            await PersistObjectiveAsync(auth, objective, token).ConfigureAwait(false);
            OnObjectiveChanged?.Invoke(objective);
            return objective;
        }

        /// <summary>
        /// Apply a batch of backlog rank updates.
        /// </summary>
        public async Task<List<Objective>> ReorderAsync(
            AuthContext auth,
            ObjectiveReorderRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            await EnsureBackfilledAsync(token).ConfigureAwait(false);

            if (request.Items == null || request.Items.Count < 1)
                throw new InvalidOperationException("At least one reorder item is required.");

            HashSet<string> objectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> ranks = new HashSet<int>();
            List<Objective> updated = new List<Objective>();
            DateTime now = DateTime.UtcNow;

            foreach (ObjectiveReorderItem item in request.Items)
            {
                if (item == null)
                    throw new InvalidOperationException("Reorder items must not be null.");

                string objectiveId = NormalizeRequired(item.ObjectiveId, nameof(item.ObjectiveId));
                if (!objectiveIds.Add(objectiveId))
                    throw new InvalidOperationException("Duplicate objective ID in reorder request: " + objectiveId);
                if (!ranks.Add(item.Rank))
                    throw new InvalidOperationException("Duplicate rank in reorder request: " + item.Rank);

                Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Objective not found.");

                objective.Rank = item.Rank;
                objective.LastUpdateUtc = now;

                SanitizeObjective(objective);
                await ValidateLinksAsync(auth, objective, token).ConfigureAwait(false);
                ApplyLifecycleTimestamps(objective);
                await PersistObjectiveAsync(auth, objective, token).ConfigureAwait(false);
                OnObjectiveChanged?.Invoke(objective);
                updated.Add(objective);
            }

            return updated
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Persist an imported or externally refreshed objective snapshot.
        /// </summary>
        public async Task<Objective> PersistImportedAsync(AuthContext auth, Objective objective, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (objective == null) throw new ArgumentNullException(nameof(objective));
            await EnsureBackfilledAsync(token).ConfigureAwait(false);

            objective.LastUpdateUtc = DateTime.UtcNow;
            SanitizeObjective(objective);
            await ValidateLinksAsync(auth, objective, token).ConfigureAwait(false);
            ApplyLifecycleTimestamps(objective);
            await PersistObjectiveAsync(auth, objective, token).ConfigureAwait(false);
            OnObjectiveChanged?.Invoke(objective);
            return objective;
        }

        /// <summary>
        /// Delete an objective and all of its snapshots.
        /// </summary>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await EnsureBackfilledAsync(token).ConfigureAwait(false);

            Objective? existing = await ReadObjectiveRowAsync(auth, id, token).ConfigureAwait(false);
            List<ArmadaEvent> snapshots = await ReadObjectiveSnapshotEventsAsync(auth, id, token).ConfigureAwait(false);
            if (existing == null && snapshots.Count == 0)
                throw new KeyNotFoundException("Objective not found.");

            if (existing != null)
            {
                await DeleteObjectiveRowAsync(auth, id, token).ConfigureAwait(false);
            }

            foreach (ArmadaEvent snapshot in snapshots)
            {
                await DeleteEventAsync(auth, snapshot.Id, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Enumerate objectives linked to a planning session.
        /// </summary>
        public async Task<List<Objective>> EnumerateByPlanningSessionAsync(
            AuthContext auth,
            string planningSessionId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(planningSessionId)) throw new ArgumentNullException(nameof(planningSessionId));

            EnumerationResult<Objective> results = await EnumerateAsync(auth, new ObjectiveQuery
            {
                PageNumber = 1,
                PageSize = 500,
                PlanningSessionId = planningSessionId
            }, token).ConfigureAwait(false);

            return results.Objects;
        }

        /// <summary>
        /// Link a refinement session to an objective and promote the objective into refinement.
        /// </summary>
        public async Task<Objective> LinkRefinementSessionAsync(
            AuthContext auth,
            string objectiveId,
            string refinementSessionId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(refinementSessionId)) throw new ArgumentNullException(nameof(refinementSessionId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");
            ObjectiveRefinementSession? session = await ReadObjectiveRefinementSessionEntityAsync(auth, refinementSessionId, token).ConfigureAwait(false);
            if (session == null)
                throw new KeyNotFoundException("Objective refinement session not found or not accessible: " + refinementSessionId);

            AddIfMissing(objective.RefinementSessionIds, session.Id);
            AddIfMissing(objective.VesselIds, session.VesselId);
            AddIfMissing(objective.FleetIds, session.FleetId);
            PromoteStatus(objective, ObjectiveStatusEnum.Scoped);
            objective.BacklogState = ObjectiveBacklogStateEnum.Refining;
            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a refinement session link from an objective.
        /// </summary>
        public async Task<Objective> UnlinkRefinementSessionAsync(
            AuthContext auth,
            string objectiveId,
            string refinementSessionId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(refinementSessionId)) throw new ArgumentNullException(nameof(refinementSessionId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");

            objective.RefinementSessionIds = objective.RefinementSessionIds
                .Where(id => !String.Equals(id, refinementSessionId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (objective.BacklogState == ObjectiveBacklogStateEnum.Refining && objective.RefinementSessionIds.Count == 0)
            {
                objective.BacklogState = objective.VesselIds.Count > 0
                    ? ObjectiveBacklogStateEnum.ReadyForPlanning
                    : ObjectiveBacklogStateEnum.Triaged;
            }

            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Link a planning session to an objective and promote the objective into planning.
        /// </summary>
        public async Task<Objective> LinkPlanningSessionAsync(
            AuthContext auth,
            string objectiveId,
            string planningSessionId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(planningSessionId)) throw new ArgumentNullException(nameof(planningSessionId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");
            PlanningSession? session = await ReadPlanningSessionEntityAsync(auth, planningSessionId, token).ConfigureAwait(false);
            if (session == null)
                throw new KeyNotFoundException("Planning session not found or not accessible: " + planningSessionId);

            AddIfMissing(objective.PlanningSessionIds, session.Id);
            AddIfMissing(objective.VesselIds, session.VesselId);
            AddIfMissing(objective.FleetIds, session.FleetId);
            PromoteStatus(objective, ObjectiveStatusEnum.Planned);
            objective.BacklogState = ObjectiveBacklogStateEnum.ReadyForDispatch;
            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Link a voyage and its current mission scope to an objective.
        /// </summary>
        public async Task<Objective> LinkVoyageAsync(
            AuthContext auth,
            string objectiveId,
            string voyageId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(voyageId)) throw new ArgumentNullException(nameof(voyageId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");
            Voyage? voyage = await ReadVoyageEntityAsync(auth, voyageId, token).ConfigureAwait(false);
            if (voyage == null)
                throw new KeyNotFoundException("Voyage not found or not accessible: " + voyageId);

            AddIfMissing(objective.VoyageIds, voyage.Id);

            List<Mission> missions = await ReadAccessibleMissionsForVoyageAsync(auth, voyage.Id, token).ConfigureAwait(false);
            foreach (Mission mission in missions)
            {
                AddIfMissing(objective.MissionIds, mission.Id);
                AddIfMissing(objective.VesselIds, mission.VesselId);
                await AppendFleetForVesselAsync(auth, objective, mission.VesselId, token).ConfigureAwait(false);
            }

            PromoteStatus(objective, ObjectiveStatusEnum.InProgress);
            objective.BacklogState = ObjectiveBacklogStateEnum.Dispatched;
            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Link a release and its derived work scope to an objective.
        /// </summary>
        public async Task<Objective> LinkReleaseAsync(
            AuthContext auth,
            string objectiveId,
            string releaseId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(releaseId)) throw new ArgumentNullException(nameof(releaseId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");
            Release? release = await ReadReleaseEntityAsync(auth, releaseId, token).ConfigureAwait(false);
            if (release == null)
                throw new KeyNotFoundException("Release not found or not accessible: " + releaseId);

            AddIfMissing(objective.ReleaseIds, release.Id);
            AddIfMissing(objective.VesselIds, release.VesselId);
            await AppendFleetForVesselAsync(auth, objective, release.VesselId, token).ConfigureAwait(false);

            foreach (string voyageId in release.VoyageIds)
                AddIfMissing(objective.VoyageIds, voyageId);
            foreach (string missionId in release.MissionIds)
                AddIfMissing(objective.MissionIds, missionId);
            foreach (string checkRunId in release.CheckRunIds)
                AddIfMissing(objective.CheckRunIds, checkRunId);

            PromoteStatus(objective, ObjectiveStatusEnum.Released);
            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Link a deployment and its derived delivery scope to an objective.
        /// </summary>
        public async Task<Objective> LinkDeploymentAsync(
            AuthContext auth,
            string objectiveId,
            string deploymentId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(deploymentId)) throw new ArgumentNullException(nameof(deploymentId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");
            Deployment? deployment = await ReadDeploymentEntityAsync(auth, deploymentId, token).ConfigureAwait(false);
            if (deployment == null)
                throw new KeyNotFoundException("Deployment not found or not accessible: " + deploymentId);

            AddIfMissing(objective.DeploymentIds, deployment.Id);
            AddIfMissing(objective.VesselIds, deployment.VesselId);
            await AppendFleetForVesselAsync(auth, objective, deployment.VesselId, token).ConfigureAwait(false);
            AddIfMissing(objective.ReleaseIds, deployment.ReleaseId);
            AddIfMissing(objective.MissionIds, deployment.MissionId);
            AddIfMissing(objective.VoyageIds, deployment.VoyageId);
            foreach (string checkRunId in deployment.CheckRunIds)
                AddIfMissing(objective.CheckRunIds, checkRunId);

            PromoteStatus(objective, ObjectiveStatusEnum.Deployed);
            objective.BacklogState = ObjectiveBacklogStateEnum.Dispatched;
            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Link an incident and its related delivery context to an objective.
        /// </summary>
        public async Task<Objective> LinkIncidentAsync(
            AuthContext auth,
            string objectiveId,
            string incidentId,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            if (String.IsNullOrWhiteSpace(incidentId)) throw new ArgumentNullException(nameof(incidentId));

            Objective objective = await ReadAsync(auth, objectiveId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Objective not found.");
            Incident? incident = await ReadIncidentEntityAsync(auth, incidentId, token).ConfigureAwait(false);
            if (incident == null)
                throw new KeyNotFoundException("Incident not found or not accessible: " + incidentId);

            AddIfMissing(objective.IncidentIds, incident.Id);
            AddIfMissing(objective.DeploymentIds, incident.DeploymentId);
            AddIfMissing(objective.DeploymentIds, incident.RollbackDeploymentId);
            AddIfMissing(objective.ReleaseIds, incident.ReleaseId);
            AddIfMissing(objective.MissionIds, incident.MissionId);
            AddIfMissing(objective.VoyageIds, incident.VoyageId);
            AddIfMissing(objective.VesselIds, incident.VesselId);
            await AppendFleetForVesselAsync(auth, objective, incident.VesselId, token).ConfigureAwait(false);
            return await PersistLinkedObjectiveAsync(auth, objective, token).ConfigureAwait(false);
        }

        private async Task<List<Objective>> ReadAllObjectivesAsync(AuthContext auth, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Objectives.EnumerateAsync(token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Objectives.EnumerateAsync(auth.TenantId!, token).ConfigureAwait(false);
            return await _Database.Objectives.EnumerateAsync(auth.TenantId!, auth.UserId!, token).ConfigureAwait(false);
        }

        private async Task<Objective?> ReadObjectiveRowAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Objectives.ReadAsync(id, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Objectives.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return await _Database.Objectives.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
        }

        private async Task DeleteObjectiveRowAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
            {
                await _Database.Objectives.DeleteAsync(id, token).ConfigureAwait(false);
                return;
            }

            await _Database.Objectives.DeleteAsync(auth.TenantId!, id, token).ConfigureAwait(false);
        }

        private async Task EnsureBackfilledAsync(CancellationToken token)
        {
            if (_BackfillCompleted)
                return;

            await _BackfillLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_BackfillCompleted)
                    return;

                await BackfillFromSnapshotsAsync(token).ConfigureAwait(false);
                _BackfillCompleted = true;
            }
            finally
            {
                _BackfillLock.Release();
            }
        }

        private async Task BackfillFromSnapshotsAsync(CancellationToken token)
        {
            Dictionary<string, ArmadaEvent> latestByObjectiveId = new Dictionary<string, ArmadaEvent>(StringComparer.OrdinalIgnoreCase);
            int pageNumber = 1;

            while (true)
            {
                EnumerationResult<ArmadaEvent> page = await _Database.Events.EnumerateAsync(new EnumerationQuery
                {
                    PageNumber = pageNumber,
                    PageSize = 1000,
                    EventType = "objective.snapshot"
                }, token).ConfigureAwait(false);

                foreach (ArmadaEvent snapshot in page.Objects.Where(evt =>
                    String.Equals(evt.EntityType, "objective", StringComparison.OrdinalIgnoreCase)
                    && String.Equals(evt.EventType, "objective.snapshot", StringComparison.OrdinalIgnoreCase)))
                {
                    if (String.IsNullOrWhiteSpace(snapshot.EntityId))
                        continue;

                    if (!latestByObjectiveId.TryGetValue(snapshot.EntityId, out ArmadaEvent? existing)
                        || IsSnapshotNewer(snapshot, existing))
                    {
                        latestByObjectiveId[snapshot.EntityId] = snapshot;
                    }
                }

                if (page.PageNumber >= page.TotalPages || page.Objects.Count == 0)
                    break;

                pageNumber++;
            }

            foreach (ArmadaEvent snapshot in latestByObjectiveId.Values)
            {
                Objective? objective = DeserializeObjective(snapshot);
                if (objective == null)
                    continue;

                SanitizeObjective(objective);
                await UpsertObjectiveRowAsync(objective, token, skipIfExistingNewer: true).ConfigureAwait(false);
            }
        }

        private async Task<Objective?> TryRehydrateObjectiveFromSnapshotsAsync(AuthContext auth, string id, CancellationToken token)
        {
            List<ArmadaEvent> snapshots = await ReadObjectiveSnapshotEventsAsync(auth, id, token).ConfigureAwait(false);
            Objective? objective = ProjectLatestObjective(snapshots);
            if (objective == null)
                return null;

            SanitizeObjective(objective);
            await UpsertObjectiveRowAsync(objective, token, skipIfExistingNewer: true).ConfigureAwait(false);
            return objective;
        }

        private async Task PersistObjectiveAsync(AuthContext auth, Objective objective, CancellationToken token)
        {
            await UpsertObjectiveRowAsync(objective, token, skipIfExistingNewer: false).ConfigureAwait(false);
            await WriteSnapshotAsync(auth, objective, token).ConfigureAwait(false);
        }

        private async Task UpsertObjectiveRowAsync(Objective objective, CancellationToken token, bool skipIfExistingNewer)
        {
            Objective? existing = await _Database.Objectives.ReadAsync(objective.Id, token).ConfigureAwait(false);
            if (existing == null)
            {
                await _Database.Objectives.CreateAsync(objective, token).ConfigureAwait(false);
                return;
            }

            if (skipIfExistingNewer && existing.LastUpdateUtc > objective.LastUpdateUtc)
                return;

            await _Database.Objectives.UpdateAsync(objective, token).ConfigureAwait(false);
        }

        private async Task<List<ArmadaEvent>> ReadObjectiveSnapshotEventsAsync(
            AuthContext auth,
            string? objectiveId,
            CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(objectiveId))
            {
                if (auth.IsAdmin)
                    return await _Database.Events.EnumerateByEntityAsync("objective", objectiveId, 500, token).ConfigureAwait(false);
                if (auth.IsTenantAdmin)
                    return await _Database.Events.EnumerateByEntityAsync(auth.TenantId!, "objective", objectiveId, 500, token).ConfigureAwait(false);
                return (await _Database.Events.EnumerateAsync(auth.TenantId!, auth.UserId!, new EnumerationQuery
                {
                    PageNumber = 1,
                    PageSize = 500
                }, token).ConfigureAwait(false)).Objects
                    .Where(item =>
                        String.Equals(item.EntityType, "objective", StringComparison.OrdinalIgnoreCase)
                        && String.Equals(item.EntityId, objectiveId, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(item.EventType, "objective.snapshot", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            List<ArmadaEvent> events;
            if (auth.IsAdmin)
                events = await _Database.Events.EnumerateByTypeAsync("objective.snapshot", 5000, token).ConfigureAwait(false);
            else if (auth.IsTenantAdmin)
                events = await _Database.Events.EnumerateByTypeAsync(auth.TenantId!, "objective.snapshot", 5000, token).ConfigureAwait(false);
            else
                events = (await _Database.Events.EnumerateAsync(auth.TenantId!, auth.UserId!, new EnumerationQuery
                {
                    PageNumber = 1,
                    PageSize = 5000
                }, token).ConfigureAwait(false)).Objects
                    .Where(item => String.Equals(item.EventType, "objective.snapshot", StringComparison.OrdinalIgnoreCase))
                    .ToList();

            return events
                .Where(item => String.Equals(item.EntityType, "objective", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private async Task ValidateLinksAsync(AuthContext auth, Objective objective, CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(objective.ParentObjectiveId)
                && !await ReadObjectiveExistsAsync(auth, objective.ParentObjectiveId, token).ConfigureAwait(false))
            {
                throw new KeyNotFoundException("Parent objective not found or not accessible: " + objective.ParentObjectiveId);
            }

            await ValidateIdsAsync(objective.BlockedByObjectiveIds, async id => await ReadObjectiveExistsAsync(auth, id, token).ConfigureAwait(false), "Blocking objective").ConfigureAwait(false);
            await ValidateIdsAsync(objective.FleetIds, async id => await ReadFleetAsync(auth, id, token).ConfigureAwait(false), "Fleet").ConfigureAwait(false);
            await ValidateIdsAsync(objective.VesselIds, async id => await ReadVesselAsync(auth, id, token).ConfigureAwait(false), "Vessel").ConfigureAwait(false);
            await ValidateIdsAsync(objective.PlanningSessionIds, async id => await ReadPlanningSessionAsync(auth, id, token).ConfigureAwait(false), "Planning session").ConfigureAwait(false);
            await ValidateIdsAsync(objective.RefinementSessionIds, async id => await ReadObjectiveRefinementSessionAsync(auth, id, token).ConfigureAwait(false), "Refinement session").ConfigureAwait(false);
            await ValidateIdsAsync(objective.VoyageIds, async id => await ReadVoyageAsync(auth, id, token).ConfigureAwait(false), "Voyage").ConfigureAwait(false);
            await ValidateIdsAsync(objective.MissionIds, async id => await ReadMissionAsync(auth, id, token).ConfigureAwait(false), "Mission").ConfigureAwait(false);
            await ValidateIdsAsync(objective.CheckRunIds, async id => await ReadCheckRunAsync(auth, id, token).ConfigureAwait(false), "Check run").ConfigureAwait(false);
            await ValidateIdsAsync(objective.ReleaseIds, async id => await ReadReleaseAsync(auth, id, token).ConfigureAwait(false), "Release").ConfigureAwait(false);
            await ValidateIdsAsync(objective.DeploymentIds, async id => await ReadDeploymentAsync(auth, id, token).ConfigureAwait(false), "Deployment").ConfigureAwait(false);
            await ValidateIdsAsync(objective.IncidentIds, async id => await ReadIncidentAsync(auth, id, token).ConfigureAwait(false), "Incident").ConfigureAwait(false);

            if (!String.IsNullOrWhiteSpace(objective.SuggestedPipelineId)
                && !await ReadPipelineAsync(objective.SuggestedPipelineId, token).ConfigureAwait(false))
            {
                throw new KeyNotFoundException("Pipeline not found or not accessible: " + objective.SuggestedPipelineId);
            }
        }

        private async Task ValidateIdsAsync(
            IEnumerable<string> ids,
            Func<string, Task<bool>> exists,
            string label)
        {
            foreach (string id in ids)
            {
                bool present = await exists(id).ConfigureAwait(false);
                if (!present)
                    throw new KeyNotFoundException(label + " not found or not accessible: " + id);
            }
        }

        private async Task<bool> ReadFleetAsync(AuthContext auth, string id, CancellationToken token)
        {
            Fleet? fleet = auth.IsAdmin
                ? await _Database.Fleets.ReadAsync(id, token).ConfigureAwait(false)
                : await _Database.Fleets.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return fleet != null;
        }

        private async Task<bool> ReadVesselAsync(AuthContext auth, string id, CancellationToken token)
        {
            Vessel? vessel = auth.IsAdmin
                ? await _Database.Vessels.ReadAsync(id, token).ConfigureAwait(false)
                : await _Database.Vessels.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return vessel != null;
        }

        private async Task<bool> ReadPlanningSessionAsync(AuthContext auth, string id, CancellationToken token)
        {
            PlanningSession? session = auth.IsAdmin
                ? await _Database.PlanningSessions.ReadAsync(id, token).ConfigureAwait(false)
                : auth.IsTenantAdmin
                    ? await _Database.PlanningSessions.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false)
                    : await _Database.PlanningSessions.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
            return session != null;
        }

        private async Task<PlanningSession?> ReadPlanningSessionEntityAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.PlanningSessions.ReadAsync(id, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.PlanningSessions.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return await _Database.PlanningSessions.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
        }

        private async Task<bool> ReadVoyageAsync(AuthContext auth, string id, CancellationToken token)
        {
            Voyage? voyage = auth.IsAdmin
                ? await _Database.Voyages.ReadAsync(id, token).ConfigureAwait(false)
                : await _Database.Voyages.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return voyage != null;
        }

        private async Task<Voyage?> ReadVoyageEntityAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Voyages.ReadAsync(id, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Voyages.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return await _Database.Voyages.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
        }

        private async Task<bool> ReadMissionAsync(AuthContext auth, string id, CancellationToken token)
        {
            Mission? mission = auth.IsAdmin
                ? await _Database.Missions.ReadAsync(id, token).ConfigureAwait(false)
                : auth.IsTenantAdmin
                    ? await _Database.Missions.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false)
                    : await _Database.Missions.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
            return mission != null;
        }

        private async Task<bool> ReadCheckRunAsync(AuthContext auth, string id, CancellationToken token)
        {
            CheckRunQuery query = BuildCheckRunScopeQuery(auth);
            CheckRun? run = await _Database.CheckRuns.ReadAsync(id, query, token).ConfigureAwait(false);
            return run != null;
        }

        private async Task<bool> ReadReleaseAsync(AuthContext auth, string id, CancellationToken token)
        {
            ReleaseQuery query = BuildReleaseScopeQuery(auth);
            Release? release = await _Database.Releases.ReadAsync(id, query, token).ConfigureAwait(false);
            return release != null;
        }

        private async Task<Release?> ReadReleaseEntityAsync(AuthContext auth, string id, CancellationToken token)
        {
            ReleaseQuery query = BuildReleaseScopeQuery(auth);
            return await _Database.Releases.ReadAsync(id, query, token).ConfigureAwait(false);
        }

        private async Task<bool> ReadDeploymentAsync(AuthContext auth, string id, CancellationToken token)
        {
            DeploymentQuery query = BuildDeploymentScopeQuery(auth);
            Deployment? deployment = await _Database.Deployments.ReadAsync(id, query, token).ConfigureAwait(false);
            return deployment != null;
        }

        private async Task<Deployment?> ReadDeploymentEntityAsync(AuthContext auth, string id, CancellationToken token)
        {
            DeploymentQuery query = BuildDeploymentScopeQuery(auth);
            return await _Database.Deployments.ReadAsync(id, query, token).ConfigureAwait(false);
        }

        private async Task<bool> ReadIncidentAsync(AuthContext auth, string id, CancellationToken token)
        {
            IncidentService incidents = new IncidentService(_Database);
            Incident? incident = await incidents.ReadAsync(auth, id, token).ConfigureAwait(false);
            return incident != null;
        }

        private async Task<Incident?> ReadIncidentEntityAsync(AuthContext auth, string id, CancellationToken token)
        {
            IncidentService incidents = new IncidentService(_Database);
            return await incidents.ReadAsync(auth, id, token).ConfigureAwait(false);
        }

        private async Task<bool> ReadObjectiveExistsAsync(AuthContext auth, string id, CancellationToken token)
        {
            Objective? objective = await ReadObjectiveRowAsync(auth, id, token).ConfigureAwait(false);
            return objective != null;
        }

        private async Task<bool> ReadObjectiveRefinementSessionAsync(AuthContext auth, string id, CancellationToken token)
        {
            ObjectiveRefinementSession? session = auth.IsAdmin
                ? await _Database.ObjectiveRefinementSessions.ReadAsync(id, token).ConfigureAwait(false)
                : auth.IsTenantAdmin
                    ? await _Database.ObjectiveRefinementSessions.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false)
                    : await _Database.ObjectiveRefinementSessions.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
            return session != null;
        }

        private async Task<ObjectiveRefinementSession?> ReadObjectiveRefinementSessionEntityAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.ObjectiveRefinementSessions.ReadAsync(id, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.ObjectiveRefinementSessions.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            return await _Database.ObjectiveRefinementSessions.ReadAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
        }

        private async Task<bool> ReadPipelineAsync(string id, CancellationToken token)
        {
            Pipeline? pipeline = await _Database.Pipelines.ReadAsync(id, token).ConfigureAwait(false);
            return pipeline != null;
        }

        private async Task WriteSnapshotAsync(AuthContext auth, Objective objective, CancellationToken token)
        {
            ArmadaEvent snapshot = new ArmadaEvent
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.UserId,
                EventType = "objective.snapshot",
                EntityType = "objective",
                EntityId = objective.Id,
                VesselId = objective.VesselIds.Count > 0 ? objective.VesselIds[0] : null,
                VoyageId = objective.VoyageIds.Count > 0 ? objective.VoyageIds[0] : null,
                Message = objective.Title,
                Payload = JsonSerializer.Serialize(objective, _JsonOptions),
                CreatedUtc = objective.LastUpdateUtc
            };
            await _Database.Events.CreateAsync(snapshot, token).ConfigureAwait(false);
        }

        private async Task DeleteEventAsync(AuthContext auth, string eventId, CancellationToken token)
        {
            if (auth.IsAdmin)
            {
                await _Database.Events.DeleteAsync(eventId, token).ConfigureAwait(false);
                return;
            }

            await _Database.Events.DeleteAsync(auth.TenantId!, eventId, token).ConfigureAwait(false);
        }

        private async Task<Objective> PersistLinkedObjectiveAsync(AuthContext auth, Objective objective, CancellationToken token)
        {
            objective.LastUpdateUtc = DateTime.UtcNow;
            SanitizeObjective(objective);
            await ValidateLinksAsync(auth, objective, token).ConfigureAwait(false);
            ApplyLifecycleTimestamps(objective);
            await PersistObjectiveAsync(auth, objective, token).ConfigureAwait(false);
            OnObjectiveChanged?.Invoke(objective);
            return objective;
        }

        private async Task AppendFleetForVesselAsync(AuthContext auth, Objective objective, string? vesselId, CancellationToken token)
        {
            string? normalizedVesselId = Normalize(vesselId);
            if (String.IsNullOrWhiteSpace(normalizedVesselId))
                return;

            Vessel? vessel = auth.IsAdmin
                ? await _Database.Vessels.ReadAsync(normalizedVesselId, token).ConfigureAwait(false)
                : auth.IsTenantAdmin
                    ? await _Database.Vessels.ReadAsync(auth.TenantId!, normalizedVesselId, token).ConfigureAwait(false)
                    : await _Database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, normalizedVesselId, token).ConfigureAwait(false);
            if (vessel == null)
                return;

            AddIfMissing(objective.FleetIds, vessel.FleetId);
        }

        private async Task<List<Mission>> ReadAccessibleMissionsForVoyageAsync(AuthContext auth, string voyageId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Missions.EnumerateByVoyageAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);

            List<Mission> tenantMissions = await _Database.Missions.EnumerateByVoyageAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);
            return tenantMissions
                .Where(mission => String.Equals(mission.UserId, auth.UserId, StringComparison.Ordinal))
                .ToList();
        }

        private static Objective? DeserializeObjective(ArmadaEvent snapshot)
        {
            if (String.IsNullOrWhiteSpace(snapshot.Payload))
                return null;

            try
            {
                return JsonSerializer.Deserialize<Objective>(snapshot.Payload, _JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static Objective? ProjectLatestObjective(List<ArmadaEvent> snapshots)
        {
            ArmadaEvent? latest = snapshots
                .Where(item => String.Equals(item.EventType, "objective.snapshot", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CreatedUtc)
                .FirstOrDefault();
            if (latest == null)
                return null;

            return DeserializeObjective(latest);
        }

        private static void ApplyLifecycleTimestamps(Objective objective)
        {
            if (objective.Status == ObjectiveStatusEnum.Completed)
            {
                objective.CompletedUtc ??= DateTime.UtcNow;
            }
            else if (objective.Status == ObjectiveStatusEnum.Cancelled)
            {
                objective.CompletedUtc = objective.CompletedUtc ?? DateTime.UtcNow;
            }
        }

        private static void PromoteStatus(Objective objective, ObjectiveStatusEnum targetStatus)
        {
            if (objective.Status == ObjectiveStatusEnum.Blocked
                || objective.Status == ObjectiveStatusEnum.Cancelled
                || objective.Status == ObjectiveStatusEnum.Completed)
                return;

            if ((int)objective.Status < (int)targetStatus)
                objective.Status = targetStatus;
        }

        private static void AddIfMissing(List<string> values, string? value)
        {
            string? normalized = Normalize(value);
            if (String.IsNullOrWhiteSpace(normalized))
                return;

            if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                values.Add(normalized);
        }

        private static CheckRunQuery BuildCheckRunScopeQuery(AuthContext auth)
        {
            CheckRunQuery query = new CheckRunQuery();
            if (!auth.IsAdmin)
            {
                query.TenantId = auth.TenantId;
                if (!auth.IsTenantAdmin)
                    query.UserId = auth.UserId;
            }

            return query;
        }

        private static ReleaseQuery BuildReleaseScopeQuery(AuthContext auth)
        {
            ReleaseQuery query = new ReleaseQuery();
            if (!auth.IsAdmin)
            {
                query.TenantId = auth.TenantId;
                if (!auth.IsTenantAdmin)
                    query.UserId = auth.UserId;
            }

            return query;
        }

        private static DeploymentQuery BuildDeploymentScopeQuery(AuthContext auth)
        {
            DeploymentQuery query = new DeploymentQuery();
            if (!auth.IsAdmin)
            {
                query.TenantId = auth.TenantId;
                if (!auth.IsTenantAdmin)
                    query.UserId = auth.UserId;
            }

            return query;
        }

        private async Task<int> ResolveRankAsync(AuthContext auth, int? requestedRank, CancellationToken token)
        {
            if (requestedRank.HasValue)
                return requestedRank.Value;

            List<Objective> existing = await ReadAllObjectivesAsync(auth, token).ConfigureAwait(false);
            return existing.Count == 0 ? 0 : existing.Max(item => item.Rank) + 1;
        }

        private static void SanitizeObjective(Objective objective)
        {
            objective.Description = Normalize(objective.Description);
            objective.Category = Normalize(objective.Category);
            objective.Owner = Normalize(objective.Owner);
            objective.TargetVersion = Normalize(objective.TargetVersion);
            objective.ParentObjectiveId = Normalize(objective.ParentObjectiveId);
            objective.RefinementSummary = Normalize(objective.RefinementSummary);
            objective.SuggestedPipelineId = Normalize(objective.SuggestedPipelineId);
            objective.SourceProvider = Normalize(objective.SourceProvider);
            objective.SourceType = Normalize(objective.SourceType);
            objective.SourceId = Normalize(objective.SourceId);
            objective.SourceUrl = Normalize(objective.SourceUrl);
            objective.Tags = DistinctNormalized(objective.Tags);
            objective.AcceptanceCriteria = DistinctNormalized(objective.AcceptanceCriteria);
            objective.NonGoals = DistinctNormalized(objective.NonGoals);
            objective.RolloutConstraints = DistinctNormalized(objective.RolloutConstraints);
            objective.EvidenceLinks = DistinctNormalized(objective.EvidenceLinks);
            objective.FleetIds = DistinctNormalized(objective.FleetIds);
            objective.VesselIds = DistinctNormalized(objective.VesselIds);
            objective.PlanningSessionIds = DistinctNormalized(objective.PlanningSessionIds);
            objective.RefinementSessionIds = DistinctNormalized(objective.RefinementSessionIds);
            objective.VoyageIds = DistinctNormalized(objective.VoyageIds);
            objective.MissionIds = DistinctNormalized(objective.MissionIds);
            objective.CheckRunIds = DistinctNormalized(objective.CheckRunIds);
            objective.ReleaseIds = DistinctNormalized(objective.ReleaseIds);
            objective.DeploymentIds = DistinctNormalized(objective.DeploymentIds);
            objective.IncidentIds = DistinctNormalized(objective.IncidentIds);
            objective.BlockedByObjectiveIds = DistinctNormalized(objective.BlockedByObjectiveIds);
            objective.SuggestedPlaybooks = DistinctPlaybooks(objective.SuggestedPlaybooks);
        }

        private static List<string> DistinctNormalized(IEnumerable<string>? values)
        {
            if (values == null) return new List<string>();
            return values
                .Select(Normalize)
                .Where(value => !String.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
        }

        private static List<SelectedPlaybook> DistinctPlaybooks(IEnumerable<SelectedPlaybook>? values)
        {
            if (values == null) return new List<SelectedPlaybook>();

            return values
                .Where(item => item != null && !String.IsNullOrWhiteSpace(item.PlaybookId))
                .Select(item => new SelectedPlaybook
                {
                    PlaybookId = Normalize(item.PlaybookId) ?? String.Empty,
                    DeliveryMode = item.DeliveryMode
                })
                .Where(item => !String.IsNullOrWhiteSpace(item.PlaybookId))
                .GroupBy(item => item.PlaybookId + "|" + item.DeliveryMode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static string NormalizeRequired(string? value, string paramName)
        {
            string? normalized = Normalize(value);
            if (String.IsNullOrWhiteSpace(normalized))
                throw new ArgumentNullException(paramName);
            return normalized;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool ContainsIgnoreCase(string? text, string? fragment)
        {
            if (String.IsNullOrWhiteSpace(text) || String.IsNullOrWhiteSpace(fragment))
                return false;
            return text.Contains(fragment, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSnapshotNewer(ArmadaEvent candidate, ArmadaEvent existing)
        {
            if (candidate.CreatedUtc > existing.CreatedUtc)
                return true;
            if (candidate.CreatedUtc < existing.CreatedUtc)
                return false;

            string candidateId = candidate.Id ?? String.Empty;
            string existingId = existing.Id ?? String.Empty;
            return StringComparer.Ordinal.Compare(candidateId, existingId) > 0;
        }
    }
}
