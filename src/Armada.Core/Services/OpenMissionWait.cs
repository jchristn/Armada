namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>In-memory state for one Pending mission's current wait.</summary>
    internal sealed class OpenMissionWait
    {
        /// <summary>Current gate; null when no interval is open.</summary>
        internal MissionWaitGateEnum? Gate { get; set; }

        /// <summary>Start of the open interval.</summary>
        internal DateTime StartedUtc { get; set; }

        /// <summary>First capacity-gate observation in this pending episode.</summary>
        internal DateTime? ReadyUtc { get; set; }

        /// <summary>Most recent capacity gate in this pending episode.</summary>
        internal MissionWaitGateEnum? LastCapacityGate { get; set; }

        /// <summary>Start of this pending episode, the first wait observed after the last assignment.</summary>
        internal DateTime EpisodeStartUtc { get; set; }

        /// <summary>True once the starvation warning was raised for this episode.</summary>
        internal bool StarvationReported { get; set; }
    }
}
