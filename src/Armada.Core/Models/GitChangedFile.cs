namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One changed path between two commits, read from git's machine formats
    /// (diff --name-status -z and diff --numstat -z).
    /// </summary>
    public class GitChangedFile
    {
        #region Public-Members

        /// <summary>
        /// Kind of change.
        /// </summary>
        public GitChangeKindEnum Kind { get; set; } = GitChangeKindEnum.Modified;

        /// <summary>
        /// Repository-relative path (forward slashes). For a rename or copy, the new path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Repository-relative source path for a rename or copy; null otherwise.
        /// </summary>
        public string? OldPath { get; set; } = null;

        /// <summary>
        /// Added line count from numstat; null for binary files or when not measured.
        /// </summary>
        public int? AddedLines { get; set; } = null;

        /// <summary>
        /// Deleted line count from numstat; null for binary files or when not measured.
        /// </summary>
        public int? DeletedLines { get; set; } = null;

        /// <summary>
        /// True when numstat reported the file as binary.
        /// </summary>
        public bool IsBinary { get; set; } = false;

        #endregion
    }
}
