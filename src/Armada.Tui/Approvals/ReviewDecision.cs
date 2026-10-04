namespace Armada.Tui.Approvals
{
    /// <summary>
    /// A chosen review verdict with its feedback.
    /// </summary>
    public class ReviewDecision
    {
        #region Public-Members

        /// <summary>
        /// Verdict.
        /// </summary>
        public ReviewVerdictEnum Verdict { get; set; } = ReviewVerdictEnum.Approve;

        /// <summary>
        /// Feedback (trimmed; may be empty for Approve and Deny).
        /// </summary>
        public string Comment { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <param name="comment">Feedback.</param>
        public ReviewDecision(ReviewVerdictEnum verdict, string comment)
        {
            Verdict = verdict;
            Comment = (comment ?? "").Trim();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the verdict requires feedback (Conditionally Approve and More Work Required).
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <returns>True when feedback is required.</returns>
        public static bool RequiresFeedback(ReviewVerdictEnum verdict)
        {
            return verdict == ReviewVerdictEnum.Conditional || verdict == ReviewVerdictEnum.MoreWork;
        }

        #endregion
    }
}
