namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
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
    /// Workspace enrichment, on the host that owns the dock: worktree existence, whether the branch still exists and its
    /// tip, porcelain status of the worktree and of the target checkout (evidence/git-status.txt, dirty paths), ahead
    /// and behind against the target (GetDivergenceAsync), conflicted files of an unfinished merge, the paths the mission
    /// changed (ChangedByMission rows), and the Harbor link state. Facts captured at the failure win over facts read now;
    /// an unreachable Harbor leaves them unknown with a note.
    /// </summary>
    public class BlackBoxWorkspaceEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "Workspace";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly VesselGitInspector _Inspector;
        private const int _MaxStatusChars = 65536;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="inspector">Git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxWorkspaceEnricher(DatabaseDriver database, VesselGitInspector inspector, LoggingModule logging) : base(logging)
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
            Mission? mission = context.Mission;
            Dock? dock = state.Dock;

            w.DockId ??= dock?.Id ?? mission?.DockId;
            w.DockState ??= dock?.State.ToString();
            w.WorktreePath ??= dock?.WorktreePath;
            w.BranchName ??= state.Branch;
            w.MissionCommit ??= mission?.CommitHash;
            w.TargetBranch ??= state.TargetBranch;
            if (w.StartCommit == null && state.StartCommit != null)
            {
                w.StartCommit = state.StartCommit;
                w.StartCommitSource = mission?.StartCommit != null ? "Mission" : "DockAnchors";
            }

            await AddHarborLinkAsync(context, state, w, token).ConfigureAwait(false);

            if (state.Host == null)
            {
                w.InspectedHost = null;
                w.InspectionNote = state.HostUnavailableReason;
                if (state.HostUnavailableReason != null && state.Repository != null)
                    BlackBoxRecordBounder.AddNote(record.Truncation, "Workspace facts are unknown: " + state.HostUnavailableReason);
                AddChangedByMissionRows(context, state);
                return;
            }

            DockHost host = state.Host;
            w.InspectedHost = state.HostLabel;
            if (w.WorktreeExists == null && state.WorktreeExists.HasValue) w.WorktreeExists = state.WorktreeExists;
            List<string> notes = new List<string>();
            if (state.WorktreeExists == false) notes.Add("The worktree " + dock?.WorktreePath + " no longer exists on " + state.HostLabel + ".");

            string? repoPath = state.Repository != null && !String.IsNullOrWhiteSpace(state.Repository.Path) ? state.Repository.Path : null;
            string? gitPath = state.GitPath;
            string? tip = null;
            if (gitPath != null && state.Branch != null && GitRevisionNames.IsSafeBranchName(state.Branch))
            {
                bool exists = await BlackBoxForensics.TryAsync(() => host.Git.BranchExistsAsync(repoPath ?? gitPath, state.Branch, token), false).ConfigureAwait(false);
                w.BranchExists = exists;
                if (exists) tip = await BlackBoxForensics.TryAsync(() => host.Git.ResolveBranchTipAsync(repoPath ?? gitPath, state.Branch, token), (string?)null).ConfigureAwait(false);
                if (!exists) notes.Add("The branch " + state.Branch + " no longer exists" + (w.MissionCommit != null ? "; it can be restored from the mission commit " + w.MissionCommit : String.Empty) + ".");
                w.BranchTip ??= tip;
            }

            string? head = tip ?? w.BranchTip ?? w.MissionCommit;
            if (gitPath != null && head != null && state.TargetBranch != null && GitRevisionNames.IsSafeBranchName(state.TargetBranch))
            {
                GitDivergenceCounts? divergence = await BlackBoxForensics.TryAsync(() => host.Git.GetDivergenceAsync(gitPath, state.TargetBranch, head, token), (GitDivergenceCounts?)null).ConfigureAwait(false);
                if (divergence != null)
                {
                    w.CommitsBehindTarget ??= divergence.Behind;
                    w.CommitsAheadOfTarget ??= divergence.Ahead;
                }
            }
            if (gitPath != null && head != null && state.StartCommit != null && w.CommitsAheadOfStart == null)
            {
                GitDivergenceCounts? sinceStart = await BlackBoxForensics.TryAsync(() => host.Git.GetDivergenceAsync(gitPath, state.StartCommit, head, token), (GitDivergenceCounts?)null).ConfigureAwait(false);
                if (sinceStart != null) w.CommitsAheadOfStart = sinceStart.Ahead;
            }

            StringBuilder status = new StringBuilder();
            if (state.WorktreeExists == true && dock?.WorktreePath != null)
            {
                string? porcelain = await PorcelainAsync(host, dock.WorktreePath, token).ConfigureAwait(false);
                status.Append("## worktree ").Append(dock.WorktreePath).Append(" on ").Append(state.HostLabel).Append('\n');
                status.Append(porcelain == null ? "(git status failed)\n" : (porcelain.Length == 0 ? "(clean)\n" : porcelain));
                await AddConflictsFromWorktreeAsync(record, host, dock.WorktreePath, token).ConfigureAwait(false);
                if (!state.MissionChangedPathsKnown && state.StartCommit != null)
                {
                    IReadOnlyList<string>? changed = await BlackBoxForensics.TryAsync<IReadOnlyList<string>?>(async () => await host.Git.GetChangedFilesSinceAsync(dock.WorktreePath, state.StartCommit, token).ConfigureAwait(false), null).ConfigureAwait(false);
                    if (changed != null)
                    {
                        foreach (string p in changed) BlackBoxForensics.AddMissionPath(state, p);
                        state.MissionChangedPathsKnown = true;
                    }
                }
            }
            if (!state.MissionChangedPathsKnown && gitPath != null && state.StartCommit != null && head != null)
            {
                GitCommitRange? mine = await BlackBoxForensics.TryAsync(() => host.Git.GetCommitRangeAsync(gitPath, state.StartCommit, head, GitCommitRange.MaxLimit, token), (GitCommitRange?)null).ConfigureAwait(false);
                if (mine != null)
                {
                    foreach (GitCommitInfo c in mine.Commits) foreach (string p in c.ChangedPaths) BlackBoxForensics.AddMissionPath(state, p);
                    state.MissionChangedPathsKnown = true;
                }
            }

            string? checkout = w.CheckoutPath ?? dock?.CheckoutPath;
            if (!String.IsNullOrWhiteSpace(checkout))
            {
                // A checkout path recorded for a Harbor dock is on the Harbor; any other is on the Admiral.
                DockHost? checkoutHost = DockHostResolver.IsHarborDock(dock) ? host : _Inspector.ResolveHost(null);
                string? porcelain = checkoutHost == null ? null : await PorcelainAsync(checkoutHost, checkout!, token).ConfigureAwait(false);
                if (porcelain != null)
                {
                    status.Append("## checkout ").Append(checkout).Append('\n').Append(porcelain.Length == 0 ? "(clean)\n" : porcelain);
                    List<string> dirty = TrackedChanges(porcelain);
                    if (w.CheckoutDirty == null) w.CheckoutDirty = dirty.Count > 0;
                    if (w.DirtyPaths.Count == 0 && dirty.Count > 0)
                    {
                        w.DirtyPathTotal = dirty.Count;
                        w.DirtyPaths = dirty.Take(BlackBoxLimits.MaxDirtyPaths).ToList();
                        if (dirty.Count > BlackBoxLimits.MaxDirtyPaths) BlackBoxRecordBounder.AddField(record.Truncation, "Workspace.DirtyPaths", dirty.Count, BlackBoxLimits.MaxDirtyPaths, "items");
                    }
                }
            }

            if (status.Length > 0)
            {
                string text = status.Length > _MaxStatusChars ? status.ToString(0, _MaxStatusChars) + "\n[... cut by Armada: " + status.Length + " chars originally ...]\n" : status.ToString();
                if (status.Length > _MaxStatusChars) BlackBoxRecordBounder.AddField(record.Truncation, BlackBoxObjectNames.GitStatus, status.Length, _MaxStatusChars, "chars");
                BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(BlackBoxObjectNames.GitStatus, text));
            }

            w.InspectionNote = notes.Count == 0 ? null : String.Join(" ", notes);
            AddChangedByMissionRows(context, state);
        }

        private async Task AddHarborLinkAsync(BlackBoxEnrichmentContext context, BlackBoxForensicState state, BlackBoxWorkspace w, CancellationToken token)
        {
            string? harborId = state.Dock?.HarborId ?? context.Entry.HarborId ?? context.Mission?.AssignedHarborId;
            if (String.IsNullOrWhiteSpace(harborId)) return;
            if (w.HarborLinkState == null)
            {
                if (DockHostResolver.IsHarborDock(state.Dock)) w.HarborLinkState = state.Host != null ? "Connected" : "Disconnected";
                else if (_Inspector.HarborHostOverride != null) w.HarborLinkState = _Inspector.HarborHostOverride(harborId!) != null ? "Connected" : "Disconnected";
                else if (_Inspector.Hosts.Harbors != null) w.HarborLinkState = _Inspector.Hosts.Harbors.IsConnected(harborId!) ? "Connected" : "Disconnected";
                else w.HarborLinkState = "Unknown";
            }
            if (w.HarborLastSeenUtc == null)
            {
                Harbor? harbor = await _Database.Harbors.ReadAsync(harborId!, token).ConfigureAwait(false);
                w.HarborLastSeenUtc = harbor?.LastSeenUtc;
            }
        }

        private static async Task AddConflictsFromWorktreeAsync(BlackBoxRecord record, DockHost host, string worktree, CancellationToken token)
        {
            if (record.Conflict != null && record.Conflict.ConflictedFiles.Count > 0) return;
            IReadOnlyList<string>? conflicted = await BlackBoxForensics.TryAsync<IReadOnlyList<string>?>(async () => await host.Git.GetConflictedFilesAsync(worktree, token).ConfigureAwait(false), null).ConfigureAwait(false);
            if (conflicted == null || conflicted.Count == 0) return;
            List<string> files = conflicted.Select(BlackBoxForensics.NormalizePath).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            BlackBoxConflict c = record.Conflict ?? new BlackBoxConflict();
            c.IsConflict = true;
            c.ConflictedFileTotal = files.Count;
            c.ConflictedFilesTruncated = files.Count > BlackBoxLimits.MaxConflictedFiles;
            c.ConflictedFiles = files.Take(BlackBoxLimits.MaxConflictedFiles).ToList();
            record.Conflict = c;
        }

        private static void AddChangedByMissionRows(BlackBoxEnrichmentContext context, BlackBoxForensicState state)
        {
            foreach (string p in state.MissionChangedPaths.OrderBy(x => x, StringComparer.Ordinal).Take(BlackBoxForensics.MaxFileRowsPerRole))
                context.Files.Add(BlackBoxForensics.FileRow(context.Entry, p, BlackBoxFileRoleEnum.ChangedByMission));
            if (state.MissionChangedPaths.Count > BlackBoxForensics.MaxFileRowsPerRole)
                BlackBoxRecordBounder.AddField(context.Record.Truncation, "blackbox_files.ChangedByMission", state.MissionChangedPaths.Count, BlackBoxForensics.MaxFileRowsPerRole, "items");
        }

        private static async Task<string?> PorcelainAsync(DockHost host, string directory, CancellationToken token)
        {
            try
            {
                HostCommandResult result = await host.Commands.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    WorkingDirectory = directory,
                    Arguments = new List<string> { "status", "--porcelain=v1", "--untracked-files=all" },
                    TimeoutMs = 30000
                }, token).ConfigureAwait(false);
                if (!result.Success) return null;
                return result.StandardOutput.Replace("\r\n", "\n");
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                return null;
            }
        }

        private static List<string> TrackedChanges(string porcelain)
        {
            List<string> paths = new List<string>();
            foreach (string line in porcelain.Split('\n'))
            {
                if (line.Length < 4 || line.StartsWith("??", StringComparison.Ordinal) || line.StartsWith("!!", StringComparison.Ordinal)) continue;
                string path = line.Substring(3);
                int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0) path = path.Substring(arrow + 4);
                path = BlackBoxForensics.NormalizePath(path.Trim('"'));
                if (path.Length > 0) paths.Add(path);
            }
            return paths;
        }

        #endregion
    }
}
