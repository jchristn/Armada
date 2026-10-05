namespace Armada.Tui.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// Remembered Workspace state for one vessel: the expanded folders and the recently opened files (the dashboard's
    /// <c>armada_workspace_state_*</c> local storage entry).
    /// </summary>
    public class WorkspacePreferences
    {
        #region Public-Members

        /// <summary>
        /// Expanded folder paths (relative, forward slashes). Never null.
        /// </summary>
        public List<string> ExpandedPaths { get; set; } = new List<string>();

        /// <summary>
        /// Recently opened files, newest first (at most twelve). Never null.
        /// </summary>
        public List<string> RecentFiles { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkspacePreferences()
        {
        }

        #endregion
    }
}
