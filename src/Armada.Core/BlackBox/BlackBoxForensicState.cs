namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Facts the git forensics enrichers share while one entry is enriched (not part of the record): the dock, the host
    /// and repository its git work happens in, whether the worktree exists, the target commits since the start commit
    /// (with their changed paths) and the paths the mission changed. Resolved once per entry by
    /// <see cref="Armada.Core.BlackBox.Enrichment.BlackBoxForensics.EnsureStateAsync"/>.
    /// </summary>
    public class BlackBoxForensicState
    {
        /// <summary>
        /// Whether the state was resolved.
        /// </summary>
        public bool Resolved { get; set; } = false;

        /// <summary>
        /// The mission's dock, or null.
        /// </summary>
        public Dock? Dock { get; set; } = null;

        /// <summary>
        /// The repository git work happens in, on its owning host (Host null when a Harbor is not connected).
        /// </summary>
        public VesselRepositoryState? Repository { get; set; } = null;

        /// <summary>
        /// The owning host, or null when it cannot be reached.
        /// </summary>
        public DockHost? Host { get; set; } = null;

        /// <summary>
        /// Label of the owning host: Admiral or Harbor:{id}.
        /// </summary>
        public string HostLabel { get; set; } = "Admiral";

        /// <summary>
        /// Why the owning host cannot be inspected, or null when it can.
        /// </summary>
        public string? HostUnavailableReason { get; set; } = null;

        /// <summary>
        /// Whether the worktree exists on the host now, or null when unknown.
        /// </summary>
        public bool? WorktreeExists { get; set; } = null;

        /// <summary>
        /// Path to run git in: the worktree when it exists, otherwise the repository; null when neither is known.
        /// </summary>
        public string? GitPath { get; set; } = null;

        /// <summary>
        /// The mission branch, or null.
        /// </summary>
        public string? Branch { get; set; } = null;

        /// <summary>
        /// The target branch, or null.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// The start commit, or null.
        /// </summary>
        public string? StartCommit { get; set; } = null;

        /// <summary>
        /// Target commits since the start commit (up to <see cref="GitCommitRange.MaxLimit"/>), or null when not read.
        /// </summary>
        public GitCommitRange? TargetCommits { get; set; } = null;

        /// <summary>
        /// Paths the failed mission changed (repository-relative, forward slashes).
        /// </summary>
        public HashSet<string> MissionChangedPaths { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the mission's changed paths are known (from its diff or its branch).
        /// </summary>
        public bool MissionChangedPathsKnown { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxForensicState()
        {
        }
    }
}
