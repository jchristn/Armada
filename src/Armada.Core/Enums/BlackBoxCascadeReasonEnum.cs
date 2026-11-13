namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why garbage collection deletes black box entries with their subject (retention cascades).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxCascadeReasonEnum
    {
        /// <summary>
        /// The entry's mission no longer exists (blackBox.retention.deleteWithMission).
        /// </summary>
        [EnumMember(Value = "MissionDeleted")]
        MissionDeleted,

        /// <summary>
        /// The entry's vessel no longer exists (blackBox.retention.deleteWithVessel).
        /// </summary>
        [EnumMember(Value = "VesselDeleted")]
        VesselDeleted,

        /// <summary>
        /// The entry's tenant is being deleted (always cascades, pinned entries included).
        /// </summary>
        [EnumMember(Value = "TenantDeleted")]
        TenantDeleted,

        /// <summary>
        /// A value this build does not recognize.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
