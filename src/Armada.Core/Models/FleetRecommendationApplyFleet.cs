namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One fleet in an apply request: the (possibly edited) name, description, and vessels of a recommendation.
    /// </summary>
    public class FleetRecommendationApplyFleet
    {
        #region Public-Members

        /// <summary>
        /// Fleet name. An existing fleet in the tenant with the same name (case-insensitive) is reused; otherwise a
        /// fleet is created. Never null.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? String.Empty;
        }

        /// <summary>
        /// Description for a newly created fleet, or null. Ignored when an existing fleet is reused.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Vessels (vsl_ prefix) to assign to the fleet. Never null.
        /// </summary>
        public List<string> VesselIds
        {
            get => _VesselIds;
            set => _VesselIds = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private List<string> _VesselIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRecommendationApplyFleet()
        {
        }

        #endregion
    }
}
