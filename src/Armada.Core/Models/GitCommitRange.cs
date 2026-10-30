namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// The commits reachable from one revision but not another (git log from..to), newest first, bounded.
    /// </summary>
    public class GitCommitRange
    {
        #region Public-Members

        /// <summary>
        /// Largest limit <see cref="Armada.Core.Services.Interfaces.IGitService.GetCommitRangeAsync"/> accepts.
        /// </summary>
        public const int MaxLimit = 500;

        /// <summary>
        /// Most changed paths kept per commit.
        /// </summary>
        public const int MaxPathsPerCommit = 200;

        /// <summary>
        /// Most trailers kept per commit.
        /// </summary>
        public const int MaxTrailersPerCommit = 32;

        /// <summary>
        /// The resolved full SHA of the lower bound (excluded).
        /// </summary>
        public string FromSha { get; set; } = String.Empty;

        /// <summary>
        /// The resolved full SHA of the upper bound (included).
        /// </summary>
        public string ToSha { get; set; } = String.Empty;

        /// <summary>
        /// Commits, newest first. Never null.
        /// </summary>
        [CollectionBound(MaxLimit, Reason = "GitService.GetCommitRangeAsync rejects a larger limit")]
        public List<GitCommitInfo> Commits { get; set; } = new List<GitCommitInfo>();

        /// <summary>
        /// Total commits in the range before the limit (git rev-list --count).
        /// </summary>
        public int TotalCount { get; set; } = 0;

        /// <summary>
        /// Whether the range held more commits than were returned.
        /// </summary>
        public bool Truncated { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitCommitRange()
        {
        }

        #endregion
    }
}
