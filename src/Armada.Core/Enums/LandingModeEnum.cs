namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Determines how completed mission work is landed (integrated).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LandingModeEnum
    {
        /// <summary>
        /// Merge the mission branch into the vessel's default branch in the user's local working directory,
        /// without pushing to the remote. Requires the vessel to have a WorkingDirectory and LocalPath configured.
        /// Use MergeAndPush to also push the merged branch.
        /// </summary>
        [EnumMember(Value = "LocalMerge")]
        LocalMerge,

        /// <summary>
        /// Push the mission branch and create a pull request on the remote.
        /// The mission stays in PullRequestOpen until the PR is merged.
        /// </summary>
        [EnumMember(Value = "PullRequest")]
        PullRequest,

        /// <summary>
        /// Enqueue the mission branch for validated merge through the merge queue.
        /// The merge queue runs tests and merges sequentially per vessel.
        /// </summary>
        [EnumMember(Value = "MergeQueue")]
        MergeQueue,

        /// <summary>
        /// No automatic landing. Work stays as WorkProduced and the branch
        /// remains available in the bare repo for manual integration.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// Merge the mission branch into the vessel's default branch in the user's local working directory, then
        /// push the working directory's branch to its remote. A failed push marks the landing failed and keeps the
        /// mission branch. Requires the vessel to have a WorkingDirectory and LocalPath configured.
        /// </summary>
        [EnumMember(Value = "MergeAndPush")]
        MergeAndPush
    }
}
