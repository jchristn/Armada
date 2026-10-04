namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Query for enumerating fleet action runs.
    /// </summary>
    public class FleetActionRunEnumerateQuery
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
        /// Status filter, or null.
        /// </summary>
        public Armada.Core.Enums.FleetActionRunStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// CreatedAscending or CreatedDescending, or null.
        /// </summary>
        public string? Order { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunEnumerateQuery()
        {
        }

        #endregion
    }
}
