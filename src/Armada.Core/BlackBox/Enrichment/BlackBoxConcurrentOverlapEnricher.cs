namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Concurrent overlap: other missions on the vessel that were active during this mission's run window (started
    /// before it ended and not finished before it started) and touched the same files, whether or not they landed. Their
    /// paths come from their diff snapshot, or from their branch on the owning host (start commit or merge base to the
    /// branch tip, at most <see cref="MaxGitLookups"/> branches). Each becomes a Conflict.ConcurrentMissions entry with an
    /// OverlappingFiles link. Missions already attributed as landed conflicting missions are not repeated.
    /// </summary>
    public class BlackBoxConcurrentOverlapEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <summary>
        /// Most candidate missions read for one entry.
        /// </summary>
        public const int MaxCandidates = 500;

        /// <summary>
        /// Most branch reads (missions without a diff snapshot) for one entry.
        /// </summary>
        public const int MaxGitLookups = 10;

        /// <inheritdoc />
        public override string Name => "ConcurrentOverlap";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly VesselGitInspector _Inspector;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="inspector">Git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxConcurrentOverlapEnricher(DatabaseDriver database, VesselGitInspector inspector, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            Mission? mission = context.Mission;
            if (context.Entry.SubjectType != BlackBoxSubjectTypeEnum.Mission || mission == null || String.IsNullOrEmpty(mission.VesselId)) return;
            BlackBoxForensicState state = await BlackBoxForensics.EnsureStateAsync(context, _Database, _Inspector, token).ConfigureAwait(false);
            BlackBoxRecord record = context.Record;
            HashSet<string> conflicted = new HashSet<string>((record.Conflict?.ConflictedFiles ?? new List<string>()).Select(BlackBoxForensics.NormalizePath).Where(p => p.Length > 0), StringComparer.Ordinal);
            if (conflicted.Count == 0 && state.MissionChangedPaths.Count == 0)
            {
                BlackBoxRecordBounder.AddNote(record.Truncation, "Concurrent overlap skipped: neither the conflicted files nor the mission's changed files are known.");
                return;
            }

            DateTime windowStart = mission.StartedUtc ?? mission.CreatedUtc;
            DateTime windowEnd = mission.CompletedUtc ?? context.Entry.CreatedUtc;
            if (windowEnd < windowStart) windowEnd = windowStart;
            HashSet<string> attributed = new HashSet<string>((record.Conflict?.ConflictingMissions ?? new List<BlackBoxConflictingMission>()).Where(m => m.MissionId != null).Select(m => m.MissionId!), StringComparer.Ordinal);

            List<Mission> candidates = await ReadCandidatesAsync(mission, windowStart, token).ConfigureAwait(false);
            List<BlackBoxConflictingMission> found = new List<BlackBoxConflictingMission>();
            int gitLookups = 0;
            int skippedLookups = 0;
            foreach (Mission other in candidates)
            {
                if (other.Id == mission.Id || attributed.Contains(other.Id)) continue;
                if (context.Entry.TenantId != null && other.TenantId != context.Entry.TenantId) continue;
                if (!WasActiveDuring(other, windowStart, windowEnd)) continue;

                List<string>? paths = null;
                if (!String.IsNullOrEmpty(other.DiffSnapshot)) paths = BlackBoxForensics.DiffPaths(other.DiffSnapshot);
                else if (state.Host != null && state.GitPath != null && !String.IsNullOrEmpty(other.BranchName) && GitRevisionNames.IsSafeBranchName(other.BranchName))
                {
                    if (gitLookups >= MaxGitLookups) { skippedLookups++; continue; }
                    gitLookups++;
                    paths = await BranchPathsAsync(state, other, token).ConfigureAwait(false);
                }
                if (paths == null || paths.Count == 0) continue;

                List<string> overlap = BlackBoxForensics.Overlap(paths, conflicted, state.MissionChangedPaths);
                if (overlap.Count == 0) continue;
                found.Add(new BlackBoxConflictingMission
                {
                    MissionId = other.Id,
                    Title = BlackBoxLimits.Clip(other.Title, BlackBoxLimits.MaxTitleChars),
                    CommitHash = other.CommitHash,
                    LandedUtc = other.Status == MissionStatusEnum.Complete ? other.CompletedUtc : null,
                    OverlappingFileTotal = overlap.Count,
                    OverlappingFiles = overlap.Take(BlackBoxLimits.MaxOverlappingFiles).ToList()
                });
                context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, other.Id, BlackBoxLinkRelationEnum.OverlappingFiles, other.CommitHash, overlap.Count));
                foreach (string p in overlap.Take(BlackBoxForensics.MaxFileRowsPerRole)) context.Files.Add(BlackBoxForensics.FileRow(context.Entry, p, BlackBoxFileRoleEnum.ChangedByConflictingMission, other.Id));
            }

            BlackBoxConflict c = record.Conflict ?? new BlackBoxConflict();
            record.Conflict = c;
            List<BlackBoxConflictingMission> ordered = found.OrderByDescending(m => m.OverlappingFileTotal).ThenBy(m => m.MissionId, StringComparer.Ordinal).ToList();
            if (ordered.Count > BlackBoxLimits.MaxConflictingMissions)
                BlackBoxRecordBounder.AddField(record.Truncation, "Conflict.ConcurrentMissions", ordered.Count, BlackBoxLimits.MaxConflictingMissions, "items");
            c.ConcurrentMissions = ordered.Take(BlackBoxLimits.MaxConflictingMissions).ToList();
            if (skippedLookups > 0)
                BlackBoxRecordBounder.AddNote(record.Truncation, "Concurrent overlap read the branches of " + MaxGitLookups + " missions without a diff snapshot; " + skippedLookups + " more were not compared.");
        }

        private async Task<List<Mission>> ReadCandidatesAsync(Mission mission, DateTime windowStart, CancellationToken token)
        {
            List<Mission> candidates = new List<Mission>();
            EnumerationQuery query = new EnumerationQuery { VesselId = mission.VesselId, PageSize = 100, PageNumber = 1, Order = EnumerationOrderEnum.CreatedDescending };
            while (candidates.Count < MaxCandidates)
            {
                EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(query, token).ConfigureAwait(false);
                candidates.AddRange(page.Objects);
                if (page.Objects.Count < query.PageSize || query.PageNumber >= page.TotalPages) break;
                query.PageNumber++;
            }
            return candidates.Take(MaxCandidates).ToList();
        }

        private static bool WasActiveDuring(Mission other, DateTime windowStart, DateTime windowEnd)
        {
            DateTime? start = other.StartedUtc;
            if (start == null)
            {
                if (other.Status == MissionStatusEnum.Pending) return false;
                start = other.CreatedUtc;
            }
            DateTime end = other.CompletedUtc ?? (VesselGitInspector.ActiveMissionStatuses.Contains(other.Status) ? DateTime.MaxValue : other.LastUpdateUtc);
            return start.Value <= windowEnd && end >= windowStart;
        }

        private async Task<List<string>?> BranchPathsAsync(BlackBoxForensicState state, Mission other, CancellationToken token)
        {
            DockHost host = state.Host!;
            string gitPath = state.GitPath!;
            string branch = other.BranchName!;
            string? baseRef = other.StartCommit;
            if (baseRef == null && state.TargetBranch != null)
                baseRef = await BlackBoxForensics.TryAsync(() => host.Git.GetMergeBaseAsync(gitPath, state.TargetBranch, branch, token), (string?)null).ConfigureAwait(false);
            if (baseRef == null) return null;
            GitCommitRange? range = await BlackBoxForensics.TryAsync(() => host.Git.GetCommitRangeAsync(gitPath, baseRef, branch, 100, token), (GitCommitRange?)null).ConfigureAwait(false);
            if (range == null) return null;
            return range.Commits.SelectMany(c => c.ChangedPaths).Distinct(StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
