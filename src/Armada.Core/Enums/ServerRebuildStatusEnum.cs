namespace Armada.Core.Enums
{
    /// <summary>
    /// Lifecycle state for a self-rebuild of the Admiral server.
    /// </summary>
    public enum ServerRebuildStatusEnum
    {
        /// <summary>
        /// The new slot is being published (git worktree add + dotnet publish + dashboard build).
        /// </summary>
        Building,

        /// <summary>
        /// The build succeeded, the current pointer was flipped, and the Admiral is handing over to the new
        /// slot (launching the replacement and stopping this instance).
        /// </summary>
        CuttingOver,

        /// <summary>
        /// The new slot was published and the cutover was initiated successfully.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The build failed; the current pointer was left untouched and no cutover was attempted.
        /// </summary>
        Failed,

        /// <summary>
        /// The new slot failed to come up healthy and the previous slot was restored.
        /// </summary>
        RolledBack
    }
}
