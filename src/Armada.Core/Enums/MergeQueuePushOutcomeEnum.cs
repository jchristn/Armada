namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How the merge queue's push of a verified commit to the target branch ended. Decided from git exit statuses, never
    /// from the text git prints.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MergeQueuePushOutcomeEnum
    {
        /// <summary>
        /// The remote accepted the push.
        /// </summary>
        Pushed,

        /// <summary>
        /// The remote refused the push and its target branch now holds a commit the verified commit does not contain:
        /// the target moved after the entry was integrated, so the push was not a fast-forward. The entry is requeued
        /// from a fresh fetch.
        /// </summary>
        NonFastForward,

        /// <summary>
        /// The remote refused the push for another reason (permissions, a protected branch, a hook), or the target
        /// could not be compared afterwards.
        /// </summary>
        Rejected
    }
}
