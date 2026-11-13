namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How two missions in a voyage cascade graph relate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxCascadeEdgeKindEnum
    {
        /// <summary>
        /// The target mission depends on the source mission (DependsOnMissionId).
        /// </summary>
        DependsOn,

        /// <summary>
        /// The target mission was cancelled or failed because the source mission failed (dependency failed or cancelled).
        /// </summary>
        CancelledBecause,

        /// <summary>
        /// The target mission is held (Pending or Blocked) behind the failed source mission.
        /// </summary>
        BlockedBy,

        /// <summary>
        /// Both missions are in the voyage and neither depends on the other; drawn from the cascade root to its failed siblings.
        /// </summary>
        Sibling,

        /// <summary>
        /// Unrecognized value.
        /// </summary>
        Unknown
    }
}
