namespace Armada.Tui.Screens.Build
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What a Workspace refresh loads in one round: the status, the root and restored folder listings, and the folders
    /// that ended up expanded.
    /// </summary>
    public class WorkspaceLoad
    {
        #region Public-Members

        /// <summary>
        /// Workspace status.
        /// </summary>
        public WorkspaceStatusResult? Status { get; set; } = null;

        /// <summary>
        /// Folder listings by folder path ("" is the root).
        /// </summary>
        public Dictionary<string, List<WorkspaceTreeEntry>> Entries { get; set; } = new Dictionary<string, List<WorkspaceTreeEntry>>();

        /// <summary>
        /// Expanded folders.
        /// </summary>
        public List<string> Expanded { get; set; } = new List<string>();

        #endregion
    }
}
