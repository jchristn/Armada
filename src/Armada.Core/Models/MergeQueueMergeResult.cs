namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Outcome of merging a merge queue entry's branch into its integration worktree. A failed merge carries the files
    /// git reported as conflicted, read before the merge was aborted.
    /// </summary>
    public class MergeQueueMergeResult
    {
        #region Public-Members

        /// <summary>
        /// True when the branch merged (including a CHANGELOG conflict Armada resolved itself).
        /// </summary>
        public bool Merged { get; set; } = false;

        /// <summary>
        /// Files git reported as conflicted; empty when the merge succeeded or failed for another reason.
        /// </summary>
        public List<string> ConflictedFiles { get; set; } = new List<string>();

        /// <summary>
        /// The git failure message when the merge failed, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
