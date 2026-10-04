namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to approve a mission review.
    /// </summary>
    public class MissionReviewApproveRequest
    {
        #region Public-Members

        /// <summary>
        /// Reviewer comment, or null.
        /// </summary>
        public string? Comment { get; set; } = null;

        /// <summary>
        /// True for a conditional approval.
        /// </summary>
        public bool? Conditional { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionReviewApproveRequest()
        {
        }

        #endregion
    }
}
