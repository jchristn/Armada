namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a file is recorded on a black box entry (blackbox_files).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxFileRoleEnum
    {
        /// <summary>
        /// Git reported the file as conflicted.
        /// </summary>
        [EnumMember(Value = "Conflicted")]
        Conflicted,

        /// <summary>
        /// The file had uncommitted tracked changes in the target checkout.
        /// </summary>
        [EnumMember(Value = "DirtyInCheckout")]
        DirtyInCheckout,

        /// <summary>
        /// The mission changed the file outside its scoped file list.
        /// </summary>
        [EnumMember(Value = "OutOfScope")]
        OutOfScope,

        /// <summary>
        /// The failed mission changed the file.
        /// </summary>
        [EnumMember(Value = "ChangedByMission")]
        ChangedByMission,

        /// <summary>
        /// A conflicting mission's landed commit also changed the file.
        /// </summary>
        [EnumMember(Value = "ChangedByConflictingMission")]
        ChangedByConflictingMission,

        /// <summary>
        /// The file matched a protected path pattern.
        /// </summary>
        [EnumMember(Value = "BoundaryViolation")]
        BoundaryViolation,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
