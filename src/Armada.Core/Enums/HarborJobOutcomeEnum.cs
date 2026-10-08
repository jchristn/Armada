namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a captain launch delegated to a Harbor ended, as the Admiral recorded it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborJobOutcomeEnum
    {
        /// <summary>
        /// The job has not ended yet (launched or running).
        /// </summary>
        [EnumMember(Value = "Running")]
        Running,

        /// <summary>
        /// The process exited with code 0.
        /// </summary>
        [EnumMember(Value = "Succeeded")]
        Succeeded,

        /// <summary>
        /// The process exited with a non-zero code, or the Harbor could not launch it.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// The Admiral asked the Harbor to stop the job, and it then exited.
        /// </summary>
        [EnumMember(Value = "Stopped")]
        Stopped,

        /// <summary>
        /// The job never reported an exit: its Harbor reconnected without it among its live jobs.
        /// </summary>
        [EnumMember(Value = "Lost")]
        Lost
    }
}
