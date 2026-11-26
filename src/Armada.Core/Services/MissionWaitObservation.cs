namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>The outcome of one admission attempt, recorded after the admission locks are released.</summary>
    internal sealed class MissionWaitObservation
    {
        /// <summary>Mission as the attempt last read it.</summary>
        internal Mission Mission { get; set; } = null!;

        /// <summary>Gate the mission waits on for wait tracking; null when no wait interval changes.</summary>
        internal MissionWaitGateEnum? Gate { get; set; }

        /// <summary>True when the mission left Pending during the attempt.</summary>
        internal bool LeftPending { get; set; }

        /// <summary>Typed reason the attempt did not admit the mission (RS-4.6); null when it left Pending.</summary>
        internal MissionAssignmentBlocker? Blocker { get; set; }

        /// <summary>Short reason recorded on the interval.</summary>
        internal string? Detail { get; set; }
    }
}
