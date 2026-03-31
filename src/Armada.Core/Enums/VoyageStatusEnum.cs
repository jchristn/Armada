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
        /// Voyage was cancelled.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
