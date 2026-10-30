namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Audience a FIX THIS brief is rendered for.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxAudienceEnum
    {
        /// <summary>
        /// A coding agent asked to FIX THIS (default).
        /// </summary>
        [EnumMember(Value = "Agent")]
        Agent,

        /// <summary>
        /// The operator: plain language, no stack frames.
        /// </summary>
        [EnumMember(Value = "User")]
        User,

        /// <summary>
        /// An Armada maintainer: source pointers and improvement hypotheses.
        /// </summary>
        [EnumMember(Value = "Maintainer")]
        Maintainer,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
