namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Type of work tracked by an Ask Armada thread.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskTrackedEntityTypeEnum
    {
        /// <summary>
        /// A voyage (vyg_).
        /// </summary>
        [EnumMember(Value = "Voyage")]
        Voyage,

        /// <summary>
        /// A mission (msn_).
        /// </summary>
        [EnumMember(Value = "Mission")]
        Mission,

        /// <summary>
        /// A fleet action run (far_).
        /// </summary>
        [EnumMember(Value = "FleetActionRun")]
        FleetActionRun,

        /// <summary>
        /// A background job (job_).
        /// </summary>
        [EnumMember(Value = "Job")]
        Job,

        /// <summary>
        /// A vessel import batch (vib_).
        /// </summary>
        [EnumMember(Value = "VesselImportBatch")]
        VesselImportBatch,

        /// <summary>
        /// A black box entry (bbx_) shown as a card: a read-only reference, not work, so it posts no milestones.
        /// </summary>
        [EnumMember(Value = "BlackBox")]
        BlackBox
    }
}
