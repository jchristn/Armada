namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>A vessel's voyage and mission slots, waiting missions and recent slot wait.</summary>
    public class VesselConcurrency
    {
        /// <summary>The vessel.</summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>Vessel name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Voyages that may run on the vessel at once; 0 is unlimited.</summary>
        public int MaxParallelVoyages { get; set; }

        /// <summary>Missions per voyage that may run at once; 0 is unlimited.</summary>
        public int MaxParallelMissionsPerVoyage { get; set; }

        /// <summary>Voyage slots held now.</summary>
        public int VoyageSlotsInUse { get; set; }

        /// <summary>Missions running on the vessel now (Assigned or InProgress).</summary>
        public int RunningMissions { get; set; }

        /// <summary>Voyages holding a slot, busiest first.</summary>
        [CollectionBound(100, Reason = "The 100 busiest holding voyages; VoyageSlotsInUse is the total")]
        public List<VesselVoyageSlotUsage> HoldingVoyages { get; set; } = new List<VesselVoyageSlotUsage>();

        /// <summary>Pending missions on the vessel and their current gate, longest wait first (at most 100).</summary>
        [CollectionBound(100, Reason = "The 100 longest waits; WaitingCount is the total")]
        public List<WaitingMissionInfo> Waiting { get; set; } = new List<WaitingMissionInfo>();

        /// <summary>Total Pending missions waiting on a recorded gate, which may exceed the listed count.</summary>
        public int WaitingCount { get; set; }

        /// <summary>Slot wait over the last 24 hours.</summary>
        public SlotWaitStatistics SlotWait24h { get; set; } = new SlotWaitStatistics { WindowHours = 24 };

        /// <summary>Slot wait over the last 7 days.</summary>
        public SlotWaitStatistics SlotWait7d { get; set; } = new SlotWaitStatistics { WindowHours = 168 };

        /// <summary>When the report was computed.</summary>
        public DateTime ComputedUtc { get; set; } = DateTime.UtcNow;
    }
}
