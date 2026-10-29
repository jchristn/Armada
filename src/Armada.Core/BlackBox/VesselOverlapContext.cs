namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What <see cref="MissionOverlapAnalyzer"/> knows about a vessel at one moment: its tracked files, its active
    /// missions with their likely files, its hotspot directories and the reservations they produce.
    /// </summary>
    public class VesselOverlapContext
    {
        #region Public-Members

        /// <summary>The vessel.</summary>
        public Vessel Vessel { get; }

        /// <summary>Tracked files, or null when they could not be read.</summary>
        public VesselTrackedFiles? Tracked { get; set; } = null;

        /// <summary>Active missions with their likely files, newest first.</summary>
        public List<ActiveMissionFileSet> ActiveMissions { get; } = new List<ActiveMissionFileSet>();

        /// <summary>True when the vessel has more active missions than were read.</summary>
        public bool ActiveMissionsTruncated { get; set; } = false;

        /// <summary>Hotspot directories with their entry counts.</summary>
        public Dictionary<string, BlackBoxHotspot> Hotspots { get; } = new Dictionary<string, BlackBoxHotspot>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when the vessel has more hotspot directories than were read.</summary>
        public bool HotspotsTruncated { get; set; } = false;

        /// <summary>Entries a directory needs to count as hot (warnings).</summary>
        public int HotThreshold { get; set; } = 3;

        /// <summary>Entries a directory needs before a scoped mission reserves it; 0 turns reservations off.</summary>
        public int ReservationThreshold { get; set; } = 3;

        /// <summary>Soft reservations, by directory then mission id.</summary>
        public List<VesselReservation> Reservations { get; } = new List<VesselReservation>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        public VesselOverlapContext(Vessel vessel)
        {
            Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
        }

        #endregion
    }
}
