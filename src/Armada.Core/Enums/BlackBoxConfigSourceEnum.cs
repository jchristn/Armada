namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where an effective configuration value came from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxConfigSourceEnum
    {
        /// <summary>
        /// The mission.
        /// </summary>
        [EnumMember(Value = "Mission")]
        Mission,

        /// <summary>
        /// The voyage.
        /// </summary>
        [EnumMember(Value = "Voyage")]
        Voyage,

        /// <summary>
        /// The vessel.
        /// </summary>
        [EnumMember(Value = "Vessel")]
        Vessel,

        /// <summary>
        /// The workflow profile.
        /// </summary>
        [EnumMember(Value = "WorkflowProfile")]
        WorkflowProfile,

        /// <summary>
        /// The captain.
        /// </summary>
        [EnumMember(Value = "Captain")]
        Captain,

        /// <summary>
        /// The persona.
        /// </summary>
        [EnumMember(Value = "Persona")]
        Persona,

        /// <summary>
        /// Global Armada settings.
        /// </summary>
        [EnumMember(Value = "Global")]
        Global,

        /// <summary>
        /// A built-in default.
        /// </summary>
        [EnumMember(Value = "Default")]
        Default,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
