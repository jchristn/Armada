namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How another mission relates to a black box entry (blackbox_links).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxLinkRelationEnum
    {
        /// <summary>
        /// The mission landed a commit on the target after this mission started.
        /// </summary>
        [EnumMember(Value = "LandedOnTargetSinceStart")]
        LandedOnTargetSinceStart,

        /// <summary>
        /// The mission touched overlapping files while this mission ran.
        /// </summary>
        [EnumMember(Value = "OverlappingFiles")]
        OverlappingFiles,

        /// <summary>
        /// The mission used the same dock.
        /// </summary>
        [EnumMember(Value = "SharedDock")]
        SharedDock,

        /// <summary>
        /// The mission is in the same voyage.
        /// </summary>
        [EnumMember(Value = "SiblingInVoyage")]
        SiblingInVoyage,

        /// <summary>
        /// This mission depended on the linked mission.
        /// </summary>
        [EnumMember(Value = "DependencyOf")]
        DependencyOf,

        /// <summary>
        /// The linked mission is the rescue of this mission.
        /// </summary>
        [EnumMember(Value = "ParentOfRescue")]
        ParentOfRescue,

        /// <summary>
        /// This mission is a rescue of the linked mission.
        /// </summary>
        [EnumMember(Value = "RescueOf")]
        RescueOf,

        /// <summary>
        /// The linked mission's failure is the root of a cascade that reached this entry.
        /// </summary>
        [EnumMember(Value = "CascadeRoot")]
        CascadeRoot,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
