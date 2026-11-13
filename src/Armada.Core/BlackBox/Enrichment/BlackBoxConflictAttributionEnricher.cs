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
    using SyslogLogging;

    /// <summary>
    /// Conflict attribution: for each commit that landed on the target since the start (from target movement), the
    /// Armada-Mission-Id trailer names the mission that landed it; its changed paths are intersected with the conflicted
    /// files and with the paths the failed mission changed. Each intersecting mission becomes a conflictingMissions[]
    /// entry (newest commit, overlapping files) with a LandedOnTargetSinceStart link and ChangedByConflictingMission file
    /// rows; a commit without the trailer is reported as outside Armada with its author.
    /// </summary>
    public class BlackBoxConflictAttributionEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "ConflictAttribution";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxConflictAttributionEnricher(DatabaseDriver database, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            if (context.Entry.SubjectType != BlackBoxSubjectTypeEnum.Mission) return;
            BlackBoxForensicState state = context.Forensics;
            GitCommitRange? range = state.TargetCommits;
            if (range == null || range.Commits.Count == 0) return;
            BlackBoxRecord record = context.Record;
            BlackBoxConflict c = record.Conflict ?? new BlackBoxConflict();
            record.Conflict = c;

            HashSet<string> conflicted = new HashSet<string>(c.ConflictedFiles.Select(BlackBoxForensics.NormalizePath).Where(p => p.Length > 0), StringComparer.Ordinal);
            if (conflicted.Count == 0 && state.MissionChangedPaths.Count == 0)
            {
                BlackBoxRecordBounder.AddNote(record.Truncation, "Conflict attribution skipped: neither the conflicted files nor the mission's changed files are known.");
                return;
            }

            string? self = context.Mission?.Id ?? context.Entry.MissionId;
            Dictionary<string, BlackBoxConflictingMission> byMission = new Dictionary<string, BlackBoxConflictingMission>(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> overlapByMission = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            List<BlackBoxConflictingMission> ordered = new List<BlackBoxConflictingMission>();
            bool partialPaths = false;

            foreach (GitCommitInfo commit in range.Commits)
            {
                string? missionId = commit.MissionId;
                if (missionId != null && missionId == self) continue;
                List<string> overlap = BlackBoxForensics.Overlap(commit.ChangedPaths, conflicted, state.MissionChangedPaths);
                if (commit.ChangedPathTotal > commit.ChangedPaths.Count) partialPaths = true;
                if (overlap.Count == 0) continue;
                DateTime? landed = commit.CommittedUtc == DateTime.MinValue ? (DateTime?)null : commit.CommittedUtc;

                if (missionId == null)
                {
                    BlackBoxConflictingMission outside = new BlackBoxConflictingMission
                    {
                        IsOutsideArmada = true,
                        Author = BlackBoxForensics.AuthorOf(commit),
                        Title = BlackBoxLimits.Clip(commit.Subject, BlackBoxLimits.MaxTitleChars),
                        CommitHash = commit.Sha,
                        LandedUtc = landed,
                        OverlappingFileTotal = overlap.Count,
                        OverlappingFiles = overlap.Take(BlackBoxLimits.MaxOverlappingFiles).ToList()
                    };
                    ordered.Add(outside);
                    foreach (string p in overlap.Take(BlackBoxForensics.MaxFileRowsPerRole)) context.Files.Add(BlackBoxForensics.FileRow(context.Entry, p, BlackBoxFileRoleEnum.ChangedByConflictingMission, null));
                    continue;
                }

                if (!byMission.TryGetValue(missionId, out BlackBoxConflictingMission? cm))
                {
                    // Commits are newest first: the first commit seen for a mission is its latest landing.
                    Mission? landedMission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                    if (landedMission != null && context.Entry.TenantId != null && landedMission.TenantId != context.Entry.TenantId) landedMission = null;
                    cm = new BlackBoxConflictingMission
                    {
                        MissionId = missionId,
                        Title = BlackBoxLimits.Clip(landedMission?.Title ?? commit.Subject, BlackBoxLimits.MaxTitleChars),
                        CommitHash = commit.Sha,
                        LandedUtc = landed,
                        Author = BlackBoxForensics.AuthorOf(commit)
                    };
                    byMission[missionId] = cm;
                    overlapByMission[missionId] = new HashSet<string>(StringComparer.Ordinal);
                    ordered.Add(cm);
                }
                foreach (string p in overlap) overlapByMission[missionId].Add(p);
            }

            foreach (KeyValuePair<string, BlackBoxConflictingMission> pair in byMission)
            {
                List<string> files = overlapByMission[pair.Key].OrderBy(p => p, StringComparer.Ordinal).ToList();
                pair.Value.OverlappingFileTotal = files.Count;
                pair.Value.OverlappingFiles = files.Take(BlackBoxLimits.MaxOverlappingFiles).ToList();
                if (files.Count > BlackBoxLimits.MaxOverlappingFiles)
                    BlackBoxRecordBounder.AddField(record.Truncation, "Conflict.ConflictingMissions[" + pair.Key + "].OverlappingFiles", files.Count, BlackBoxLimits.MaxOverlappingFiles, "items");
                context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, pair.Key, BlackBoxLinkRelationEnum.LandedOnTargetSinceStart, pair.Value.CommitHash, files.Count));
                foreach (string p in files.Take(BlackBoxForensics.MaxFileRowsPerRole)) context.Files.Add(BlackBoxForensics.FileRow(context.Entry, p, BlackBoxFileRoleEnum.ChangedByConflictingMission, pair.Key));
            }

            // Missions first (they are actionable), then commits outside Armada; each group newest first.
            List<BlackBoxConflictingMission> result = ordered.Where(m => !m.IsOutsideArmada).Concat(ordered.Where(m => m.IsOutsideArmada)).ToList();
            if (result.Count > BlackBoxLimits.MaxConflictingMissions)
                BlackBoxRecordBounder.AddField(record.Truncation, "Conflict.ConflictingMissions", result.Count, BlackBoxLimits.MaxConflictingMissions, "items");
            c.ConflictingMissions = result.Take(BlackBoxLimits.MaxConflictingMissions).ToList();
            if (partialPaths) BlackBoxRecordBounder.AddNote(record.Truncation, "Some target commits changed more files than were read per commit; overlaps may be undercounted.");
        }

        #endregion
    }
}
