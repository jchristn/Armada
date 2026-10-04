namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One subdirectory returned by the import browse endpoint.
    /// </summary>
    public class VesselBrowseEntry
    {
        #region Public-Members

        /// <summary>
        /// Directory name. Never null.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? String.Empty;
        }

        /// <summary>
        /// Full path. Never null.
        /// </summary>
        public string Path
        {
            get => _Path;
            set => _Path = value ?? String.Empty;
        }

        /// <summary>
        /// True when the directory contains a .git directory.
        /// </summary>
        public bool IsGitRepository { get; set; } = false;

        /// <summary>
        /// True when the directory contains a .git file (a worktree or submodule).
        /// </summary>
        public bool IsWorktree { get; set; } = false;

        /// <summary>
        /// True when the directory has at least one browsable subdirectory (excluded and dot-prefixed names do not count).
        /// </summary>
        public bool HasSubdirectories { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private string _Path = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselBrowseEntry()
        {
        }

        #endregion
    }
}
