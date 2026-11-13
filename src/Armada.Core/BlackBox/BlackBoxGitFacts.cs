namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Volatile git facts a failure site holds at the moment of failure (nobody can reconstruct them later). All optional.
    /// </summary>
    public class BlackBoxGitFacts
    {
        /// <summary>
        /// Files git reported as conflicted (any count; the capture bounds them and records the total).
        /// </summary>
        public List<string> ConflictedFiles
        {
            get => _ConflictedFiles;
            set => _ConflictedFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Paths with uncommitted tracked changes (porcelain paths).
        /// </summary>
        public List<string> DirtyPaths
        {
            get => _DirtyPaths;
            set => _DirtyPaths = value ?? new List<string>();
        }

        /// <summary>
        /// Target tip before the integration attempt.
        /// </summary>
        public string? TargetTipBefore { get; set; } = null;

        /// <summary>
        /// Target tip after the attempt (or at the failure).
        /// </summary>
        public string? TargetTipAfter { get; set; } = null;

        /// <summary>
        /// Whether the worktree existed (false when it had to be, or could not be, recreated).
        /// </summary>
        public bool? WorktreeExisted { get; set; } = null;

        /// <summary>
        /// Target checkout path.
        /// </summary>
        public string? CheckoutPath { get; set; } = null;

        /// <summary>
        /// Mission branch tip.
        /// </summary>
        public string? BranchTip { get; set; } = null;

        /// <summary>
        /// Git operation that failed (for example merge, push, fast-forward).
        /// </summary>
        public string? GitOperation { get; set; } = null;

        /// <summary>
        /// Git stderr.
        /// </summary>
        public string? GitStderr { get; set; } = null;

        private List<string> _ConflictedFiles = new List<string>();
        private List<string> _DirtyPaths = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxGitFacts()
        {
        }
    }
}
