namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>The outcome of one admission attempt, recorded after the admission locks are released.</summary>
    internal sealed class MissionWaitObservation
    {
        /// <summary>Mission as the attempt last read it.</summary>
        internal Mission Mission { get; set; } = null!;

        /// <summary>Gate the mission waits on; null when it left Pending.</summary>
        internal MissionWaitGateEnum? Gate { get; set; }

        /// <summary>Short reason recorded on the interval.</summary>
        internal string? Detail { get; set; }
    }
}
