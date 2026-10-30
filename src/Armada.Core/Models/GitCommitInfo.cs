namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services;

    /// <summary>
    /// A commit read by <see cref="Armada.Core.Services.Interfaces.IGitService.GetCommitRangeAsync"/>: identity, author,
    /// dates, subject, parsed trailers and changed paths.
    /// </summary>
    public class GitCommitInfo
    {
        #region Public-Members

        /// <summary>
        /// Full commit SHA.
        /// </summary>
        public string Sha { get; set; } = String.Empty;

        /// <summary>
        /// Author name.
        /// </summary>
        public string AuthorName { get; set; } = String.Empty;

        /// <summary>
        /// Author email.
        /// </summary>
        public string AuthorEmail { get; set; } = String.Empty;

        /// <summary>
        /// Author date (UTC).
        /// </summary>
        public DateTime AuthoredUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Committer date (UTC).
        /// </summary>
        public DateTime CommittedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Subject line.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Whether the commit has more than one parent.
        /// </summary>
        public bool IsMerge { get; set; } = false;

        /// <summary>
        /// Trailers from the last paragraph of the message, in order.
        /// </summary>
        [CollectionBound(GitCommitRange.MaxTrailersPerCommit, Reason = "GitTrailerParser keeps at most this many trailers")]
        public List<GitTrailer> Trailers { get; set; } = new List<GitTrailer>();

        /// <summary>
        /// Changed paths (repository-relative, forward slashes; merges against their first parent), capped at
        /// <see cref="GitCommitRange.MaxPathsPerCommit"/>.
        /// </summary>
        [CollectionBound(GitCommitRange.MaxPathsPerCommit, Reason = "GitService.GetCommitRangeAsync caps the paths per commit")]
        public List<string> ChangedPaths { get; set; } = new List<string>();

        /// <summary>
        /// Total changed paths before capping.
        /// </summary>
        public int ChangedPathTotal { get; set; } = 0;

        /// <summary>
        /// The Armada-Mission-Id trailer value, or null for a commit made outside Armada.
        /// </summary>
        public string? MissionId
        {
            get
            {
                GitTrailer? trailer = Trailers.LastOrDefault(t => String.Equals(t.Key, LandingReconciler.MissionIdTrailerKey, StringComparison.OrdinalIgnoreCase) && t.Value.Length > 0);
                return trailer?.Value;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitCommitInfo()
        {
        }

        #endregion
    }
}
