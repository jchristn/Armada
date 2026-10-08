namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a <see cref="HarborDockRequest"/> asks the Harbor to do with a mission dock on its host.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborDockOperationEnum
    {
        /// <summary>
        /// Report where the vessel's repository is on the Harbor host (a mapped or discovered checkout, or the Harbor's
        /// own clone), without creating anything. Used by routing to choose a Harbor that can serve the vessel.
        /// </summary>
        [EnumMember(Value = "Resolve")]
        Resolve,

        /// <summary>
        /// Create the dock: find or clone the repository, fetch, create the mission branch, and add a git worktree for
        /// it under the Harbor's docks directory.
        /// </summary>
        [EnumMember(Value = "Provision")]
        Provision,

        /// <summary>
        /// Remove the dock's worktree (git worktree remove, then prune) and its directory.
        /// </summary>
        [EnumMember(Value = "Reclaim")]
        Reclaim
    }
}
