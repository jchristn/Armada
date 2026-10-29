namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The merge queue step that failed for an entry, recorded with the failure so the mission's failure kind and any
    /// later evidence capture describe what actually failed instead of assuming a merge conflict.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MergeQueueFailureStepEnum
    {
        /// <summary>
        /// The entry could not be processed at all: its repository or host could not be resolved.
        /// </summary>
        Resolution,

        /// <summary>
        /// Preparing the integration worktree (fetching the target, creating the worktree) failed.
        /// </summary>
        Preparation,

        /// <summary>
        /// Merging the entry's branch into the current target failed.
        /// </summary>
        Merge,

        /// <summary>
        /// The landing checks run against the integrated commit failed, timed out or could not run.
        /// </summary>
        Verification,

        /// <summary>
        /// Publishing the verified commit (ancestry validation or the push to the target branch) failed.
        /// </summary>
        Publication
    }
}
