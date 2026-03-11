namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// State of a captain agent.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainStateEnum
    {
        /// <summary>
        /// Captain is idle and available for assignment.
        /// </summary>
        [EnumMember(Value = "Idle")]
        Idle,

        /// <summary>
        /// Captain is actively working on a mission.
        /// </summary>
        [EnumMember(Value = "Working")]
        Working,

        /// <summary>
        /// Captain process appears stalled.
        /// </summary>
        [EnumMember(Value = "Stalled")]
        Stalled,

        /// <summary>
        /// Captain is in the process of stopping.
        /// </summary>
        [EnumMember(Value = "Stopping")]
        Stopping
    }
}
