namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Normalized GitHub pull-request detail returned by Armada.
    /// </summary>
    public class GitHubPullRequestDetail
    {
        /// <summary>
        /// Repository full name (owner/repo).
        /// </summary>
        public string Repository { get; set; } = String.Empty;

        /// <summary>
        /// Pull-request number.
        /// </summary>
        public int Number { get; set; } = 0;

        /// <summary>
        /// Optional linked mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Pull-request URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Human-facing title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Optional body text.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Current GitHub state string.
        /// </summary>
        public string State { get; set; } = String.Empty;

        /// <summary>
        /// Whether the pull request is a draft.
        /// </summary>
        public bool IsDraft { get; set; } = false;

        /// <summary>
        /// Whether the pull request has been merged.
        /// </summary>
        public bool IsMerged { get; set; } = false;

        /// <summary>
        /// GitHub mergeability state when available.
        /// </summary>
        public string? MergeableState { get; set; } = null;

        /// <summary>
        /// Derived summary of current review state.
        /// </summary>
        public string ReviewStatus { get; set; } = "NoReview";

        /// <summary>
        /// Base branch name.
        /// </summary>
        public string BaseRefName { get; set; } = String.Empty;

        /// <summary>
        /// Head branch name.
        /// </summary>
        public string HeadRefName { get; set; } = String.Empty;

        /// <summary>
        /// Latest head commit SHA.
        /// </summary>
        public string? HeadSha { get; set; } = null;

        /// <summary>
        /// Author login when available.
        /// </summary>
        public string? AuthorLogin { get; set; } = null;

        /// <summary>
        /// Merge actor login when available.
        /// </summary>
        public string? MergedByLogin { get; set; } = null;

        /// <summary>
        /// Requested reviewer logins: the first <see cref="MaxListed"/> GitHub reports, de-duplicated.
        /// <see cref="RequestedReviewerCount"/> is the full count.
        /// </summary>
        [CollectionBound(MaxListed, Reason = "The first MaxListed reviewers; RequestedReviewerCount carries the full count")]
        public List<string> RequestedReviewers { get; set; } = new List<string>();

        /// <summary>
        /// Number of distinct requested reviewers on the pull request.
        /// </summary>
        public int RequestedReviewerCount { get; set; } = 0;

        /// <summary>
        /// Label names: the first <see cref="MaxListed"/>, de-duplicated. <see cref="LabelCount"/> is the full count.
        /// </summary>
        [CollectionBound(MaxListed, Reason = "The first MaxListed labels; LabelCount carries the full count")]
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Number of distinct labels on the pull request.
        /// </summary>
        public int LabelCount { get; set; } = 0;

        /// <summary>
        /// Most entries listed in <see cref="RequestedReviewers"/> and <see cref="Labels"/> (GitHub's largest page).
        /// </summary>
        public const int MaxListed = 100;

        /// <summary>
        /// Total changed files count.
        /// </summary>
        public int ChangedFiles { get; set; } = 0;

        /// <summary>
        /// Total additions count.
        /// </summary>
        public int Additions { get; set; } = 0;

        /// <summary>
        /// Total deletions count.
        /// </summary>
        public int Deletions { get; set; } = 0;

        /// <summary>
        /// Total commit count.
        /// </summary>
        public int CommitCount { get; set; } = 0;

        /// <summary>
        /// Number of reviews submitted on the pull request. The reviews are read one page at a time from
        /// POST /api/v1/missions/{id}/github/pull-request/reviews/enumerate.
        /// </summary>
        public int ReviewCount { get; set; } = 0;

        /// <summary>
        /// Number of issue comments on the pull request. The comments are read one page at a time from
        /// POST /api/v1/missions/{id}/github/pull-request/comments/enumerate.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Number of provider checks on the head commit. The checks are read one page at a time from
        /// POST /api/v1/missions/{id}/github/pull-request/checks/enumerate.
        /// </summary>
        public int CheckCount { get; set; } = 0;

        /// <summary>
        /// True when <see cref="ReviewStatus"/> was computed from every review. False when the pull request has more
        /// reviews than the server reads to compute it (1000, in pages of 100); the status then reflects the first 1000.
        /// </summary>
        public bool ReviewStatusComplete { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC when available.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC when available.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        /// <summary>
        /// Merge timestamp in UTC when available.
        /// </summary>
        public DateTime? MergedUtc { get; set; } = null;
    }

    /// <summary>
    /// One GitHub pull-request review record.
    /// </summary>
    public class GitHubPullRequestReview
    {
        /// <summary>
        /// Reviewer login.
        /// </summary>
        public string? ReviewerLogin { get; set; } = null;

        /// <summary>
        /// Review state string.
        /// </summary>
        public string State { get; set; } = String.Empty;

        /// <summary>
        /// Optional review body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Submission timestamp in UTC when available.
        /// </summary>
        public DateTime? SubmittedUtc { get; set; } = null;
    }

    /// <summary>
    /// One GitHub pull-request issue comment.
    /// </summary>
    public class GitHubPullRequestComment
    {
        /// <summary>
        /// Comment author login.
        /// </summary>
        public string? AuthorLogin { get; set; } = null;

        /// <summary>
        /// Comment body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Comment URL.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC when available.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;
    }

    /// <summary>
    /// One GitHub check-run summary attached to the pull request head commit.
    /// </summary>
    public class GitHubPullRequestCheck
    {
        /// <summary>
        /// Check name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Current status string.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Final conclusion string when available.
        /// </summary>
        public string? Conclusion { get; set; } = null;

        /// <summary>
        /// Optional provider details URL.
        /// </summary>
        public string? DetailsUrl { get; set; } = null;
    }
}
