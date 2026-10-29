namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Target movement: the commits that landed on the target between the mission's start commit and the target tip at
    /// the failure (or the current tip, when the failure site did not record it), read on the owning host with
    /// GetCommitRangeAsync. The record keeps the newest 50 (Conflict.TargetCommitsSinceStart, with TargetCommitTotal and
    /// a truncation entry); evidence/target-commits.txt lists up to 500 with their changed paths; the full range feeds
    /// conflict attribution. Without a start commit, or when the host or the start commit cannot be reached, the
    /// movement is omitted and Workspace.TargetMovementNote says why.
    /// </summary>
    public class BlackBoxTargetMovementEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "TargetMovement";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly VesselGitInspector _Inspector;
        private const int _MaxPathsListed = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="inspector">Git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxTargetMovementEnricher(DatabaseDriver database, VesselGitInspector inspector, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            if (context.Entry.SubjectType != BlackBoxSubjectTypeEnum.Mission) return;
            BlackBoxForensicState state = await BlackBoxForensics.EnsureStateAsync(context, _Database, _Inspector, token).ConfigureAwait(false);
            BlackBoxRecord record = context.Record;
            BlackBoxWorkspace w = record.Workspace ?? new BlackBoxWorkspace();
            record.Workspace = w;

            if (state.StartCommit == null)
            {
                Omit(record, w, "Target movement omitted: the mission's start commit was not recorded, so the commits that landed on the target since it started are unknown.");
                return;
            }
            if (state.Host == null)
            {
                Omit(record, w, "Target movement omitted: " + (state.HostUnavailableReason ?? "the repository's host cannot be reached."));
                return;
            }
            if (state.GitPath == null)
            {
                Omit(record, w, "Target movement omitted: no repository is available on " + state.HostLabel + ".");
                return;
            }

            DockHost host = state.Host;
            string gitPath = state.GitPath;
            string? tip = w.TargetTipAtFailure;
            string tipSource = "the target tip at the failure";
            if (tip == null)
            {
                if (state.TargetBranch == null || !GitRevisionNames.IsSafeBranchName(state.TargetBranch))
                {
                    Omit(record, w, "Target movement omitted: neither the target tip at the failure nor the target branch is known.");
                    return;
                }
                tip = await BlackBoxForensics.TryAsync(() => host.Git.ResolveBranchTipAsync(gitPath, state.TargetBranch, token), (string?)null).ConfigureAwait(false);
                tipSource = "the current tip of " + state.TargetBranch + " (the failure did not record the tip)";
                if (tip == null)
                {
                    Omit(record, w, "Target movement omitted: the target branch " + state.TargetBranch + " does not resolve on " + state.HostLabel + ".");
                    return;
                }
            }

            GitCommitRange? range = await BlackBoxForensics.TryAsync(() => host.Git.GetCommitRangeAsync(gitPath, state.StartCommit, tip, GitCommitRange.MaxLimit, token), (GitCommitRange?)null).ConfigureAwait(false);
            if (range == null)
            {
                Omit(record, w, "Target movement omitted: the start commit " + state.StartCommit + " or the target tip " + tip + " is not in the repository on " + state.HostLabel + ".");
                return;
            }

            state.TargetCommits = range;
            w.TargetMovementNote = range.TotalCount == 0 ? "No commits landed on the target since the mission started (compared with " + tipSource + ")." : "Compared with " + tipSource + ".";
            BlackBoxConflict c = record.Conflict ?? new BlackBoxConflict();
            record.Conflict = c;
            c.TargetCommitTotal = range.TotalCount;
            c.TargetCommitsSinceStart = range.Commits.Take(BlackBoxLimits.MaxTargetCommits).Select(Ref).ToList();
            if (range.TotalCount > c.TargetCommitsSinceStart.Count)
                BlackBoxRecordBounder.AddField(record.Truncation, "Conflict.TargetCommitsSinceStart", range.TotalCount, c.TargetCommitsSinceStart.Count, "items");
            if (range.Truncated)
                BlackBoxRecordBounder.AddNote(record.Truncation, "Only the newest " + range.Commits.Count + " of " + range.TotalCount + " target commits since the start were read for attribution.");

            if (range.Commits.Count > 0) BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(BlackBoxObjectNames.TargetCommits, Render(range, state, tip, tipSource)));
        }

        private static void Omit(BlackBoxRecord record, BlackBoxWorkspace w, string reason)
        {
            w.TargetMovementNote = reason;
            BlackBoxRecordBounder.AddNote(record.Truncation, reason);
        }

        private static BlackBoxCommitRef Ref(GitCommitInfo commit)
        {
            return new BlackBoxCommitRef
            {
                CommitHash = commit.Sha,
                Subject = BlackBoxLimits.Clip(commit.Subject, BlackBoxLimits.MaxShortTextChars),
                Author = BlackBoxForensics.AuthorOf(commit),
                CommittedUtc = commit.CommittedUtc == DateTime.MinValue ? (DateTime?)null : commit.CommittedUtc,
                MissionId = commit.MissionId
            };
        }

        private static string Render(GitCommitRange range, BlackBoxForensicState state, string tip, string tipSource)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Target commits since the start commit ").Append(state.StartCommit).Append(" up to ").Append(tip).Append(" (").Append(tipSource).Append(") on ").Append(state.HostLabel).Append('\n');
            sb.Append(range.TotalCount.ToString(CultureInfo.InvariantCulture)).Append(" commits");
            if (range.Truncated) sb.Append(", newest ").Append(range.Commits.Count.ToString(CultureInfo.InvariantCulture)).Append(" listed");
            sb.Append("\n\n");
            foreach (GitCommitInfo c in range.Commits)
            {
                sb.Append(c.Sha).Append(' ').Append(c.CommittedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(' ').Append(BlackBoxForensics.AuthorOf(c)).Append('\n');
                sb.Append("    ").Append(c.Subject).Append('\n');
                sb.Append("    ").Append(c.MissionId != null ? "Armada mission " + c.MissionId : "outside Armada").Append('\n');
                foreach (string p in c.ChangedPaths.Take(_MaxPathsListed)) sb.Append("    M ").Append(p).Append('\n');
                if (c.ChangedPathTotal > _MaxPathsListed) sb.Append("    ... ").Append((c.ChangedPathTotal - _MaxPathsListed).ToString(CultureInfo.InvariantCulture)).Append(" more files\n");
            }
            return sb.ToString();
        }

        #endregion
    }
}
