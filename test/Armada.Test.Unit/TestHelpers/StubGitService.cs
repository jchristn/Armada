namespace Armada.Test.Unit.TestHelpers
{
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Stub git service for testing that records calls but doesn't execute git.
    /// </summary>
    public class StubGitService : IGitService
    {
        public List<string> CloneCalls { get; } = new List<string>();
        public List<string> WorktreeCalls { get; } = new List<string>();
        public bool IsRepositoryResult { get; set; } = true;
        public bool ShouldThrowOnWorktree { get; set; } = false;

        public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default)
        {
            CloneCalls.Add(repoUrl + " -> " + localPath);
            return Task.CompletedTask;
        }

        public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
        {
            if (ShouldThrowOnWorktree) throw new InvalidOperationException("Simulated worktree failure");
            WorktreeCalls.Add(worktreePath);
            return Task.CompletedTask;
        }

        public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;
        public Task FetchAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
        public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default) => Task.CompletedTask;
        public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default) => Task.FromResult("https://github.com/test/repo/pull/1");
        public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;
        public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(IsRepositoryResult);
        public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;
        public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
        public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) => Task.CompletedTask;
        public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? commitMessage = null, CancellationToken token = default) => Task.CompletedTask;
        public Task PullAsync(string workingDirectory, CancellationToken token = default) => Task.CompletedTask;
        public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default) => Task.FromResult("");
        public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(true);
        public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123def456");
        public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
    }
}
