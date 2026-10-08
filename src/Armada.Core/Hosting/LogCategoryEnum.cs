namespace Armada.Core.Hosting
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A group of files under the Admiral's log directory.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogCategoryEnum
    {
        /// <summary>
        /// The Admiral's own log (admiral.log and its daily files).
        /// </summary>
        [EnumMember(Value = "Admiral")]
        Admiral,

        /// <summary>
        /// Per-mission agent session logs (missions/msn_*.log).
        /// </summary>
        [EnumMember(Value = "Missions")]
        Missions,

        /// <summary>
        /// Per-captain logs (captains/cpt_*.log).
        /// </summary>
        [EnumMember(Value = "Captains")]
        Captains,

        /// <summary>
        /// Saved mission diffs (diffs/msn_*.diff).
        /// </summary>
        [EnumMember(Value = "Diffs")]
        Diffs,

        /// <summary>
        /// Instructions written for captains (instructions/).
        /// </summary>
        [EnumMember(Value = "Instructions")]
        Instructions,

        /// <summary>
        /// Captains' final messages (final-messages/).
        /// </summary>
        [EnumMember(Value = "FinalMessages")]
        FinalMessages,

        /// <summary>
        /// Dock lifecycle logs (docks/).
        /// </summary>
        [EnumMember(Value = "Docks")]
        Docks
    }
}
