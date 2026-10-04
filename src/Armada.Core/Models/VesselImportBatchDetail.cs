namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A vessel import batch with all of its items.
    /// </summary>
    public class VesselImportBatchDetail
    {
        #region Public-Members

        /// <summary>
        /// The batch. Never null.
        /// </summary>
        public VesselImportBatch Batch
        {
            get => _Batch;
            set => _Batch = value ?? throw new ArgumentNullException(nameof(Batch));
        }

        /// <summary>
        /// Items ordered by path. Never null.
        /// </summary>
        public List<VesselImportItem> Items
        {
            get => _Items;
            set => _Items = value ?? new List<VesselImportItem>();
        }

        #endregion

        #region Private-Members

        private VesselImportBatch _Batch = new VesselImportBatch();
        private List<VesselImportItem> _Items = new List<VesselImportItem>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportBatchDetail()
        {
        }

        #endregion
    }
}
