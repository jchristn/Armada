namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/ask/threads/{id}/messages/enumerate.
    /// </summary>
    public class AskMessageEnumerateRequest
    {
        #region Public-Members

        /// <summary>
        /// Return messages with a sequence lower than this (paging backwards), or null for the newest page.
        /// </summary>
        public int? BeforeSequence { get; set; } = null;

        /// <summary>
        /// Page size. Default 50, minimum 1, maximum 200.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < 1 ? 1 : (value > 200 ? 200 : value);
        }

        #endregion

        #region Private-Members

        private int _PageSize = 50;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessageEnumerateRequest()
        {
        }

        #endregion
    }
}
