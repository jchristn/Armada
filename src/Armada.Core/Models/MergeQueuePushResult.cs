namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Result of the merge queue's push of a verified commit to the target branch.
    /// </summary>
    public class MergeQueuePushResult
    {
        /// <summary>
        /// How the push ended.
        /// </summary>
        public MergeQueuePushOutcomeEnum Outcome { get; set; } = MergeQueuePushOutcomeEnum.Pushed;

        /// <summary>
        /// What git reported on standard error when the push was refused, for the operator; never used to classify the
        /// outcome. Null when the push succeeded.
        /// </summary>
        public string? Message { get; set; } = null;
    }
}
