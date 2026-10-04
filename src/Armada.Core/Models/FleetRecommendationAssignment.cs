namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One vessel-to-fleet assignment made by applying fleet recommendations.
    /// </summary>
    public class FleetRecommendationAssignment
    {
        #region Public-Members

        /// <summary>
        /// Vessel identifier (vsl_ prefix). Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Vessel name at the time of the assignment, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Fleet identifier (flt_ prefix) the vessel now belongs to. Never null.
        /// </summary>
        public string FleetId
        {
            get => _FleetId;
            set => _FleetId = value ?? String.Empty;
        }

        /// <summary>
        /// Fleet name. Never null.
        /// </summary>
        public string FleetName
        {
            get => _FleetName;
            set => _FleetName = value ?? String.Empty;
        }

        /// <summary>
        /// Fleet the vessel belonged to before the assignment, or null.
        /// </summary>
        public string? PreviousFleetId { get; set; } = null;

        #endregion

        #region Private-Members

        private string _VesselId = String.Empty;
        private string _FleetId = String.Empty;
        private string _FleetName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRecommendationAssignment()
        {
        }

        #endregion
    }
}
