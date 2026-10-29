namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether a live process is the one recorded at launch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LaunchedProcessIdentityEnum
    {
        /// <summary>
        /// No process with that id is running.
        /// </summary>
        NotRunning,

        /// <summary>
        /// The live process start time matches the recorded launch.
        /// </summary>
        Verified,

        /// <summary>
        /// A process is running, but its start time is a different launch.
        /// </summary>
        Reused,

        /// <summary>
        /// A process is running, and its start time cannot be compared with a recorded launch.
        /// </summary>
        Unverified
    }
}
