namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Playbook-backed runbooks with event-backed execution records.
    /// </summary>
    public class RunbookService
    {
        /// <summary>
        /// Optional callback invoked whenever a runbook execution changes.
        /// </summary>
        public Action<RunbookExecution>? OnRunbookExecutionChanged { get; set; }

        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private static readonly string _MetadataMarker = "<!-- armada:runbook";
        private static readonly Regex _ParameterPattern = new Regex(@"\{\{\s*([a-zA-Z0-9_.-]+)\s*\}\}", RegexOptions.Compiled);
        private static readonly Regex _NumberedStepPattern = new Regex(@"^\s*\d+\.\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex _CheckboxStepPattern = new Regex(@"^\s*[-*]\s+\[\s*\]\s+(.+)$", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RunbookService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Enumerate runbooks visible to the caller.
        /// </summary>
        public async Task<EnumerationResult<Runbook>> EnumerateAsync(
            AuthContext auth,
            RunbookQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<Runbook> runbooks = (await ReadPlaybooksAsync(auth, token).ConfigureAwait(false))
                .Select(ParseRunbook)
                .ToList();

            if (!auth.IsAdmin)
                runbooks = runbooks.Where(item => ScopedVisibility.CanView(auth, item.Scope, item.TenantId, item.UserId)).ToList();

            IEnumerable<Runbook> filtered = runbooks;
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
                filtered = filtered.Where(item => String.Equals(item.WorkflowProfileId, query.WorkflowProfileId, StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.EnvironmentId))
                filtered = filtered.Where(item => String.Equals(item.EnvironmentId, query.EnvironmentId, StringComparison.OrdinalIgnoreCase));
            if (query.DefaultCheckType.HasValue)
                filtered = filtered.Where(item => item.DefaultCheckType == query.DefaultCheckType.Value);
            if (query.Active.HasValue)
                filtered = filtered.Where(item => item.Active == query.Active.Value);

            string? search = Normalize(query.Search);
            if (!String.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(item =>
                    ContainsIgnoreCase(item.Title, search)
                    || ContainsIgnoreCase(item.Description, search)
                    || ContainsIgnoreCase(item.FileName, search)
                    || ContainsIgnoreCase(item.OverviewMarkdown, search)
                    || item.Steps.Any(step => ContainsIgnoreCase(step.Title, search) || ContainsIgnoreCase(step.Instructions, search)));
            }

            List<Runbook> ordered = filtered
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 500);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;
            List<Runbook> page = ordered.Skip(offset).Take(pageSize).ToList();

            return new EnumerationResult<Runbook>
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
        /// Read one runbook.
        /// </summary>
        public async Task<Runbook?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Playbook? playbook = await ReadPlaybookAsync(auth, id, token).ConfigureAwait(false);
            if (playbook == null) return null;
            Runbook runbook = ParseRunbook(playbook);
            return ScopedVisibility.CanView(auth, runbook.Scope, runbook.TenantId, runbook.UserId) ? runbook : null;
        }

        /// <summary>
        /// Create a runbook backed by a playbook record.
        /// </summary>
        public async Task<Runbook> CreateAsync(AuthContext auth, RunbookUpsertRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Playbook playbook = BuildPlaybook(auth, request, null);
            playbook.Scope = ScopedVisibility.ResolveCreateScope(auth, request.Scope);
            PlaybookService playbookService = new PlaybookService(_Database, _Logging);
            playbookService.Validate(playbook);

            bool exists = !String.IsNullOrWhiteSpace(auth.TenantId)
                && await _Database.Playbooks.ExistsByFileNameAsync(auth.TenantId, playbook.FileName, token).ConfigureAwait(false);
            if (exists)
                throw new InvalidOperationException("A runbook with that file name already exists.");

            playbook = await _Database.Playbooks.CreateAsync(playbook, token).ConfigureAwait(false);
            return ParseRunbook(playbook);
        }

        /// <summary>
        /// Update a runbook.
        /// </summary>
        public async Task<Runbook> UpdateAsync(AuthContext auth, string id, RunbookUpsertRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Playbook existing = await ReadPlaybookAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Runbook not found.");
            if (!ScopedVisibility.CanView(auth, existing.Scope, existing.TenantId, existing.UserId))
                throw new InvalidOperationException("Runbook not found.");
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId))
                throw new UnauthorizedAccessException("You may only modify your own runbooks; a tenant-wide runbook requires a tenant admin.");

            Playbook updated = BuildPlaybook(auth, request, existing);
            updated.Id = existing.Id;
            updated.TenantId = existing.TenantId;
            updated.UserId = existing.UserId;
            updated.CreatedUtc = existing.CreatedUtc;
            updated.Scope = (auth.IsAdmin || auth.IsTenantAdmin) ? (request.Scope ?? existing.Scope) : existing.Scope;

            PlaybookService playbookService = new PlaybookService(_Database, _Logging);
            playbookService.Validate(updated);

            if (!String.IsNullOrWhiteSpace(updated.TenantId))
            {
                Playbook? duplicate = await _Database.Playbooks.ReadByFileNameAsync(updated.TenantId, updated.FileName, token).ConfigureAwait(false);
                if (duplicate != null && !String.Equals(duplicate.Id, updated.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("A runbook with that file name already exists.");
            }

            updated = await _Database.Playbooks.UpdateAsync(updated, token).ConfigureAwait(false);
            return ParseRunbook(updated);
        }

        /// <summary>
        /// Delete one runbook.
        /// </summary>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Playbook? existing = await ReadPlaybookAsync(auth, id, token).ConfigureAwait(false);
            if (existing == null || !ScopedVisibility.CanView(auth, existing.Scope, existing.TenantId, existing.UserId))
                throw new InvalidOperationException("Runbook not found.");
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId))
                throw new UnauthorizedAccessException("You may only delete your own runbooks; a tenant-wide runbook requires a tenant admin.");

            await _Database.Playbooks.DeleteAsync(id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate runbook executions.
        /// </summary>
        public async Task<EnumerationResult<RunbookExecution>> EnumerateExecutionsAsync(
            AuthContext auth,
            RunbookExecutionQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<RunbookExecution> executions = await ReadAllExecutionsAsync(auth, token).ConfigureAwait(false);
            IEnumerable<RunbookExecution> filtered = executions;

            if (!String.IsNullOrWhiteSpace(query.RunbookId))
                filtered = filtered.Where(item => String.Equals(item.RunbookId, query.RunbookId, StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.DeploymentId))
                filtered = filtered.Where(item => String.Equals(item.DeploymentId, query.DeploymentId, StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.IncidentId))
                filtered = filtered.Where(item => String.Equals(item.IncidentId, query.IncidentId, StringComparison.OrdinalIgnoreCase));
            if (query.Status.HasValue)
                filtered = filtered.Where(item => item.Status == query.Status.Value);

            string? search = Normalize(query.Search);
            if (!String.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(item =>
                    ContainsIgnoreCase(item.Title, search)
                    || ContainsIgnoreCase(item.EnvironmentName, search)
                    || ContainsIgnoreCase(item.Notes, search));
            }

            List<RunbookExecution> ordered = filtered
                .OrderByDescending(item => item.LastUpdateUtc)
                .ThenByDescending(item => item.Id, StringComparer.Ordinal)
                .ToList();

            int pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 500);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;
            List<RunbookExecution> page = ordered.Skip(offset).Take(pageSize).ToList();

            return new EnumerationResult<RunbookExecution>
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
        /// Read one execution.
        /// </summary>
        public async Task<RunbookExecution?> ReadExecutionAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            List<ArmadaEvent> snapshots = await ReadExecutionSnapshotEventsAsync(auth, id, token).ConfigureAwait(false);
            return ProjectLatestExecution(snapshots);
        }

        /// <summary>
        /// Start a new runbook execution.
        /// </summary>
        public async Task<RunbookExecution> StartExecutionAsync(
            AuthContext auth,
            string runbookId,
            RunbookExecutionStartRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(runbookId)) throw new ArgumentNullException(nameof(runbookId));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Runbook runbook = await ReadAsync(auth, runbookId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Runbook not found.");

            Dictionary<string, string> parameterValues = ResolveParameterValues(runbook, request.ParameterValues);
            RunbookExecution execution = new RunbookExecution
            {
                RunbookId = runbook.Id,
                PlaybookId = runbook.PlaybookId,
                TenantId = runbook.TenantId,
                UserId = auth.UserId,
                Title = Normalize(request.Title) ?? runbook.Title,
                Status = RunbookExecutionStatusEnum.Running,
                WorkflowProfileId = Normalize(request.WorkflowProfileId) ?? runbook.WorkflowProfileId,
                EnvironmentId = Normalize(request.EnvironmentId) ?? runbook.EnvironmentId,
                EnvironmentName = Normalize(request.EnvironmentName) ?? runbook.EnvironmentName,
                CheckType = request.CheckType ?? runbook.DefaultCheckType,
                DeploymentId = Normalize(request.DeploymentId),
                IncidentId = Normalize(request.IncidentId),
                ParameterValues = parameterValues,
                CompletedStepIds = new List<string>(),
                StepNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Notes = Normalize(request.Notes),
                StartedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            await WriteExecutionSnapshotAsync(auth, execution, token).ConfigureAwait(false);
            OnRunbookExecutionChanged?.Invoke(execution);
            return execution;
        }

        /// <summary>
        /// Update an existing runbook execution.
        /// </summary>
        public async Task<RunbookExecution> UpdateExecutionAsync(
            AuthContext auth,
            string id,
            RunbookExecutionUpdateRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));

            RunbookExecution execution = await ReadExecutionAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Runbook execution not found.");

            execution.Status = request.Status ?? execution.Status;
            if (request.CompletedStepIds != null)
                execution.CompletedStepIds = request.CompletedStepIds.Where(item => !String.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (request.StepNotes != null)
            {
                execution.StepNotes = new Dictionary<string, string>(request.StepNotes
                    .Where(item => !String.IsNullOrWhiteSpace(item.Key))
                    .ToDictionary(item => item.Key.Trim(), item => item.Value ?? String.Empty, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            }
            if (request.Notes != null)
                execution.Notes = Normalize(request.Notes);

            execution.LastUpdateUtc = DateTime.UtcNow;
            if (execution.Status != RunbookExecutionStatusEnum.Running && !execution.CompletedUtc.HasValue)
                execution.CompletedUtc = DateTime.UtcNow;
            if (execution.Status == RunbookExecutionStatusEnum.Running)
                execution.CompletedUtc = null;

            await WriteExecutionSnapshotAsync(auth, execution, token).ConfigureAwait(false);
            OnRunbookExecutionChanged?.Invoke(execution);
            return execution;
        }

        /// <summary>
        /// Delete a runbook execution and its snapshots.
        /// </summary>
        public async Task DeleteExecutionAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            List<ArmadaEvent> snapshots = await ReadExecutionSnapshotEventsAsync(auth, id, token).ConfigureAwait(false);
            if (snapshots.Count == 0)
                throw new InvalidOperationException("Runbook execution not found.");

            foreach (ArmadaEvent snapshot in snapshots)
            {
                await DeleteEventAsync(auth, snapshot.Id, token).ConfigureAwait(false);
            }
        }

        private async Task<List<Playbook>> ReadPlaybooksAsync(AuthContext auth, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery
            {
                PageNumber = 1,
                PageSize = 1000
            };

            if (auth.IsAdmin)
                return (await _Database.Playbooks.EnumerateAsync(query, token).ConfigureAwait(false)).Objects;
            return (await _Database.Playbooks.EnumerateAsync(auth.TenantId!, query, token).ConfigureAwait(false)).Objects;
        }

        private async Task<Playbook?> ReadPlaybookAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Playbooks.ReadAsync(id, token).ConfigureAwait(false);
            return await _Database.Playbooks.ReadAsync(auth.TenantId!, id, token).ConfigureAwait(false);
        }

        private Runbook ParseRunbook(Playbook playbook)
        {
            RunbookMetadataDocument metadata = ParseMetadataDocument(playbook.Content, out string overviewMarkdown);
            List<RunbookStep> steps = metadata.Steps != null && metadata.Steps.Count > 0
                ? NormalizeSteps(metadata.Steps)
                : DeriveStepsFromMarkdown(overviewMarkdown, metadata.Title ?? PathToTitle(playbook.FileName));
            List<RunbookParameter> parameters = metadata.Parameters != null && metadata.Parameters.Count > 0
                ? NormalizeParameters(metadata.Parameters)
                : DetectParameters(overviewMarkdown, steps);

            return new Runbook
            {
                Id = playbook.Id,
                PlaybookId = playbook.Id,
                TenantId = playbook.TenantId,
                UserId = playbook.UserId,
                Scope = playbook.Scope,
                FileName = playbook.FileName,
                Title = metadata.Title ?? PathToTitle(playbook.FileName),
                Description = playbook.Description,
                WorkflowProfileId = Normalize(metadata.WorkflowProfileId),
                EnvironmentId = Normalize(metadata.EnvironmentId),
                EnvironmentName = Normalize(metadata.EnvironmentName),
                DefaultCheckType = metadata.DefaultCheckType,
                Parameters = parameters,
                Steps = steps,
                OverviewMarkdown = overviewMarkdown,
                Active = playbook.Active,
                CreatedUtc = playbook.CreatedUtc,
                LastUpdateUtc = playbook.LastUpdateUtc
            };
        }

        private Playbook BuildPlaybook(AuthContext auth, RunbookUpsertRequest request, Playbook? existing)
        {
            string fileName = Normalize(request.FileName)
                ?? existing?.FileName
                ?? "RUNBOOK.md";
            string title = Normalize(request.Title)
                ?? PathToTitle(fileName);
            string overviewMarkdown = request.OverviewMarkdown ?? ExtractOverview(existing?.Content) ?? String.Empty;
            List<RunbookParameter> parameters = NormalizeParameters(request.Parameters ?? new List<RunbookParameter>());
            List<RunbookStep> steps = NormalizeSteps(request.Steps ?? new List<RunbookStep>());
            RunbookMetadataDocument metadata = new RunbookMetadataDocument
            {
                Title = title,
                WorkflowProfileId = Normalize(request.WorkflowProfileId),
                EnvironmentId = Normalize(request.EnvironmentId),
                EnvironmentName = Normalize(request.EnvironmentName),
                DefaultCheckType = request.DefaultCheckType,
                Parameters = parameters,
                Steps = steps
            };

            string metadataJson = JsonSerializer.Serialize(metadata, _JsonOptions);
            string content = _MetadataMarker + Environment.NewLine
                + metadataJson + Environment.NewLine
                + "-->" + Environment.NewLine + Environment.NewLine
                + overviewMarkdown;

            Playbook playbook = new Playbook
            {
                TenantId = existing?.TenantId ?? auth.TenantId,
                UserId = existing?.UserId ?? auth.UserId,
                FileName = fileName,
                Description = Normalize(request.Description) ?? existing?.Description,
                Content = content,
                Active = request.Active ?? existing?.Active ?? true,
                CreatedUtc = existing?.CreatedUtc ?? DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            if (existing != null)
            {
                playbook.Id = existing.Id;
            }

            return playbook;
        }

        private async Task<List<RunbookExecution>> ReadAllExecutionsAsync(AuthContext auth, CancellationToken token)
        {
            List<ArmadaEvent> snapshots = await ReadExecutionSnapshotEventsAsync(auth, null, token).ConfigureAwait(false);
            Dictionary<string, ArmadaEvent> latestByExecutionId = new Dictionary<string, ArmadaEvent>(StringComparer.OrdinalIgnoreCase);

            foreach (ArmadaEvent snapshot in snapshots)
            {
                if (String.IsNullOrWhiteSpace(snapshot.EntityId))
                    continue;

                if (!latestByExecutionId.TryGetValue(snapshot.EntityId, out ArmadaEvent? existing)
                    || IsSnapshotNewer(snapshot, existing))
                {
                    latestByExecutionId[snapshot.EntityId] = snapshot;
                }
            }

            List<RunbookExecution> executions = new List<RunbookExecution>();
            foreach (ArmadaEvent snapshot in latestByExecutionId.Values)
            {
                RunbookExecution? execution = DeserializeExecution(snapshot);
                if (execution != null)
                    executions.Add(execution);
            }

            return executions;
        }

        private async Task<List<ArmadaEvent>> ReadExecutionSnapshotEventsAsync(
            AuthContext auth,
            string? executionId,
            CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(executionId))
            {
                if (auth.IsAdmin)
                    return await _Database.Events.EnumerateByEntityAsync("runbook-execution", executionId, 500, token).ConfigureAwait(false);
                if (auth.IsTenantAdmin)
                    return await _Database.Events.EnumerateByEntityAsync(auth.TenantId!, "runbook-execution", executionId, 500, token).ConfigureAwait(false);
                return (await _Database.Events.EnumerateAsync(auth.TenantId!, auth.UserId!, new EnumerationQuery
                {
                    PageNumber = 1,
                    PageSize = 500
                }, token).ConfigureAwait(false)).Objects
                    .Where(item =>
                        String.Equals(item.EntityType, "runbook-execution", StringComparison.OrdinalIgnoreCase)
                        && String.Equals(item.EntityId, executionId, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(item.EventType, "runbook-execution.snapshot", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            EnumerationQuery query = new EnumerationQuery
            {
                PageNumber = 1,
                PageSize = 500,
                EventType = "runbook-execution.snapshot"
            };

            List<ArmadaEvent> results = new List<ArmadaEvent>();
            while (true)
            {
                EnumerationResult<ArmadaEvent> page;
                if (auth.IsAdmin)
                    page = await _Database.Events.EnumerateAsync(query, token).ConfigureAwait(false);
                else if (auth.IsTenantAdmin)
                    page = await _Database.Events.EnumerateAsync(auth.TenantId!, query, token).ConfigureAwait(false);
                else
                    page = await _Database.Events.EnumerateAsync(auth.TenantId!, auth.UserId!, query, token).ConfigureAwait(false);

                results.AddRange(page.Objects.Where(item => String.Equals(item.EntityType, "runbook-execution", StringComparison.OrdinalIgnoreCase)));
                if (page.Objects.Count < query.PageSize)
                    break;
                query.PageNumber += 1;
            }

            return results;
        }

        private async Task WriteExecutionSnapshotAsync(AuthContext auth, RunbookExecution execution, CancellationToken token)
        {
            ArmadaEvent snapshot = new ArmadaEvent("runbook-execution.snapshot", execution.Title)
            {
                TenantId = execution.TenantId,
                UserId = auth.UserId,
                EntityType = "runbook-execution",
                EntityId = execution.Id,
                Payload = JsonSerializer.Serialize(execution, _JsonOptions),
                CreatedUtc = execution.LastUpdateUtc
            };

            await _Database.Events.CreateAsync(snapshot, token).ConfigureAwait(false);
        }

        private async Task DeleteEventAsync(AuthContext auth, string id, CancellationToken token)
        {
            if (auth.IsAdmin)
                await _Database.Events.DeleteAsync(id, token).ConfigureAwait(false);
            else if (auth.IsTenantAdmin)
                await _Database.Events.DeleteAsync(auth.TenantId!, id, token).ConfigureAwait(false);
            else
                await _Database.Events.DeleteAsync(auth.TenantId!, auth.UserId!, id, token).ConfigureAwait(false);
        }

        private static RunbookExecution? ProjectLatestExecution(List<ArmadaEvent> snapshots)
        {
            ArmadaEvent? latest = snapshots
                .OrderByDescending(item => item.CreatedUtc)
                .ThenByDescending(item => item.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            return latest != null ? DeserializeExecution(latest) : null;
        }

        private static RunbookExecution? DeserializeExecution(ArmadaEvent snapshot)
        {
            if (String.IsNullOrWhiteSpace(snapshot.Payload))
                return null;

            RunbookExecution? execution = JsonSerializer.Deserialize<RunbookExecution>(snapshot.Payload, _JsonOptions);
            if (execution == null)
                return null;

            execution.TenantId = execution.TenantId ?? snapshot.TenantId;
            execution.UserId = execution.UserId ?? snapshot.UserId;
            execution.LastUpdateUtc = execution.LastUpdateUtc == default ? snapshot.CreatedUtc : execution.LastUpdateUtc;
            return execution;
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

        private static RunbookMetadataDocument ParseMetadataDocument(string content, out string overviewMarkdown)
        {
            string normalizedContent = content ?? String.Empty;
            if (!normalizedContent.StartsWith(_MetadataMarker, StringComparison.Ordinal))
            {
                overviewMarkdown = normalizedContent;
                return new RunbookMetadataDocument();
            }

            int endMarkerIndex = normalizedContent.IndexOf("-->", StringComparison.Ordinal);
            if (endMarkerIndex < 0)
            {
                overviewMarkdown = normalizedContent;
                return new RunbookMetadataDocument();
            }

            string metadataJson = normalizedContent
                .Substring(_MetadataMarker.Length, endMarkerIndex - _MetadataMarker.Length)
                .Trim();
            string body = normalizedContent.Substring(endMarkerIndex + 3).TrimStart('\r', '\n');
            overviewMarkdown = body;

            try
            {
                return JsonSerializer.Deserialize<RunbookMetadataDocument>(metadataJson, _JsonOptions) ?? new RunbookMetadataDocument();
            }
            catch
            {
                return new RunbookMetadataDocument();
            }
        }

        private static string? ExtractOverview(string? content)
        {
            if (String.IsNullOrWhiteSpace(content))
                return content;
            ParseMetadataDocument(content, out string overviewMarkdown);
            return overviewMarkdown;
        }

        private static List<RunbookStep> DeriveStepsFromMarkdown(string markdown, string fallbackTitle)
        {
            List<RunbookStep> headingSteps = DeriveHeadingSteps(markdown);
            if (headingSteps.Count > 0)
                return headingSteps;

            List<RunbookStep> listSteps = DeriveListSteps(markdown);
            if (listSteps.Count > 0)
                return listSteps;

            return new List<RunbookStep>
            {
                new RunbookStep
                {
                    Title = fallbackTitle,
                    Instructions = markdown
                }
            };
        }

        private static List<RunbookStep> DeriveHeadingSteps(string markdown)
        {
            List<RunbookStep> steps = new List<RunbookStep>();
            string[] lines = (markdown ?? String.Empty).Replace("\r\n", "\n").Split('\n');
            string? currentTitle = null;
            StringBuilder currentBody = new StringBuilder();

            foreach (string line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
                {
                    if (!String.IsNullOrWhiteSpace(currentTitle))
                    {
                        steps.Add(new RunbookStep
                        {
                            Title = currentTitle,
                            Instructions = currentBody.ToString().Trim()
                        });
                    }

                    currentTitle = line.TrimStart('#', ' ').Trim();
                    currentBody = new StringBuilder();
                    continue;
                }

                if (!String.IsNullOrWhiteSpace(currentTitle))
                    currentBody.AppendLine(line);
            }

            if (!String.IsNullOrWhiteSpace(currentTitle))
            {
                steps.Add(new RunbookStep
                {
                    Title = currentTitle,
                    Instructions = currentBody.ToString().Trim()
                });
            }

            return steps;
        }

        private static List<RunbookStep> DeriveListSteps(string markdown)
        {
            List<RunbookStep> steps = new List<RunbookStep>();
            string[] lines = (markdown ?? String.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                Match numberedMatch = _NumberedStepPattern.Match(line);
                if (numberedMatch.Success)
                {
                    steps.Add(new RunbookStep
                    {
                        Title = numberedMatch.Groups[1].Value.Trim(),
                        Instructions = numberedMatch.Groups[1].Value.Trim()
                    });
                    continue;
                }

                Match checkboxMatch = _CheckboxStepPattern.Match(line);
                if (checkboxMatch.Success)
                {
                    steps.Add(new RunbookStep
                    {
                        Title = checkboxMatch.Groups[1].Value.Trim(),
                        Instructions = checkboxMatch.Groups[1].Value.Trim()
                    });
                }
            }

            return steps;
        }

        private static List<RunbookParameter> DetectParameters(string markdown, List<RunbookStep> steps)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<RunbookParameter> parameters = new List<RunbookParameter>();

            foreach (Match match in _ParameterPattern.Matches(markdown ?? String.Empty))
            {
                string name = match.Groups[1].Value.Trim();
                if (names.Add(name))
                    parameters.Add(new RunbookParameter { Name = name, Label = name, Required = true });
            }

            foreach (RunbookStep step in steps)
            {
                foreach (Match match in _ParameterPattern.Matches(step.Title + Environment.NewLine + step.Instructions))
                {
                    string name = match.Groups[1].Value.Trim();
                    if (names.Add(name))
                        parameters.Add(new RunbookParameter { Name = name, Label = name, Required = true });
                }
            }

            return parameters;
        }

        private static Dictionary<string, string> ResolveParameterValues(
            Runbook runbook,
            Dictionary<string, string>? requestedValues)
        {
            Dictionary<string, string> values = requestedValues != null
                ? new Dictionary<string, string>(requestedValues
                    .Where(item => !String.IsNullOrWhiteSpace(item.Key))
                    .ToDictionary(item => item.Key.Trim(), item => item.Value ?? String.Empty, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (RunbookParameter parameter in runbook.Parameters)
            {
                if (!values.TryGetValue(parameter.Name, out string? value) || String.IsNullOrWhiteSpace(value))
                {
                    if (!String.IsNullOrWhiteSpace(parameter.DefaultValue))
                    {
                        values[parameter.Name] = parameter.DefaultValue;
                        continue;
                    }

                    if (parameter.Required)
                        throw new InvalidOperationException("Runbook parameter " + parameter.Name + " is required.");
                }
            }

            return values;
        }

        private static List<RunbookParameter> NormalizeParameters(List<RunbookParameter> parameters)
        {
            List<RunbookParameter> normalized = new List<RunbookParameter>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RunbookParameter parameter in parameters ?? new List<RunbookParameter>())
            {
                if (parameter == null)
                    continue;

                string? name = Normalize(parameter.Name);
                if (String.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;

                normalized.Add(new RunbookParameter
                {
                    Name = name,
                    Label = Normalize(parameter.Label) ?? name,
                    Description = Normalize(parameter.Description),
                    DefaultValue = Normalize(parameter.DefaultValue),
                    Required = parameter.Required
                });
            }

            return normalized;
        }

        private static List<RunbookStep> NormalizeSteps(List<RunbookStep> steps)
        {
            List<RunbookStep> normalized = new List<RunbookStep>();
            foreach (RunbookStep step in steps ?? new List<RunbookStep>())
            {
                if (step == null)
                    continue;

                string? title = Normalize(step.Title);
                if (String.IsNullOrWhiteSpace(title))
                    continue;

                normalized.Add(new RunbookStep
                {
                    Id = Normalize(step.Id) ?? Constants.IdGenerator.GenerateKSortable("rbs_", 24),
                    Title = title,
                    Instructions = step.Instructions ?? String.Empty
                });
            }

            return normalized;
        }

        private static string PathToTitle(string fileName)
        {
            string normalized = Normalize(fileName) ?? "Runbook";
            string withoutExtension = normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(0, normalized.Length - 3)
                : normalized;
            return withoutExtension.Replace('_', ' ').Replace('-', ' ').Trim();
        }

        private static bool ContainsIgnoreCase(string? value, string? search)
        {
            if (String.IsNullOrWhiteSpace(value) || String.IsNullOrWhiteSpace(search))
                return false;
            return value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class RunbookMetadataDocument
        {
            public string? Title { get; set; } = null;
            public string? WorkflowProfileId { get; set; } = null;
            public string? EnvironmentId { get; set; } = null;
            public string? EnvironmentName { get; set; } = null;
            public CheckRunTypeEnum? DefaultCheckType { get; set; } = null;
            public List<RunbookParameter>? Parameters { get; set; } = null;
            public List<RunbookStep>? Steps { get; set; } = null;
        }
    }
}
