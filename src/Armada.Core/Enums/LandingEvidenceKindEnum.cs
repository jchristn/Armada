namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How the landing reconciler proved that a LandingFailed mission's work reached the target branch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LandingEvidenceKindEnum
    {
        /// <summary>
        /// The mission's own commit is an ancestor of the remote target branch.
        /// </summary>
        CommitAncestry,

        /// <summary>
        /// A commit on the remote target branch carries the mission's <c>Armada-Mission-Id</c> trailer.
        /// </summary>
        MissionTrailer
    }
}
