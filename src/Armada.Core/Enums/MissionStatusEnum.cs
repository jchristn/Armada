namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Status of a mission.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionStatusEnum
    {
        /// <summary>
        /// Mission created but not yet assigned.
        /// </summary>
        [EnumMember(Value = "Pending")]
        Pending,

        /// <summary>
        /// Mission assigned to a captain.
        /// </summary>
        [EnumMember(Value = "Assigned")]
        Assigned,

        /// <summary>
        /// Captain is actively working on the mission.
        /// </summary>
        [EnumMember(Value = "InProgress")]
        InProgress,

        /// <summary>
        /// Mission work complete, under testing.
        /// </summary>
        [EnumMember(Value = "Testing")]
        Testing,

        /// <summary>
        /// Mission awaiting human review.
        /// </summary>
        [EnumMember(Value = "Review")]
        Review,

        /// <summary>
        /// Mission successfully completed.
        /// </summary>
        [EnumMember(Value = "Complete")]
        Complete,

        /// <summary>
        /// Mission failed.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// Mission cancelled.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
