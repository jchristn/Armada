namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Workspace section: dock, worktree, branch and target state at the failure.
    /// </summary>
    public class BlackBoxWorkspace
    {
        /// <summary>
        /// Dock, or null.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Dock state name, or null.
        /// </summary>
        public string? DockState { get; set; } = null;

        /// <summary>
        /// Worktree path, or null.
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// Whether the worktree existed, or null when unknown.
        /// </summary>
        public bool? WorktreeExists { get; set; } = null;

        /// <summary>
        /// Target checkout path, or null.
        /// </summary>
        public string? CheckoutPath { get; set; } = null;

        /// <summary>
        /// Mission branch, or null.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Mission branch tip, or null.
        /// </summary>
        public string? BranchTip { get; set; } = null;

        /// <summary>
        /// Mission commit, or null.
        /// </summary>
        public string? MissionCommit { get; set; } = null;

        /// <summary>
        /// Commit the mission started from, or null.
        /// </summary>
        public string? StartCommit { get; set; } = null;

        /// <summary>
        /// Where the start commit came from (Mission, DockAnchors, DockStartFile), or null.
        /// </summary>
        public string? StartCommitSource { get; set; } = null;

        /// <summary>
        /// Target branch, or null.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// Target tip when the mission started, or null.
        /// </summary>
        public string? TargetTipAtStart { get; set; } = null;

        /// <summary>
        /// Target tip at the failure, or null.
        /// </summary>
        public string? TargetTipAtFailure { get; set; } = null;

        /// <summary>
        /// Commits the branch is behind the target, or null.
        /// </summary>
        public int? CommitsBehindTarget { get; set; } = null;

        /// <summary>
        /// Commits the branch is ahead of its start, or null.
        /// </summary>
        public int? CommitsAheadOfStart { get; set; } = null;

        /// <summary>
        /// Whether the target checkout had uncommitted tracked changes, or null.
        /// </summary>
        public bool? CheckoutDirty { get; set; } = null;

        /// <summary>
        /// Dirty paths in the checkout.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxDirtyPaths, Reason = "Capped by BlackBoxRecordBounder; DirtyPathTotal holds the real count")]
        public List<string> DirtyPaths
        {
            get => _DirtyPaths;
            set => _DirtyPaths = value ?? new List<string>();
        }

        /// <summary>
        /// Total dirty paths before capping.
        /// </summary>
        public int DirtyPathTotal { get; set; } = 0;

        /// <summary>
        /// Harbor link state for Harbor-hosted docks, or null.
        /// </summary>
        public string? HarborLinkState { get; set; } = null;

        /// <summary>
        /// When the Harbor was last seen (UTC), or null.
        /// </summary>
        public DateTime? HarborLastSeenUtc { get; set; } = null;

        /// <summary>
        /// Whether the mission branch still exists in the repository, or null when unknown.
        /// </summary>
        public bool? BranchExists { get; set; } = null;

        /// <summary>
        /// Commits the branch is ahead of the target, or null.
        /// </summary>
        public int? CommitsAheadOfTarget { get; set; } = null;

        /// <summary>
        /// Host the workspace was inspected on at enrichment (Admiral or Harbor:{id}), or null when not inspected.
        /// </summary>
        public string? InspectedHost { get; set; } = null;

        /// <summary>
        /// Why workspace facts are unknown or partial (for example a disconnected Harbor), or null.
        /// </summary>
        public string? InspectionNote { get; set; } = null;

        /// <summary>
        /// Why target movement since the start commit was not read (for example no start commit), or null.
        /// </summary>
        public string? TargetMovementNote { get; set; } = null;

        private List<string> _DirtyPaths = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxWorkspace()
        {
        }
    }
}
