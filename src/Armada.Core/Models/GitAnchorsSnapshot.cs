namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The resolved git anchors captured once at dock provisioning: the commit the working branch starts
    /// from, the branch it targets, the working branch, recent commits on the paths the mission names, and
    /// which subject terms already exist in the tree. Serialized to <see cref="Dock.GitAnchorsJson"/> for the
    /// dashboard and for a resuming captain.
    /// </summary>
    public class GitAnchorsSnapshot
    {
        #region Public-Members

        /// <summary>
        /// The commit the working branch starts from (short or full hash), or null.
        /// </summary>
        public string? StartCommit { get; set; } = null;

        /// <summary>
        /// The branch the change targets (e.g. the default branch), or null.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// The mission's working branch name, or null.
        /// </summary>
        public string? WorkingBranch { get; set; } = null;

        /// <summary>
        /// Recent commit summaries on the paths the mission names ("path: shorthash subject").
        /// </summary>
        public List<string> RecentPathCommits { get; set; } = new List<string>();

        /// <summary>
        /// Subject terms from the mission that already appear in the tree.
        /// </summary>
        public List<string> SubjectTermsPresent { get; set; } = new List<string>();

        #endregion
    }
}
