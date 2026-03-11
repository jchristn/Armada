namespace Armada.Core.Services.Interfaces
{
    /// <summary>
    /// Git operations for repository and worktree management.
    /// </summary>
    public interface IGitService
    {
        /// <summary>
        /// Clone a repository as a bare repo.
        /// </summary>
        /// <param name="repoUrl">Remote repository URL.</param>
        /// <param name="localPath">Local path for the bare clone.</param>
        /// <param name="token">Cancellation token.</param>
        Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default);

        /// <summary>
        /// Create a git worktree from a bare repository.
        /// </summary>
        /// <param name="repoPath">Path to the bare repository.</param>
        /// <param name="worktreePath">Path for the new worktree.</param>
        /// <param name="branchName">Branch name to create and checkout.</param>
        /// <param name="baseBranch">Base branch to create from.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default);

        /// <summary>
        /// Remove a git worktree.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree to remove.</param>
        /// <param name="token">Cancellation token.</param>
        Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default);

        /// <summary>
        /// Fetch latest changes from remote.
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="token">Cancellation token.</param>
        Task FetchAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// Push a branch to the remote.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="remoteName">Remote name.</param>
        /// <param name="token">Cancellation token.</param>
        Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default);

        /// <summary>
        /// Create a pull request using the gh CLI.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="title">PR title.</param>
        /// <param name="body">PR body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>PR URL.</returns>
        Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default);

        /// <summary>
        /// Repair a worktree by resetting it to a clean state.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="token">Cancellation token.</param>
        Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default);

        /// <summary>
        /// Check if a path is a valid git repository.
        /// </summary>
        /// <param name="path">Path to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the path is a git repository.</returns>
        Task<bool> IsRepositoryAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Delete a local branch from a repository.
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="branchName">Branch name to delete.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default);

        /// <summary>
        /// Prune stale worktree registrations (entries for worktrees whose directories no longer exist).
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="token">Cancellation token.</param>
        Task PruneWorktreesAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// Enable auto-merge on a pull request using the gh CLI.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree (for gh context).</param>
        /// <param name="prUrl">PR URL to auto-merge.</param>
        /// <param name="token">Cancellation token.</param>
        Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default);

        /// <summary>
        /// Merge a branch from a source repository into the current branch of a target working directory.
        /// Fetches the branch from sourceRepoPath and merges it into targetWorkDir.
        /// </summary>
        /// <param name="targetWorkDir">The user's local working directory.</param>
        /// <param name="sourceRepoPath">Path to the bare repo containing the branch.</param>
        /// <param name="branchName">Branch name to fetch and merge.</param>
        /// <param name="commitMessage">Optional custom merge commit message. If null, uses default.</param>
        /// <param name="token">Cancellation token.</param>
        Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? commitMessage = null, CancellationToken token = default);

        /// <summary>
        /// Pull latest changes from remote into a working directory.
        /// </summary>
        /// <param name="workingDirectory">Path to the working directory.</param>
        /// <param name="token">Cancellation token.</param>
        Task PullAsync(string workingDirectory, CancellationToken token = default);

        /// <summary>
        /// Get the diff of all changes in a worktree against the base branch.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="baseBranch">Base branch to diff against.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Unified diff output.</returns>
        Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default);

        /// <summary>
        /// Get the HEAD commit hash of a worktree.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The full SHA-1 commit hash, or null if it cannot be determined.</returns>
        Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default);

        /// <summary>
        /// Check if a pull request has been merged using the gh CLI.
        /// </summary>
        /// <param name="workingDirectory">Path to a repo for gh context.</param>
        /// <param name="prUrl">PR URL to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the PR has been merged.</returns>
        Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default);

        /// <summary>
        /// Check if a local branch exists in the repository.
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="branchName">Branch name to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the branch exists.</returns>
        Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default);

        /// <summary>
        /// Check if a path is registered as a git worktree.
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="worktreePath">Path to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the path is a registered worktree.</returns>
        Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default);
    }
}
