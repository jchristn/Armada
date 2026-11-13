namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// One page of commit history read from git (git log --date-order from a fixed tip commit).
    /// </summary>
    public class GitCommitLogPage
    {
        #region Public-Members

        /// <summary>
        /// Commits, newest commit date first. Never null.
        /// </summary>
        [CollectionBound(Constants.VesselHistoryMaxPageSize, Reason = "GitService.GetCommitLogAsync rejects a larger limit")]
        public List<VesselCommit> Commits { get; set; } = new List<VesselCommit>();

        /// <summary>
        /// True when older commits follow this page.
        /// </summary>
        public bool HasMore { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitCommitLogPage()
        {
        }

        #endregion
    }
}
