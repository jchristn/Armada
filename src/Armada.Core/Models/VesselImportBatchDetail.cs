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

        /// <summary>
        /// Advisory discovery hints rebuilt from the batch (candidate cap reached, paths not visible to the Admiral).
        /// Never null.
        /// </summary>
        public List<VesselImportHint> Hints
        {
            get => _Hints;
            set => _Hints = value ?? new List<VesselImportHint>();
        }

        /// <summary>
        /// Fleet recommendations from the latest categorization run, in display order. Never null.
        /// </summary>
        public List<VesselImportFleetRecommendation> FleetRecommendations
        {
            get => _FleetRecommendations;
            set => _FleetRecommendations = value ?? new List<VesselImportFleetRecommendation>();
        }

        #endregion

        #region Private-Members

        private VesselImportBatch _Batch = new VesselImportBatch();
        private List<VesselImportItem> _Items = new List<VesselImportItem>();
        private List<VesselImportHint> _Hints = new List<VesselImportHint>();
        private List<VesselImportFleetRecommendation> _FleetRecommendations = new List<VesselImportFleetRecommendation>();

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
