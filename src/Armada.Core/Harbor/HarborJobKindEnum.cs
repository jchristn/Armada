namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a captain launched on a Harbor is doing, so the Harbor can show and log its jobs in plain terms. Sent on
    /// the wire as a string (<see cref="HarborLaunchRequest.JobKind"/>), so a Harbor that does not know a newer value
    /// still accepts the launch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborJobKindEnum
    {
        /// <summary>
        /// Not stated (an Admiral that predates the field).
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// A mission.
        /// </summary>
        [EnumMember(Value = "Mission")]
        Mission,

        /// <summary>
        /// An Ask Armada turn.
        /// </summary>
        [EnumMember(Value = "AskTurn")]
        AskTurn,

        /// <summary>
        /// A planning session.
        /// </summary>
        [EnumMember(Value = "Planning")]
        Planning,

        /// <summary>
        /// An objective refinement session.
        /// </summary>
        [EnumMember(Value = "Refinement")]
        Refinement,

        /// <summary>
        /// A vessel's model context build.
        /// </summary>
        [EnumMember(Value = "ContextBuild")]
        ContextBuild,

        /// <summary>
        /// Any other captain launch.
        /// </summary>
        [EnumMember(Value = "Other")]
        Other
    }
}
