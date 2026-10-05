namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Git operations via the git CLI.
    /// </summary>
    public class GitService : IGitService
    {
        #region Public-Members

        private const string _SafeFetchRefspec = "+refs/heads/*:refs/remotes/origin/*";

        #endregion

        #region Private-Members

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _RepoLocks =
            new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private string _Header = "[GitService] ";
        private LoggingModule _Logging;

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

        #endregion

        #region Public-Methods

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
                    if (Directory.Exists(worktreePath))
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
            GitProcessResult view = await ExecuteProcessAsync(worktreePath, "gh", token, "pr", "view", "--json", "url").ConfigureAwait(false);
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
        /// Merge a branch from a source repository into a target branch of a working directory.
        /// </summary>
        public async Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetWorkDir)) throw new ArgumentNullException(nameof(targetWorkDir));
            if (String.IsNullOrEmpty(sourceRepoPath)) throw new ArgumentNullException(nameof(sourceRepoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Debug(_Header + "merging branch " + branchName + " from " + sourceRepoPath + " into " + targetWorkDir);
            await EnsureTrackedFilesCleanAsync(targetWorkDir, token).ConfigureAwait(false);

            // Ensure we are on the correct target branch before merging.
            // Without this, a previous fetch/merge could leave HEAD on a different branch,
            // causing subsequent merges to target the wrong branch.
            if (!String.IsNullOrEmpty(targetBranch))
            {
                await EnsureTargetBranchCheckedOutAsync(targetWorkDir, sourceRepoPath, targetBranch, token).ConfigureAwait(false);
            }

            // Fetch the specific branch from the bare repo using explicit refspec
            // Branch names with slashes (e.g. armada/claude-code-1/msn_xxx) require
            // the full refs/heads/ prefix to resolve correctly from bare repos.
            string fetchedBranchRef = await FetchBranchIntoLocalRefAsync(targetWorkDir, sourceRepoPath, branchName, token).ConfigureAwait(false);

            // Merge the fetched local ref into the current branch.
            // Using a concrete ref avoids FETCH_HEAD resolution issues across separate git
            // invocations and keeps landing stable even when the target checkout is a worktree.
            string message = commitMessage ?? ("Merge armada mission: " + branchName);
            try
            {
                await MergeFetchedBranchAsync(targetWorkDir, sourceRepoPath, branchName, targetBranch, fetchedBranchRef, message, token).ConfigureAwait(false);
            }
            finally
            {
                // The armada-landing/* ref only carries the fetched branch into the merge; leaving it would add one
                // branch to the user's checkout for every landed mission.
                // Not cancellable: it must not replace the merge's own exception or leave the ref behind on cancel.
                await DeleteLocalRefQuietlyAsync(targetWorkDir, fetchedBranchRef, CancellationToken.None).ConfigureAwait(false);
            }

            _Logging.Debug(_Header + "merged " + branchName + " into " + targetWorkDir + (String.IsNullOrEmpty(targetBranch) ? "" : " (target: " + targetBranch + ")"));
        }

        private async Task MergeFetchedBranchAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch, string fetchedBranchRef, string message, CancellationToken token)
        {
            try
            {
                // Decide up front from git merge-base's exit code (1 = no common ancestor) instead of
                // retrying after matching git's error wording.
                GitProcessResult mergeBase = await ExecuteProcessAsync(targetWorkDir, "git", token, "merge-base", "HEAD", fetchedBranchRef).ConfigureAwait(false);
                if (mergeBase.ExitCode == 1)
                {
                    _Logging.Warn(_Header + "no merge base between target and " + branchName + ", merging with --allow-unrelated-histories");
                    await RunGitAsync(targetWorkDir, token, "merge", fetchedBranchRef, "--no-edit", "--allow-unrelated-histories", "-m", message).ConfigureAwait(false);
                }
                else
                {
                    await RunGitAsync(targetWorkDir, token, "merge", fetchedBranchRef, "--no-edit", "-m", message).ConfigureAwait(false);
                }
            }
            catch
            {
                await RestoreAfterFailedMergeAsync(targetWorkDir, sourceRepoPath, targetBranch, token).ConfigureAwait(false);
                throw;
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
                await RunGitAsync(worktreePath, token, "update-ref", "refs/heads/" + branchName, commitHash).ConfigureAwait(false);
                _Logging.Debug(_Header + "force-advanced branch " + branchName + " to " + commitHash + " via " + worktreePath);
                return true;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "force-advance of branch " + branchName + " to " + commitHash + " failed: " + ex.ToString());
                return false;
            }
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
                        string grep = await RunGitAsync(worktreePath, token, "grep", "-l", "-F", "-e", term).ConfigureAwait(false);
                        if (!String.IsNullOrWhiteSpace(grep)) present = true;
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

            if (!Directory.Exists(path)) return false;
            try
            {
                GitProcessResult result = await ExecuteProcessAsync(path, "git", token, "rev-parse", "--git-dir").ConfigureAwait(false);
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
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch to measure divergence against (for example "main").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches, default branch first, then by name.</returns>
        public async Task<IReadOnlyList<BranchInfo>> ListBranchesAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));

            // Field-separated, one line per branch. Unit separator (\x1f) avoids collisions with commit text.
            string format = "%(refname:short)\x1f%(HEAD)\x1f%(objectname:short)\x1f%(committerdate:iso-strict)\x1f%(contents:subject)";
            string raw = await RunGitAsync(repoPath, "for-each-ref", "--format=" + format, "refs/heads/").ConfigureAwait(false);

            List<BranchInfo> branches = new List<BranchInfo>();
            string[] lines = raw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] parts = line.Split('\x1f');
                if (parts.Length < 3) continue;

                BranchInfo info = new BranchInfo();
                info.Name = parts[0].Trim();
                info.IsCurrent = parts.Length > 1 && parts[1].Trim() == "*";
                info.CommitHash = parts.Length > 2 ? parts[2].Trim() : null;
                if (parts.Length > 3 && DateTimeOffset.TryParse(parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset parsedDate))
                    info.CommitDate = parsedDate.UtcDateTime;
                info.CommitSubject = parts.Length > 4 ? parts[4].Trim() : null;
                info.IsDefault = String.Equals(info.Name, defaultBranch, StringComparison.Ordinal);

                if (!info.IsDefault)
                {
                    try
                    {
                        string counts = await RunGitAsync(repoPath, "rev-list", "--left-right", "--count", defaultBranch + "..." + info.Name).ConfigureAwait(false);
                        string[] countParts = counts.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (countParts.Length == 2)
                        {
                            // left = commits on default not on branch (branch is behind); right = commits on branch not on default (ahead).
                            Int32.TryParse(countParts[0], out int behind);
                            Int32.TryParse(countParts[1], out int ahead);
                            info.Behind = behind;
                            info.Ahead = ahead;
                        }
                    }
                    catch
                    {
                        // Divergence unknown (for example the default branch does not exist yet); leave zeros.
                    }
                }

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
                try { if (Directory.Exists(worktreePath)) Directory.Delete(worktreePath, true); } catch { }
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

            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", token, "merge-base", "--is-ancestor", ancestorRef, descendantRef).ConfigureAwait(false);
            if (result.ExitCode == 0) return true;
            if (result.ExitCode == 1) return false;
            return null;
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
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", token, "symbolic-ref", "--short", "-q", "HEAD").ConfigureAwait(false);
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
            if (!Directory.Exists(path)) return false;
            GitProcessResult result = await ExecuteProcessAsync(path, "git", token, "rev-parse", "--is-bare-repository").ConfigureAwait(false);
            return result.Succeeded && String.Equals(result.StandardOutput.Trim(), "true", StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private async Task<bool> RefResolvesAsync(string repoPath, string gitRef, CancellationToken token)
        {
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", token, "rev-parse", "--verify", "--quiet", gitRef + "^{commit}").ConfigureAwait(false);
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
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", CancellationToken.None, "symbolic-ref", "-q", "refs/remotes/origin/HEAD").ConfigureAwait(false);
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

            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", CancellationToken.None, "rev-parse", "--verify", "--quiet", gitRef).ConfigureAwait(false);
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

        private async Task RestoreAfterFailedMergeAsync(string targetWorkDir, string sourceRepoPath, string? targetBranch, CancellationToken token)
        {
            bool mergeInProgress = false;
            try
            {
                GitProcessResult mergeHead = await ExecuteProcessAsync(targetWorkDir, "git", token, "rev-parse", "-q", "--verify", "MERGE_HEAD").ConfigureAwait(false);
                mergeInProgress = mergeHead.Succeeded;
            }
            catch (TimeoutException ex)
            {
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
                    _Logging.Warn(_Header + "unable to return to target branch " + targetBranch + " after failed merge in " + targetWorkDir + ": " + ex.ToString());
                }
            }
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
        private static async Task<GitProcessResult> ExecuteProcessAsync(string? workingDirectory, string command, CancellationToken token, params string[] args)
        {
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
            GitProcessResult result = await ExecuteProcessAsync(repoPath, "git", token, "show-ref", "--verify", "--quiet", fullRef).ConfigureAwait(false);
            return result.Succeeded;
        }

        private async Task<List<GitWorktreeEntry>> ListWorktreesAsync(string repoPath, CancellationToken token)
        {
            GitProcessResult nul = await ExecuteProcessAsync(repoPath, "git", token, "worktree", "list", "--porcelain", "-z").ConfigureAwait(false);
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
            GitProcessResult mergeBase = await ExecuteProcessAsync(worktreePath, "git", token, "merge-base", baseBranch, "HEAD").ConfigureAwait(false);
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
