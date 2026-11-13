namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Fleet action application service shared by the REST routes and MCP tools: action CRUD, starting runs, run and
    /// target reads, and cancellation. Every operation is scoped to the caller's tenant (from the auth context, never
    /// the request body); another tenant's identifier behaves as not found.
    /// <para>
    /// Errors are reported with specific exception types that the API layers map to status codes:
    /// <see cref="ArgumentException"/> (including <see cref="FleetActionTemplateException"/>) is 400,
    /// <see cref="UnauthorizedAccessException"/> is 403, <see cref="KeyNotFoundException"/> is 404, and
    /// <see cref="InvalidOperationException"/> is 409.
    /// </para>
    /// Thread-safe.
    /// </summary>
    public class FleetActionService
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of target vessels in one run. Default 500, minimum 1, maximum
        /// <see cref="CollectionLimits.MaxRequestItems"/> (the request id limit, docs/ENUMERATION.md).
        /// </summary>
        public int MaxTargetsPerRun
        {
            get => _MaxTargetsPerRun;
            set => _MaxTargetsPerRun = value < 1 ? 1 : (value > CollectionLimits.MaxRequestItems ? CollectionLimits.MaxRequestItems : value);
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[FleetActionService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly FleetActionRunner _Runner;
        private readonly FleetActionSeedService _Seeder;
        private readonly LoggingModule _Logging;
        private int _MaxTargetsPerRun = 500;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="runner">Runner that executes runs.</param>
        /// <param name="seeder">Built-in seeder.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public FleetActionService(DatabaseDriver database, ArmadaSettings settings, FleetActionRunner runner, FleetActionSeedService seeder, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _Seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate the tenant's actions (built-ins are seeded for the tenant first if this process has not yet).
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="query">Query; null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of actions.</returns>
        public async Task<EnumerationResult<FleetAction>> EnumerateActionsAsync(AuthContext auth, FleetActionEnumerateRequest? query, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            FleetActionEnumerateRequest q = query ?? new FleetActionEnumerateRequest();
            await _Seeder.EnsureSeededAsync(tenantId, token).ConfigureAwait(false);
            return await _Database.FleetActions.EnumerateAsync(tenantId, q, q.IncludeInactive, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one action in the caller's tenant.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="actionId">Action identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The action.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when not found.</exception>
        public async Task<FleetAction> ReadActionAsync(AuthContext auth, string actionId, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            if (String.IsNullOrWhiteSpace(actionId)) throw new ArgumentException("An action id is required.", nameof(actionId));
            FleetAction? action = await _Database.FleetActions.ReadAsync(tenantId, actionId, token).ConfigureAwait(false);
            if (action == null) throw new KeyNotFoundException("Fleet action not found: " + actionId);
            return action;
        }

        /// <summary>
        /// Create an action. Command kind requires tenant admin.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="request">Definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created action.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid input.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when a non-admin creates a Command action or one with an applicability command.</exception>
        public async Task<FleetAction> CreateActionAsync(AuthContext auth, FleetActionUpsertRequest request, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            if (request == null) throw new ArgumentException("A request body is required.", nameof(request));

            FleetAction action = BuildDefinition(request, null);
            RequireExecutionPermission(auth, action);
            action.TenantId = tenantId;
            action.UserId = auth.UserId;
            action.IsBuiltIn = false;
            action.BuiltInKey = null;
            action.Active = true;
            action.CreatedUtc = DateTime.UtcNow;
            action.LastUpdateUtc = DateTime.UtcNow;

            FleetAction created = await _Database.FleetActions.CreateAsync(action, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created " + created.Kind + " action " + created.Id + " in tenant " + tenantId);
            return created;
        }

        /// <summary>
        /// Update an action with the supplied fields. Changing to or from Command kind requires tenant admin.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="actionId">Action identifier.</param>
        /// <param name="request">Fields to change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated action.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid input.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when not found.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when a non-admin edits a Command action or one with an applicability command.</exception>
        public async Task<FleetAction> UpdateActionAsync(AuthContext auth, string actionId, FleetActionUpsertRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentException("A request body is required.", nameof(request));
            FleetAction existing = await ReadActionAsync(auth, actionId, token).ConfigureAwait(false);
            RequireExecutionPermission(auth, existing);

            FleetAction updated = BuildDefinition(request, existing);
            RequireExecutionPermission(auth, updated);
            updated.LastUpdateUtc = DateTime.UtcNow;
            return await _Database.FleetActions.UpdateAsync(updated, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete an action. Built-ins are soft-deleted (Active = false) so they are not re-seeded; others are removed.
        /// Past runs keep their snapshot.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="actionId">Action identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when not found.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when a non-admin deletes a Command action or one with an applicability command.</exception>
        public async Task DeleteActionAsync(AuthContext auth, string actionId, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            FleetAction existing = await ReadActionAsync(auth, actionId, token).ConfigureAwait(false);
            RequireExecutionPermission(auth, existing);
            await _Database.FleetActions.DeleteAsync(tenantId, existing.Id, token).ConfigureAwait(false);
            _Logging.Info(_Header + "deleted action " + existing.Id + " in tenant " + tenantId);
        }

        /// <summary>
        /// Start a run of a saved action (when <paramref name="actionId"/> is set) or an ad hoc run from
        /// <see cref="FleetActionRunRequest.Definition"/>. Every target vessel is re-read with the tenant-scoped read
        /// before anything is written; one unknown or cross-tenant vessel rejects the whole request.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="actionId">Saved action identifier, or null for an ad hoc run.</param>
        /// <param name="request">Run request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created run (status Pending).</returns>
        /// <exception cref="ArgumentException">Thrown for invalid input.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the action or any vessel is not found in the tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when a non-admin runs a Command action or one with an applicability command.</exception>
        public async Task<FleetActionRun> StartRunAsync(AuthContext auth, string? actionId, FleetActionRunRequest request, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            if (request == null) throw new ArgumentException("A request body is required.", nameof(request));

            FleetAction definition;
            string? sourceActionId = null;
            if (!String.IsNullOrWhiteSpace(actionId))
            {
                definition = await ReadActionAsync(auth, actionId!, token).ConfigureAwait(false);
                if (!definition.Active) throw new KeyNotFoundException("Fleet action not found: " + actionId);
                sourceActionId = definition.Id;
            }
            else
            {
                if (request.Definition == null) throw new ArgumentException("An ad hoc run requires a definition.", nameof(request));
                definition = BuildDefinition(request.Definition, null);
            }

            RequireExecutionPermission(auth, definition);
            request.Overrides?.ApprovalOptions?.ValidateFor(auth);
            CollectionLimits.RequireRequestItems(request.VesselIds, "VesselIds");
            Dictionary<string, string> inputValues = FleetActionInputValidator.Resolve(definition.Inputs, request.InputValues);

            List<string> vesselIds = request.VesselIds
                .Where(v => !String.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (!String.IsNullOrWhiteSpace(request.FromRunId))
            {
                if (vesselIds.Count > 0) throw new ArgumentException("Set either VesselIds or FromRunId, not both.", nameof(request));
                vesselIds = await ReadFollowUpVesselIdsAsync(auth, request.FromRunId!.Trim(), request.FromTargetStatuses, token).ConfigureAwait(false);
                if (vesselIds.Count == 0) throw new ArgumentException("No target of run " + request.FromRunId!.Trim() + " has a selected status.", nameof(request));
            }
            else if (request.FromTargetStatuses != null && request.FromTargetStatuses.Count > 0)
            {
                throw new ArgumentException("FromTargetStatuses requires FromRunId.", nameof(request));
            }

            if (vesselIds.Count == 0) throw new ArgumentException("At least one vessel id is required.", nameof(request));
            if (vesselIds.Count > MaxTargetsPerRun) throw new ArgumentException("A run may target at most " + MaxTargetsPerRun + " vessels.", nameof(request));

            List<Vessel> vessels = new List<Vessel>();
            foreach (string vesselId in vesselIds)
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, vesselId, token).ConfigureAwait(false);
                if (vessel == null) throw new KeyNotFoundException("Vessel not found: " + vesselId);
                vessels.Add(vessel);
            }

            FleetActionRun run = new FleetActionRun
            {
                TenantId = tenantId,
                UserId = auth.UserId,
                ActionId = sourceActionId,
                ActionName = definition.Name,
                CommandWindows = definition.CommandWindows,
                OutputParser = definition.OutputParser,
                RequiredTools = definition.RequiredTools,
                RequiresNetwork = definition.RequiresNetwork,
                ScanSourceActionKey = definition.ScanSourceActionKey,
                ApplicabilityCommand = definition.ApplicabilityCommand,
                ApplicabilityCommandWindows = definition.ApplicabilityCommandWindows,
                MissionMode = definition.Kind == FleetActionKindEnum.Mission ? definition.MissionMode : MissionModeEnum.Implementation,
                Inputs = definition.Inputs,
                InputValues = inputValues,
                Kind = definition.Kind,
                CommandText = definition.Kind == FleetActionKindEnum.Command ? definition.CommandText : null,
                PromptTemplate = definition.Kind == FleetActionKindEnum.Mission ? definition.PromptTemplate : null,
                PipelineId = definition.Kind == FleetActionKindEnum.Mission ? definition.PipelineId : null,
                Persona = definition.Kind == FleetActionKindEnum.Mission ? definition.Persona : null,
                TimeoutSeconds = definition.TimeoutSeconds,
                RequiresCleanWorkingTree = definition.Kind == FleetActionKindEnum.Command && definition.RequiresCleanWorkingTree,
                Concurrency = request.Concurrency ?? definition.DefaultConcurrency,
                Status = FleetActionRunStatusEnum.Pending,
                ApprovalOptions = request.Overrides?.ApprovalOptions?.Snapshot(),
                TargetCount = vessels.Count
            };

            if (request.Overrides != null)
            {
                if (request.Overrides.TimeoutSeconds.HasValue) run.TimeoutSeconds = request.Overrides.TimeoutSeconds.Value;
                if (request.Overrides.RequiresCleanWorkingTree.HasValue && run.Kind == FleetActionKindEnum.Command)
                    run.RequiresCleanWorkingTree = request.Overrides.RequiresCleanWorkingTree.Value;
                if (request.Overrides.PipelineId != null && run.Kind == FleetActionKindEnum.Mission)
                    run.PipelineId = String.IsNullOrWhiteSpace(request.Overrides.PipelineId) ? null : request.Overrides.PipelineId.Trim();
            }

            CollectionBounds.Validate(run);
            run = await _Database.FleetActionRuns.CreateAsync(run, token).ConfigureAwait(false);

            DateTime created = DateTime.UtcNow;
            for (int i = 0; i < vessels.Count; i++)
            {
                FleetActionRunTarget target = new FleetActionRunTarget
                {
                    TenantId = tenantId,
                    RunId = run.Id,
                    VesselId = vessels[i].Id,
                    VesselName = vessels[i].Name,
                    Status = FleetActionTargetStatusEnum.Pending,
                    CreatedUtc = created.AddMilliseconds(i),
                    LastUpdateUtc = created
                };
                await _Database.FleetActionRunTargets.CreateAsync(target, token).ConfigureAwait(false);
            }

            _Logging.Info(_Header + "started " + run.Kind + " run " + run.Id + " (" + run.ActionName + ") over " + vessels.Count + " vessel(s) in tenant " + tenantId);
            _Runner.Enqueue(run);
            return run;
        }

        /// <summary>
        /// Enumerate the tenant's runs, newest first by default. Query.Status filters by run status.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="query">Query; null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of runs.</returns>
        /// <exception cref="ArgumentException">Thrown for an unknown status filter.</exception>
        public async Task<EnumerationResult<FleetActionRun>> EnumerateRunsAsync(AuthContext auth, EnumerationQuery? query, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (!String.IsNullOrWhiteSpace(q.Status))
            {
                if (!Enum.TryParse(q.Status, true, out FleetActionRunStatusEnum parsed))
                    throw new ArgumentException("Unknown run status: " + q.Status, nameof(query));
                q.Status = parsed.ToString();
            }

            return await _Database.FleetActionRuns.EnumerateAsync(tenantId, q, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a run. Its targets are not embedded (docs/ENUMERATION.md): the run carries the target counts, and the
        /// targets are read one page at a time with <see cref="EnumerateTargetSummariesAsync"/>.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run detail.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when not found.</exception>
        public async Task<FleetActionRunDetail> ReadRunAsync(AuthContext auth, string runId, CancellationToken token = default)
        {
            FleetActionRun run = await ReadRunOnlyAsync(auth, runId, token).ConfigureAwait(false);
            return new FleetActionRunDetail
            {
                Run = run
            };
        }

        /// <summary>
        /// Read a run without its targets.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when not found.</exception>
        public async Task<FleetActionRun> ReadRunOnlyAsync(AuthContext auth, string runId, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            if (String.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A run id is required.", nameof(runId));
            FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(tenantId, runId, token).ConfigureAwait(false);
            if (run == null) throw new KeyNotFoundException("Fleet action run not found: " + runId);
            return run;
        }

        /// <summary>
        /// Enumerate a run's targets as full rows (including output).
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="request">Paging and status filter; null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of targets.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the run is not found.</exception>
        public async Task<EnumerationResult<FleetActionRunTarget>> EnumerateTargetsAsync(AuthContext auth, string runId, FleetActionTargetEnumerateRequest? request, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            FleetActionRun run = await ReadRunOnlyAsync(auth, runId, token).ConfigureAwait(false);
            FleetActionTargetEnumerateRequest r = request ?? new FleetActionTargetEnumerateRequest();
            return await _Database.FleetActionRunTargets.EnumerateByRunAsync(tenantId, run.Id, r.Status, r.PageNumber, r.PageSize, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate a run's targets as summaries (no output).
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="request">Paging and status filter; null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of target summaries.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the run is not found.</exception>
        public async Task<EnumerationResult<FleetActionRunTargetSummary>> EnumerateTargetSummariesAsync(AuthContext auth, string runId, FleetActionTargetEnumerateRequest? request, CancellationToken token = default)
        {
            EnumerationResult<FleetActionRunTarget> page = await EnumerateTargetsAsync(auth, runId, request, token).ConfigureAwait(false);
            return new EnumerationResult<FleetActionRunTargetSummary>
            {
                Success = page.Success,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalPages = page.TotalPages,
                TotalRecords = page.TotalRecords,
                Objects = page.Objects.Select(FleetActionRunTargetSummary.FromTarget).ToList(),
                TotalMs = page.TotalMs
            };
        }

        /// <summary>
        /// Read one target of a run, including RenderedText, OutputText and ErrorText.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the run or target is not found.</exception>
        public async Task<FleetActionRunTarget> ReadTargetAsync(AuthContext auth, string runId, string targetId, CancellationToken token = default)
        {
            string tenantId = RequireTenant(auth);
            FleetActionRun run = await ReadRunOnlyAsync(auth, runId, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("A target id is required.", nameof(targetId));
            FleetActionRunTarget? target = await _Database.FleetActionRunTargets.ReadAsync(tenantId, targetId, token).ConfigureAwait(false);
            if (target == null || !String.Equals(target.RunId, run.Id, StringComparison.Ordinal))
                throw new KeyNotFoundException("Fleet action run target not found: " + targetId);
            return target;
        }

        /// <summary>
        /// Cancel a run. Command runs require tenant admin, the same as starting them.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated run.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when not found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the run already finished.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when a non-admin cancels a Command run.</exception>
        public async Task<FleetActionRun> CancelRunAsync(AuthContext auth, string runId, CancellationToken token = default)
        {
            FleetActionRun run = await ReadRunOnlyAsync(auth, runId, token).ConfigureAwait(false);
            RequireKindPermission(auth, run.Kind);
            FleetActionRun cancelled = await _Runner.CancelRunAsync(run.Id, token).ConfigureAwait(false);
            _Logging.Info(_Header + "cancelled run " + run.Id);
            return cancelled;
        }

        #endregion

        #region Private-Methods

        private static string RequireTenant(AuthContext auth)
        {
            if (auth == null) throw new UnauthorizedAccessException("Authentication required.");
            if (String.IsNullOrEmpty(auth.TenantId)) throw new UnauthorizedAccessException("The caller has no tenant.");
            return auth.TenantId!;
        }

        private static void RequireKindPermission(AuthContext auth, FleetActionKindEnum kind)
        {
            // Command actions execute arbitrary code on the host: tenant admin, matching workspace exec.
            if (kind == FleetActionKindEnum.Command && !auth.IsAdmin && !auth.IsTenantAdmin)
                throw new UnauthorizedAccessException("Command fleet actions require tenant admin permission.");
        }

        private static void RequireExecutionPermission(AuthContext auth, FleetAction action)
        {
            // Command actions and applicability checks execute arbitrary code on the host: tenant admin, matching workspace exec.
            if (auth.IsAdmin || auth.IsTenantAdmin) return;
            if (action.Kind == FleetActionKindEnum.Command)
                throw new UnauthorizedAccessException("Command fleet actions require tenant admin permission.");
            if (!String.IsNullOrWhiteSpace(action.ApplicabilityCommand) || !String.IsNullOrWhiteSpace(action.ApplicabilityCommandWindows))
                throw new UnauthorizedAccessException("Fleet actions with an applicability command require tenant admin permission.");
        }

        private async Task<List<string>> ReadFollowUpVesselIdsAsync(AuthContext auth, string fromRunId, List<FleetActionTargetStatusEnum>? statuses, CancellationToken token)
        {
            FleetActionRun source = await ReadRunOnlyAsync(auth, fromRunId, token).ConfigureAwait(false);
            CollectionLimits.RequireConfigItems(statuses, "FromTargetStatuses");
            List<FleetActionTargetStatusEnum> wanted = statuses != null && statuses.Count > 0
                ? statuses.Distinct().ToList()
                : new List<FleetActionTargetStatusEnum> { FleetActionTargetStatusEnum.Findings, FleetActionTargetStatusEnum.Failed };
            List<string> vesselIds = new List<string>();
            foreach (FleetActionRunTarget target in await _Database.FleetActionRunTargets.ReadAllByRunAsync(source.Id, token).ConfigureAwait(false))
            {
                if (wanted.Contains(target.Status) && !vesselIds.Contains(target.VesselId, StringComparer.Ordinal)) vesselIds.Add(target.VesselId);
            }

            return vesselIds;
        }

        private FleetAction BuildDefinition(FleetActionUpsertRequest request, FleetAction? existing)
        {
            FleetAction action = existing ?? new FleetAction
            {
                Kind = request.Kind ?? FleetActionKindEnum.Command,
                TimeoutSeconds = _Settings.FleetActions.DefaultTimeoutSeconds
            };

            if (existing == null)
            {
                action.RequiresCleanWorkingTree = action.Kind == FleetActionKindEnum.Command;
            }
            else if (request.Kind.HasValue)
            {
                action.Kind = request.Kind.Value;
            }

            if (request.CommandWindows != null) action.CommandWindows = request.CommandWindows;
            if (request.OutputParser != null) action.OutputParser = String.IsNullOrWhiteSpace(request.OutputParser) ? null : request.OutputParser.Trim();
            if (request.ScanSourceActionKey != null) action.ScanSourceActionKey = String.IsNullOrWhiteSpace(request.ScanSourceActionKey) ? null : request.ScanSourceActionKey.Trim();
            if (request.ApplicabilityCommand != null) action.ApplicabilityCommand = String.IsNullOrWhiteSpace(request.ApplicabilityCommand) ? null : request.ApplicabilityCommand;
            if (request.ApplicabilityCommandWindows != null) action.ApplicabilityCommandWindows = String.IsNullOrWhiteSpace(request.ApplicabilityCommandWindows) ? null : request.ApplicabilityCommandWindows;
            if (request.MissionMode.HasValue) action.MissionMode = request.MissionMode.Value;
            CollectionLimits.RequireConfigItems(request.RequiredTools, "RequiredTools");
            CollectionLimits.RequireConfigItems(request.Inputs, "Inputs");
            if (request.RequiredTools != null) action.RequiredTools = request.RequiredTools;
            if (request.RequiresNetwork.HasValue) action.RequiresNetwork = request.RequiresNetwork.Value;
            if (request.Inputs != null) action.Inputs = request.Inputs;
            FleetActionInputValidator.ValidateDefinitions(action.Inputs);
            if (action.OutputParser != null && !FleetActionOutputParsers.FleetActionParserRegistry.IsKnown(action.OutputParser)) throw new ArgumentException("Unknown output parser: " + action.OutputParser);
            foreach (string tool in action.RequiredTools)
                if (String.IsNullOrWhiteSpace(tool) || !System.Text.RegularExpressions.Regex.IsMatch(tool, @"^[A-Za-z0-9_][A-Za-z0-9_.+-]*\z")) throw new ArgumentException("RequiredTools must contain executable names, not paths or shell commands.");
            if (request.Name != null) action.Name = request.Name.Trim();
            if (request.Description != null) action.Description = String.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            if (request.CommandText != null) action.CommandText = String.IsNullOrWhiteSpace(request.CommandText) ? null : request.CommandText;
            if (request.PromptTemplate != null) action.PromptTemplate = String.IsNullOrWhiteSpace(request.PromptTemplate) ? null : request.PromptTemplate;
            if (request.PipelineId != null) action.PipelineId = String.IsNullOrWhiteSpace(request.PipelineId) ? null : request.PipelineId.Trim();
            if (request.Persona != null) action.Persona = String.IsNullOrWhiteSpace(request.Persona) ? null : request.Persona.Trim();
            if (request.TimeoutSeconds.HasValue) action.TimeoutSeconds = request.TimeoutSeconds.Value;
            if (request.DefaultConcurrency.HasValue) action.DefaultConcurrency = request.DefaultConcurrency.Value;
            if (request.RequiresCleanWorkingTree.HasValue) action.RequiresCleanWorkingTree = request.RequiresCleanWorkingTree.Value;

            if (String.IsNullOrWhiteSpace(action.Name)) throw new ArgumentException("Name is required.", nameof(request));
            if (action.Name.Length > 200) throw new ArgumentException("Name must be 200 characters or fewer.", nameof(request));

            CollectionBounds.Validate(action);
            FleetActionTemplateRenderer.Validate(action.ApplicabilityCommand, action.Inputs.Select(i => i.Name));
            FleetActionTemplateRenderer.Validate(action.ApplicabilityCommandWindows, action.Inputs.Select(i => i.Name));

            if (action.Kind == FleetActionKindEnum.Command)
            {
                if (String.IsNullOrWhiteSpace(action.CommandText)) throw new ArgumentException("CommandText is required for a Command action.", nameof(request));
                if (action.MissionMode != MissionModeEnum.Implementation) throw new ArgumentException("MissionMode applies to Mission actions only.", nameof(request));
                FleetActionTemplateRenderer.Validate(action.CommandText, action.Inputs.Select(i => i.Name));
                FleetActionTemplateRenderer.Validate(action.CommandWindows, action.Inputs.Select(i => i.Name));
            }
            else
            {
                if (String.IsNullOrWhiteSpace(action.PromptTemplate)) throw new ArgumentException("PromptTemplate is required for a Mission action.", nameof(request));
                FleetActionTemplateRenderer.Validate(action.PromptTemplate, action.Inputs.Select(i => i.Name));
            }

            return action;
        }

        #endregion
    }
}
