namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One page of a vessel's soft directory reservations, ordered by directory then mission id.
    /// </summary>
    public class VesselReservationPage : EnumerationResult<VesselReservation>
    {
        #region Public-Members

        /// <summary>
        /// Vessel (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// The hotspot threshold in effect (blackBox.hotspotReservationThreshold); 0 means reservations are off and the page is empty.
        /// </summary>
        public int Threshold { get; set; } = 0;

        /// <summary>
        /// Directories at or above the threshold.
        /// </summary>
        public int HotDirectoryCount { get; set; } = 0;

        /// <summary>
        /// Active missions read (at most 200).
        /// </summary>
        public int ActiveMissionsScanned { get; set; } = 0;

        /// <summary>
        /// True when the vessel has more than 200 active missions or more than 1000 hotspot directories and the rest were not read.
        /// </summary>
        public bool ScanTruncated { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselReservationPage()
        {
        }

        #endregion
    }
}
