namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Who wrote a black box note.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxNoteAuthorKindEnum
    {
        /// <summary>
        /// A person.
        /// </summary>
        [EnumMember(Value = "Operator")]
        Operator,

        /// <summary>
        /// An agent over MCP.
        /// </summary>
        [EnumMember(Value = "Agent")]
        Agent,

        /// <summary>
        /// Ask Armada.
        /// </summary>
        [EnumMember(Value = "AskArmada")]
        AskArmada,

        /// <summary>
        /// Armada itself (for example a recovery run record).
        /// </summary>
        [EnumMember(Value = "System")]
        System,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
