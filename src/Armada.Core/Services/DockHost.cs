namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// The host a dock lives on, and how to work on it there: git and gh, other commands, and file operations. A dock on
    /// the Admiral's host uses this machine; a Harbor-hosted dock uses the Harbor, over its link.
    /// </summary>
    public class DockHost
    {
        #region Public-Members

        /// <summary>
        /// The Harbor the dock lives on, or null for the Admiral's own host.
        /// </summary>
        public string? HarborId { get; }

        /// <summary>
        /// Whether the dock lives on a Harbor.
        /// </summary>
        public bool IsHarbor
        {
            get { return HarborId != null; }
        }

        /// <summary>
        /// Git and gh operations on that host.
        /// </summary>
        public IGitService Git { get; }

        /// <summary>
        /// Commands (for example a Definition-of-Done build) on that host.
        /// </summary>
        public IHostCommandExecutor Commands { get; }

        /// <summary>
        /// File operations on that host.
        /// </summary>
        public IDockFileSystem Files { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">Harbor identifier, or null for the Admiral's host.</param>
        /// <param name="git">Git service for that host.</param>
        /// <param name="commands">Command executor for that host.</param>
        /// <param name="files">File operations for that host.</param>
        public DockHost(string? harborId, IGitService git, IHostCommandExecutor commands, IDockFileSystem files)
        {
            HarborId = String.IsNullOrWhiteSpace(harborId) ? null : harborId;
            Git = git ?? throw new ArgumentNullException(nameof(git));
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Files = files ?? throw new ArgumentNullException(nameof(files));
        }

        #endregion
    }
}
