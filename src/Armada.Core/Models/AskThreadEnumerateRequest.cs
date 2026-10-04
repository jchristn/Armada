namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/ask/threads/enumerate.
    /// </summary>
    public class AskThreadEnumerateRequest
    {
        #region Public-Members

        /// <summary>
        /// 1-based page number. Default 1, minimum 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Page size. Default 25, minimum 1, maximum 100.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < 1 ? 1 : (value > 100 ? 100 : value);
        }

        /// <summary>
        /// Case-insensitive substring matched against the title, or null.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Whether archived threads are included. Default false.
        /// </summary>
        public bool IncludeArchived { get; set; } = false;

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadEnumerateRequest()
        {
        }

        #endregion
    }
}
