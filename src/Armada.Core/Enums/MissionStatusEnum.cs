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
        /// Agent exited successfully and work exists on the mission branch,
        /// but landing (merge, PR, or push) has not yet been attempted or completed.
        /// </summary>
        [EnumMember(Value = "WorkProduced")]
        WorkProduced,

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
        /// Landing was attempted but failed (merge conflict, push failure, PR creation failure, etc.).
        /// </summary>
        [EnumMember(Value = "LandingFailed")]
        LandingFailed,

        /// <summary>
        /// Mission cancelled.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
