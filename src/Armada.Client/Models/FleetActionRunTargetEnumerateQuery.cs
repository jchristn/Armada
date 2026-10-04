namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Query for enumerating the targets of a fleet action run.
    /// </summary>
    public class FleetActionRunTargetEnumerateQuery
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
        public Armada.Core.Enums.FleetActionTargetStatusEnum? Status { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunTargetEnumerateQuery()
        {
        }

        #endregion
    }
}
