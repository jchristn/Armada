namespace Armada.Core.Services
{
    using System.Diagnostics;
    using SyslogLogging;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Git operations via the git CLI.
    /// </summary>
    public class GitService : IGitService
    {
        #region Public-Members

        #endregion

        #region Private-Members

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

            _Logging.Info(_Header + "cloning bare: " + repoUrl + " -> " + localPath);
            await RunGitAsync(null, "clone", "--bare", repoUrl, localPath).ConfigureAwait(false);

            // Bare clones do not configure a fetch refspec by default, so git fetch
            // will not update local branch refs. Configure the refspec so that every
            // fetch keeps local branches in sync with the remote.
            await RunGitAsync(localPath, "config", "remote.origin.fetch", "+refs/heads/*:refs/heads/*").ConfigureAwait(false);
        }

        /// <summary>
        /// Create a git worktree from a bare repository.
        /// </summary>
        public async Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(repoPath)) throw new ArgumentNullException(nameof(repoPath));
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Info(_Header + "creating worktree: " + worktreePath + " branch: " + branchName);

            // Fetch latest before creating worktree
            await FetchAsync(repoPath, token).ConfigureAwait(false);

            // Create worktree with new branch from base
            await RunGitAsync(repoPath, "worktree", "add", "-b", branchName, worktreePath, baseBranch).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a git worktree.
        /// </summary>
        public async Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Info(_Header + "removing worktree: " + worktreePath);
            await RunGitAsync(null, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
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
                if (String.IsNullOrWhiteSpace(currentRefspec))
                {
                    await RunGitAsync(repoPath, "config", "remote.origin.fetch", "+refs/heads/*:refs/heads/*").ConfigureAwait(false);
                }
            }
            catch
            {
                // Config key missing entirely — set it
                await RunGitAsync(repoPath, "config", "remote.origin.fetch", "+refs/heads/*:refs/heads/*").ConfigureAwait(false);
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
            catch (InvalidOperationException ex) when (ex.Message.Contains("refusing to fetch"))
            {
                // A checked-out worktree is blocking the full fetch.
                // Fall back to fetching just the remote refs without updating local branches.
                _Logging.Warn(_Header + "full fetch blocked by checked-out worktree, trying fetch origin: " + ex.Message);
                try
                {
                    await RunGitAsync(repoPath, "fetch", "origin").ConfigureAwait(false);
                }
                catch (Exception fallbackEx)
                {
                    _Logging.Warn(_Header + "fallback fetch also failed: " + fallbackEx.Message);
                    // Continue without fetch — worktree creation may still succeed
                    // if the base branch is already available locally.
                }
            }
        }

        /// <summary>
        /// Push a branch to the remote.
        /// </summary>
        public async Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Info(_Header + "pushing branch from: " + worktreePath);
            await RunGitAsync(worktreePath, "push", "-u", remoteName, "HEAD").ConfigureAwait(false);
        }

        /// <summary>
        /// Create a pull request using the gh CLI.
        /// </summary>
        public async Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));

            _Logging.Info(_Header + "creating PR: " + title);

            string result = await RunProcessAsync(worktreePath, "gh", "pr", "create", "--title", title, "--body", body ?? "").ConfigureAwait(false);
            string prUrl = result.Trim();

            _Logging.Info(_Header + "PR created: " + prUrl);
            return prUrl;
        }

        /// <summary>
        /// Repair a worktree by resetting it to a clean state.
        /// </summary>
        public async Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Info(_Header + "repairing worktree: " + worktreePath);

            // Reset any uncommitted changes
            await RunGitAsync(worktreePath, "checkout", "--", ".").ConfigureAwait(false);

            // Remove untracked files
            await RunGitAsync(worktreePath, "clean", "-fd").ConfigureAwait(false);

            _Logging.Info(_Header + "worktree repaired: " + worktreePath);
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

            _Logging.Info(_Header + "enabling auto-merge for PR: " + prUrl);
            await RunProcessAsync(worktreePath, "gh", "pr", "merge", prUrl, "--merge", "--auto").ConfigureAwait(false);
        }

        /// <summary>
        /// Merge a branch from a source repository into the current branch of a target working directory.
        /// </summary>
        public async Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? commitMessage = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(targetWorkDir)) throw new ArgumentNullException(nameof(targetWorkDir));
            if (String.IsNullOrEmpty(sourceRepoPath)) throw new ArgumentNullException(nameof(sourceRepoPath));
            if (String.IsNullOrEmpty(branchName)) throw new ArgumentNullException(nameof(branchName));

            _Logging.Info(_Header + "merging branch " + branchName + " from " + sourceRepoPath + " into " + targetWorkDir);

            // Ensure we are on the main branch before merging.
            // Without this, a previous fetch/merge could leave HEAD on a different branch,
            // causing subsequent merges to target the wrong branch.
            await RunGitAsync(targetWorkDir, "checkout", "main").ConfigureAwait(false);

            // Fetch the specific branch from the bare repo using explicit refspec
            // Branch names with slashes (e.g. armada/claude-code-1/msn_xxx) require
            // the full refs/heads/ prefix to resolve correctly from bare repos.
            string refspec = "refs/heads/" + branchName;
            await RunGitAsync(targetWorkDir, "fetch", sourceRepoPath, refspec).ConfigureAwait(false);

            // Merge FETCH_HEAD into the current branch (main)
            string message = commitMessage ?? ("Merge armada mission: " + branchName);
            await RunGitAsync(targetWorkDir, "merge", "FETCH_HEAD", "--no-edit", "-m", message).ConfigureAwait(false);

            _Logging.Info(_Header + "merged " + branchName + " into " + targetWorkDir);
        }

        /// <summary>
        /// Pull latest changes from remote into a working directory.
        /// </summary>
        public async Task PullAsync(string workingDirectory, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));

            _Logging.Info(_Header + "pulling latest in " + workingDirectory);
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
                string result = await RunProcessAsync(workingDirectory, "gh", "pr", "view", prUrl, "--json", "state", "--jq", ".state").ConfigureAwait(false);
                return result.Trim().Equals("MERGED", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the diff of all changes in a worktree against the base branch.
        /// </summary>
        public async Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));

            _Logging.Debug(_Header + "diffing worktree " + worktreePath + " against " + baseBranch);

            // Diff committed changes on the current branch vs the base branch
            try
            {
                return await RunGitAsync(worktreePath, token, "diff", baseBranch + "...HEAD").ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch
            {
                // Fallback: diff against working tree (uncommitted changes)
                return await RunGitAsync(worktreePath, token, "diff", "HEAD").ConfigureAwait(false);
            }
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

        /// <summary>
        /// Check if a path is a valid git repository.
        /// </summary>
        public async Task<bool> IsRepositoryAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) return false;

            try
            {
                await RunGitAsync(path, "rev-parse", "--git-dir").ConfigureAwait(false);
                return true;
            }
            catch
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

            try
            {
                string result = await RunGitAsync(repoPath, "branch", "--list", branchName).ConfigureAwait(false);
                return !String.IsNullOrWhiteSpace(result);
            }
            catch
            {
                return false;
            }
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
                string result = await RunGitAsync(repoPath, "worktree", "list", "--porcelain").ConfigureAwait(false);
                string normalizedTarget = Path.GetFullPath(worktreePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                foreach (string line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("worktree "))
                    {
                        string registeredPath = line.Substring("worktree ".Length).Trim();
                        string normalizedRegistered = Path.GetFullPath(registeredPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (String.Equals(normalizedRegistered, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private async Task<string> RunGitAsync(string? workingDirectory, params string[] args)
        {
            return await RunProcessAsync(workingDirectory, "git", args).ConfigureAwait(false);
        }

        private async Task<string> RunGitAsync(string? workingDirectory, CancellationToken token, params string[] args)
        {
            return await RunProcessAsync(workingDirectory, "git", token, args).ConfigureAwait(false);
        }

        private async Task<string> RunProcessAsync(string? workingDirectory, string command, params string[] args)
        {
            return await RunProcessAsync(workingDirectory, command, CancellationToken.None, args).ConfigureAwait(false);
        }

        private async Task<string> RunProcessAsync(string? workingDirectory, string command, CancellationToken token, params string[] args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!String.IsNullOrEmpty(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token).ConfigureAwait(false);
            string stderr = await process.StandardError.ReadToEndAsync(linkedCts.Token).ConfigureAwait(false);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException(command + " timed out after 30 seconds");
            }

            if (process.ExitCode != 0)
            {
                string errorMessage = command + " failed (exit " + process.ExitCode + "): " + stderr.Trim();

                // Demote expected "not found" messages during cleanup to Debug level
                bool isExpectedFailure =
                    stderr.Contains("not a working tree", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("is not a git repository", StringComparison.OrdinalIgnoreCase);

                if (isExpectedFailure)
                    _Logging.Debug(_Header + errorMessage);
                else
                    _Logging.Warn(_Header + errorMessage);

                throw new InvalidOperationException(errorMessage);
            }

            return stdout;
        }

        #endregion
    }
}
