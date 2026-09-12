namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Execution mode of a mission. The mode selects how much brief the captain receives and how the
    /// landing gate interprets the result. Implementation is the default write mode; Audit and Research
    /// are read-only modes that produce a written report instead of a commit.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionModeEnum
    {
        /// <summary>
        /// Standard write mission: the captain modifies the repository and the result lands (or holds for
        /// review). An empty diff is treated as a no-op failure. This is the default.
        /// </summary>
        [EnumMember(Value = "Implementation")]
        Implementation,

        /// <summary>
        /// Read-only audit: the captain inspects the repository and reports findings without modifying files.
        /// The landing gate treats "no commit" as success, not failure.
        /// </summary>
        [EnumMember(Value = "Audit")]
        Audit,

        /// <summary>
        /// Read-only research: the captain investigates a question and reports its conclusions without
        /// modifying files. The landing gate treats "no commit" as success, not failure.
        /// </summary>
        [EnumMember(Value = "Research")]
        Research
    }
}
