namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One page of commit history read from git (git log --date-order from a fixed tip commit).
    /// </summary>
    public class GitCommitLogPage
    {
        #region Public-Members

        /// <summary>
        /// Commits, newest commit date first. Never null.
        /// </summary>
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
