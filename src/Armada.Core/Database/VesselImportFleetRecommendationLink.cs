namespace Armada.Core.Database
{
    using System;

    /// <summary>
    /// One row of vessel_import_fleet_recommendation_vessels, as read by the provider implementations.
    /// </summary>
    public class VesselImportFleetRecommendationLink
    {
        #region Public-Members

        /// <summary>
        /// Recommendation identifier (vfr_ prefix). Never null.
        /// </summary>
        public string RecommendationId
        {
            get => _RecommendationId;
            set => _RecommendationId = value ?? String.Empty;
        }

        /// <summary>
        /// Vessel identifier (vsl_ prefix). Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _RecommendationId = String.Empty;
        private string _VesselId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportFleetRecommendationLink()
        {
        }

        #endregion
    }
}
