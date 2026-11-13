namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Typed shape of the fleet-recommendations.json file a captain writes during fleet categorization:
    /// <c>{ "fleets": [ { "name", "description", "rationale", "vesselIds": [] } ] }</c>.
    /// </summary>
    public class FleetRecommendationDocument
    {
        #region Public-Members

        /// <summary>
        /// Recommended fleets. Null when the file omitted the property.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "Validated when the captain's file is parsed; a larger file is a format error")]
        public List<FleetRecommendationDocumentFleet>? Fleets { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetRecommendationDocument()
        {
        }

        #endregion
    }
}
