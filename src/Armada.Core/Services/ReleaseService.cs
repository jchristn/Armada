namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Creates, updates, refreshes, and reads first-class release records.
    /// </summary>
    public class ReleaseService
    {
        private readonly string _Header = "[ReleaseService] ";
        private readonly DatabaseDriver _Database;
        private readonly WorkflowProfileService _WorkflowProfiles;
        private readonly LoggingModule _Logging;
        private static readonly Regex _SemanticVersionRegex = new Regex(@"(?<!\d)(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z\.-]+)?(?!\d)", RegexOptions.Compiled);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReleaseService(
            DatabaseDriver database,
            WorkflowProfileService workflowProfiles,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _WorkflowProfiles = workflowProfiles ?? throw new ArgumentNullException(nameof(workflowProfiles));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Enumerate releases within the caller scope.
        /// </summary>
        public async Task<EnumerationResult<Release>> EnumerateAsync(
            AuthContext auth,
            ReleaseQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            ReleaseQuery scopedQuery = query ?? new ReleaseQuery();
            ApplyScope(auth, scopedQuery);
            return await _Database.Releases.EnumerateAsync(scopedQuery, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one release within the caller scope.
        /// </summary>
        public async Task<Release?> ReadAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            return await _Database.Releases.ReadAsync(id, BuildScopeQuery(auth), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new release.
        /// </summary>
        public async Task<Release> CreateAsync(
            AuthContext auth,
            ReleaseUpsertRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));

            ResolvedReleaseDraft draft = await ResolveDraftAsync(auth, null, request, token).ConfigureAwait(false);
            Release release = new Release
            {
                TenantId = draft.Vessel.TenantId,
                UserId = auth.UserId,
                VesselId = draft.Vessel.Id,
                WorkflowProfileId = draft.WorkflowProfile?.Id,
                Title = draft.Title,
                Version = draft.Version,
                TagName = draft.TagName,
                Summary = draft.Summary,
                Notes = draft.Notes,
                Status = draft.Status,
                VoyageIds = draft.VoyageIds,
                MissionIds = draft.MissionIds,
                CheckRunIds = draft.CheckRunIds,
                Artifacts = draft.Artifacts,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow,
                PublishedUtc = draft.PublishedUtc
            };

            release = await _Database.Releases.CreateAsync(release, token).ConfigureAwait(false);
            _Logging.Info(_Header + "created release " + release.Id + " for vessel " + release.VesselId);
            return release;
        }

        /// <summary>
        /// Update an existing release.
        /// </summary>
        public async Task<Release> UpdateAsync(
            AuthContext auth,
            string id,
            ReleaseUpsertRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Release existing = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Release not found.");

            ResolvedReleaseDraft draft = await ResolveDraftAsync(auth, existing, request, token).ConfigureAwait(false);
            ApplyDraft(existing, draft, preserveExistingPublishedUtc: true);
            existing = await _Database.Releases.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Info(_Header + "updated release " + existing.Id);
            return existing;
        }

        /// <summary>
        /// Refresh derived release fields from linked voyages, missions, and checks.
        /// </summary>
        public async Task<Release> RefreshAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Release existing = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Release not found.");

            ReleaseUpsertRequest request = new ReleaseUpsertRequest
            {
                VesselId = existing.VesselId,
                WorkflowProfileId = existing.WorkflowProfileId,
                Title = existing.Title,
                Version = existing.Version,
                TagName = existing.TagName,
                Summary = existing.Summary,
                Notes = existing.Notes,
                Status = existing.Status,
                VoyageIds = new List<string>(existing.VoyageIds),
                MissionIds = new List<string>(existing.MissionIds),
                CheckRunIds = new List<string>(existing.CheckRunIds)
            };

            ResolvedReleaseDraft draft = await ResolveDraftAsync(auth, existing, request, token).ConfigureAwait(false);
            ApplyDraft(existing, draft, preserveExistingPublishedUtc: true);
            existing = await _Database.Releases.UpdateAsync(existing, token).ConfigureAwait(false);
            _Logging.Info(_Header + "refreshed release " + existing.Id);
            return existing;
        }

        /// <summary>
        /// Delete one release.
        /// </summary>
        public async Task DeleteAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Release? existing = await ReadAsync(auth, id, token).ConfigureAwait(false);
            if (existing == null)
                throw new KeyNotFoundException("Release not found.");

            await _Database.Releases.DeleteAsync(id, BuildScopeQuery(auth), token).ConfigureAwait(false);
            _Logging.Info(_Header + "deleted release " + id);
        }

        private async Task<ResolvedReleaseDraft> ResolveDraftAsync(
            AuthContext auth,
            Release? existing,
            ReleaseUpsertRequest request,
            CancellationToken token)
        {
            List<string> voyageIds = DistinctNormalized(request.VoyageIds);
            List<string> explicitMissionIds = DistinctNormalized(request.MissionIds);
            List<string> checkRunIds = DistinctNormalized(request.CheckRunIds);

            List<Voyage> voyages = new List<Voyage>();
            foreach (string voyageId in voyageIds)
            {
                Voyage? voyage = await ReadAccessibleVoyageAsync(auth, voyageId, token).ConfigureAwait(false);
                if (voyage == null)
                    throw new KeyNotFoundException("Voyage not found or not accessible: " + voyageId);
                voyages.Add(voyage);
            }

            List<Mission> missions = new List<Mission>();
            HashSet<string> missionIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string missionId in explicitMissionIds)
            {
                Mission? mission = await ReadAccessibleMissionAsync(auth, missionId, token).ConfigureAwait(false);
                if (mission == null)
                    throw new KeyNotFoundException("Mission not found or not accessible: " + missionId);
                if (missionIdsSeen.Add(mission.Id))
                    missions.Add(mission);
            }

            foreach (Voyage voyage in voyages)
            {
                List<Mission> voyageMissions = await ReadAccessibleMissionsForVoyageAsync(auth, voyage.Id, token).ConfigureAwait(false);
                foreach (Mission mission in voyageMissions)
                {
                    if (missionIdsSeen.Add(mission.Id))
                        missions.Add(mission);
                }
            }

            List<CheckRun> checkRuns = new List<CheckRun>();
            foreach (string checkRunId in checkRunIds)
            {
                CheckRun? run = await ReadAccessibleCheckRunAsync(auth, checkRunId, token).ConfigureAwait(false);
                if (run == null)
                    throw new KeyNotFoundException("Check run not found or not accessible: " + checkRunId);
                checkRuns.Add(run);
            }

            string? requestedVesselId = Normalize(request.VesselId);
            string? fallbackExistingVesselId = requestedVesselId == null ? Normalize(existing?.VesselId) : null;
            HashSet<string> candidateVesselIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidateVesselIds, requestedVesselId);
            AddCandidate(candidateVesselIds, fallbackExistingVesselId);

            foreach (Mission mission in missions)
                AddCandidate(candidateVesselIds, mission.VesselId);
            foreach (CheckRun run in checkRuns)
                AddCandidate(candidateVesselIds, run.VesselId);

            if (candidateVesselIds.Count == 0)
                throw new InvalidOperationException("A vessel must be supplied or inferable from linked missions or check runs.");
            if (candidateVesselIds.Count > 1)
                throw new InvalidOperationException("Linked voyages, missions, and checks must all belong to a single vessel.");

            string resolvedVesselId = candidateVesselIds.First();
            Vessel vessel = await ReadAccessibleVesselAsync(auth, resolvedVesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Vessel not found or not accessible.");

            foreach (Mission mission in missions)
            {
                if (!String.Equals(mission.VesselId, vessel.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Mission " + mission.Id + " does not belong to vessel " + vessel.Id + ".");
            }

            foreach (CheckRun run in checkRuns)
            {
                if (!String.Equals(run.VesselId, vessel.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Check run " + run.Id + " does not belong to vessel " + vessel.Id + ".");
            }

            string? explicitWorkflowProfileId = Normalize(request.WorkflowProfileId) ?? Normalize(existing?.WorkflowProfileId);
            WorkflowProfile? workflowProfile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, explicitWorkflowProfileId, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(explicitWorkflowProfileId) && workflowProfile == null)
                throw new KeyNotFoundException("Workflow profile not found or not accessible.");

            List<ReleaseArtifact> artifacts = BuildArtifacts(checkRuns);
            string version = await ResolveVersionAsync(auth, vessel, checkRuns, request, existing, token).ConfigureAwait(false);
            string tagName = Normalize(request.TagName)
                ?? Normalize(existing?.TagName)
                ?? ("v" + version);
            string title = Normalize(request.Title)
                ?? Normalize(existing?.Title)
                ?? (vessel.Name + " " + version);
            string summary = Normalize(request.Summary)
                ?? Normalize(existing?.Summary)
                ?? BuildSummary(vessel, voyages, missions, checkRuns, version);
            string notes = Normalize(request.Notes)
                ?? Normalize(existing?.Notes)
                ?? BuildNotes(vessel, voyages, missions, checkRuns, artifacts, version, summary);
            ReleaseStatusEnum status = request.Status ?? existing?.Status ?? ReleaseStatusEnum.Draft;
            DateTime? publishedUtc = existing?.PublishedUtc;
            if (status == ReleaseStatusEnum.Shipped && !publishedUtc.HasValue)
                publishedUtc = DateTime.UtcNow;

            return new ResolvedReleaseDraft
            {
                Vessel = vessel,
                WorkflowProfile = workflowProfile,
                VoyageIds = voyageIds,
                MissionIds = missions.Select(mission => mission.Id).ToList(),
                CheckRunIds = checkRunIds,
                Artifacts = artifacts,
                Version = version,
                TagName = tagName,
                Title = title,
                Summary = summary,
                Notes = notes,
                Status = status,
                PublishedUtc = publishedUtc
            };
        }

        private static void ApplyDraft(Release release, ResolvedReleaseDraft draft, bool preserveExistingPublishedUtc)
        {
            release.VesselId = draft.Vessel.Id;
            release.WorkflowProfileId = draft.WorkflowProfile?.Id;
            release.Title = draft.Title;
            release.Version = draft.Version;
            release.TagName = draft.TagName;
            release.Summary = draft.Summary;
            release.Notes = draft.Notes;
            release.Status = draft.Status;
            release.VoyageIds = draft.VoyageIds;
            release.MissionIds = draft.MissionIds;
            release.CheckRunIds = draft.CheckRunIds;
            release.Artifacts = draft.Artifacts;
            release.LastUpdateUtc = DateTime.UtcNow;
            if (!preserveExistingPublishedUtc || !release.PublishedUtc.HasValue)
                release.PublishedUtc = draft.PublishedUtc;
        }

        private async Task<string> ResolveVersionAsync(
            AuthContext auth,
            Vessel vessel,
            List<CheckRun> checkRuns,
            ReleaseUpsertRequest request,
            Release? existing,
            CancellationToken token)
        {
            string? explicitVersion = Normalize(request.Version) ?? Normalize(existing?.Version);
            if (!String.IsNullOrWhiteSpace(explicitVersion))
                return explicitVersion;

            string? derivedVersion = ExtractVersionFromCheckRuns(checkRuns);
            if (!String.IsNullOrWhiteSpace(derivedVersion))
                return derivedVersion;

            ReleaseQuery releaseQuery = BuildScopeQuery(auth);
            releaseQuery.VesselId = vessel.Id;

            List<Release> existingReleases = await _Database.Releases.EnumerateAllAsync(releaseQuery, token).ConfigureAwait(false);
            SemanticVersionValue? latest = null;
            foreach (Release release in existingReleases)
            {
                if (existing != null && String.Equals(existing.Id, release.Id, StringComparison.Ordinal))
                    continue;

                SemanticVersionValue? candidate = ParseSemanticVersion(release.Version);
                if (candidate == null)
                    continue;

                if (latest == null || candidate.CompareTo(latest) > 0)
                    latest = candidate;
            }

            if (latest == null)
                return "0.1.0";

            latest.Patch += 1;
            return latest.ToString();
        }

        private static string? ExtractVersionFromCheckRuns(List<CheckRun> checkRuns)
        {
            IEnumerable<CheckRun> ordered = checkRuns
                .Where(run => run.Type == CheckRunTypeEnum.ReleaseVersioning)
                .OrderByDescending(run => run.CompletedUtc ?? run.LastUpdateUtc);

            foreach (CheckRun run in ordered)
            {
                string? summaryVersion = ExtractSemanticVersion(run.Summary);
                if (!String.IsNullOrWhiteSpace(summaryVersion))
                    return summaryVersion;

                string? outputVersion = ExtractSemanticVersion(run.Output);
                if (!String.IsNullOrWhiteSpace(outputVersion))
                    return outputVersion;
            }

            return null;
        }

        private static string ExtractSemanticVersion(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return String.Empty;

            Match match = _SemanticVersionRegex.Match(value);
            if (!match.Success)
                return String.Empty;

            return match.Value;
        }

        private static SemanticVersionValue? ParseSemanticVersion(string? value)
        {
            string normalized = ExtractSemanticVersion(value);
            if (String.IsNullOrWhiteSpace(normalized))
                return null;

            string[] parts = normalized.Split('.');
            if (parts.Length < 3)
                return null;

            if (!Int32.TryParse(parts[0], out int major))
                return null;
            if (!Int32.TryParse(parts[1], out int minor))
                return null;

            string patchText = parts[2];
            int patchStop = 0;
            while (patchStop < patchText.Length && Char.IsDigit(patchText[patchStop]))
                patchStop += 1;
            if (patchStop == 0)
                return null;
            if (!Int32.TryParse(patchText.Substring(0, patchStop), out int patch))
                return null;

            return new SemanticVersionValue
            {
                Major = major,
                Minor = minor,
                Patch = patch
            };
        }

        private static List<ReleaseArtifact> BuildArtifacts(List<CheckRun> checkRuns)
        {
            List<ReleaseArtifact> artifacts = new List<ReleaseArtifact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CheckRun run in checkRuns)
            {
                foreach (CheckRunArtifact artifact in run.Artifacts ?? new List<CheckRunArtifact>())
                {
                    if (String.IsNullOrWhiteSpace(artifact.Path))
                        continue;

                    string key = run.Id + "|" + artifact.Path;
                    if (!seen.Add(key))
                        continue;

                    artifacts.Add(new ReleaseArtifact
                    {
                        SourceType = "CheckRun",
                        SourceId = run.Id,
                        Path = artifact.Path,
                        SizeBytes = artifact.SizeBytes ?? 0,
                        LastWriteUtc = artifact.LastWriteUtc
                    });
                }
            }

            return artifacts;
        }

        private static string BuildSummary(
            Vessel vessel,
            List<Voyage> voyages,
            List<Mission> missions,
            List<CheckRun> checkRuns,
            string version)
        {
            List<string> fragments = new List<string>();
            if (voyages.Count > 0)
                fragments.Add(voyages.Count + " voyage(s)");
            if (missions.Count > 0)
                fragments.Add(missions.Count + " mission(s)");
            if (checkRuns.Count > 0)
                fragments.Add(checkRuns.Count + " check run(s)");

            if (fragments.Count == 0)
                return "Release " + version + " for " + vessel.Name + ".";

            return "Release " + version + " for " + vessel.Name + " bundles " + JoinWithAnd(fragments) + ".";
        }

        private static string BuildNotes(
            Vessel vessel,
            List<Voyage> voyages,
            List<Mission> missions,
            List<CheckRun> checkRuns,
            List<ReleaseArtifact> artifacts,
            string version,
            string summary)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Release " + version + " for " + vessel.Name);
            builder.AppendLine();
            builder.AppendLine(summary);

            if (voyages.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Voyages");
                foreach (Voyage voyage in voyages)
                    builder.AppendLine("- " + voyage.Id + ": " + voyage.Title);
            }

            if (missions.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Missions");
                foreach (Mission mission in missions.OrderBy(mission => mission.Title, StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine("- " + mission.Id + ": " + mission.Title);
            }

            List<string> pullRequests = missions
                .Select(mission => Normalize(mission.PrUrl))
                .Where(url => !String.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
            if (pullRequests.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Pull Requests");
                foreach (string pullRequest in pullRequests)
                    builder.AppendLine("- " + pullRequest);
            }

            if (checkRuns.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Checks");
                foreach (CheckRun run in checkRuns.OrderBy(run => run.Type.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    string label = !String.IsNullOrWhiteSpace(run.Label) ? run.Label! : run.Type.ToString();
                    string detail = !String.IsNullOrWhiteSpace(run.Summary) ? run.Summary! : run.Status.ToString();
                    builder.AppendLine("- " + label + " [" + run.Status + "]: " + detail);
                }
            }

            if (artifacts.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Artifacts");
                foreach (ReleaseArtifact artifact in artifacts.OrderBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine("- " + artifact.Path + " (" + artifact.SizeBytes + " bytes)");
            }

            return builder.ToString().Trim();
        }

        private async Task<Vessel?> ReadAccessibleVesselAsync(AuthContext auth, string vesselId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Vessels.ReadAsync(auth.TenantId!, vesselId, token).ConfigureAwait(false);
            return await _Database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, vesselId, token).ConfigureAwait(false);
        }

        private async Task<Voyage?> ReadAccessibleVoyageAsync(AuthContext auth, string voyageId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Voyages.ReadAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);
            return await _Database.Voyages.ReadAsync(auth.TenantId!, auth.UserId!, voyageId, token).ConfigureAwait(false);
        }

        private async Task<Mission?> ReadAccessibleMissionAsync(AuthContext auth, string missionId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Missions.ReadAsync(auth.TenantId!, missionId, token).ConfigureAwait(false);
            return await _Database.Missions.ReadAsync(auth.TenantId!, auth.UserId!, missionId, token).ConfigureAwait(false);
        }

        private async Task<List<Mission>> ReadAccessibleMissionsForVoyageAsync(AuthContext auth, string voyageId, CancellationToken token)
        {
            List<Mission> missions;
            if (auth.IsAdmin)
            {
                missions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            }
            else
            {
                missions = await _Database.Missions.EnumerateByVoyageAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);
                if (!auth.IsTenantAdmin)
                {
                    missions = missions
                        .Where(mission => String.Equals(mission.UserId, auth.UserId, StringComparison.Ordinal))
                        .ToList();
                }
            }

            return missions;
        }

        private async Task<CheckRun?> ReadAccessibleCheckRunAsync(AuthContext auth, string checkRunId, CancellationToken token)
        {
            return await _Database.CheckRuns.ReadAsync(checkRunId, BuildCheckRunScopeQuery(auth), token).ConfigureAwait(false);
        }

        private static ReleaseQuery BuildScopeQuery(AuthContext auth)
        {
            ReleaseQuery query = new ReleaseQuery();
            ApplyScope(auth, query);
            return query;
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

        private static void ApplyScope(AuthContext auth, ReleaseQuery query)
        {
            if (auth.IsAdmin)
                return;

            query.TenantId = auth.TenantId;
            if (!auth.IsTenantAdmin)
                query.UserId = auth.UserId;
        }

        private static List<string> DistinctNormalized(List<string>? values)
        {
            List<string> results = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in values ?? new List<string>())
            {
                string? normalized = Normalize(value);
                if (String.IsNullOrWhiteSpace(normalized))
                    continue;
                if (seen.Add(normalized))
                    results.Add(normalized);
            }

            return results;
        }

        private static void AddCandidate(HashSet<string> set, string? value)
        {
            string? normalized = Normalize(value);
            if (!String.IsNullOrWhiteSpace(normalized))
                set.Add(normalized);
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string JoinWithAnd(List<string> values)
        {
            if (values.Count == 0)
                return String.Empty;
            if (values.Count == 1)
                return values[0];
            if (values.Count == 2)
                return values[0] + " and " + values[1];

            return String.Join(", ", values.Take(values.Count - 1)) + ", and " + values[values.Count - 1];
        }

        private sealed class ResolvedReleaseDraft
        {
            public Vessel Vessel { get; set; } = new Vessel();
            public WorkflowProfile? WorkflowProfile { get; set; } = null;
            public List<string> VoyageIds { get; set; } = new List<string>();
            public List<string> MissionIds { get; set; } = new List<string>();
            public List<string> CheckRunIds { get; set; } = new List<string>();
            public List<ReleaseArtifact> Artifacts { get; set; } = new List<ReleaseArtifact>();
            public string Version { get; set; } = "0.1.0";
            public string TagName { get; set; } = "v0.1.0";
            public string Title { get; set; } = "Draft Release";
            public string Summary { get; set; } = String.Empty;
            public string Notes { get; set; } = String.Empty;
            public ReleaseStatusEnum Status { get; set; } = ReleaseStatusEnum.Draft;
            public DateTime? PublishedUtc { get; set; } = null;
        }

        private sealed class SemanticVersionValue : IComparable<SemanticVersionValue>
        {
            public int Major { get; set; } = 0;
            public int Minor { get; set; } = 0;
            public int Patch { get; set; } = 0;

            public int CompareTo(SemanticVersionValue? other)
            {
                if (other == null)
                    return 1;
                if (Major != other.Major)
                    return Major.CompareTo(other.Major);
                if (Minor != other.Minor)
                    return Minor.CompareTo(other.Minor);
                return Patch.CompareTo(other.Patch);
            }

            public override string ToString()
            {
                return Major + "." + Minor + "." + Patch;
            }
        }
    }
}
