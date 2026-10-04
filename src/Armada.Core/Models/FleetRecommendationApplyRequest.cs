namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to apply fleet recommendations to an import batch. The list may differ from what the captain
    /// recommended: operators can rename fleets, move vessels, and add or drop fleets before applying.
    /// </summary>
    public class FleetRecommendationApplyRequest
    {
        #region Public-Members

        /// <summary>
        /// Fleets to create or reuse and the vessels to assign to each. A vessel may appear in at most one fleet.
        /// Fleets without vessels are skipped. Never null.
        /// </summary>
        public List<FleetRecommendationApplyFleet> Fleets
        {
            get => _Fleets;
            set => _Fleets = value ?? new List<FleetRecommendationApplyFleet>();
        }

        #endregion

        #region Private-Members

        private List<FleetRecommendationApplyFleet> _Fleets = new List<FleetRecommendationApplyFleet>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRecommendationApplyRequest()
        {
        }

        #endregion
    }
}
