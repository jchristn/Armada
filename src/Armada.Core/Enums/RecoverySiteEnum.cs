namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a failure was seen, which picks the recovery actions open to
    /// <see cref="Armada.Core.Services.RecoveryPolicyEngine"/> (RS-5.10).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoverySiteEnum
    {
        /// <summary>
        /// A captain's agent process exited with a failure the runtime classified.
        /// </summary>
        [EnumMember(Value = "RuntimeExit")]
        RuntimeExit,

        /// <summary>
        /// The health monitor found the captain's process gone; only the exit code is known.
        /// </summary>
        [EnumMember(Value = "ProcessLost")]
        ProcessLost,

        /// <summary>
        /// A work stage completed with no changes, or ended its run unfinished.
        /// </summary>
        [EnumMember(Value = "NoOpCompletion")]
        NoOpCompletion,

        /// <summary>
        /// A stage's output broke its output contract.
        /// </summary>
        [EnumMember(Value = "OutputContract")]
        OutputContract,

        /// <summary>
        /// A Judge or verifier rejected a Worker slice.
        /// </summary>
        [EnumMember(Value = "JudgeRejection")]
        JudgeRejection,

        /// <summary>
        /// A recovery incident is open for a failed standalone mission.
        /// </summary>
        [EnumMember(Value = "Incident")]
        Incident
    }
}
