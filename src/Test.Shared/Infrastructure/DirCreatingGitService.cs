namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Git service stub for suites that assign missions: it creates the worktree directory so mission
    /// instructions can be written during dock provisioning, and treats every other git operation as a no-op
    /// success. Shared so assignment/dispatch suites do not each carry their own copy.
    /// </summary>
    public sealed class DirCreatingGitService : IGitService
    {
        private readonly HashSet<string> _Branches = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "main" };

        /// <summary>Clone a bare repository (no-op).</summary>
        public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Create a worktree, materializing the directory so instructions can be written into it.</summary>
        public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
        {
            Directory.CreateDirectory(worktreePath);
            _Branches.Add(branchName);
            return Task.CompletedTask;
        }

        /// <summary>Remove a worktree (no-op).</summary>
        public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Fetch a repository (no-op).</summary>
        public Task FetchAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Push a branch (no-op).</summary>
        public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Create a pull request, returning a fixed URL.</summary>
        public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default)
            => Task.FromResult("https://github.com/test/repo/pull/1");

        /// <summary>Repair a worktree (no-op).</summary>
        public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Report whether a path is a repository (always true).</summary>
        public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(true);

        /// <summary>Delete a local branch (no-op).</summary>
        public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Delete a remote branch (no-op).</summary>
        public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Prune worktrees (no-op).</summary>
        public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Enable auto-merge on a pull request (no-op).</summary>
        public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Merge a branch locally (no-op).</summary>
        public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Pull the latest changes (no-op).</summary>
        public Task PullAsync(string workingDirectory, CancellationToken token = default) => Task.CompletedTask;

        /// <summary>Return a diff (empty).</summary>
        public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default) => Task.FromResult(String.Empty);

        /// <summary>Return files changed since a commit (none).</summary>
        public Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        /// <summary>Return conflicted files (none).</summary>
        public Task<IReadOnlyList<string>> GetConflictedFilesAsync(string worktreePath, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        /// <summary>Report whether a pull request is merged (always true).</summary>
        public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(true);

        /// <summary>Return the HEAD commit hash (fixed).</summary>
        public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123def456");

        /// <summary>Report whether a branch exists (tracked in-memory).</summary>
        public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default)
            => Task.FromResult(_Branches.Contains(branchName));

        /// <summary>Ensure a local branch exists (delegates to <see cref="BranchExistsAsync"/>).</summary>
        public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
            => BranchExistsAsync(repoPath, branchName, token);

        /// <summary>Report whether a worktree is registered (always false).</summary>
        public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> ForceAdvanceBranchAsync(string worktreePath, string branchName, string commitHash, CancellationToken token = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> GetRecentCommitsForPathsAsync(string worktreePath, IReadOnlyList<string> paths, int maxPerPath, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
        public Task<IReadOnlyList<string>> FindExistingSubjectTermsAsync(string worktreePath, IReadOnlyList<string> terms, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }
}
