namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Query for one page of Ask Armada messages.
    /// </summary>
    public class AskMessageEnumerateQuery
    {
        #region Public-Members

        /// <summary>
        /// Return messages before this sequence, or null for the newest page.
        /// </summary>
        public long? BeforeSequence { get; set; } = null;

        /// <summary>
        /// Page size. Default 30.
        /// </summary>
        public int PageSize { get; set; } = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessageEnumerateQuery()
        {
        }

        #endregion
    }
}
