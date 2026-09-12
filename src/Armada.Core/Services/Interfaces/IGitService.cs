namespace Armada.Core.Services.Interfaces
{
    using System.Collections.Generic;
    using Armada.Core.Models;

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
        /// Delete a branch from the remote origin.
        /// Executes: git push origin --delete {branchName}
        /// </summary>
        /// <param name="repoPath">Path to a repository with the remote configured.</param>
        /// <param name="branchName">Remote branch name to delete.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default);

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
        /// <param name="targetBranch">Target branch to checkout before merging (e.g. "main", "develop"). If null, uses current branch.</param>
        /// <param name="commitMessage">Optional custom merge commit message. If null, uses default.</param>
        /// <param name="token">Cancellation token.</param>
        Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default);

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
        /// Get the list of files with unresolved merge conflicts (unmerged paths) in a worktree.
        /// Returns an empty list when the working tree is clean.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Repository-relative paths of conflicted files.</returns>
        Task<IReadOnlyList<string>> GetConflictedFilesAsync(string worktreePath, CancellationToken token = default);

        /// <summary>
        /// Get the HEAD commit hash of a worktree.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The full SHA-1 commit hash, or null if it cannot be determined.</returns>
        Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default);

        /// <summary>
        /// List repository-relative files changed during a mission since the worktree was provisioned.
        /// Includes committed changes plus current tracked and untracked working tree changes.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="startCommit">Commit hash recorded when the dock was provisioned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Normalized changed file paths.</returns>
        Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default);

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
        /// Ensure a local branch exists in the repository.
        /// If the matching remote branch exists, sync from it. Otherwise create the branch
        /// from the repository's effective default branch or another available base ref.
        /// Returns false only when the repository has no usable branch history yet.
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="branchName">Branch name to ensure.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the branch exists or was created; false if the repo has no commits/branches.</returns>
        Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default);

        /// <summary>
        /// List local branches with their tip commit and ahead/behind position relative to the default branch.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch to measure divergence against.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches, default branch first, then by name.</returns>
        Task<IReadOnlyList<BranchInfo>> ListBranchesAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default);

        /// <summary>
        /// Push a named local branch to the remote.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="branchName">Branch to push.</param>
        /// <param name="remoteName">Remote name.</param>
        /// <param name="token">Cancellation token.</param>
        Task PushLocalBranchAsync(string repoPath, string branchName, string remoteName = "origin", CancellationToken token = default);

        /// <summary>
        /// Merge one branch into another within a repository, optionally pushing the updated target.
        /// </summary>
        /// <param name="repoPath">Bare repository path.</param>
        /// <param name="sourceBranch">Branch to merge from.</param>
        /// <param name="targetBranch">Branch to merge into.</param>
        /// <param name="push">Whether to push the target branch after a successful merge.</param>
        /// <param name="token">Cancellation token.</param>
        Task MergeBranchesAsync(string repoPath, string sourceBranch, string targetBranch, bool push, CancellationToken token = default);

        /// <summary>
        /// Check if a path is registered as a git worktree.
        /// </summary>
        /// <param name="repoPath">Path to the repository.</param>
        /// <param name="worktreePath">Path to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the path is a registered worktree.</returns>
        Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default);

        /// <summary>
        /// Force-advance a local branch ref to a specific commit. Unlike <c>git branch -f</c>, this uses
        /// <c>git update-ref</c>, which tolerates a branch that is currently checked out (or detached) in a
        /// worktree that shares the same repository. Used by pipeline stage handoff to lift a prior stage's
        /// produced commit -- resolved from a detached dock's live HEAD -- onto the shared branch ref so the
        /// next stage's checkout sees the work.
        /// </summary>
        /// <param name="worktreePath">A worktree (or repository) path that shares the target branch's repository.</param>
        /// <param name="branchName">Branch name to advance (without the refs/heads/ prefix).</param>
        /// <param name="commitHash">Commit hash the branch ref should point to.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the ref was updated; false when inputs were missing or the update failed.</returns>
        Task<bool> ForceAdvanceBranchAsync(string worktreePath, string branchName, string commitHash, CancellationToken token = default);

        /// <summary>
        /// Return recent commit summaries touching the given repository paths, as "path: shorthash subject"
        /// lines, so a mission brief can state what changed under the paths it names. Best-effort: unknown
        /// paths and git failures yield fewer (or no) entries rather than throwing.
        /// </summary>
        /// <param name="worktreePath">Worktree/repository path.</param>
        /// <param name="paths">Repository-relative paths the mission names.</param>
        /// <param name="maxPerPath">Maximum commits to report per path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Formatted recent-commit lines.</returns>
        Task<IReadOnlyList<string>> GetRecentCommitsForPathsAsync(string worktreePath, IReadOnlyList<string> paths, int maxPerPath, CancellationToken token = default);

        /// <summary>
        /// Return the subset of the given subject terms that already appear in the tracked tree (as a path
        /// substring or in file contents), so a mission brief can tell a captain which terms already exist.
        /// Best-effort: git failures yield an empty set rather than throwing.
        /// </summary>
        /// <param name="worktreePath">Worktree/repository path.</param>
        /// <param name="terms">Candidate subject terms.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The terms found in the tree.</returns>
        Task<IReadOnlyList<string>> FindExistingSubjectTermsAsync(string worktreePath, IReadOnlyList<string> terms, CancellationToken token = default);
    }
}
