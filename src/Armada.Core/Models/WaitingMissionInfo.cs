namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>A Pending mission and the gate it currently waits on.</summary>
    public class WaitingMissionInfo
    {
        /// <summary>The mission.</summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>Mission title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>The mission's voyage, if any.</summary>
        public string? VoyageId { get; set; }

        /// <summary>The gate the mission waits on.</summary>
        public MissionWaitGateEnum Gate { get; set; } = MissionWaitGateEnum.Other;

        /// <summary>Selection or blocker detail recorded with the wait.</summary>
        public string? Detail { get; set; }

        /// <summary>When the mission started waiting on this gate.</summary>
        public DateTime WaitingSinceUtc { get; set; }

        /// <summary>Seconds waited on this gate so far.</summary>
        public double WaitedSeconds { get; set; }
    }
}
