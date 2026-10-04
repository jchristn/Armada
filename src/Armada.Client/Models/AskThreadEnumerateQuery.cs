namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Query for enumerating Ask Armada threads.
    /// </summary>
    public class AskThreadEnumerateQuery
    {
        #region Public-Members

        /// <summary>
        /// Page number (1-based). Default 1.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size. Default 50.
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Server-side search text, or null.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Include archived threads. Default false.
        /// </summary>
        public bool IncludeArchived { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadEnumerateQuery()
        {
        }

        #endregion
    }
}
