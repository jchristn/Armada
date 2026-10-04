namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Query for enumerating vessel import batches.
    /// </summary>
    public class VesselImportBatchEnumerateQuery
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
        public string? Status { get; set; } = null;

        /// <summary>
        /// Order, or null.
        /// </summary>
        public string? Order { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportBatchEnumerateQuery()
        {
        }

        #endregion
    }
}
