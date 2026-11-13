namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle of a model probe run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelProbeRunStatusEnum
    {
        /// <summary>
        /// Created, waiting for an evaluation concurrency slot.
        /// </summary>
        [EnumMember(Value = "Queued")]
        Queued,

        /// <summary>
        /// Probes are running.
        /// </summary>
        [EnumMember(Value = "Running")]
        Running,

        /// <summary>
        /// Every probe has a result.
        /// </summary>
        [EnumMember(Value = "Completed")]
        Completed,

        /// <summary>
        /// The run stopped before every probe had a result (for example the Admiral restarted).
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// Stopped before it finished (cancelled, timed out or out of budget); what finished is kept.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
