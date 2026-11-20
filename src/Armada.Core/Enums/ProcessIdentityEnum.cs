namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether a live process id still belongs to the agent process that was launched with it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProcessIdentityEnum
    {
        /// <summary>
        /// No process with that id is running.
        /// </summary>
        NotRunning,

        /// <summary>
        /// The live process started at the recorded launch start time.
        /// </summary>
        Verified,

        /// <summary>
        /// A process is running with that id, but it is a later process (the id was reused).
        /// </summary>
        Reused,

        /// <summary>
        /// A process is running with that id and nothing contradicts it being the launched one: no start time was
        /// recorded, or the live start time cannot be read. Treated as the launched process.
        /// </summary>
        Unverified
    }
}
