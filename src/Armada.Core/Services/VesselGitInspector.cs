namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Host-aware, read-only git inspection of a vessel's repositories, docks and branches, shared by
    /// <see cref="VesselCleanupService"/> and the black box enrichers. Each repository is inspected on the host that owns
    /// it (the Admiral or a Harbor, through <see cref="DockHostResolver"/>); a Harbor that is not connected resolves to
    /// no host, so callers report its resources as unknown instead of failing. Nothing here changes a repository.
    /// Thread-safe.
    /// </summary>
    public class VesselGitInspector
    {
        #region Public-Members

        /// <summary>
        /// The dock host resolver. The Admiral replaces the default (Admiral host only) once Harbor delegation exists.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public DockHostResolver Hosts
        {
            get => _Hosts;
            set => _Hosts = value ?? throw new ArgumentNullException(nameof(Hosts));
        }

        /// <summary>
        /// Test hook: resolve a Harbor host (null = not reachable). When unset, a Harbor is reachable only while it is
        /// connected through <see cref="DockHostResolver.Harbors"/>.
        /// </summary>
        public Func<string, DockHost?>? HarborHostOverride { get; set; } = null;

        /// <summary>
        /// Mission statuses that mean the mission still needs its dock, branch and worktree.
        /// </summary>
        public static IReadOnlyCollection<MissionStatusEnum> ActiveMissionStatuses => _ActiveMissionStatuses;

        #endregion

        #region Private-Members

        private readonly string _Header = "[VesselGitInspector] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private DockHostResolver _Hosts;

        private static readonly HashSet<MissionStatusEnum> _ActiveMissionStatuses = new HashSet<MissionStatusEnum>
        {
            MissionStatusEnum.Pending, MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced,
            MissionStatusEnum.PullRequestOpen, MissionStatusEnum.Testing, MissionStatusEnum.Review, MissionStatusEnum.Blocked,
            MissionStatusEnum.Verifying
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver (voyage lookups).</param>
        /// <param name="hosts">Dock host resolver (Admiral host and Harbors).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public VesselGitInspector(DatabaseDriver database, DockHostResolver hosts, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The host for a Harbor, or the Admiral's host for null; null when the Harbor is not connected (or Harbor
        /// delegation is not available).
        /// </summary>
        /// <param name="harborId">Harbor identifier, or null for the Admiral.</param>
        /// <returns>The host, or null.</returns>
        public DockHost? ResolveHost(string? harborId)
        {
            if (String.IsNullOrWhiteSpace(harborId)) return _Hosts.Local;
            if (HarborHostOverride != null) return HarborHostOverride(harborId!);
            if (_Hosts.Harbors == null || !_Hosts.Harbors.IsConnected(harborId!)) return null;
            return _Hosts.ForHarbor(harborId!);
        }

        /// <summary>
        /// The repository a dock's git work happens in, on the host that owns it: the Harbor's repository for a
        /// Harbor-hosted dock, otherwise the vessel's bare clone on the Admiral. Nothing is listed (Worktrees and
        /// Branches stay null). Host is null when the Harbor is not connected; Path is empty when no repository is known.
        /// </summary>
        /// <param name="vessel">Vessel, or null.</param>
        /// <param name="dock">Dock, or null.</param>
        /// <returns>The repository state.</returns>
        public VesselRepositoryState ResolveRepository(Vessel? vessel, Dock? dock)
        {
            if (DockHostResolver.IsHarborDock(dock))
            {
                VesselRepositoryState remote = new VesselRepositoryState { HarborId = dock!.HarborId, Path = dock.RepositoryPath ?? String.Empty };
                remote.Host = ResolveHost(dock.HarborId);
                if (remote.Host == null) remote.Error = "Harbor " + dock.HarborId + " is not connected.";
                return remote;
            }

            VesselRepositoryState local = new VesselRepositoryState { HarborId = null, Path = vessel?.LocalPath ?? String.Empty, Host = _Hosts.Local };
            if (String.IsNullOrWhiteSpace(local.Path)) local.Error = "The vessel has no local repository.";
            return local;
        }

        /// <summary>
        /// The repositories to inspect for a vessel, with their worktrees and branches loaded on the owning host: the
        /// Admiral's bare clone (when it exists) plus each distinct Harbor repository one of the given docks was created
        /// from. A Harbor that is not connected yields a state with a null Host; a git failure sets Error.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="vesselDocks">The vessel's docks.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One state per repository.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel or vesselDocks is null.</exception>
        public async Task<List<VesselRepositoryState>> LoadRepositoriesAsync(Vessel vessel, IEnumerable<Dock> vesselDocks, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (vesselDocks == null) throw new ArgumentNullException(nameof(vesselDocks));

            List<VesselRepositoryState> repos = new List<VesselRepositoryState>();
            if (!String.IsNullOrWhiteSpace(vessel.LocalPath) && Directory.Exists(vessel.LocalPath))
                repos.Add(new VesselRepositoryState { HarborId = null, Path = vessel.LocalPath! });
            foreach (Dock dock in vesselDocks)
            {
                if (!DockHostResolver.IsHarborDock(dock)) continue;
                if (repos.Any(r => r.HarborId == dock.HarborId && SamePath(r.Path, dock.RepositoryPath!, true))) continue;
                repos.Add(new VesselRepositoryState { HarborId = dock.HarborId, Path = dock.RepositoryPath! });
            }

            foreach (VesselRepositoryState repo in repos)
            {
                repo.Host = ResolveHost(repo.HarborId);
                if (repo.Host == null) continue;
                try
                {
                    repo.Worktrees = await repo.Host.Git.ListWorktreeEntriesAsync(repo.Path, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    repo.Error = "could not list worktrees: " + ex.Message;
                }
                try
                {
                    repo.Branches = await repo.Host.Git.ListBranchTipsAsync(repo.Path, vessel.DefaultBranch, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    repo.Error = (repo.Error == null ? "" : repo.Error + "; ") + "could not list branches: " + ex.Message;
                }
            }

            return repos;
        }

        /// <summary>
        /// Why a worktree directory cannot be proven disposable, or null when it is clean and its HEAD is merged into the
        /// default branch or is the tip of a branch that is kept.
        /// </summary>
        /// <param name="vessel">Vessel (default branch).</param>
        /// <param name="host">Host the worktree lives on.</param>
        /// <param name="worktreePath">Worktree path on that host.</param>
        /// <param name="repo">The repository the worktree belongs to, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The risk, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel, host or worktreePath is null.</exception>
        public async Task<string?> DescribeWorktreeRiskAsync(Vessel vessel, DockHost host, string worktreePath, VesselRepositoryState? repo, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            GitWorkingTreeStatus status;
            try
            {
                status = await host.Git.GetWorkingTreeStatusAsync(worktreePath, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return "Could not read the worktree status (" + ex.Message + "); it may hold uncommitted work.";
            }
            if (!status.IsClean)
                return "Worktree has uncommitted changes (" + status.ModifiedCount + " modified, " + status.UntrackedCount + " untracked).";

            string? head;
            try
            {
                head = await host.Git.GetHeadCommitHashAsync(worktreePath, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return "Could not read the worktree HEAD: " + ex.Message;
            }
            if (String.IsNullOrWhiteSpace(head)) return "Could not read the worktree HEAD.";

            bool? merged = await IsMergedAsync(host.Git, worktreePath, head!, vessel.DefaultBranch, token).ConfigureAwait(false);
            if (merged == true) return null;

            string? branch;
            try
            {
                branch = await host.Git.GetCurrentBranchAsync(worktreePath, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                branch = null;
                _Logging.Debug(_Header + "could not read the branch of " + worktreePath + ": " + ex.Message);
            }
            if (!String.IsNullOrWhiteSpace(branch) && branch != "HEAD" && repo?.Host != null)
            {
                string? tip = null;
                try
                {
                    tip = await repo.Host.Git.ResolveBranchTipAsync(repo.Path, branch!, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _Logging.Debug(_Header + "could not resolve " + branch + ": " + ex.Message);
                }
                if (tip != null && String.Equals(tip.Trim(), head!.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            }

            return merged == null
                ? "Could not prove the worktree's commits are merged into " + vessel.DefaultBranch + "."
                : "Worktree HEAD has commits that are neither merged into " + vessel.DefaultBranch + " nor kept on a branch.";
        }

        /// <summary>
        /// Why the given missions, branch, or merge queue still need a resource, or null.
        /// </summary>
        /// <param name="missions">Missions that reference the resource (newest first).</param>
        /// <param name="branch">Branch, or null.</param>
        /// <param name="mergeEntries">The vessel's merge-queue entries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reason, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missions or mergeEntries is null.</exception>
        public async Task<string?> DescribeWorkReferenceAsync(IReadOnlyList<Mission> missions, string? branch, IReadOnlyList<MergeEntry> mergeEntries, CancellationToken token = default)
        {
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            if (mergeEntries == null) throw new ArgumentNullException(nameof(mergeEntries));

            foreach (Mission m in missions)
            {
                if (_ActiveMissionStatuses.Contains(m.Status))
                    return "Mission " + m.Id + " is " + m.Status + ".";
                if (m.Status == MissionStatusEnum.LandingFailed)
                    return "Mission " + m.Id + " is LandingFailed; landing can still be retried.";
            }

            foreach (MergeEntry e in mergeEntries)
            {
                bool refers = (!String.IsNullOrEmpty(branch) && e.BranchName == branch)
                    || (!String.IsNullOrEmpty(e.MissionId) && missions.Any(m => m.Id == e.MissionId));
                if (!refers) continue;
                if (IsLiveMergeEntry(e))
                    return "Merge-queue entry " + e.Id + " is " + e.Status + ".";
                if (e.Status == MergeStatusEnum.Failed)
                    return "Merge-queue entry " + e.Id + " failed and can still be retried; resolve or remove it first.";
            }

            foreach (string voyageId in missions.Where(m => !String.IsNullOrEmpty(m.VoyageId)).Select(m => m.VoyageId!).Distinct())
            {
                Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
                if (voyage != null && (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress))
                    return "Voyage " + voyage.Id + " is " + voyage.Status + " and may still use this work.";
            }

            return null;
        }

        /// <summary>
        /// Whether a reference is merged into the default branch, locally or on origin.
        /// </summary>
        /// <param name="git">Git service of the host.</param>
        /// <param name="repoPath">Repository or worktree path on that host.</param>
        /// <param name="reference">Branch name or commit.</param>
        /// <param name="defaultBranch">Default branch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when merged, false when not, null when it could not be compared.</returns>
        /// <exception cref="ArgumentNullException">Thrown when git is null.</exception>
        public async Task<bool?> IsMergedAsync(IGitService git, string repoPath, string reference, string defaultBranch, CancellationToken token = default)
        {
            if (git == null) throw new ArgumentNullException(nameof(git));
            bool? local = null;
            bool? remote = null;
            try
            {
                local = await git.IsAncestorAsync(repoPath, reference, defaultBranch, token).ConfigureAwait(false);
                if (local == true) return true;
                remote = await git.IsAncestorAsync(repoPath, reference, "refs/remotes/origin/" + defaultBranch, token).ConfigureAwait(false);
                if (remote == true) return true;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not compare " + reference + " with " + defaultBranch + " in " + repoPath + ": " + ex.Message);
                return null;
            }
            if (local == false || remote == false) return false;
            return null;
        }

        /// <summary>
        /// Whether a merge-queue entry is still live (queued, testing, or passed and waiting to land).
        /// </summary>
        /// <param name="entry">Merge entry.</param>
        /// <returns>True when live.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public static bool IsLiveMergeEntry(MergeEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            return entry.Status == MergeStatusEnum.Queued || entry.Status == MergeStatusEnum.Testing || entry.Status == MergeStatusEnum.Passed;
        }

        /// <summary>
        /// Whether two paths name the same directory (case-insensitive, separators normalized; canonicalized when the
        /// paths are on this machine).
        /// </summary>
        /// <param name="a">First path.</param>
        /// <param name="b">Second path.</param>
        /// <param name="local">Whether the paths are on the Admiral's host.</param>
        /// <returns>True when they match.</returns>
        public static bool SamePath(string a, string b, bool local)
        {
            return String.Equals(NormalizePath(a, local), NormalizePath(b, local), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalize a path for comparison: canonicalized when local, forward slashes, no trailing slash.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="local">Whether the path is on the Admiral's host.</param>
        /// <returns>The normalized path.</returns>
        public static string NormalizePath(string path, bool local)
        {
            string p = local ? PathCanonicalizer.Canonicalize(path ?? String.Empty) : (path ?? String.Empty);
            return p.Replace('\\', '/').TrimEnd('/');
        }

        #endregion
    }
}
