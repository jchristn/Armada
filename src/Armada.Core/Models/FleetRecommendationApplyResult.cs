namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of applying fleet recommendations: the fleets used and every vessel assignment.
    /// </summary>
    public class FleetRecommendationApplyResult
    {
        #region Public-Members

        /// <summary>
        /// Import batch identifier (vib_ prefix). Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Fleets that received vessels, created or reused, in request order. Never null.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One per requested fleet with vessels")]
        public List<Fleet> Fleets
        {
            get => _Fleets;
            set => _Fleets = value ?? new List<Fleet>();
        }

        /// <summary>
        /// Identifiers of the fleets this apply created (the rest were reused by name). Never null.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "Subset of the requested fleets")]
        public List<string> CreatedFleetIds
        {
            get => _CreatedFleetIds;
            set => _CreatedFleetIds = value ?? new List<string>();
        }

        /// <summary>
        /// Vessel assignments. Never null.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One per distinct batch vessel (a batch holds at most 1000)")]
        public List<FleetRecommendationAssignment> Assignments
        {
            get => _Assignments;
            set => _Assignments = value ?? new List<FleetRecommendationAssignment>();
        }

        /// <summary>
        /// The batch after the apply (categorization status Applied), or null.
        /// </summary>
        public VesselImportBatch? Batch { get; set; } = null;

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private List<Fleet> _Fleets = new List<Fleet>();
        private List<string> _CreatedFleetIds = new List<string>();
        private List<FleetRecommendationAssignment> _Assignments = new List<FleetRecommendationAssignment>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRecommendationApplyResult()
        {
        }

        #endregion
    }
}
