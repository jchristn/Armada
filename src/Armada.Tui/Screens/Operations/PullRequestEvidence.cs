namespace Armada.Tui.Screens.Operations
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// A mission pull request's detail with the first page of its reviews and checks, loaded together for the mission
    /// screen.
    /// </summary>
    public class PullRequestEvidence
    {
        #region Public-Members

        /// <summary>
        /// Detail, or null when unavailable.
        /// </summary>
        public GitHubPullRequestDetail? Detail { get; }

        /// <summary>
        /// First page of reviews.
        /// </summary>
        public List<GitHubPullRequestReview> Reviews { get; }

        /// <summary>
        /// First page of checks.
        /// </summary>
        public List<GitHubPullRequestCheck> Checks { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="detail">Detail, or null.</param>
        /// <param name="reviews">First page of reviews, or null for none.</param>
        /// <param name="checks">First page of checks, or null for none.</param>
        public PullRequestEvidence(GitHubPullRequestDetail? detail, List<GitHubPullRequestReview>? reviews, List<GitHubPullRequestCheck>? checks)
        {
            Detail = detail;
            Reviews = reviews ?? new List<GitHubPullRequestReview>();
            Checks = checks ?? new List<GitHubPullRequestCheck>();
        }

        #endregion
    }
}
