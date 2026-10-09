namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a Harbor log entry is about, set where the entry is logged.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLogCategoryEnum
    {
        /// <summary>
        /// Anything else: the app's own events (connect requested, settings saved, menu commands).
        /// </summary>
        [EnumMember(Value = "General")]
        General,

        /// <summary>
        /// The link itself: handshakes, heartbeats, and deferred launches.
        /// </summary>
        [EnumMember(Value = "Link")]
        Link,

        /// <summary>
        /// A captain job: launch, start, stop, and exit.
        /// </summary>
        [EnumMember(Value = "Job")]
        Job,

        /// <summary>
        /// A mission dock or vessel checkout: resolve, provision, and reclaim.
        /// </summary>
        [EnumMember(Value = "Dock")]
        Dock,

        /// <summary>
        /// A routine git or gh command the Admiral delegated.
        /// </summary>
        [EnumMember(Value = "Git")]
        Git,

        /// <summary>
        /// A file operation in a dock or checkout.
        /// </summary>
        [EnumMember(Value = "File")]
        File,

        /// <summary>
        /// A check run (build, test, or other check command) in a checkout.
        /// </summary>
        [EnumMember(Value = "CheckRun")]
        CheckRun,

        /// <summary>
        /// Another command the Admiral ran on this host at an operator's request (a fleet action, a Workspace command).
        /// </summary>
        [EnumMember(Value = "Command")]
        Command
    }
}
