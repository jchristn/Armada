namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a dispatch overlap warning compares the new mission with.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionOverlapKindEnum
    {
        /// <summary>
        /// Files of an active mission on the same vessel (scoped files, paths in its brief, recent path commits, its diff or branch).
        /// </summary>
        ActiveMission,

        /// <summary>
        /// A directory the vessel's black box entries mark as a conflict hotspot.
        /// </summary>
        Hotspot,

        /// <summary>
        /// A hotspot directory an active mission reserves through its scoped files.
        /// </summary>
        Reservation,

        /// <summary>
        /// Unrecognized value.
        /// </summary>
        Unknown
    }
}
