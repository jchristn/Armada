namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Event-backed incident records tied to the existing deployment and release lifecycle.
    /// </summary>
    public class IncidentService
    {
        /// <summary>
        /// Optional callback invoked whenever an incident changes.
        /// </summary>
        public Action<Incident>? OnIncidentChanged { get; set; }

        private readonly DatabaseDriver _Database;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public IncidentService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Enumerate incidents visible to the caller.
        /// </summary>
        public async Task<EnumerationResult<Incident>> EnumerateAsync(
            AuthContext auth,
            IncidentQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) throw new ArgumentNullException(nameof(query));

            // Paged, filtered, and counted in the database, inside the caller's scope.
            ApplyScope(auth, query);
            return await _Database.Incidents.EnumerateAsync(query, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one incident.
        /// </summary>
        public async Task<Incident?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            return await _Database.Incidents.ReadAsync(id, BuildScope(auth), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create an incident.
        /// </summary>
        public async Task<Incident> CreateAsync(AuthContext auth, IncidentUpsertRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            CollectionBounds.Validate(request);

            Incident incident = new Incident
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.UserId,
                Title = NormalizeRequired(request.Title, nameof(request.Title)),
                Summary = Normalize(request.Summary),
                Status = request.Status ?? IncidentStatusEnum.Open,
                Severity = request.Severity ?? IncidentSeverityEnum.High,
                EnvironmentId = Normalize(request.EnvironmentId),
                EnvironmentName = Normalize(request.EnvironmentName),
                DeploymentId = Normalize(request.DeploymentId),
                ReleaseId = Normalize(request.ReleaseId),
                VesselId = Normalize(request.VesselId),
                MissionId = Normalize(request.MissionId),
                VoyageId = Normalize(request.VoyageId),
                RollbackDeploymentId = Normalize(request.RollbackDeploymentId),
                Impact = Normalize(request.Impact),
                RootCause = Normalize(request.RootCause),
                RecoveryNotes = Normalize(request.RecoveryNotes),
                Postmortem = Normalize(request.Postmortem),
                FailureKind = Normalize(request.FailureKind),
                RecoveryAttempts = request.RecoveryAttempts.HasValue ? Math.Max(0, request.RecoveryAttempts.Value) : 0,
                RescueMissionIds = DistinctIds(request.RescueMissionIds),
                DetectedUtc = request.DetectedUtc?.ToUniversalTime() ?? DateTime.UtcNow,
                MitigatedUtc = request.MitigatedUtc?.ToUniversalTime(),
                ClosedUtc = request.ClosedUtc?.ToUniversalTime(),
                LastUpdateUtc = DateTime.UtcNow
            };

            ApplyLifecycleTimestamps(incident);
            CollectionBounds.Validate(incident);
            await _Database.Incidents.CreateAsync(incident, token).ConfigureAwait(false);
            await WriteSnapshotAsync(auth, incident, token).ConfigureAwait(false);
            OnIncidentChanged?.Invoke(incident);
            return incident;
        }

        /// <summary>
        /// Update an incident.
        /// </summary>
        public async Task<Incident> UpdateAsync(AuthContext auth, string id, IncidentUpsertRequest request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));
            CollectionBounds.Validate(request);

            Incident incident = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Incident not found.");

            incident.Title = Normalize(request.Title) ?? incident.Title;
            incident.Summary = request.Summary != null ? Normalize(request.Summary) : incident.Summary;
            incident.Status = request.Status ?? incident.Status;
            incident.Severity = request.Severity ?? incident.Severity;
            incident.EnvironmentId = request.EnvironmentId != null ? Normalize(request.EnvironmentId) : incident.EnvironmentId;
            incident.EnvironmentName = request.EnvironmentName != null ? Normalize(request.EnvironmentName) : incident.EnvironmentName;
            incident.DeploymentId = request.DeploymentId != null ? Normalize(request.DeploymentId) : incident.DeploymentId;
            incident.ReleaseId = request.ReleaseId != null ? Normalize(request.ReleaseId) : incident.ReleaseId;
            incident.VesselId = request.VesselId != null ? Normalize(request.VesselId) : incident.VesselId;
            incident.MissionId = request.MissionId != null ? Normalize(request.MissionId) : incident.MissionId;
            incident.VoyageId = request.VoyageId != null ? Normalize(request.VoyageId) : incident.VoyageId;
            incident.RollbackDeploymentId = request.RollbackDeploymentId != null ? Normalize(request.RollbackDeploymentId) : incident.RollbackDeploymentId;
            incident.Impact = request.Impact != null ? Normalize(request.Impact) : incident.Impact;
            incident.RootCause = request.RootCause != null ? Normalize(request.RootCause) : incident.RootCause;
            incident.RecoveryNotes = request.RecoveryNotes != null ? Normalize(request.RecoveryNotes) : incident.RecoveryNotes;
            incident.Postmortem = request.Postmortem != null ? Normalize(request.Postmortem) : incident.Postmortem;
            incident.FailureKind = request.FailureKind != null ? Normalize(request.FailureKind) : incident.FailureKind;
            incident.RecoveryAttempts = request.RecoveryAttempts.HasValue ? Math.Max(0, request.RecoveryAttempts.Value) : incident.RecoveryAttempts;
            incident.RescueMissionIds = request.RescueMissionIds != null ? DistinctIds(request.RescueMissionIds) : incident.RescueMissionIds;
            incident.MitigatedUtc = request.MitigatedUtc.HasValue ? request.MitigatedUtc.Value.ToUniversalTime() : incident.MitigatedUtc;
            incident.ClosedUtc = request.ClosedUtc.HasValue ? request.ClosedUtc.Value.ToUniversalTime() : incident.ClosedUtc;
            incident.LastUpdateUtc = DateTime.UtcNow;

            ApplyLifecycleTimestamps(incident);
            CollectionBounds.Validate(incident);
            await _Database.Incidents.UpdateAsync(incident, token).ConfigureAwait(false);
            await WriteSnapshotAsync(auth, incident, token).ConfigureAwait(false);
            OnIncidentChanged?.Invoke(incident);
            return incident;
        }

        /// <summary>
        /// Delete an incident and all of its snapshots.
        /// </summary>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Incident? existing = await ReadAsync(auth, id, token).ConfigureAwait(false);
            if (existing == null)
                throw new KeyNotFoundException("Incident not found.");

            await _Database.Incidents.DeleteAsync(id, token).ConfigureAwait(false);
            List<ArmadaEvent> snapshots = await ReadIncidentSnapshotEventsAsync(auth, id, token).ConfigureAwait(false);
            foreach (ArmadaEvent snapshot in snapshots)
            {
                await DeleteEventAsync(auth, snapshot.Id, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// One-time import of incidents recorded before the incidents table existed: when the table is empty, the latest
        /// incident.snapshot event of each incident becomes its row (a newer snapshot wins; equal timestamps fall back to
        /// the larger event id). Later calls do nothing.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of incidents imported.</returns>
        public async Task<int> ImportLegacySnapshotsAsync(CancellationToken token = default)
        {
            if (await _Database.Incidents.CountAsync(token).ConfigureAwait(false) > 0) return 0;
            AuthContext system = new AuthContext { IsAuthenticated = true, IsAdmin = true, AuthMethod = "system" };
            List<Incident> incidents = await ReadLatestSnapshotsAsync(system, token).ConfigureAwait(false);
            foreach (Incident incident in incidents)
            {
                if (await _Database.Incidents.ReadAsync(incident.Id, null, token).ConfigureAwait(false) == null)
                    await _Database.Incidents.CreateAsync(incident, token).ConfigureAwait(false);
            }

            return incidents.Count;
        }

        private async Task<List<Incident>> ReadLatestSnapshotsAsync(AuthContext auth, CancellationToken token)
        {
            List<ArmadaEvent> snapshots = await ReadIncidentSnapshotEventsAsync(auth, null, token).ConfigureAwait(false);
            Dictionary<string, ArmadaEvent> latestByIncidentId = new Dictionary<string, ArmadaEvent>(StringComparer.OrdinalIgnoreCase);

            foreach (ArmadaEvent snapshot in snapshots)
            {
                if (String.IsNullOrWhiteSpace(snapshot.EntityId))
                    continue;

                if (!latestByIncidentId.TryGetValue(snapshot.EntityId, out ArmadaEvent? existing)
                    || IsSnapshotNewer(snapshot, existing))
                {
                    latestByIncidentId[snapshot.EntityId] = snapshot;
                }
            }

            List<Incident> incidents = new List<Incident>();
            foreach (ArmadaEvent snapshot in latestByIncidentId.Values)
            {
                Incident? incident = DeserializeIncident(snapshot);
                if (incident != null)
                    incidents.Add(incident);
            }

            return incidents;
        }

        private async Task<List<ArmadaEvent>> ReadIncidentSnapshotEventsAsync(
            AuthContext auth,
            string? incidentId,
            CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(incidentId))
            {
                if (auth.IsAdmin)
                    return await _Database.Events.EnumerateByEntityAsync("incident", incidentId, 500, token).ConfigureAwait(false);
                if (auth.IsTenantAdmin)
                    return await _Database.Events.EnumerateByEntityAsync(auth.TenantId!, "incident", incidentId, 500, token).ConfigureAwait(false);
                return (await _Database.Events.EnumerateAsync(auth.TenantId!, auth.UserId!, new EnumerationQuery
                {
                    PageNumber = 1,
                    PageSize = 500
                }, token).ConfigureAwait(false)).Objects
                    .Where(item =>
                        String.Equals(item.EntityType, "incident", StringComparison.OrdinalIgnoreCase)
                        && String.Equals(item.EntityId, incidentId, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(item.EventType, "incident.snapshot", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            EnumerationQuery query = new EnumerationQuery
            {
                PageNumber = 1,
                PageSize = 500,
                EventType = "incident.snapshot"
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

                results.AddRange(page.Objects.Where(item => String.Equals(item.EntityType, "incident", StringComparison.OrdinalIgnoreCase)));
                if (page.Objects.Count < query.PageSize)
                    break;
                query.PageNumber += 1;
            }

            return results;
        }

        private async Task WriteSnapshotAsync(AuthContext auth, Incident incident, CancellationToken token)
        {
            ArmadaEvent snapshot = new ArmadaEvent("incident.snapshot", incident.Title)
            {
                TenantId = incident.TenantId,
                UserId = auth.UserId,
                EntityType = "incident",
                EntityId = incident.Id,
                MissionId = incident.MissionId,
                VesselId = incident.VesselId,
                VoyageId = incident.VoyageId,
                Payload = JsonSerializer.Serialize(incident, _JsonOptions),
                CreatedUtc = incident.LastUpdateUtc
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

        private static Incident? DeserializeIncident(ArmadaEvent snapshot)
        {
            if (String.IsNullOrWhiteSpace(snapshot.Payload))
                return null;

            Incident? incident = JsonSerializer.Deserialize<Incident>(snapshot.Payload, _JsonOptions);
            if (incident == null)
                return null;

            incident.TenantId = incident.TenantId ?? snapshot.TenantId;
            incident.UserId = incident.UserId ?? snapshot.UserId;
            incident.LastUpdateUtc = incident.LastUpdateUtc == default ? snapshot.CreatedUtc : incident.LastUpdateUtc;
            return incident;
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

        private static void ApplyLifecycleTimestamps(Incident incident)
        {
            if (incident.Status == IncidentStatusEnum.Mitigated && !incident.MitigatedUtc.HasValue)
                incident.MitigatedUtc = DateTime.UtcNow;
            if ((incident.Status == IncidentStatusEnum.Closed || incident.Status == IncidentStatusEnum.RolledBack)
                && !incident.ClosedUtc.HasValue)
            {
                incident.ClosedUtc = DateTime.UtcNow;
            }
        }

        private static void ApplyScope(AuthContext auth, IncidentQuery query)
        {
            if (auth.IsAdmin) return;
            query.TenantId = auth.TenantId;
            if (!auth.IsTenantAdmin) query.UserId = auth.UserId;
        }

        private static IncidentQuery? BuildScope(AuthContext auth)
        {
            if (auth.IsAdmin) return null;
            IncidentQuery scope = new IncidentQuery();
            ApplyScope(auth, scope);
            return scope;
        }

        private static List<string> DistinctIds(List<string>? ids)
        {
            if (ids == null) return new List<string>();
            return ids.Where(item => !String.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.Ordinal).ToList();
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string NormalizeRequired(string? value, string parameterName)
        {
            string? normalized = Normalize(value);
            if (String.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException(parameterName + " is required.");
            return normalized;
        }
    }
}
