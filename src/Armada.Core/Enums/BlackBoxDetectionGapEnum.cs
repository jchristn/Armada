namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why Armada did not catch or prevent a failure earlier (resilience analysis).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxDetectionGapEnum
    {
        /// <summary>
        /// No check ran before the step that failed.
        /// </summary>
        [EnumMember(Value = "NoPreflightCheck")]
        NoPreflightCheck,

        /// <summary>
        /// A check exists but runs after the damage is done.
        /// </summary>
        [EnumMember(Value = "CheckTooLate")]
        CheckTooLate,

        /// <summary>
        /// State the step needed was not persisted, so a restart or another component lost it.
        /// </summary>
        [EnumMember(Value = "StateNotPersisted")]
        StateNotPersisted,

        /// <summary>
        /// Two components acted on the same mission, dock, branch or target concurrently.
        /// </summary>
        [EnumMember(Value = "Race")]
        Race,

        /// <summary>
        /// An invariant that should hold is not asserted anywhere.
        /// </summary>
        [EnumMember(Value = "MissingInvariant")]
        MissingInvariant,

        /// <summary>
        /// The failure is retryable but Armada did not retry (or gave up too early).
        /// </summary>
        [EnumMember(Value = "MissingRetry")]
        MissingRetry,

        /// <summary>
        /// No detection gap: Armada detected it as early as it could.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// Not decided, or a value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
