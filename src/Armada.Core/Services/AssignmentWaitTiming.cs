namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Wait timing resolved just before a mission's assignment is recorded.</summary>
    internal sealed class AssignmentWaitTiming
    {
        /// <summary>Assignment time.</summary>
        internal DateTime AssignedUtc { get; set; }

        /// <summary>First capacity-ready time, or the assignment time when no capacity gate was observed.</summary>
        internal DateTime ReadyUtc { get; set; }

        /// <summary>Start of the queue time measurement; null when unknown.</summary>
        internal DateTime? QueueStartUtc { get; set; }

        /// <summary>Last capacity gate before assignment; null when the mission was admitted on first sight.</summary>
        internal MissionWaitGateEnum? LastCapacityGate { get; set; }
    }
}
