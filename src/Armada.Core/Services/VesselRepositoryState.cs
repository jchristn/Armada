namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// One repository of a vessel on one host (the Admiral's bare clone or a Harbor's repository), as loaded by
    /// <see cref="VesselGitInspector.LoadRepositoriesAsync"/>: the host it was reached on and its worktrees and
    /// branches. A null Host means the Harbor is not connected; Error is set when the host answered but git failed.
    /// </summary>
    public class VesselRepositoryState
    {
        /// <summary>
        /// The Harbor that holds the repository, or null for the Admiral's host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Repository path on that host.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// The host, or null when the Harbor is not connected.
        /// </summary>
        public DockHost? Host { get; set; } = null;

        /// <summary>
        /// Registered worktrees, or null when they could not be listed.
        /// </summary>
        public IReadOnlyList<GitWorktreeEntry>? Worktrees { get; set; } = null;

        /// <summary>
        /// Local branches with their tips, or null when they could not be listed.
        /// </summary>
        public IReadOnlyList<BranchInfo>? Branches { get; set; } = null;

        /// <summary>
        /// Why the repository could not be fully inspected, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselRepositoryState()
        {
        }
    }
}
