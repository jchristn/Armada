namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Optional comment for an approve or deny decision.
    /// </summary>
    public class DecisionCommentRequest
    {
        #region Public-Members

        /// <summary>
        /// Comment, or null.
        /// </summary>
        public string? Comment { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DecisionCommentRequest()
        {
        }

        #endregion
    }
}
