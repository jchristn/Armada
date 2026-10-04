namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to deny a mission review.
    /// </summary>
    public class MissionReviewDenyRequest
    {
        #region Public-Members

        /// <summary>
        /// Reviewer comment, or null.
        /// </summary>
        public string? Comment { get; set; } = null;

        /// <summary>
        /// RetryStage or FailPipeline, or null.
        /// </summary>
        public string? Action { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionReviewDenyRequest()
        {
        }

        #endregion
    }
}
