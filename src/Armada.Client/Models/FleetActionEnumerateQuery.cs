namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Query for enumerating fleet actions.
    /// </summary>
    public class FleetActionEnumerateQuery
    {
        #region Public-Members

        /// <summary>
        /// Page number. Default 1.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size. Default 25.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Include inactive actions. Default false.
        /// </summary>
        public bool IncludeInactive { get; set; } = false;

        /// <summary>
        /// CreatedAscending or CreatedDescending, or null.
        /// </summary>
        public string? Order { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionEnumerateQuery()
        {
        }

        #endregion
    }
}
