namespace Armada.Test.Unit.TestHelpers
{
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Stub git service for testing that records calls but doesn't execute git.
    /// </summary>
    public class StubGitService : IGitService
    {
        // Call tracking
        public List<string> CloneCalls { get; } = new List<string>();
        public List<string> WorktreeCalls { get; } = new List<string>();
        public List<string> DeleteBranchCalls { get; } = new List<string>();
        public List<string> RemoveWorktreeCalls { get; } = new List<string>();
        public List<string> MergeBranchCalls { get; } = new List<string>();
        public List<string> PushCalls { get; } = new List<string>();
        public List<string> PrCalls { get; } = new List<string>();
        public List<string> PullCalls { get; } = new List<string>();
        public List<string> DiffCalls { get; } = new List<string>();
        public List<string> OperationCalls { get; } = new List<string>();

        // Result controls
        public bool IsRepositoryResult { get; set; } = true;
        public bool IsPrMergedResult { get; set; } = true;
        public string CreatePrResult { get; set; } = "https://github.com/test/repo/pull/1";
        public string DiffResult { get; set; } = "";
        public bool DefaultBranchExistsResult { get; set; } = true;
        public HashSet<string> ExistingBranches { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "main" };

        // Failure injection
        public bool ShouldThrowOnWorktree { get; set; } = false;
        public bool ShouldThrowOnPush { get; set; } = false;
        public bool ShouldThrowOnCreatePr { get; set; } = false;
        public bool ShouldThrowOnMergeLocal { get; set; } = false;
        public bool ShouldThrowOnDeleteBranch { get; set; } = false;

        public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default)
        {
            CloneCalls.Add(repoUrl + " -> " + localPath);
            OperationCalls.Add("clone:" + localPath);
            return Task.CompletedTask;
        }

        public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
        {
            if (ShouldThrowOnWorktree) throw new InvalidOperationException("Simulated worktree failure");
            ExistingBranches.Add(branchName);
            WorktreeCalls.Add(worktreePath);
            OperationCalls.Add("create-worktree:" + worktreePath);
            return Task.CompletedTask;
        }

        public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default)
        {
            RemoveWorktreeCalls.Add(worktreePath);
            OperationCalls.Add("remove-worktree:" + worktreePath);
            return Task.CompletedTask;
        }
        public Task FetchAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;

        public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default)
        {
            if (ShouldThrowOnPush) throw new InvalidOperationException("Simulated push failure");
            PushCalls.Add(worktreePath);
            OperationCalls.Add("push:" + worktreePath);
            return Task.CompletedTask;
        }

        public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default)
        {
            if (ShouldThrowOnCreatePr) throw new InvalidOperationException("Simulated PR creation failure");
            PrCalls.Add(title);
            OperationCalls.Add("create-pr:" + title);
            return Task.FromResult(CreatePrResult);
        }

        public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;
        public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(IsRepositoryResult);

        public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            if (ShouldThrowOnDeleteBranch) throw new InvalidOperationException("Simulated branch delete failure");
            DeleteBranchCalls.Add(repoPath + ":" + branchName);
            OperationCalls.Add("delete-local-branch:" + branchName);
            return Task.CompletedTask;
        }

        public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            DeleteBranchCalls.Add("remote:" + branchName);
            OperationCalls.Add("delete-remote-branch:" + branchName);
            return Task.CompletedTask;
        }

        public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
        public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) => Task.CompletedTask;

        public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default)
        {
            if (ShouldThrowOnMergeLocal) throw new InvalidOperationException("Simulated merge failure");
            MergeBranchCalls.Add(branchName + " -> " + targetWorkDir);
            OperationCalls.Add("merge-local:" + branchName);
            return Task.CompletedTask;
        }

        public Task PullAsync(string workingDirectory, CancellationToken token = default)
        {
            PullCalls.Add(workingDirectory);
            return Task.CompletedTask;
        }

        public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default)
        {
            DiffCalls.Add(worktreePath);
            return Task.FromResult(DiffResult);
        }

        public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(IsPrMergedResult);
        public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123def456");
        public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default)
        {
            if (ExistingBranches.Contains(branchName)) return Task.FromResult(true);
            if (branchName == "main") return Task.FromResult(DefaultBranchExistsResult);
            return Task.FromResult(false);
        }
        public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default, bool skipFetch = false)
            => BranchExistsAsync(repoPath, branchName, token);
        public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
    }
}
