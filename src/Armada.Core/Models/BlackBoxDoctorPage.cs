namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One page of a vessel's configuration doctor: recurring recommendations, most frequent first (then newest).
    /// </summary>
    public class BlackBoxDoctorPage : EnumerationResult<BlackBoxDoctorFinding>
    {
        #region Public-Members

        /// <summary>
        /// Vessel (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// The window in days (1 to 365, default 30).
        /// </summary>
        public int WindowDays { get; set; } = 30;

        /// <summary>
        /// Visible black box entries on the vessel in the window.
        /// </summary>
        public long EntriesInWindow { get; set; } = 0;

        /// <summary>
        /// Entries whose records were read (the newest 200).
        /// </summary>
        public int EntriesScanned { get; set; } = 0;

        /// <summary>
        /// True when the window holds more entries than were scanned.
        /// </summary>
        public bool ScanTruncated { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDoctorPage()
        {
        }

        #endregion
    }
}
