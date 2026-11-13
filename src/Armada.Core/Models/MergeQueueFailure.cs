namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Structured facts about a failed merge queue entry: which step failed, the failure kind recorded on the linked
    /// mission, and the conflicted files when the merge conflicted. Stored as the Payload JSON of the
    /// <c>mission.landing_failed</c> event the merge queue records, so a consumer reads the facts from fields instead of
    /// parsing the mission's failure reason.
    /// </summary>
    public class MergeQueueFailure
    {
        #region Public-Members

        /// <summary>
        /// The merge entry that failed.
        /// </summary>
        public string? MergeEntryId { get; set; } = null;

        /// <summary>
        /// The linked mission, when there is one.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// The entry's branch.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// The target branch the entry was being landed on.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// The target commit the integration worktree was created from, when known.
        /// </summary>
        public string? TargetCommit { get; set; } = null;

        /// <summary>
        /// The step that failed.
        /// </summary>
        public MergeQueueFailureStepEnum Step { get; set; } = MergeQueueFailureStepEnum.Preparation;

        /// <summary>
        /// The failure kind recorded on the linked mission.
        /// </summary>
        public MissionFailureKindEnum Kind { get; set; } = MissionFailureKindEnum.Infra;

        /// <summary>
        /// The failure reason recorded on the linked mission.
        /// </summary>
        public string Reason { get; set; } = "";

        /// <summary>
        /// Files git reported as conflicted (merge step only).
        /// </summary>
        public List<string> ConflictedFiles { get; set; } = new List<string>();

        #endregion
    }
}
