namespace Armada.Core.Services.Interfaces
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Git operations for repository and worktree management.
    /// </summary>
    public interface IGitService
    {
        /// <summary>Refuse publication when the candidate inherits unpublished commits from a mapped developer checkout.</summary>
        Task ValidatePublishedAncestryAsync(string worktreePath, string checkoutPath, string targetBranch, string commitHash, CancellationToken token = default)
            => Task.CompletedTask;

        /// <summary>Publish exactly a verified commit, without merging or modifying a developer checkout.</summary>
        Task PushVerifiedCommitAsync(string worktreePath, string commitHash, string targetBranch, CancellationToken token = default)
            => throw new NotSupportedException("This git service cannot publish a verified commit.");

        /// <summary>Fast-forward a clean target checkout to exactly the verified commit; never create a new merge.</summary>
        Task FastForwardVerifiedCommitAsync(string targetWorkDir, string sourceRepoPath, string commitHash, string targetBranch, CancellationToken token = default)
            => throw new NotSupportedException("This git service cannot fast-forward a verified commit.");

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
        /// List the files the worktree's HEAD changes relative to the base branch, from git's machine formats
        /// (diff --name-status -z --no-renames and diff --numstat -z --no-renames over base...HEAD, or base..HEAD
        /// when the histories share no merge base). Deletions are reported, and a rename appears as a deletion
        /// plus an addition, so both paths are visible to path rules.
        /// </summary>
        /// <param name="worktreePath">Path to the worktree.</param>
        /// <param name="baseBranch">Base branch to compare against.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Changed files with kinds and line counts.</returns>
        Task<IReadOnlyList<GitChangedFile>> GetBranchChangesAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default);

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
        /// A branch that cannot be compared has <see cref="BranchInfo.DivergenceError"/> set and keeps zero counts.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch to measure divergence against.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches, default branch first, then by name.</returns>
        Task<IReadOnlyList<BranchInfo>> ListBranchesAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default);

        /// <summary>
        /// List local branches with their tip commit, without measuring divergence (Ahead and Behind stay zero).
        /// Each name is the path under refs/heads.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch, flagged in the result.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches, default branch first, then by name.</returns>
        Task<IReadOnlyList<BranchInfo>> ListBranchTipsAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default);

        /// <summary>
        /// Fill Ahead and Behind on each branch relative to the default branch (one git command per non-default branch).
        /// The comparison uses the refs/heads form of each name. A comparison that fails, or whose output is not two
        /// non-negative integers, sets <see cref="BranchInfo.DivergenceError"/> and leaves the counts unchanged.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="defaultBranch">Default branch to measure against.</param>
        /// <param name="branches">Branches to measure.</param>
        /// <param name="token">Cancellation token.</param>
        Task MeasureBranchDivergenceAsync(string repoPath, string defaultBranch, IReadOnlyList<BranchInfo> branches, CancellationToken token = default);

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
        /// List the worktrees registered in a repository (git worktree list --porcelain), including the main entry.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repo or worktree).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The registered worktrees, or null when this git service cannot list them.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git fails.</exception>
        Task<IReadOnlyList<GitWorktreeEntry>?> ListWorktreeEntriesAsync(string repoPath, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<GitWorktreeEntry>?>(null);

        /// <summary>
        /// Advance a local branch to include a produced commit without rewinding newer work. Divergent work is
        /// preserved under refs/armada/stage-commits/&lt;commit&gt; and the update is refused. Unlike <c>git branch -f</c>, this uses
        /// <c>git update-ref</c>, which tolerates a branch that is currently checked out (or detached) in a
        /// worktree that shares the same repository. Used by pipeline stage handoff to lift a prior stage's
        /// produced commit -- resolved from a detached dock's live HEAD -- onto the shared branch ref so the
        /// next stage's checkout sees the work.
        /// </summary>
        /// <param name="worktreePath">A worktree (or repository) path that shares the target branch's repository.</param>
        /// <param name="branchName">Branch name to advance (without the refs/heads/ prefix).</param>
        /// <param name="commitHash">Commit hash the branch ref should point to.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the commit is included (possibly already); false when inputs were missing, commits diverged, or the update failed.</returns>
        Task<bool> ForceAdvanceBranchAsync(string worktreePath, string branchName, string commitHash, CancellationToken token = default);

        /// <summary>
        /// Merge one commit into the worktree's current branch with a merge commit. A commit the branch already contains
        /// changes nothing. A commit the repository lacks is fetched from origin's <paramref name="fetchBranch"/> first,
        /// when one is given. A conflicting merge is aborted, so the worktree is never left mid-merge; the conflicted
        /// files are reported. Uses the repository's git identity, falling back to "Armada" when none is configured.
        /// </summary>
        /// <param name="worktreePath">Worktree path.</param>
        /// <param name="commitHash">Commit to merge.</param>
        /// <param name="fetchBranch">Branch on origin that holds the commit, or null.</param>
        /// <param name="message">Merge commit message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What happened.</returns>
        /// <exception cref="ArgumentNullException">Thrown when worktreePath, commitHash or message is null or empty.</exception>
        Task<GitCommitMergeResult> MergeCommitAsync(string worktreePath, string commitHash, string? fetchBranch, string message, CancellationToken token = default)
            => Task.FromResult(new GitCommitMergeResult(Armada.Core.Enums.GitCommitMergeOutcomeEnum.Failed, "This git service cannot merge commits."));

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

        /// <summary>
        /// Fetch every configured remote with pruning (git fetch --all --prune) without changing any repository
        /// configuration. Intended for a user's own checkout, where Armada must not rewrite the remote refspec.
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the fetch fails.</exception>
        /// <exception cref="TimeoutException">Thrown when the fetch does not finish in time.</exception>
        Task FetchRemotesAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// Count commits between two refs (git rev-list --left-right --count baseRef...headRef). Ahead is the number
        /// of commits on headRef that are not on baseRef; Behind is the number of commits on baseRef that are not on
        /// headRef.
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="baseRef">Base ref (for example "origin/main" or "@{u}").</param>
        /// <param name="headRef">Head ref (for example "HEAD").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The counts, or null when either ref does not resolve.</returns>
        Task<GitDivergenceCounts?> GetDivergenceAsync(string repoPath, string baseRef, string headRef, CancellationToken token = default);

        /// <summary>
        /// Whether ancestorRef is an ancestor of (contained in) descendantRef, from the exit code of
        /// git merge-base --is-ancestor (0 yes, 1 no). Used to detect a branch that was merged by hand.
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="ancestorRef">Candidate ancestor (a branch name or commit hash).</param>
        /// <param name="descendantRef">Candidate descendant (for example the target branch).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True or false, or null when either ref does not resolve or git fails.</returns>
        Task<bool?> IsAncestorAsync(string repoPath, string ancestorRef, string descendantRef, CancellationToken token = default);

        /// <summary>
        /// Find commits reachable from a revision whose message carries a trailer with exactly the given key and value
        /// (for example <c>Armada-Mission-Id: msn_...</c>), newest first. The search is a bounded
        /// <c>git log &lt;revision&gt; --fixed-strings --grep</c> narrowed by <c>--since</c>, and each candidate's parsed
        /// trailers must match the value exactly, so a message that only mentions the value in prose does not count.
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="revision">Revision to search from (for example refs/remotes/origin/main).</param>
        /// <param name="trailerKey">Trailer key, without the colon.</param>
        /// <param name="trailerValue">Exact trailer value.</param>
        /// <param name="sinceUtc">Only commits committed at or after this time, or null for no bound.</param>
        /// <param name="maxCount">Maximum number of commits to return (at least 1).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching commit hashes; empty when none match, the revision does not resolve, or git fails.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath, revision, trailerKey, or trailerValue is null or empty.</exception>
        Task<IReadOnlyList<string>> FindCommitsByTrailerAsync(string repoPath, string revision, string trailerKey, string trailerValue, DateTime? sinceUtc, int maxCount, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());

        /// <summary>
        /// Summarize the working tree (git status --porcelain): modified tracked files and untracked files.
        /// </summary>
        /// <param name="repoPath">Working tree path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The working tree status.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git status fails (for example on a bare repository).</exception>
        Task<GitWorkingTreeStatus> GetWorkingTreeStatusAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// Stage and commit every change in a worktree (respecting .gitignore and the repository's exclude rules), so
        /// work a captain left uncommitted is not lost. Untracked scratch files (see <see cref="ScratchFileMatcher"/>)
        /// are left in the worktree, uncommitted. Uses the repository's git identity, falling back to "Armada" when
        /// none is configured.
        /// </summary>
        /// <param name="worktreePath">Worktree path.</param>
        /// <param name="message">Commit message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a commit was made; false when there was nothing to commit, or only scratch files.</returns>
        /// <exception cref="ArgumentNullException">Thrown when worktreePath or message is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git cannot commit (for example a failing hook).</exception>
        Task<bool> CommitAllAsync(string worktreePath, string message, CancellationToken token = default) => Task.FromResult(false);

        /// <summary>
        /// Get the checked-out branch name (git symbolic-ref --short HEAD).
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branch name, or null when HEAD is detached or cannot be read.</returns>
        Task<string?> GetCurrentBranchAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// Get the committer timestamp of the HEAD commit (git log -1 --format=%cI) in UTC.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The timestamp, or null when the repository has no commits.</returns>
        Task<DateTime?> GetLastCommitUtcAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// List repository-relative paths of every file tracked at HEAD (git ls-tree -r --name-only HEAD). Works on
        /// bare repositories as well as working trees.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tracked file paths with forward slashes; empty when the repository has no commits.</returns>
        Task<IReadOnlyList<string>> ListTrackedFilesAsync(string repoPath, CancellationToken token = default);

        /// <summary>
        /// Check whether a path is a bare git repository (git rev-parse --is-bare-repository).
        /// </summary>
        /// <param name="path">Path to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True for a bare repository; false for a working tree or a non-repository.</returns>
        Task<bool> IsBareRepositoryAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Resolve a branch to the full SHA of its tip commit, trying refs/heads/{branch}, then
        /// refs/remotes/origin/{branch}, then the name as given (a full commit id, a tag, HEAD). Works on bare
        /// repositories as well as working trees.
        /// </summary>
        /// <param name="repoPath">Repository path.</param>
        /// <param name="branch">Branch name or full commit id (see <see cref="Armada.Core.Services.GitRevisionNames"/>).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The full commit SHA, or null when nothing resolves to a commit (unknown branch, empty repository).</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath or branch is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when branch is not a safe branch name or commit id.</exception>
        Task<string?> ResolveBranchTipAsync(string repoPath, string branch, CancellationToken token = default);

        /// <summary>
        /// Per-day commit counts on a branch for the history heatmap. Counts come from one git log pass bounded by
        /// --since/--until around the range; each commit is bucketed by its committer date shifted by the offset. Days
        /// holds one zero-filled entry per day from fromDate to toDate inclusive. FirstCommitUtc is the oldest committer
        /// date among the branch's root commits and the commits in the range; LastCommitUtc the newest among the tip
        /// and the commits in the range. VesselId is left empty for the caller.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repository or working tree).</param>
        /// <param name="branch">Branch name (resolved as by <see cref="ResolveBranchTipAsync"/>).</param>
        /// <param name="fromDate">First calendar day (time of day ignored).</param>
        /// <param name="toDate">Last calendar day, inclusive (time of day ignored).</param>
        /// <param name="utcOffsetMinutes">UTC offset in minutes to bucket days in (-840 to 840).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The activity; Error is set (with zero-filled days) when the branch does not resolve.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath or branch is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when toDate is before fromDate or the offset is out of range.</exception>
        Task<VesselCommitActivity> GetCommitActivityAsync(string repoPath, string branch, DateTime fromDate, DateTime toDate, int utcOffsetMinutes, CancellationToken token = default);

        /// <summary>
        /// Read one page of history from a fixed tip commit, newest commit date first (git log --date-order), in one
        /// git invocation: each commit with its changed files from --raw --numstat -z (renames detected, merge
        /// commits diffed against their first parent, root commits against the empty tree), files capped at
        /// <see cref="Armada.Core.Constants.VesselHistoryMaxFilesPerCommit"/>.
        /// </summary>
        /// <param name="repoPath">Repository path (bare repository or working tree).</param>
        /// <param name="tipSha">Full SHA of the tip commit (from <see cref="ResolveBranchTipAsync"/>); pinning it keeps paging stable while the branch moves.</param>
        /// <param name="untilUnixSeconds">Inclusive upper bound on committer dates in Unix seconds, or null for none.</param>
        /// <param name="skip">Commits to skip (returned by earlier pages).</param>
        /// <param name="limit">Page size, 1 or more.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page; HasMore is true when older commits follow.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath or tipSha is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when tipSha is not a full commit id.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when skip is negative or limit is below 1.</exception>
        /// <exception cref="InvalidOperationException">Thrown when git fails (for example the tip commit no longer exists).</exception>
        Task<GitCommitLogPage> GetCommitLogAsync(string repoPath, string tipSha, long? untilUnixSeconds, int skip, int limit, CancellationToken token = default);

        /// <summary>
        /// The best common ancestor of two revisions (git merge-base a b).
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="a">First revision (branch, commit or ref).</param>
        /// <param name="b">Second revision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The full SHA of the merge base, or null when either revision does not resolve, they share no history,
        /// or git fails.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath, a, or b is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when a or b starts with '-'.</exception>
        Task<string?> GetMergeBaseAsync(string repoPath, string a, string b, CancellationToken token = default)
            => Task.FromResult<string?>(null);

        /// <summary>
        /// Read the commits reachable from <paramref name="toRef"/> but not from <paramref name="fromRef"/>
        /// (git log fromRef..toRef --date-order), newest first, at most <paramref name="limit"/>: each with its author,
        /// dates, subject, trailers (parsed from the message, for example Armada-Mission-Id) and changed paths (merges
        /// diffed against their first parent; capped at <see cref="GitCommitRange.MaxPathsPerCommit"/> per commit).
        /// TotalCount is the full size of the range. On a Harbor-hosted repository the same commands run on the Harbor.
        /// </summary>
        /// <param name="repoPath">Repository path (working tree or bare repository).</param>
        /// <param name="fromRef">Lower bound, excluded (for example the mission's start commit).</param>
        /// <param name="toRef">Upper bound, included (for example the target tip).</param>
        /// <param name="limit">Most commits to return, 1 to <see cref="GitCommitRange.MaxLimit"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The range, or null when either revision does not resolve or git fails.</returns>
        /// <exception cref="ArgumentNullException">Thrown when repoPath, fromRef, or toRef is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when fromRef or toRef starts with '-'.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when limit is outside 1 to <see cref="GitCommitRange.MaxLimit"/>.</exception>
        Task<GitCommitRange?> GetCommitRangeAsync(string repoPath, string fromRef, string toRef, int limit, CancellationToken token = default)
            => Task.FromResult<GitCommitRange?>(null);
    }
}
