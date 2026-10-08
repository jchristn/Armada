namespace Armada.Core.Hosting
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A group of log files Harbor can show: its own log and the jobs it ran (always, they are on Harbor's machine), and
    /// the Admiral's log groups (only when the Admiral runs on the same machine).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogSourceEnum
    {
        /// <summary>
        /// Harbor's own log (~/.armada-harbor/logs/harbor.log.yyyyMMdd).
        /// </summary>
        [EnumMember(Value = "Harbor")]
        Harbor,

        /// <summary>
        /// The output of jobs (missions, Ask turns, and other captain launches) run on this machine.
        /// </summary>
        [EnumMember(Value = "HarborJobs")]
        HarborJobs,

        /// <summary>
        /// One of the Admiral's log groups (<see cref="LogCategoryEnum"/>).
        /// </summary>
        [EnumMember(Value = "Admiral")]
        Admiral
    }
}
