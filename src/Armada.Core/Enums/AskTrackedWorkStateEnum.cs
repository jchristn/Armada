namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Coarse state of tracked work in an Ask Armada thread.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskTrackedWorkStateEnum
    {
        /// <summary>
        /// Still running; the tracker keeps watching it.
        /// </summary>
        [EnumMember(Value = "Active")]
        Active,

        /// <summary>
        /// Finished successfully.
        /// </summary>
        [EnumMember(Value = "Succeeded")]
        Succeeded,

        /// <summary>
        /// Finished with a failure.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// Cancelled before finishing.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
