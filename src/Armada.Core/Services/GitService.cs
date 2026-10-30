namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Git operations via the git CLI. By default git and gh run as processes on this machine; constructed with an
    /// <see cref="IHostCommandExecutor"/> they run through it instead (for a Harbor-hosted dock, on the Harbor's machine),
    /// so the same operations serve docks wherever they live.
    /// </summary>
    public class GitService : IGitService
    {
        #region Public-Members

        private const string _SafeFetchRefspec = "+refs/heads/*:refs/remotes/origin/*";

        #endregion

        #region Private-Members

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _RepoLocks =
            new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private const string _HeadsRefPrefix = "refs/heads/";
        private string _Header = "[GitService] ";
        private LoggingModule _Logging;
        private IHostCommandExecutor? _Commands = null;

        // Exit codes that answer a probe rather than report a failure, declared to the host executor so a Harbor does not
        // show them as failures: 1 for no match (a ref, an ancestor, a grep hit, a pull request), 128 for a directory that
        // is not a repository, and 129 for a usage error from an older git (worktree list -z).
        private static readonly int[] _NoMatch = new int[] { 1 };
        private static readonly int[] _NotARepository = new int[] { 128 };
        private static readonly int[] _UsageError = new int[] { 129 };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public GitService(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Instantiate with an executor that runs every git and gh command (for example on a Harbor's machine). Paths
        /// passed to this instance are paths on that machine; it never touches this machine's filesystem.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="commands">Executor for git and gh commands.</param>
        public GitService(LoggingModule logging, IHostCommandExecutor commands)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _Header = "[GitService remote] ";
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ValidatePublishedAncestryAsync(string worktreePath, string checkoutPath, string targetBranch, string commitHash, CancellationToken token = default)
        {
            if (!await BranchExistsAsync(checkoutPath, targetBranch, token).ConfigureAwait(false)) return;
            // Fetch only the mapped target into FETCH_HEAD. Neither the checkout nor its branches are changed.
            await RunGitAsync(worktreePath, token, "fetch", "--no-tags", checkoutPath, "refs/heads/" + targetBranch).ConfigureAwait(false);
            GitProcessResult shared = await ExecuteProcessAsync(worktreePath, "git", _NoMatch, token,
                "merge-base", commitHash, "FETCH_HEAD").ConfigureAwait(false);
            if (shared.ExitCode == 1) return; // Disjoint histories cannot contribute unpublished ancestors.
            if (!shared.Succeeded) throw new InvalidOperationException("Cannot establish the candidate's ancestry against the developer checkout.");
            bool? published = await IsAncestorAsync(worktreePath, shared.StandardOutput.Trim(), "refs/remotes/origin/" + targetBranch, token).ConfigureAwait(false);
            if (published != true)
                throw new GitUnpublishedCommitsException("Not published: the candidate includes checkout commits that are not on origin/" + targetBranch
                    + ". Publish or reconcile those commits explicitly, then retry landing. The mission branch is preserved.");
        }

        /// <inheritdoc />
        public async Task PushVerifiedCommitAsync(string worktreePath, string commitHash, string targetBranch, CancellationToken token = default)
        {
            if (await GetHeadCommitHashAsync(worktreePath, token).ConfigureAwait(false) != commitHash)
                throw new InvalidOperationException("The verified landing commit changed before push; rerun verification.");
            // An ordinary push refuses a concurrently advanced, divergent target. No force push and no new merge.
            await RunGitAsync(worktreePath, token, "push", "origin", commitHash + ":refs/heads/" + targetBranch).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task FastForwardVerifiedCommitAsync(string targetWorkDir, string sourceRepoPath, string commitHash, string targetBranch, CancellationToken token = default)
        {
            // Untracked files also block the fast-forward, so the porcelain listing includes them. Keep the leading
            // status columns (" M path"): only trailing whitespace is trimmed so ModifiedPaths parses exactly.
            string porcelain = (await RunGitAsync(targetWorkDir, token, "status", "--porcelain", "--untracked-files=all").ConfigureAwait(false)).TrimEnd();
            if (!String.IsNullOrWhiteSpace(porcelain))
                throw new GitDirtyCheckoutException(targetWorkDir, porcelain);
            string? original = await GetCurrentBranchAsync(targetWorkDir, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(original)) throw new InvalidOperationException("Cannot land into a detached target checkout.");
            await RunGitAsync(targetWorkDir, token, "fetch", sourceRepoPath, commitHash).ConfigureAwait(false);
            try
            {
                if (original != targetBranch) await RunGitAsync(targetWorkDir, token, "checkout", targetBranch).ConfigureAwait(false);
                await RunGitAsync(targetWorkDir, token, "merge", "--ff-only", commitHash).ConfigureAwait(false);
                if (await GetHeadCommitHashAsync(targetWorkDir, token).ConfigureAwait(false) != commitHash)
                    throw new InvalidOperationException("Target contains newer work; verified landing did not update it.");
            }
            finally
            {
                if (original != targetBranch) await RunGitAsync(targetWorkDir, token, "checkout", original).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Clone a repository as a bare repo.
        /// </summary>
        public async Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoUrl)) throw new ArgumentNullException(nameof(repoUrl));
            if (String.IsNullOrEmpty(localPath)) throw new ArgumentNullException(nameof(localPath));

            _Logging.Debug(_Header + "cloning bare: " + repoUrl + " -> " + localPath);
            await RunGitAsync(null, "clone", "--bare", repoUrl, localPath).ConfigureAwait(false);

            // Keep fetches on remote-tracking refs so active mission branches checked out
            // in worktrees are not overwritten by background refreshes.
            await EnsureSafeFetchRefspecAsync(localPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a git worktree from a bare repository.
        /// </summary>
        public async Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Debug(_Header + "creating worktree: " + worktreePath + " branch: " + branchName);

            string normalizedRepoPath = Path.GetFullPath(repoPath);
            SemaphoreSlim repoLock = _RepoLocks.GetOrAdd(normalizedRepoPath, _ => new SemaphoreSlim(1, 1));
            await repoLock.WaitAsync(token).ConfigureAwait(false);
            bool createdBranchRef = false;

            try
            {
                bool hasBaseBranch = await EnsureLocalBranchAsync(repoPath, baseBranch, token).ConfigureAwait(false);
                if (!hasBaseBranch)
                {
                    throw new InvalidOperationException("Unable to prepare base branch " + baseBranch + " in repository " + repoPath);
                }

                bool branchExists = await BranchExistsAsync(repoPath, branchName, token).ConfigureAwait(false);
                if (!branchExists)
                {
                    branchExists = await SyncLocalBranchFromRemoteAsync(repoPath, branchName).ConfigureAwait(false);
                }

                if (branchExists)
                {
                    _Logging.Debug(_Header + "attaching worktree to existing branch: " + branchName);
                    await RunGitAsync(repoPath, "worktree", "add", worktreePath, branchName).ConfigureAwait(false);
                }
                else
                {
                    string baseRef = "refs/heads/" + baseBranch;
                    string baseCommit = await ResolveCommitAsync(repoPath, baseRef).ConfigureAwait(false);

                    // Create the branch ref in the bare repo first, then attach the worktree to
                    // that branch by name. This keeps HEAD on the named branch and avoids the
                    // detach/rebind sequence that can materialize an unborn branch under load.
                    await RunGitAsync(repoPath, "branch", branchName, baseCommit).ConfigureAwait(false);
                    createdBranchRef = true;
                    await RunGitAsync(repoPath, "worktree", "add", worktreePath, branchName).ConfigureAwait(false);

                    string createdHead = await ResolveCommitAsync(worktreePath, "HEAD").ConfigureAwait(false);
                    if (!String.Equals(createdHead, baseCommit, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("New worktree branch " + branchName +
                            " was expected to start at " + baseCommit + " from " + baseRef +
                            " but HEAD resolved to " + createdHead);
                    }
                }

                string currentBranch = (await RunGitAsync(worktreePath, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                if (!String.Equals(currentBranch, branchName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Worktree " + worktreePath +
                        " was expected to be on branch " + branchName +
                        " but is on " + currentBranch);
                }

                await EnsureTrackedFilesCleanAsync(worktreePath, token).ConfigureAwait(false);

                // Agent-driven plain `git push` should publish the current branch rather than
                // attempting to update the inherited base-branch upstream (commonly `main`).
                await RunGitAsync(worktreePath, "config", "push.default", "current").ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    if (_Commands != null || Directory.Exists(worktreePath))
                    {
                        await RunGitAsync(repoPath, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
                    }
                }
                catch
                {
                }

                if (createdBranchRef)
                {
                    try
                    {
                        await RunGitAsync(repoPath, "branch", "-D", branchName).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
            finally
            {
                repoLock.Release();
            }
        }

        /// <summary>
        /// Remove a git worktree.
        /// </summary>
        public async Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Debug(_Header + "removing worktree: " + worktreePath);
            string repoPath = await ResolveWorktreeRepoPathAsync(worktreePath).ConfigureAwait(false);
            await RunGitAsync(repoPath, token, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetch latest changes from remote.
        /// </summary>
        public async Task FetchAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            // Ensure the fetch refspec is configured (bare repos cloned before the fix may lack it)
            try
            {
                string currentRefspec = await RunGitAsync(repoPath, "config", "--get", "remote.origin.fetch").ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(currentRefspec) || !String.Equals(currentRefspec.Trim(), _SafeFetchRefspec, StringComparison.Ordinal))
                {
                    await EnsureSafeFetchRefspecAsync(repoPath).ConfigureAwait(false);
                }
            }
            catch
            {
                // Config key missing entirely — set it
                await EnsureSafeFetchRefspecAsync(repoPath).ConfigureAwait(false);
            }

            // Prune stale worktree registrations before fetching to avoid
            // "refusing to fetch into branch checked out at ..." errors
            // from worktrees that no longer exist on disk.
            try
            {
                await RunGitAsync(repoPath, "worktree", "prune").ConfigureAwait(false);
            }
            catch
            {
                // Best effort — don't let prune failure block fetch
            }

            _Logging.Debug(_Header + "fetching: " + repoPath);
            try
            {
                await RunGitAsync(repoPath, "fetch", "--all", "--prune").ConfigureAwait(false);
            }
            catch (GitCommandException ex)
            {
                // Some remote's configured refspec could not be applied (for example a refspec that targets a
                // branch checked out in a worktree). Fetch origin with an explicit remote-tracking refspec,
                // which never writes to refs/heads and so cannot be refused for a checked-out branch.
                _Logging.Warn(_Header + "full fetch failed (exit " + ex.ExitCode + "), fetching origin into remote-tracking refs: " + ex.StandardError.Trim());
                await RunGitAsync(repoPath, "fetch", "--prune", "origin", _SafeFetchRefspec).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Push a branch to the remote.
        /// </summary>
        public async Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Debug(_Header + "pushing branch from: " + worktreePath);
            await RunGitAsync(worktreePath, "push", "-u", remoteName, "HEAD").ConfigureAwait(false);
        }

        /// <summary>
        /// Create a pull request using the gh CLI.
        /// </summary>
        public async Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));

            _Logging.Debug(_Header + "creating PR: " + title);

            string createOutput = await RunProcessAsync(worktreePath, "gh", "pr", "create", "--title", title, "--body", body ?? "").ConfigureAwait(false);

            // Read the URL back as structured data rather than from the create command's console text.
            string? prUrl = null;
            GitProcessResult view = await ExecuteProcessAsync(worktreePath, "gh", _NoMatch, token, "pr", "view", "--json", "url").ConfigureAwait(false);
            if (view.Succeeded)
            {
                GhPullRequestView? parsed = DeserializeGh(view.StandardOutput);
                if (parsed != null && IsAbsoluteHttpUrl(parsed.Url)) prUrl = parsed.Url!.Trim();
            }

            if (prUrl == null)
            {
                // gh documents that pr create prints the new pull request URL; accept it only as a valid URL.
                string[] createLines = createOutput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = createLines.Length - 1; i >= 0 && prUrl == null; i--)
                {
                    if (IsAbsoluteHttpUrl(createLines[i])) prUrl = createLines[i].Trim();
                }
            }

            if (prUrl == null)
            {
                throw new GitCommandException("gh", new List<string> { "pr", "view", "--json", "url" }, view.Succeeded ? 1 : view.ExitCode, view.StandardOutput, view.StandardError);
            }

            _Logging.Debug(_Header + "PR created: " + prUrl);
            return prUrl;
        }

        /// <summary>
        /// Repair a worktree by resetting it to a clean state.
        /// </summary>
        public async Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Debug(_Header + "repairing worktree: " + worktreePath);

            // Reset any uncommitted changes
            await RunGitAsync(worktreePath, "checkout", "--", ".").ConfigureAwait(false);

            // Remove untracked files
            await RunGitAsync(worktreePath, "clean", "-fd").ConfigureAwait(false);

            _Logging.Debug(_Header + "worktree repaired: " + worktreePath);
        }

        /// <summary>
        /// Prune stale worktree registrations.
        /// </summary>
        public async Task PruneWorktreesAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            _Logging.Debug(_Header + "pruning stale worktrees in " + repoPath);
            await RunGitAsync(repoPath, "worktree", "prune").ConfigureAwait(false);
        }

        /// <summary>
        /// Enable auto-merge on a pull request using the gh CLI.
        /// </summary>
        public async Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(prUrl)) throw new ArgumentNullException(nameof(prUrl));

            _Logging.Debug(_Header + "enabling auto-merge for PR: " + prUrl);
            await RunProcessAsync(worktreePath, "gh", "pr", "merge", prUrl, "--merge", "--auto").ConfigureAwait(false);
        }

        /// <summary>
        /// Merge a branch from a source repository into a target branch of a working directory (often the user's own
        /// checkout). The checkout must have no uncommitted changes to tracked files. When a target branch is given:
        /// the branch the checkout was on (or its detached commit) is recorded, the target branch is checked out, it is
        /// fast-forwarded to origin's copy when it is only behind it, the branch is merged, and the checkout is then
        /// switched back to the recorded branch, after a failed merge too. With a commit message the merge always makes a
        /// merge commit (--no-ff), so the message is not lost to a fast-forward.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when targetWorkDir, sourceRepoPath, or branchName is null or empty.</exception>
        /// <exception cref="GitDirtyCheckoutException">Thrown when the checkout has uncommitted changes to tracked files.</exception>
        /// <exception cref="GitMergeConflictException">Thrown when git reports conflicted files merging the branch.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git cannot check out the target or merge the branch.</exception>
        public async Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetWorkDir)) throw new ArgumentNullException(nameof(targetWorkDir));
            if (String.IsNullOrEmpty(sourceRepoPath)) throw new ArgumentNullException(nameof(sourceRepoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Debug(_Header + "merging branch " + branchName + " from " + sourceRepoPath + " into " + targetWorkDir);
            await EnsureTrackedFilesCleanAsync(targetWorkDir, token).ConfigureAwait(false);

            // Record where the checkout is so it can be put back: landing must not leave the user's checkout on a
            // different branch than the one they were working on.
            string? originalBranch = null;
            string? originalHead = null;
            if (!String.IsNullOrEmpty(targetBranch))
            {
                originalBranch = await GetCurrentBranchAsync(targetWorkDir, token).ConfigureAwait(false);
                if (originalBranch == null) originalHead = await GetHeadCommitHashAsync(targetWorkDir, token).ConfigureAwait(false);
            }

            try
            {
                // Ensure we are on the correct target branch before merging.
                // Without this, a previous fetch/merge could leave HEAD on a different branch,
                // causing subsequent merges to target the wrong branch.
                if (!String.IsNullOrEmpty(targetBranch))
                {
                    await EnsureTargetBranchCheckedOutAsync(targetWorkDir, sourceRepoPath, targetBranch, token).ConfigureAwait(false);
                    await FastForwardTargetFromOriginAsync(targetWorkDir, targetBranch, token).ConfigureAwait(false);
                }

                // Fetch the specific branch from the bare repo using explicit refspec
                // Branch names with slashes (e.g. armada/claude-code-1/msn_xxx) require
                // the full refs/heads/ prefix to resolve correctly from bare repos.
                string fetchedBranchRef = await FetchBranchIntoLocalRefAsync(targetWorkDir, sourceRepoPath, branchName, token).ConfigureAwait(false);

                // Merge the fetched local ref into the current branch.
                // Using a concrete ref avoids FETCH_HEAD resolution issues across separate git
                // invocations and keeps landing stable even when the target checkout is a worktree.
                string message = commitMessage ?? ("Merge armada mission: " + branchName);
                bool noFastForward = !String.IsNullOrWhiteSpace(commitMessage);
                try
                {
                    await MergeFetchedBranchAsync(targetWorkDir, sourceRepoPath, branchName, targetBranch, fetchedBranchRef, message, noFastForward, token).ConfigureAwait(false);
                }
                finally
                {
                    // The armada-landing/* ref only carries the fetched branch into the merge; leaving it would add one
                    // branch to the user's checkout for every landed mission.
                    // Not cancellable: it must not replace the merge's own exception or leave the ref behind on cancel.
                    await DeleteLocalRefQuietlyAsync(targetWorkDir, fetchedBranchRef, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!String.IsNullOrEmpty(targetBranch))
                {
                    // Not cancellable: the checkout must go back even when the landing was cancelled.
                    await RestoreOriginalCheckoutAsync(targetWorkDir, targetBranch, originalBranch, originalHead).ConfigureAwait(false);
                }
            }

            _Logging.Debug(_Header + "merged " + branchName + " into " + targetWorkDir + (String.IsNullOrEmpty(targetBranch) ? "" : " (target: " + targetBranch + ")"));
        }

        private async Task MergeFetchedBranchAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch, string fetchedBranchRef, string message, bool noFastForward, CancellationToken token)
        {
            try
            {
                List<string> args = new List<string> { "merge", fetchedBranchRef, "--no-edit" };
                if (noFastForward) args.Add("--no-ff");

                // Decide up front from git merge-base's exit code (1 = no common ancestor) instead of
                // retrying after matching git's error wording.
                GitProcessResult mergeBase = await ExecuteProcessAsync(targetWorkDir, "git", _NoMatch, token, "merge-base", "HEAD", fetchedBranchRef).ConfigureAwait(false);
                if (mergeBase.ExitCode == 1)
                {
                    _Logging.Warn(_Header + "no merge base between target and " + branchName + ", merging with --allow-unrelated-histories");
                    args.Add("--allow-unrelated-histories");
                }

                args.Add("-m");
                args.Add(message);
                await RunGitAsync(targetWorkDir, token, args.ToArray()).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string failure = "Merge conflict or merge failure: " + ex.Message;
                IReadOnlyList<string> conflicts = Array.Empty<string>();
                try
                {
                    conflicts = await GetConflictedFilesAsync(targetWorkDir, CancellationToken.None).ConfigureAwait(false);
                    if (conflicts.Count > 0) failure += " Conflicted files: " + String.Join(", ", conflicts) + ".";
                    if (await ChangelogLandingConflict.TryResolveAsync(_Commands ?? new LocalHostCommandExecutor(), targetWorkDir, IsWindowsHost(targetWorkDir), token).ConfigureAwait(false)) return;
                }
                catch (Exception secondary)
                {
                    failure += " Conflict resolution failed: " + secondary.Message;
                }
                finally
                {
                    try { await RestoreAfterFailedMergeAsync(targetWorkDir, sourceRepoPath, targetBranch, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception secondary) { failure += " Merge cleanup failed: " + secondary.Message; }
                }
                // Only a merge git reports as conflicted is a landing conflict; any other merge failure is not.
                if (conflicts.Count > 0) throw new GitMergeConflictException(failure, conflicts, ex as GitCommandException);
                if (ex is GitCommandException gitFailure)
                    throw new GitCommandException(gitFailure.Executable, gitFailure.Arguments, gitFailure.ExitCode, gitFailure.StandardOutput, failure);
                throw new InvalidOperationException(failure, ex);
            }
        }

        /// <summary>
        /// Bring the checked-out target branch up to origin's copy when it is only behind it (git merge --ff-only), so
        /// the landing merge is made on top of what is on the remote and a later push is a fast-forward. Best effort:
        /// a checkout without an origin, a failed fetch, or a target that has its own commits is left as it is.
        /// </summary>
        private async Task FastForwardTargetFromOriginAsync(string targetWorkDir, string targetBranch, CancellationToken token)
        {
            GitProcessResult origin = await ExecuteProcessAsync(targetWorkDir, "git", new int[] { 2 }, token, "remote", "get-url", "origin").ConfigureAwait(false);
            if (!origin.Succeeded)
            {
                _Logging.Debug(_Header + "no origin remote in " + targetWorkDir + "; not fast-forwarding " + targetBranch);
                return;
            }

            string originRef = "refs/remotes/origin/" + targetBranch;
            GitProcessResult fetched = await ExecuteProcessAsync(targetWorkDir, "git", token, "fetch", "--quiet", "origin", "+refs/heads/" + targetBranch + ":" + originRef).ConfigureAwait(false);
            if (!fetched.Succeeded)
            {
                _Logging.Warn(_Header + "could not fetch " + targetBranch + " from origin in " + targetWorkDir + ": " + fetched.StandardError.Trim());
                return;
            }

            bool? behind = await IsAncestorAsync(targetWorkDir, "HEAD", originRef, token).ConfigureAwait(false);
            if (behind != true)
            {
                if (behind == false) _Logging.Debug(_Header + targetBranch + " in " + targetWorkDir + " has commits that are not on origin; merging without fast-forwarding it");
                return;
            }

            string head = (await RunGitAsync(targetWorkDir, token, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
            string tip = (await RunGitAsync(targetWorkDir, token, "rev-parse", originRef).ConfigureAwait(false)).Trim();
            if (String.Equals(head, tip, StringComparison.Ordinal)) return;

            await RunGitAsync(targetWorkDir, token, "merge", "--ff-only", originRef).ConfigureAwait(false);
            _Logging.Info(_Header + "fast-forwarded " + targetBranch + " in " + targetWorkDir + " to origin before landing");
        }

        /// <summary>
        /// Switch the checkout back to the branch (or detached commit) it was on before landing switched it to the target.
        /// </summary>
        private async Task RestoreOriginalCheckoutAsync(string targetWorkDir, string targetBranch, string? originalBranch, string? originalHead)
        {
            if (originalBranch == null && originalHead == null) return;
            if (originalBranch != null && String.Equals(originalBranch, targetBranch, StringComparison.Ordinal)) return;

            try
            {
                string? current = await GetCurrentBranchAsync(targetWorkDir, CancellationToken.None).ConfigureAwait(false);
                if (originalBranch != null)
                {
                    if (String.Equals(current, originalBranch, StringComparison.Ordinal)) return;
                    await RunGitAsync(targetWorkDir, CancellationToken.None, "checkout", originalBranch).ConfigureAwait(false);
                    _Logging.Info(_Header + "returned " + targetWorkDir + " to branch " + originalBranch + " after landing into " + targetBranch);
                }
                else
                {
                    await RunGitAsync(targetWorkDir, CancellationToken.None, "checkout", "--detach", originalHead!).ConfigureAwait(false);
                    _Logging.Info(_Header + "returned " + targetWorkDir + " to commit " + originalHead + " after landing into " + targetBranch);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "landing into " + targetBranch + " could not switch " + targetWorkDir + " back to "
                    + (originalBranch ?? originalHead) + "; it is still on " + targetBranch + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Pull latest changes from remote into a working directory.
        /// </summary>
        public async Task PullAsync(string workingDirectory, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));

            _Logging.Debug(_Header + "pulling latest in " + workingDirectory);
            await RunGitAsync(workingDirectory, "pull").ConfigureAwait(false);
        }

        /// <summary>
        /// Check if a pull request has been merged using the gh CLI.
        /// </summary>
        public async Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (String.IsNullOrEmpty(prUrl)) throw new ArgumentNullException(nameof(prUrl));

            try
            {
                string result = await RunProcessAsync(workingDirectory, "gh", "pr", "view", prUrl, "--json", "state").ConfigureAwait(false);
                GhPullRequestView? view = DeserializeGh(result);
                return view != null && String.Equals(view.State, "MERGED", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the diff of all changes in a worktree against the base branch.
        /// </summary>
        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetConflictedFilesAsync(string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            try
            {
                string output = await RunGitAsync(worktreePath, token, "diff", "--name-only", "-z", "--diff-filter=U").ConfigureAwait(false);
                return GitMachineOutputParser.ParsePathListZ(output);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not list conflicted files in " + worktreePath + ": " + ex.Message);
                return new List<string>();
            }
        }

        /// <inheritdoc />
        public async Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Debug(_Header + "diffing worktree " + worktreePath + " against " + baseBranch);

            // Diff committed changes on the current branch vs the base branch. Branches with unrelated
            // history (merge-base exit code 1) use a direct tree-to-tree comparison against the base tip.
            string? range = await ResolveDiffRangeAsync(worktreePath, baseBranch, token).ConfigureAwait(false);
            if (range != null)
            {
                try
                {
                    return await RunGitAsync(worktreePath, token, BuildDiffArgs(range)).ConfigureAwait(false);
                }
                catch (GitCommandException ex)
                {
                    _Logging.Debug(_Header + "diff " + range + " failed (exit " + ex.ExitCode + "), diffing working tree instead");
                }
            }

            // Fallback: diff against working tree (uncommitted changes)
            return await RunGitAsync(worktreePath, token, BuildDiffArgs("HEAD")).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<GitChangedFile>> GetBranchChangesAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(baseBranch)) throw new ArgumentNullException(nameof(baseBranch));

            string? range = await ResolveDiffRangeAsync(worktreePath, baseBranch, token).ConfigureAwait(false);
            if (range == null)
                throw new InvalidOperationException("Base branch " + baseBranch + " or HEAD does not resolve in " + worktreePath);

            string nameStatus = await RunGitAsync(worktreePath, token,
                "diff", "--no-color", "--no-ext-diff", "--no-renames", "--name-status", "-z", range).ConfigureAwait(false);
            string numstat = await RunGitAsync(worktreePath, token,
                "diff", "--no-color", "--no-ext-diff", "--no-renames", "--numstat", "-z", range).ConfigureAwait(false);

            return GitMachineOutputParser.MergeNameStatusAndNumstat(
                GitMachineOutputParser.ParseNameStatusZ(nameStatus),
                GitMachineOutputParser.ParseNumstatZ(numstat));
        }

        /// <summary>
        /// Get the HEAD commit hash of a worktree.
        /// </summary>
        public async Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) return null;

            try
            {
                string result = await RunGitAsync(worktreePath, "rev-parse", "HEAD").ConfigureAwait(false);
                return result.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ForceAdvanceBranchAsync(string worktreePath, string branchName, string commitHash, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath) || String.IsNullOrEmpty(branchName) || String.IsNullOrEmpty(commitHash))
                return false;

            try
            {
                string proposed = (await RunGitAsync(worktreePath, token, "rev-parse", "--verify", commitHash + "^{commit}").ConfigureAwait(false)).Trim();
                string branchRef = "refs/heads/" + branchName;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    string current = await RefResolvesAsync(worktreePath, branchRef, token).ConfigureAwait(false)
                        ? (await RunGitAsync(worktreePath, token, "rev-parse", "--verify", branchRef).ConfigureAwait(false)).Trim()
                        : new string('0', proposed.Length);
                    if (current == proposed || (current != new string('0', proposed.Length) && await IsAncestorAsync(worktreePath, proposed, current, token).ConfigureAwait(false) == true))
                        return true; // A late read-only stage must never rewind newer integrated work.
                    if (current != new string('0', proposed.Length) && await IsAncestorAsync(worktreePath, current, proposed, token).ConfigureAwait(false) != true)
                    {
                        await RunGitAsync(worktreePath, token, "update-ref", "refs/armada/stage-commits/" + proposed, proposed).ConfigureAwait(false);
                        _Logging.Warn(_Header + "refusing divergent pipeline branch update for " + branchName + "; preserved stage commit " + proposed);
                        return false;
                    }
                    try
                    {
                        // Compare-and-swap protects against a writer advancing the ref after our ancestry check.
                        await RunGitAsync(worktreePath, token, "update-ref", branchRef, proposed, current).ConfigureAwait(false);
                        return true;
                    }
                    catch (GitCommandException) when (!token.IsCancellationRequested && attempt < 2)
                    {
                        // Re-read the ref before retrying; never overwrite a concurrent writer blindly.
                    }
                }
                return false;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "safe branch advance for " + branchName + " to " + commitHash + " failed: " + ex.ToString());
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<GitCommitMergeResult> MergeCommitAsync(string worktreePath, string commitHash, string? fetchBranch, string message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(commitHash)) throw new ArgumentNullException(nameof(commitHash));
            if (String.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));

            if (!await RefResolvesAsync(worktreePath, commitHash, token).ConfigureAwait(false))
            {
                if (!String.IsNullOrWhiteSpace(fetchBranch))
                    await ExecuteProcessAsync(worktreePath, "git", _NotARepository, token, "fetch", "origin", "refs/heads/" + fetchBranch!.Trim()).ConfigureAwait(false);
                if (!await RefResolvesAsync(worktreePath, commitHash, token).ConfigureAwait(false))
                    return new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.Missing, "Commit " + commitHash + " is not in this repository"
                        + (String.IsNullOrWhiteSpace(fetchBranch) ? "." : " and branch " + fetchBranch + " on origin does not provide it."));
            }

            if (await IsAncestorAsync(worktreePath, commitHash, "HEAD", token).ConfigureAwait(false) == true)
                return new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.AlreadyContained);

            GitProcessResult merge = await ExecuteProcessAsync(worktreePath, "git", _NoMatch, token, "merge", "--no-ff", "--no-edit", "-m", message, commitHash).ConfigureAwait(false);
            string failure = (merge.StandardError + "\n" + merge.StandardOutput).Trim();
            if (!merge.Succeeded && !await MergeInProgressAsync(worktreePath, token).ConfigureAwait(false)
                && (failure.Contains("user.email", StringComparison.OrdinalIgnoreCase) || failure.Contains("user.name", StringComparison.OrdinalIgnoreCase)
                    || failure.Contains("identity", StringComparison.OrdinalIgnoreCase)))
            {
                // No git identity on this host: merge as Armada.
                merge = await ExecuteProcessAsync(worktreePath, "git", _NoMatch, token, "-c", "user.name=Armada", "-c", "user.email=armada@localhost",
                    "merge", "--no-ff", "--no-edit", "-m", message, commitHash).ConfigureAwait(false);
                failure = (merge.StandardError + "\n" + merge.StandardOutput).Trim();
            }
            if (merge.Succeeded) return new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.Merged);
            IReadOnlyList<string> conflicted = Array.Empty<string>();
            bool resolved = false;
            try
            {
                conflicted = await GetConflictedFilesAsync(worktreePath, CancellationToken.None).ConfigureAwait(false);
                resolved = await ChangelogLandingConflict.TryResolveAsync(_Commands ?? new LocalHostCommandExecutor(), worktreePath, IsWindowsHost(worktreePath), token).ConfigureAwait(false);
            }
            catch (Exception secondary)
            {
                failure += " Merge conflict resolution failed: " + secondary.Message;
            }
            finally
            {
                // Cleanup must run even if the resolver or conflict listing throws.
                if (!resolved)
                {
                    // Abort whenever a merge may be in progress; a failed probe is not proof that none is.
                    bool mergeInProgress = true;
                    try { mergeInProgress = await MergeInProgressAsync(worktreePath, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception) { }
                    try { if (mergeInProgress) await RunGitAsync(worktreePath, CancellationToken.None, "merge", "--abort").ConfigureAwait(false); }
                    catch (Exception secondary) { failure += " Merge abort failed: " + secondary.Message; }
                }
            }
            if (resolved) return new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.Merged);

            if (conflicted.Count > 0)
            {
                GitCommitMergeResult result = new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.Conflicted, failure);
                result.ConflictedFiles = new List<string>(conflicted);
                return result;
            }
            return new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.Failed, failure.Length > 0 ? failure : "git merge exited with code " + merge.ExitCode + ".");
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetRecentCommitsForPathsAsync(string worktreePath, IReadOnlyList<string> paths, int maxPerPath, CancellationToken token = default)
        {
            List<string> results = new List<string>();
            if (String.IsNullOrEmpty(worktreePath) || paths == null || paths.Count == 0) return results;

            int perPath = Math.Max(1, maxPerPath);
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    string output = await RunGitAsync(worktreePath, token,
                        "log", "-n", perPath.ToString(), "--pretty=format:%h %s", "--", path).ConfigureAwait(false);
                    foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        results.Add(path.Trim() + ": " + line);
                    }
                }
                catch
                {
                    // Best-effort: an unknown path or a git failure contributes no entries.
                }
            }
            return results;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> FindExistingSubjectTermsAsync(string worktreePath, IReadOnlyList<string> terms, CancellationToken token = default)
        {
            List<string> found = new List<string>();
            if (String.IsNullOrEmpty(worktreePath) || terms == null || terms.Count == 0) return found;

            foreach (string term in terms)
            {
                if (String.IsNullOrWhiteSpace(term)) continue;
                bool present = false;

                // A tracked filename containing the term.
                try
                {
                    string files = await RunGitAsync(worktreePath, token, "ls-files", "*" + term + "*").ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(files)) present = true;
                }
                catch { }

                // Or the term appears in tracked file contents.
                if (!present)
                {
                    try
                    {
                        // git grep exits 1 when nothing matches: an answer, not a failure.
                        GitProcessResult grep = await ExecuteProcessAsync(worktreePath, "git", _NoMatch, token, "grep", "-l", "-F", "-e", term).ConfigureAwait(false);
                        if (grep.Succeeded && !String.IsNullOrWhiteSpace(grep.StandardOutput)) present = true;
                    }
                    catch { }
                }

                if (present) found.Add(term.Trim());
            }
            return found;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(startCommit)) throw new ArgumentNullException(nameof(startCommit));

            HashSet<string> changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // --no-renames reports both sides of a rename; -z keeps unusual path names verbatim (no C-quoting).
            string committedOutput = await RunGitAsync(worktreePath, token, "diff", "--no-renames", "--name-only", "-z", startCommit + "..HEAD").ConfigureAwait(false);
            AddChangedPaths(committedOutput, changedFiles);

            string workingTreeOutput = await RunGitAsync(worktreePath, token, "diff", "--no-renames", "--name-only", "-z", "HEAD").ConfigureAwait(false);
            AddChangedPaths(workingTreeOutput, changedFiles);

            string untrackedOutput = await RunGitAsync(worktreePath, token, "ls-files", "-z", "--others", "--exclude-standard").ConfigureAwait(false);
            AddChangedPaths(untrackedOutput, changedFiles);

            return changedFiles
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Delete a local branch from a repository.
        /// </summary>
        public async Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Debug(_Header + "deleting branch " + branchName + " from " + repoPath);
            await RunGitAsync(repoPath, "branch", "-D", branchName).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Debug(_Header + "deleting remote branch " + branchName + " from origin");
            await RunGitAsync(repoPath, "push", "origin", "--delete", branchName).ConfigureAwait(false);
        }

        /// <summary>
        /// Check if a path is a valid git repository.
        /// </summary>
        public async Task<bool> IsRepositoryAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) return false;

            if (_Commands == null && !Directory.Exists(path)) return false;
            try
            {
                GitProcessResult result = await ExecuteProcessAsync(path, "git", _NotARepository, token, "rev-parse", "--git-dir").ConfigureAwait(false);
                return result.Succeeded;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a local branch exists in the repository.
        /// </summary>
        public async Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            // show-ref --verify matches the exact ref; branch --list treats the name as a glob pattern.
            return await ShowRefExistsAsync(repoPath, "refs/heads/" + branchName, token).ConfigureAwait(false);
        }

        /// <summary>
        /// List the local branches in a repository with their tip commit and their position (ahead/behind)
        /// relative to the given default branch. The default branch is flagged and marked at zero divergence.
        /// A branch that cannot be compared has <see cref="BranchInfo.DivergenceError"/> set and keeps zero counts.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch to measure divergence against (for example "main").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches, default branch first, then by name.</returns>
        public async Task<IReadOnlyList<BranchInfo>> ListBranchesAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            IReadOnlyList<BranchInfo> branches = await ListBranchTipsAsync(repoPath, defaultBranch, token).ConfigureAwait(false);
            await MeasureBranchDivergenceAsync(repoPath, defaultBranch, branches, token).ConfigureAwait(false);
            return branches;
        }

        /// <summary>
        /// List the local branches in a repository with their tip commit, without measuring divergence (one git
        /// command). Ahead and behind are left at zero; fill them for the branches shown with
        /// <see cref="MeasureBranchDivergenceAsync"/>. Each name is the path under refs/heads.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch, flagged in the result (for example "main").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches, default branch first, then by name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
        public async Task<IReadOnlyList<BranchInfo>> ListBranchTipsAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            // %(refname) stays refs/heads/<name> when a tag uses the same short name. The listed name is that path.
            // Field separator is the unit separator (\x1f) so commit text cannot split a record.
            string format = "%(refname)\x1f%(HEAD)\x1f%(objectname:short)\x1f%(committerdate:iso-strict)\x1f%(contents:subject)";
            string raw = await RunGitAsync(repoPath, token, "for-each-ref", "--format=" + format, "refs/heads/").ConfigureAwait(false);

            List<BranchInfo> branches = new List<BranchInfo>();
            string[] lines = raw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] parts = line.Split('\x1f');
                if (parts.Length < 3) continue;

                BranchInfo info = new BranchInfo();
                info.Name = LocalBranchName(parts[0].Trim());
                info.IsCurrent = parts.Length > 1 && parts[1].Trim() == "*";
                info.CommitHash = parts.Length > 2 ? parts[2].Trim() : null;
                if (parts.Length > 3 && DateTimeOffset.TryParse(parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset parsedDate))
                    info.CommitDate = parsedDate.UtcDateTime;
                info.CommitSubject = parts.Length > 4 ? parts[4].Trim() : null;
                info.IsDefault = String.Equals(info.Name, defaultBranch, StringComparison.Ordinal);
                branches.Add(info);
            }

            branches.Sort((a, b) =>
            {
                if (a.IsDefault && !b.IsDefault) return -1;
                if (!a.IsDefault && b.IsDefault) return 1;
                return String.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            return branches;
        }

        /// <summary>
        /// Fill Ahead and Behind on each branch, relative to the default branch: one git command per branch other
        /// than the default branch, which stays at zero. The comparison uses the refs/heads form of each name.
        /// A comparison that fails, or whose output is not two non-negative integers, sets
        /// <see cref="BranchInfo.DivergenceError"/> and leaves the counts unchanged.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch to measure against (for example "main").</param>
        /// <param name="branches">Branches to measure, as listed by <see cref="ListBranchTipsAsync"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty, or branches is null.</exception>
        public async Task MeasureBranchDivergenceAsync(string repoPath, string defaultBranch, IReadOnlyList<BranchInfo> branches, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (branches == null) throw new ArgumentNullException(nameof(branches));

            foreach (BranchInfo info in branches)
            {
                if (info.IsDefault || String.Equals(info.Name, defaultBranch, StringComparison.Ordinal)) continue;
                try
                {
                    string counts = await RunGitAsync(
                        repoPath,
                        token,
                        "rev-list",
                        "--left-right",
                        "--count",
                        _HeadsRefPrefix + defaultBranch + "..." + _HeadsRefPrefix + info.Name).ConfigureAwait(false);
                    string[] countParts = counts.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    int behind;
                    int ahead;
                    if (countParts.Length == 2
                        && Int32.TryParse(countParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out behind)
                        && Int32.TryParse(countParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ahead))
                    {
                        // left = commits on default not on branch (branch is behind); right = commits on branch not on default (ahead).
                        info.Behind = behind;
                        info.Ahead = ahead;
                        info.DivergenceError = null;
                    }
                    else
                    {
                        info.DivergenceError = "Unable to calculate branch divergence.";
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    info.DivergenceError = "Unable to calculate branch divergence.";
                }
            }
        }

        /// <summary>
        /// Push a named local branch to the remote.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="branchName">Branch to push.</param>
        /// <param name="remoteName">Remote name (default "origin").</param>
        /// <param name="token">Cancellation token.</param>
        public async Task PushLocalBranchAsync(string repoPath, string branchName, string remoteName = "origin", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Debug(_Header + "pushing branch " + branchName + " to " + remoteName + " from " + repoPath);
            await RunGitAsync(repoPath, "push", remoteName, "refs/heads/" + branchName + ":refs/heads/" + branchName).ConfigureAwait(false);
        }

        /// <summary>
        /// Merge one branch into another within a repository using a temporary worktree, then optionally push
        /// the updated target. A merge conflict throws with the git error, leaving the target branch unchanged.
        /// </summary>
        /// <param name="repoPath">Bare repository path.</param>
        /// <param name="sourceBranch">Branch to merge from.</param>
        /// <param name="targetBranch">Branch to merge into.</param>
        /// <param name="push">Whether to push the target branch to the remote after a successful merge.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task MergeBranchesAsync(string repoPath, string sourceBranch, string targetBranch, bool push, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(sourceBranch)) throw new ArgumentNullException(nameof(sourceBranch));
            if (String.IsNullOrEmpty(targetBranch)) throw new ArgumentNullException(nameof(targetBranch));
            if (String.Equals(sourceBranch, targetBranch, StringComparison.Ordinal))
                throw new InvalidOperationException("Cannot merge a branch into itself.");

            string mergeDirName = "merge-" + Guid.NewGuid().ToString("N");
            string worktreePath = Path.Combine(Path.GetDirectoryName(repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                ?? Path.GetTempPath(), mergeDirName);

            _Logging.Debug(_Header + "merging " + sourceBranch + " into " + targetBranch + " via " + worktreePath);
            try
            {
                await RunGitAsync(repoPath, "worktree", "add", worktreePath, targetBranch).ConfigureAwait(false);

                try
                {
                    await RunGitAsync(worktreePath, "merge", "--no-edit", sourceBranch).ConfigureAwait(false);
                }
                catch
                {
                    try { await RunGitAsync(worktreePath, "merge", "--abort").ConfigureAwait(false); } catch { }
                    throw;
                }

                if (push)
                    await RunGitAsync(worktreePath, "push", "origin", "HEAD:refs/heads/" + targetBranch).ConfigureAwait(false);
            }
            finally
            {
                try { await RunGitAsync(repoPath, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false); } catch { }
                try { if (_Commands == null && Directory.Exists(worktreePath)) Directory.Delete(worktreePath, true); } catch { }
            }
        }

        /// <summary>
        /// Ensure a local branch exists, preferring the matching remote branch and otherwise
        /// creating it from the repository's default available history.
        /// </summary>
        public async Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            await FetchAsync(repoPath, token).ConfigureAwait(false);

            if (await SyncLocalBranchFromRemoteAsync(repoPath, branchName).ConfigureAwait(false))
            {
                return true;
            }

            if (await BranchExistsAsync(repoPath, branchName, token).ConfigureAwait(false))
            {
                return true;
            }

            string? baseRef = await ResolveFallbackBranchSourceAsync(repoPath).ConfigureAwait(false);
            if (String.IsNullOrEmpty(baseRef))
            {
                return false;
            }

            _Logging.Debug(_Header + "creating local branch " + branchName + " from " + baseRef);
            await RunGitAsync(repoPath, "branch", branchName, baseRef).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<GitWorktreeEntry>?> ListWorktreeEntriesAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            return await ListWorktreesAsync(repoPath, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Check if a path is registered as a git worktree.
        /// </summary>
        public async Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            try
            {
                List<GitWorktreeEntry> worktrees = await ListWorktreesAsync(repoPath, token).ConfigureAwait(false);
                // git reports symlink-resolved paths (e.g. /private/var on macOS), so compare canonical forms.
                string normalizedTarget = PathCanonicalizer.Canonicalize(worktreePath);

                foreach (GitWorktreeEntry entry in worktrees)
                {
                    string normalizedRegistered = PathCanonicalizer.Canonicalize(entry.Path);
                    if (String.Equals(normalizedRegistered, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is TimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Fetch every configured remote with pruning without changing any repository configuration.
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the fetch fails.</exception>
        /// <exception cref="TimeoutException">Thrown when the fetch does not finish in time.</exception>
        public async Task FetchRemotesAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            _Logging.Debug(_Header + "fetching remotes: " + repoPath);
            await RunGitAsync(repoPath, token, "fetch", "--all", "--prune", "--quiet").ConfigureAwait(false);
        }

        /// <summary>
        /// Count commits between two refs (git rev-list --left-right --count baseRef...headRef).
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="baseRef">Base ref.</param>
        /// <param name="headRef">Head ref.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The counts, or null when either ref does not resolve.</returns>
        public async Task<GitDivergenceCounts?> GetDivergenceAsync(string repoPath, string baseRef, string headRef, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(baseRef)) throw new ArgumentNullException(nameof(baseRef));
            if (String.IsNullOrEmpty(headRef)) throw new ArgumentNullException(nameof(headRef));

            if (!await RefResolvesAsync(repoPath, baseRef, token).ConfigureAwait(false)) return null;
            if (!await RefResolvesAsync(repoPath, headRef, token).ConfigureAwait(false)) return null;

            string counts;
            try
            {
                counts = await RunGitAsync(repoPath, token, "rev-list", "--left-right", "--count", baseRef + "..." + headRef).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            string[] parts = counts.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;
            if (!Int32.TryParse(parts[0], out int behind)) return null;
            if (!Int32.TryParse(parts[1], out int ahead)) return null;
            return new GitDivergenceCounts(ahead, behind);
        }

        /// <inheritdoc />
        public async Task<bool?> IsAncestorAsync(string repoPath, string ancestorRef, string descendantRef, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(ancestorRef)) throw new ArgumentNullException(nameof(ancestorRef));
            if (String.IsNullOrEmpty(descendantRef)) throw new ArgumentNullException(nameof(descendantRef));

            if (!await RefResolvesAsync(repoPath, ancestorRef, token).ConfigureAwait(false)) return null;
            if (!await RefResolvesAsync(repoPath, descendantRef, token).ConfigureAwait(false)) return null;

            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "merge-base", "--is-ancestor", ancestorRef, descendantRef).ConfigureAwait(false);
            if (result.ExitCode == 0) return true;
            if (result.ExitCode == 1) return false;
            return null;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> FindCommitsByTrailerAsync(string repoPath, string revision, string trailerKey, string trailerValue, DateTime? sinceUtc, int maxCount, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(revision)) throw new ArgumentNullException(nameof(revision));
            if (String.IsNullOrEmpty(trailerKey)) throw new ArgumentNullException(nameof(trailerKey));
            if (String.IsNullOrEmpty(trailerValue)) throw new ArgumentNullException(nameof(trailerValue));

            List<string> found = new List<string>();
            int limit = Math.Max(1, maxCount);
            if (!await RefResolvesAsync(repoPath, revision, token).ConfigureAwait(false)) return found;

            // The grep narrows the walk to messages that mention the value; the parsed trailers then decide, so a message
            // that only quotes the value in prose is not a match. The candidate window is wider than the result limit for
            // that reason.
            List<string> args = new List<string>
            {
                "log",
                revision,
                "--fixed-strings",
                "--grep=" + trailerValue,
                "--max-count=" + (limit * 4).ToString(CultureInfo.InvariantCulture),
                "--format=%H%x09%(trailers:key=" + trailerKey + ",valueonly,separator=%x09)"
            };
            if (sinceUtc.HasValue)
                args.Add("--since=" + sinceUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

            GitProcessResult result;
            try
            {
                result = await ExecuteProcessAsync(repoPath, "git", token, args.ToArray()).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "trailer search failed in " + repoPath + ": " + ex.Message);
                return found;
            }

            if (!result.Succeeded || String.IsNullOrEmpty(result.StandardOutput)) return found;
            foreach (string rawLine in result.StandardOutput.Replace("\r", String.Empty).Split('\n'))
            {
                string[] fields = rawLine.Split('\t');
                if (fields.Length < 2 || String.IsNullOrWhiteSpace(fields[0])) continue;
                bool matches = false;
                for (int i = 1; i < fields.Length; i++)
                {
                    if (String.Equals(fields[i].Trim(), trailerValue, StringComparison.Ordinal)) matches = true;
                }

                if (matches) found.Add(fields[0].Trim());
                if (found.Count >= limit) break;
            }

            return found;
        }

        /// <inheritdoc />
        public async Task<bool> CommitAllAsync(string worktreePath, string message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));

            string status = (await RunGitAsync(worktreePath, token, "status", "--porcelain", "--untracked-files=all").ConfigureAwait(false)).Trim();
            if (status.Length == 0) return false;

            // Untracked scratch files (README.md.backup, *.orig) are not the captain's work: leave them in the worktree.
            List<string> scratch = new List<string>();
            string untracked = await RunGitAsync(worktreePath, token, "ls-files", "--others", "--exclude-standard", "-z").ConfigureAwait(false);
            foreach (string path in GitMachineOutputParser.ParsePathListZ(untracked))
                if (ScratchFileMatcher.IsScratchFile(path)) scratch.Add(path);

            await RunGitAsync(worktreePath, token, "add", "-A").ConfigureAwait(false);
            if (scratch.Count > 0)
            {
                for (int i = 0; i < scratch.Count; i += 100)
                {
                    List<string> args = new List<string> { "reset", "-q", "--" };
                    foreach (string path in scratch.Skip(i).Take(100)) args.Add(":(literal)" + path);
                    await RunGitAsync(worktreePath, token, args.ToArray()).ConfigureAwait(false);
                }

                string staged = await RunGitAsync(worktreePath, token, "diff", "--cached", "--name-only", "-z").ConfigureAwait(false);
                if (GitMachineOutputParser.ParsePathListZ(staged).Count == 0) return false;
            }

            try
            {
                await RunGitAsync(worktreePath, token, "commit", "--no-verify", "-m", message).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("user.email", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("user.name", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("identity", StringComparison.OrdinalIgnoreCase))
            {
                // No git identity on this host: commit as Armada rather than lose the work.
                await RunGitAsync(worktreePath, token, "-c", "user.name=Armada", "-c", "user.email=armada@localhost", "commit", "--no-verify", "-m", message).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Summarize the working tree (git status --porcelain).
        /// </summary>
        /// <param name="repoPath">Working tree path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The working tree status.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git status fails.</exception>
        public async Task<GitWorkingTreeStatus> GetWorkingTreeStatusAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            string output = await RunGitAsync(repoPath, token, "status", "--porcelain=v2", "-z", "--untracked-files=all").ConfigureAwait(false);
            GitWorkingTreeStatus status = new GitWorkingTreeStatus();
            int modified = 0;
            int untracked = 0;
            List<string> fields = GitMachineOutputParser.SplitNul(output);
            for (int i = 0; i < fields.Count; i++)
            {
                string field = fields[i];
                if (field.Length < 2 || field[1] != ' ') continue;
                switch (field[0])
                {
                    case '1':
                    case 'u':
                        modified++;
                        break;
                    case '2':
                        // A rename or copy record is followed by its original path as a separate field.
                        modified++;
                        i++;
                        break;
                    case '?':
                        untracked++;
                        break;
                }
            }

            status.ModifiedCount = modified;
            status.UntrackedCount = untracked;
            return status;
        }

        /// <summary>
        /// Get the checked-out branch name.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branch name, or null when HEAD is detached or cannot be read.</returns>
        public async Task<string?> GetCurrentBranchAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "symbolic-ref", "--short", "-q", "HEAD").ConfigureAwait(false);
            if (!result.Succeeded) return null;
            string trimmed = result.StandardOutput.Trim();
            return String.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        /// <summary>
        /// Get the committer timestamp of the HEAD commit in UTC.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The timestamp, or null when the repository has no commits.</returns>
        public async Task<DateTime?> GetLastCommitUtcAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (!await RefResolvesAsync(repoPath, "HEAD", token).ConfigureAwait(false)) return null;

            try
            {
                string output = await RunGitAsync(repoPath, token, "log", "-1", "--format=%cI", "HEAD").ConfigureAwait(false);
                if (DateTimeOffset.TryParse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset parsed))
                    return parsed.UtcDateTime;
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// List repository-relative paths of every file tracked at HEAD.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tracked file paths with forward slashes; empty when the repository has no commits.</returns>
        public async Task<IReadOnlyList<string>> ListTrackedFilesAsync(string repoPath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (!await RefResolvesAsync(repoPath, "HEAD", token).ConfigureAwait(false)) return new List<string>();

            string output = await RunGitAsync(repoPath, token, "ls-tree", "-r", "-z", "--name-only", "HEAD").ConfigureAwait(false);
            return GitMachineOutputParser.ParsePathListZ(output);
        }

        /// <summary>
        /// Check whether a path is a bare git repository.
        /// </summary>
        /// <param name="path">Path to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True for a bare repository; false otherwise.</returns>
        public async Task<bool> IsBareRepositoryAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) return false;
            if (_Commands == null && !Directory.Exists(path)) return false;
            GitProcessResult result = await ExecuteProcessAsync(path, "git", _NotARepository, token, "rev-parse", "--is-bare-repository").ConfigureAwait(false);
            return result.Succeeded && String.Equals(result.StandardOutput.Trim(), "true", StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public async Task<string?> ResolveBranchTipAsync(string repoPath, string branch, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branch)) throw new ArgumentNullException(nameof(branch));
            if (!GitRevisionNames.IsSafeBranchName(branch)) throw new ArgumentException("Branch is not a valid branch name: " + branch, nameof(branch));

            List<string> candidates = new List<string>();
            if (!GitRevisionNames.IsFullCommitId(branch) && !branch.StartsWith("refs/", StringComparison.Ordinal))
            {
                candidates.Add("refs/heads/" + branch);
                candidates.Add("refs/remotes/origin/" + branch);
            }
            candidates.Add(branch);

            foreach (string candidate in candidates)
            {
                GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "rev-parse", "--verify", "--quiet", candidate + "^{commit}").ConfigureAwait(false);
                if (!result.Succeeded) continue;
                string sha = result.StandardOutput.Trim();
                if (GitRevisionNames.IsFullCommitId(sha)) return sha;
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<VesselCommitActivity> GetCommitActivityAsync(string repoPath, string branch, DateTime fromDate, DateTime toDate, int utcOffsetMinutes, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branch)) throw new ArgumentNullException(nameof(branch));
            if (Math.Abs(utcOffsetMinutes) > Constants.VesselHistoryMaxUtcOffsetMinutes) throw new ArgumentOutOfRangeException(nameof(utcOffsetMinutes));
            DateTime from = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Unspecified);
            DateTime to = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Unspecified);
            if (to < from) throw new ArgumentOutOfRangeException(nameof(toDate), "toDate is before fromDate");

            VesselCommitActivity activity = new VesselCommitActivity();
            activity.Branch = branch;
            activity.From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            activity.To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            activity.UtcOffsetMinutes = utcOffsetMinutes;
            int dayCount = (int)(to - from).TotalDays + 1;
            int[] counts = new int[dayCount];
            for (int d = 0; d < dayCount; d++)
            {
                activity.Days.Add(new VesselCommitActivityDay { Date = from.AddDays(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            }

            string? tip = await ResolveBranchTipAsync(repoPath, branch, token).ConfigureAwait(false);
            if (tip == null)
            {
                activity.Error = "Branch " + branch + " was not found or has no commits";
                return activity;
            }

            long? first = null;
            long? last = null;
            string tipOutput = await RunGitAsync(repoPath, token, "log", "--no-show-signature", "-1", "--format=%ct", tip, "--").ConfigureAwait(false);
            foreach (long seconds in ParseUnixSecondsLines(tipOutput)) last = seconds;
            string rootOutput = await RunGitAsync(repoPath, token, "log", "--no-show-signature", "--max-parents=0", "--format=%ct", tip, "--").ConfigureAwait(false);
            foreach (long seconds in ParseUnixSecondsLines(rootOutput))
            {
                if (!first.HasValue || seconds < first.Value) first = seconds;
            }

            // The range in UTC: local midnight of fromDate to local midnight after toDate, shifted by the offset. git's
            // --since/--until get a day of slack on each side; the exact bounds are applied here.
            long offsetSeconds = utcOffsetMinutes * 60L;
            long rangeStart = new DateTimeOffset(from, TimeSpan.Zero).ToUnixTimeSeconds() - offsetSeconds;
            long rangeEnd = new DateTimeOffset(to.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds() - offsetSeconds;
            string since = "--since=@" + (rangeStart - 86400L).ToString(CultureInfo.InvariantCulture) + " +0000";
            string until = "--until=@" + (rangeEnd + 86400L).ToString(CultureInfo.InvariantCulture) + " +0000";
            string rangeOutput = await RunGitAsync(repoPath, token, "log", "--no-show-signature", "--format=%ct", since, until, tip, "--").ConfigureAwait(false);

            foreach (long seconds in ParseUnixSecondsLines(rangeOutput))
            {
                if (!first.HasValue || seconds < first.Value) first = seconds;
                if (!last.HasValue || seconds > last.Value) last = seconds;
                if (seconds < rangeStart || seconds >= rangeEnd) continue;
                int index = (int)((seconds - rangeStart) / 86400L);
                if (index >= 0 && index < dayCount) counts[index]++;
            }

            int total = 0;
            int max = 0;
            for (int d = 0; d < dayCount; d++)
            {
                activity.Days[d].Count = counts[d];
                total += counts[d];
                if (counts[d] > max) max = counts[d];
            }

            activity.TotalCommits = total;
            activity.MaxDayCount = max;
            activity.FirstCommitUtc = first.HasValue ? DateTimeOffset.FromUnixTimeSeconds(first.Value).UtcDateTime : null;
            activity.LastCommitUtc = last.HasValue ? DateTimeOffset.FromUnixTimeSeconds(last.Value).UtcDateTime : null;
            return activity;
        }

        /// <inheritdoc />
        public async Task<GitCommitLogPage> GetCommitLogAsync(string repoPath, string tipSha, long? untilUnixSeconds, int skip, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(tipSha)) throw new ArgumentNullException(nameof(tipSha));
            if (!GitRevisionNames.IsFullCommitId(tipSha)) throw new ArgumentException("tipSha must be a full commit id", nameof(tipSha));
            if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
            if (limit < 1 || limit > Constants.VesselHistoryMaxPageSize) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and " + Constants.VesselHistoryMaxPageSize + ".");

            List<string> args = new List<string>
            {
                "log", "--no-show-signature", "--date-order", "-z", "--no-color", "--no-ext-diff", "--no-textconv",
                "--format=" + GitMachineOutputParser.CommitLogFormat,
                "--raw", "--numstat", "-M", "--root", "--diff-merges=first-parent",
                "--skip=" + skip.ToString(CultureInfo.InvariantCulture),
                "--max-count=" + (limit + 1).ToString(CultureInfo.InvariantCulture)
            };
            if (untilUnixSeconds.HasValue)
                args.Add("--until=@" + untilUnixSeconds.Value.ToString(CultureInfo.InvariantCulture) + " +0000");
            args.Add(tipSha);
            args.Add("--");

            string output = await RunGitAsync(repoPath, token, args.ToArray()).ConfigureAwait(false);
            List<VesselCommit> commits = GitMachineOutputParser.ParseCommitLogZ(output, Constants.VesselHistoryMaxFilesPerCommit);

            GitCommitLogPage page = new GitCommitLogPage();
            page.HasMore = commits.Count > limit;
            page.Commits = commits.Count > limit ? commits.GetRange(0, limit) : commits;
            return page;
        }

        /// <inheritdoc />
        public async Task<string?> GetMergeBaseAsync(string repoPath, string a, string b, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(a)) throw new ArgumentNullException(nameof(a));
            if (String.IsNullOrEmpty(b)) throw new ArgumentNullException(nameof(b));
            RequireRevision(a, nameof(a));
            RequireRevision(b, nameof(b));

            try
            {
                if (!await RefResolvesAsync(repoPath, a, token).ConfigureAwait(false)) return null;
                if (!await RefResolvesAsync(repoPath, b, token).ConfigureAwait(false)) return null;
                GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "merge-base", a, b).ConfigureAwait(false);
                if (!result.Succeeded) return null;
                string sha = result.StandardOutput.Trim();
                return GitRevisionNames.IsFullCommitId(sha) ? sha : null;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "merge-base failed in " + repoPath + ": " + ex.Message);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<GitCommitRange?> GetCommitRangeAsync(string repoPath, string fromRef, string toRef, int limit, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(fromRef)) throw new ArgumentNullException(nameof(fromRef));
            if (String.IsNullOrEmpty(toRef)) throw new ArgumentNullException(nameof(toRef));
            RequireRevision(fromRef, nameof(fromRef));
            RequireRevision(toRef, nameof(toRef));
            if (limit < 1 || limit > GitCommitRange.MaxLimit) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and " + GitCommitRange.MaxLimit + ".");

            try
            {
                string? from = await ResolveCommitShaAsync(repoPath, fromRef, token).ConfigureAwait(false);
                if (from == null) return null;
                string? to = await ResolveCommitShaAsync(repoPath, toRef, token).ConfigureAwait(false);
                if (to == null) return null;

                GitProcessResult count = await ExecuteProcessAsync(repoPath, "git", token, "rev-list", "--count", from + ".." + to).ConfigureAwait(false);
                if (!count.Succeeded || !Int32.TryParse(count.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int total)) return null;

                GitCommitRange range = new GitCommitRange { FromSha = from, ToSha = to, TotalCount = total };
                if (total == 0) return range;

                GitProcessResult log = await ExecuteProcessAsync(repoPath, "git", token,
                    "log", "--no-show-signature", "--date-order", "-z", "--no-color", "--no-ext-diff", "--no-textconv",
                    "--format=" + GitMachineOutputParser.CommitLogFormat,
                    "--raw", "--numstat", "-M", "--diff-merges=first-parent",
                    "--max-count=" + limit.ToString(CultureInfo.InvariantCulture),
                    from + ".." + to, "--").ConfigureAwait(false);
                if (!log.Succeeded) return null;

                foreach (VesselCommit commit in GitMachineOutputParser.ParseCommitLogZ(log.StandardOutput, GitCommitRange.MaxPathsPerCommit))
                {
                    GitCommitInfo info = new GitCommitInfo
                    {
                        Sha = commit.Sha,
                        AuthorName = commit.AuthorName,
                        AuthorEmail = commit.AuthorEmail,
                        AuthoredUtc = commit.AuthoredUtc,
                        CommittedUtc = commit.CommittedUtc,
                        Subject = commit.Subject,
                        IsMerge = commit.IsMerge,
                        Trailers = GitTrailerParser.Parse(commit.Body),
                        ChangedPathTotal = Math.Max(commit.FilesChanged, commit.Files.Count)
                    };
                    foreach (GitChangedFile file in commit.Files)
                    {
                        if (!String.IsNullOrEmpty(file.Path) && info.ChangedPaths.Count < GitCommitRange.MaxPathsPerCommit) info.ChangedPaths.Add(file.Path);
                    }
                    range.Commits.Add(info);
                }

                range.Truncated = range.TotalCount > range.Commits.Count;
                return range;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "commit range " + fromRef + ".." + toRef + " failed in " + repoPath + ": " + ex.Message);
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string LocalBranchName(string refName)
        {
            if (refName.StartsWith(_HeadsRefPrefix, StringComparison.Ordinal))
                return refName.Substring(_HeadsRefPrefix.Length);
            return refName;
        }

        private static List<long> ParseUnixSecondsLines(string output)
        {
            List<long> values = new List<long>();
            foreach (string line in (output ?? String.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Int64.TryParse(line, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long seconds)) values.Add(seconds);
            }
            return values;
        }

        private async Task<bool> MergeInProgressAsync(string worktreePath, CancellationToken token)
        {
            GitProcessResult result = await ExecuteProcessAsync(worktreePath, "git", _NoMatch, token, "rev-parse", "-q", "--verify", "MERGE_HEAD").ConfigureAwait(false);
            return result.Succeeded;
        }

        private static void RequireRevision(string revision, string name)
        {
            if (revision.StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException(name + " must not start with '-'.", name);
        }

        private async Task<string?> ResolveCommitShaAsync(string repoPath, string gitRef, CancellationToken token)
        {
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "rev-parse", "--verify", "--quiet", gitRef + "^{commit}").ConfigureAwait(false);
            if (!result.Succeeded) return null;
            string sha = result.StandardOutput.Trim();
            return GitRevisionNames.IsFullCommitId(sha) ? sha : null;
        }

        private async Task<bool> RefResolvesAsync(string repoPath, string gitRef, CancellationToken token)
        {
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "rev-parse", "--verify", "--quiet", gitRef + "^{commit}").ConfigureAwait(false);
            return result.Succeeded;
        }

        private async Task<string> RunGitAsync(string? workingDirectory, params string[] args)
        {
            return await RunProcessAsync(workingDirectory, "git", args).ConfigureAwait(false);
        }

        private async Task<string> RunGitAsync(string? workingDirectory, CancellationToken token, params string[] args)
        {
            return await RunProcessAsync(workingDirectory, "git", token, args).ConfigureAwait(false);
        }

        private async Task EnsureTrackedFilesCleanAsync(string worktreePath, CancellationToken token)
        {
            // Keep the leading status columns (" M path"): only trailing whitespace is trimmed.
            string status = (await RunGitAsync(
                worktreePath,
                token,
                "status",
                "--porcelain",
                "--untracked-files=no").ConfigureAwait(false)).TrimEnd();

            if (String.IsNullOrWhiteSpace(status))
            {
                return;
            }

            throw new GitDirtyCheckoutException(worktreePath, status);
        }

        private static void AddChangedPaths(string gitOutput, HashSet<string> changedFiles)
        {
            foreach (string path in GitMachineOutputParser.ParsePathListZ(gitOutput))
            {
                changedFiles.Add(path);
            }
        }

        private async Task<string> ResolveCommitAsync(string workingDirectory, string gitRef)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (String.IsNullOrEmpty(gitRef)) throw new ArgumentNullException(nameof(gitRef));

            string result = await RunGitAsync(workingDirectory, "rev-parse", "--verify", gitRef).ConfigureAwait(false);
            return result.Trim();
        }

        private async Task<string> ResolveWorktreeRepoPathAsync(string worktreePath)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            string commonDir = (await RunGitAsync(worktreePath, "rev-parse", "--git-common-dir").ConfigureAwait(false)).Trim();
            if (String.IsNullOrEmpty(commonDir))
            {
                throw new InvalidOperationException("Unable to resolve common git dir for worktree " + worktreePath);
            }

            if (!Path.IsPathRooted(commonDir))
            {
                commonDir = Path.GetFullPath(Path.Combine(worktreePath, commonDir));
            }

            return commonDir;
        }

        private async Task EnsureSafeFetchRefspecAsync(string repoPath)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            await RunGitAsync(repoPath, "config", "--replace-all", "remote.origin.fetch", _SafeFetchRefspec).ConfigureAwait(false);
        }

        private async Task<string?> ResolveFallbackBranchSourceAsync(string repoPath)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            string? remoteHeadRef = await TryResolveRemoteHeadRefAsync(repoPath).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(remoteHeadRef))
            {
                return remoteHeadRef;
            }

            string[] preferredRefs =
            {
                "refs/remotes/origin/main",
                "refs/remotes/origin/master",
                "refs/heads/main",
                "refs/heads/master"
            };

            foreach (string gitRef in preferredRefs)
            {
                if (await RefExistsAsync(repoPath, gitRef).ConfigureAwait(false))
                {
                    return gitRef;
                }
            }

            string? firstRemoteRef = await GetFirstBranchRefAsync(repoPath, "refs/remotes/origin").ConfigureAwait(false);
            if (!String.IsNullOrEmpty(firstRemoteRef))
            {
                return firstRemoteRef;
            }

            return await GetFirstBranchRefAsync(repoPath, "refs/heads").ConfigureAwait(false);
        }

        private async Task<string?> TryResolveRemoteHeadRefAsync(string repoPath)
        {
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, CancellationToken.None, "symbolic-ref", "-q", "refs/remotes/origin/HEAD").ConfigureAwait(false);
            if (!result.Succeeded) return null;

            string remoteHead = result.StandardOutput.Trim();
            if (!String.IsNullOrEmpty(remoteHead) && await RefExistsAsync(repoPath, remoteHead).ConfigureAwait(false))
            {
                return remoteHead;
            }

            return null;
        }

        private async Task<bool> RefExistsAsync(string repoPath, string gitRef)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(gitRef)) throw new ArgumentNullException(nameof(gitRef));

            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, CancellationToken.None, "rev-parse", "--verify", "--quiet", gitRef).ConfigureAwait(false);
            return result.Succeeded;
        }

        private async Task<string?> GetFirstBranchRefAsync(string repoPath, string refPrefix)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(refPrefix)) throw new ArgumentNullException(nameof(refPrefix));

            string refs;
            try
            {
                refs = await RunGitAsync(repoPath, "for-each-ref", "--format=%(refname)", refPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            foreach (string line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string gitRef = line.Trim();
                if (String.IsNullOrEmpty(gitRef)) continue;
                if (gitRef.EndsWith("/HEAD", StringComparison.Ordinal)) continue;
                return gitRef;
            }

            return null;
        }

        private async Task<bool> SyncLocalBranchFromRemoteAsync(string repoPath, string branchName)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            if (!await ShowRefExistsAsync(repoPath, "refs/remotes/origin/" + branchName, CancellationToken.None).ConfigureAwait(false))
            {
                return false;
            }

            if (await IsBranchCheckedOutInWorktreeAsync(repoPath, branchName).ConfigureAwait(false))
            {
                _Logging.Debug(_Header + "skipping local ref sync for " + branchName + " because it is checked out in a worktree");
                return await BranchExistsAsync(repoPath, branchName).ConfigureAwait(false);
            }

            if (await BranchExistsAsync(repoPath, branchName).ConfigureAwait(false))
            {
                await RunGitAsync(repoPath, "branch", "-f", branchName, "refs/remotes/origin/" + branchName).ConfigureAwait(false);
            }
            else
            {
                await RunGitAsync(repoPath, "branch", branchName, "refs/remotes/origin/" + branchName).ConfigureAwait(false);
            }

            return true;
        }

        private async Task<bool> IsBranchCheckedOutInWorktreeAsync(string repoPath, string branchName)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            List<GitWorktreeEntry> worktrees = await ListWorktreesAsync(repoPath, CancellationToken.None).ConfigureAwait(false);
            string targetRef = "refs/heads/" + branchName;

            foreach (GitWorktreeEntry entry in worktrees)
            {
                if (String.Equals(entry.BranchRef, targetRef, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsWindowsHost(string workDir)
        {
            // A remote (Harbor) dock runs on its own OS; infer it from the dock path rather than this process.
            if (_Commands == null) return OperatingSystem.IsWindows();
            return workDir.Contains('\\') || (workDir.Length >= 2 && workDir[1] == ':' && Char.IsLetter(workDir[0]));
        }

        private async Task RestoreAfterFailedMergeAsync(string targetWorkDir, string sourceRepoPath, string? targetBranch, CancellationToken token)
        {
            List<string> cleanupErrors = new List<string>();
            bool mergeInProgress = false;
            try
            {
                GitProcessResult mergeHead = await ExecuteProcessAsync(targetWorkDir, "git", _NoMatch, token, "rev-parse", "-q", "--verify", "MERGE_HEAD").ConfigureAwait(false);
                mergeInProgress = mergeHead.Succeeded;
            }
            catch (Exception ex)
            {
                mergeInProgress = true; // Still attempt abort when the state probe fails.
                cleanupErrors.Add(ex.Message);
                _Logging.Warn(_Header + "unable to check merge state in " + targetWorkDir + ": " + ex.Message);
            }

            if (!mergeInProgress)
            {
                _Logging.Debug(_Header + "no merge in progress to abort in " + targetWorkDir);
            }
            else
            {
                try
                {
                    await RunGitAsync(targetWorkDir, token, "merge", "--abort").ConfigureAwait(false);
                    _Logging.Warn(_Header + "aborted failed merge in " + targetWorkDir);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(ex.Message);
                    _Logging.Warn(_Header + "unable to abort failed merge in " + targetWorkDir + ": " + ex.ToString());
                }
            }

            try
            {
                await RunGitAsync(targetWorkDir, token, "reset", "--merge").ConfigureAwait(false);
                _Logging.Warn(_Header + "reset merge state in " + targetWorkDir);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex.Message);
                _Logging.Warn(_Header + "unable to reset merge state in " + targetWorkDir + ": " + ex.ToString());
            }

            if (!String.IsNullOrEmpty(targetBranch))
            {
                try
                {
                    await EnsureTargetBranchCheckedOutAsync(targetWorkDir, sourceRepoPath, targetBranch, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(ex.Message);
                    _Logging.Warn(_Header + "unable to return to target branch " + targetBranch + " after failed merge in " + targetWorkDir + ": " + ex.ToString());
                }
            }
            if (cleanupErrors.Count > 0) throw new InvalidOperationException(String.Join("; ", cleanupErrors));
        }

        private async Task EnsureTargetBranchCheckedOutAsync(string targetWorkDir, string sourceRepoPath, string targetBranch, CancellationToken token)
        {
            if (String.IsNullOrEmpty(targetWorkDir)) throw new ArgumentNullException(nameof(targetWorkDir));
            if (String.IsNullOrEmpty(sourceRepoPath)) throw new ArgumentNullException(nameof(sourceRepoPath));
            if (String.IsNullOrEmpty(targetBranch)) throw new ArgumentNullException(nameof(targetBranch));

            if (await BranchExistsAsync(targetWorkDir, targetBranch, token).ConfigureAwait(false))
            {
                await RunGitAsync(targetWorkDir, token, "checkout", targetBranch).ConfigureAwait(false);
                return;
            }

            if (await TryEnsureLocalBranchAsync(targetWorkDir, targetBranch, token).ConfigureAwait(false))
            {
                await RunGitAsync(targetWorkDir, token, "checkout", targetBranch).ConfigureAwait(false);
                return;
            }

            try
            {
                string fetchedTargetBranchRef = await FetchBranchIntoLocalRefAsync(targetWorkDir, sourceRepoPath, targetBranch, token).ConfigureAwait(false);
                await RunGitAsync(targetWorkDir, token, "checkout", "-B", targetBranch, fetchedTargetBranchRef).ConfigureAwait(false);
                await DeleteLocalRefQuietlyAsync(targetWorkDir, fetchedTargetBranchRef, token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Unable to materialize target branch " + targetBranch + " in " + targetWorkDir +
                    " using origin or source repo " + sourceRepoPath + ": " + ex.Message,
                    ex);
            }
        }

        private async Task<string> FetchBranchIntoLocalRefAsync(string targetWorkDir, string sourceRepoPath, string branchName, CancellationToken token)
        {
            if (String.IsNullOrEmpty(targetWorkDir)) throw new ArgumentNullException(nameof(targetWorkDir));
            if (String.IsNullOrEmpty(sourceRepoPath)) throw new ArgumentNullException(nameof(sourceRepoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            string localRef = "refs/heads/armada-landing/" + branchName;
            string remoteRef = "refs/heads/" + branchName;
            string refspec = "+" + remoteRef + ":" + localRef;

            await RunGitAsync(targetWorkDir, token, "fetch", sourceRepoPath, refspec).ConfigureAwait(false);
            return localRef;
        }

        private async Task DeleteLocalRefQuietlyAsync(string repoPath, string localRef, CancellationToken token)
        {
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", token, "update-ref", "-d", localRef).ConfigureAwait(false);
            if (!result.Succeeded) _Logging.Debug(_Header + "could not delete " + localRef + " in " + repoPath + ": " + result.StandardError.Trim());
        }

        private async Task<bool> TryEnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token)
        {
            try
            {
                return await EnsureLocalBranchAsync(repoPath, branchName, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "unable to sync target branch " + branchName + " from origin in " + repoPath + ": " + ex.ToString());
                return false;
            }
        }

        private async Task<string> RunProcessAsync(string? workingDirectory, string command, params string[] args)
        {
            return await RunProcessAsync(workingDirectory, command, CancellationToken.None, args).ConfigureAwait(false);
        }

        private async Task<string> RunProcessAsync(string? workingDirectory, string command, CancellationToken token, params string[] args)
        {
            GitProcessResult result = await ExecuteProcessAsync(workingDirectory, command, token, args).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                GitCommandException failure = new GitCommandException(command, args, result.ExitCode, result.StandardOutput, result.StandardError);
                // The exception carries the exit code and stderr; callers decide whether it is expected.
                _Logging.Debug(_Header + failure.Message);
                throw failure;
            }

            return result.StandardOutput;
        }

        /// <summary>
        /// Run a git or gh process and return its exit code and output without throwing on a non-zero exit.
        /// Launches with LC_ALL=C. Throws <see cref="TimeoutException"/> after 120 seconds.
        /// </summary>
        private async Task<GitProcessResult> ExecuteProcessAsync(string? workingDirectory, string command, CancellationToken token, params string[] args)
        {
            return await ExecuteProcessAsync(workingDirectory, command, null, token, args).ConfigureAwait(false);
        }

        /// <summary>
        /// Run a git or gh process as <see cref="ExecuteProcessAsync(string?, string, CancellationToken, string[])"/> does,
        /// declaring the non-zero exit codes the caller reads as an answer rather than a failure.
        /// </summary>
        private async Task<GitProcessResult> ExecuteProcessAsync(string? workingDirectory, string command, int[]? expectedExitCodes, CancellationToken token, params string[] args)
        {
            if (_Commands != null)
            {
                HostCommandResult remote = await _Commands.RunAsync(new HostCommandRequest
                {
                    Executable = command,
                    WorkingDirectory = workingDirectory ?? String.Empty,
                    Arguments = new List<string>(args),
                    TimeoutMs = 120000,
                    ExpectedExitCodes = expectedExitCodes != null ? new List<int>(expectedExitCodes) : new List<int>()
                }, token).ConfigureAwait(false);
                if (remote.TimedOut) throw new TimeoutException(command + " timed out after 120 seconds: " + remote.StandardError.Trim());
                return new GitProcessResult
                {
                    ExitCode = remote.ExitCode,
                    StandardOutput = remote.StandardOutput,
                    StandardError = remote.StandardError
                };
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            GitProcessEnvironment.Apply(startInfo);

            if (!String.IsNullOrEmpty(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // 120s timeout: clone/push/fetch of large repos over slow connections
            // can easily exceed 30s, especially in CI or container environments.
            using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            try
            {
                // Read both streams concurrently so a full stderr pipe cannot stall stdout.
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                Task<string> stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);

                return new GitProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = stdout,
                    StandardError = stderr
                };
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (token.IsCancellationRequested) throw;
                throw new TimeoutException(command + " timed out after 120 seconds");
            }
        }

        private async Task<bool> ShowRefExistsAsync(string repoPath, string fullRef, CancellationToken token)
        {
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", _NoMatch, token, "show-ref", "--verify", "--quiet", fullRef).ConfigureAwait(false);
            return result.Succeeded;
        }

        private async Task<List<GitWorktreeEntry>> ListWorktreesAsync(string repoPath, CancellationToken token)
        {
            GitProcessResult nul = await ExecuteProcessAsync(repoPath, "git", _UsageError, token, "worktree", "list", "--porcelain", "-z").ConfigureAwait(false);
            if (nul.Succeeded) return GitMachineOutputParser.ParseWorktreeList(nul.StandardOutput, true);

            // git older than 2.36 rejects -z as a usage error (exit 129); use the newline form there.
            if (nul.ExitCode == 129)
            {
                string output = await RunGitAsync(repoPath, token, "worktree", "list", "--porcelain").ConfigureAwait(false);
                return GitMachineOutputParser.ParseWorktreeList(output, false);
            }

            throw new GitCommandException("git", new List<string> { "worktree", "list", "--porcelain", "-z" }, nul.ExitCode, nul.StandardOutput, nul.StandardError);
        }

        /// <summary>
        /// Choose the diff range for HEAD against a base branch from git merge-base's exit code:
        /// 0 = base...HEAD, 1 (no common ancestor) = base..HEAD, anything else = null (a ref does not resolve).
        /// </summary>
        private async Task<string?> ResolveDiffRangeAsync(string worktreePath, string baseBranch, CancellationToken token)
        {
            GitProcessResult mergeBase = await ExecuteProcessAsync(worktreePath, "git", _NoMatch, token, "merge-base", baseBranch, "HEAD").ConfigureAwait(false);
            if (mergeBase.ExitCode == 0) return baseBranch + "...HEAD";
            if (mergeBase.ExitCode == 1) return baseBranch + "..HEAD";
            return null;
        }

        private static string[] BuildDiffArgs(string range)
        {
            // Fixed prefixes and no external diff/color so the snapshot is canonical regardless of user config.
            return new string[] { "diff", "--no-color", "--no-ext-diff", "--src-prefix=a/", "--dst-prefix=b/", range };
        }

        private static GhPullRequestView? DeserializeGh(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<GhPullRequestView>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsAbsoluteHttpUrl(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        #endregion
    }
}
