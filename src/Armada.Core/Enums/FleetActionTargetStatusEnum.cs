namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Status of a single vessel target within a fleet action run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FleetActionTargetStatusEnum
    {
        /// <summary>
        /// The target is waiting to execute.
        /// </summary>
        Pending,

        /// <summary>
        /// The target was skipped; see the skip reason code.
        /// </summary>
        Skipped,

        /// <summary>
        /// The target is executing (Command kind) or its voyage is active (Mission kind).
        /// </summary>
        Running,

        /// <summary>
        /// The command exited zero, or the voyage completed and landed.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The command exited non-zero, or the voyage failed; see the failure reason code.
        /// </summary>
        Failed,

        /// <summary>
        /// The target was cancelled before it finished.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The command exceeded the run's timeout and was terminated.
        /// </summary>
        TimedOut
    }
}
