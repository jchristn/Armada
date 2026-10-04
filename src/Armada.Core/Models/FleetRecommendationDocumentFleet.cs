namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One fleet entry of a captain-written fleet-recommendations.json file. Values are untrusted and validated
    /// before they are persisted.
    /// </summary>
    public class FleetRecommendationDocumentFleet
    {
        #region Public-Members

        /// <summary>
        /// Fleet name, or null.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Fleet description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Why these repositories belong together, or null.
        /// </summary>
        public string? Rationale { get; set; } = null;

        /// <summary>
        /// Vessel identifiers (vsl_ prefix) in the fleet, or null.
        /// </summary>
        public List<string>? VesselIds { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRecommendationDocumentFleet()
        {
        }

        #endregion
    }
}
