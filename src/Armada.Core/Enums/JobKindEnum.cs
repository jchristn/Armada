namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The kind of work a background job performs. The discriminator lets the coordinator route a claimed
    /// job to the right handler.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum JobKindEnum
    {
        /// <summary>
        /// A generic/unspecified background job.
        /// </summary>
        [EnumMember(Value = "Generic")]
        Generic,

        /// <summary>
        /// A bulk cleanup / purge job.
        /// </summary>
        [EnumMember(Value = "Cleanup")]
        Cleanup,

        /// <summary>
        /// A repository or data import/sync job.
        /// </summary>
        [EnumMember(Value = "Sync")]
        Sync,

        /// <summary>
        /// A report/aggregation job.
        /// </summary>
        [EnumMember(Value = "Report")]
        Report,

        /// <summary>
        /// A background scan for vessel import candidates (vessel import discovery).
        /// </summary>
        [EnumMember(Value = "VesselDiscovery")]
        VesselDiscovery,

        /// <summary>
        /// A background vessel import of a discovered batch.
        /// </summary>
        [EnumMember(Value = "VesselImport")]
        VesselImport,

        /// <summary>
        /// A captain analyzing imported repositories and recommending fleets for them.
        /// </summary>
        [EnumMember(Value = "FleetCategorization")]
        FleetCategorization,

        /// <summary>
        /// An account usage capture attempt: one probe of one captain account group on its credential
        /// host. A dedicated job, separate from mission launch slots and a captain's Working/Idle state.
        /// </summary>
        [EnumMember(Value = "UsageProbe")]
        UsageProbe
    }
}
