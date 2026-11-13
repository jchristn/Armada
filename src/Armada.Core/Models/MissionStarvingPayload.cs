namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Payload of a <c>mission.starving</c> event: a Pending mission waited longer than
    /// Concurrency.StarvationWarningMinutes. Raised once per pending episode.
    /// </summary>
    public class MissionStarvingPayload
    {
        /// <summary>The waiting mission.</summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>The gate the mission waits on when the warning is raised.</summary>
        public MissionWaitGateEnum Gate { get; set; } = MissionWaitGateEnum.Other;

        /// <summary>Whole minutes waited so far.</summary>
        public int WaitedMinutes { get; set; }
    }
}
