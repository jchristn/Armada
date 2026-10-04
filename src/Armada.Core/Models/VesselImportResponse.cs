namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response to an import request. An inline import returns the finished batch and every item; a background
    /// import returns the job identifier, the batch in status Importing, and no items.
    /// </summary>
    public class VesselImportResponse
    {
        #region Public-Members

        /// <summary>
        /// Batch identifier (vib_ prefix). Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Background job identifier when the import runs as a job, otherwise null.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// True when the import runs as a background job (HTTP 202); false when it ran inline (HTTP 200).
        /// </summary>
        public bool RunsInBackground { get; set; } = false;

        /// <summary>
        /// The batch with its counts and status, or null.
        /// </summary>
        public VesselImportBatch? Batch { get; set; } = null;

        /// <summary>
        /// Every item with its outcome (inline imports only). Never null.
        /// </summary>
        public List<VesselImportItem> Items
        {
            get => _Items;
            set => _Items = value ?? new List<VesselImportItem>();
        }

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private List<VesselImportItem> _Items = new List<VesselImportItem>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportResponse()
        {
        }

        #endregion
    }
}
