namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Status of a voyage.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VoyageStatusEnum
    {
        /// <summary>
        /// Voyage created, missions being set up.
        /// </summary>
        [EnumMember(Value = "Open")]
        Open,

        /// <summary>
        /// Voyage has active missions in progress.
        /// </summary>
        [EnumMember(Value = "InProgress")]
        InProgress,

        /// <summary>
        /// All missions in the voyage are complete.
        /// </summary>
        [EnumMember(Value = "Complete")]
        Complete,

        /// <summary>
        /// Voyage reached a terminal state with one or more failed missions.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// Voyage was cancelled.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled,

        /// <summary>
        /// Being created: the voyage row exists and its missions are being written. None of its missions is admitted
        /// until it moves to Open. A creation that never finishes is failed by the Admiral's health check.
        /// </summary>
        [EnumMember(Value = "Building")]
        Building
    }
}
