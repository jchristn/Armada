namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// One file section of a git unified diff, parsed from its headers and hunk ranges.
    /// </summary>
    public class UnifiedDiffFile
    {
        #region Public-Members

        /// <summary>
        /// Kind of change.
        /// </summary>
        public GitChangeKindEnum Kind { get; set; } = GitChangeKindEnum.Modified;

        /// <summary>
        /// Path before the change (forward slashes, no a/ prefix); null for an added file.
        /// </summary>
        public string? OldPath { get; set; } = null;

        /// <summary>
        /// Path after the change (forward slashes, no b/ prefix); null for a deleted file.
        /// </summary>
        public string? NewPath { get; set; } = null;

        /// <summary>
        /// True when git reported a binary difference (no line hunks).
        /// </summary>
        public bool IsBinary { get; set; } = false;

        /// <summary>
        /// Lines added inside hunks.
        /// </summary>
        public List<UnifiedDiffLine> AddedLines { get; set; } = new List<UnifiedDiffLine>();

        /// <summary>
        /// Count of lines added inside hunks.
        /// </summary>
        public int AddedLineCount { get; set; } = 0;

        /// <summary>
        /// Count of lines removed inside hunks.
        /// </summary>
        public int DeletedLineCount { get; set; } = 0;

        /// <summary>
        /// Every distinct path this section touches: the old path and the new path when present.
        /// </summary>
        public List<string> Paths
        {
            get
            {
                List<string> paths = new List<string>();
                if (!String.IsNullOrEmpty(OldPath)) paths.Add(OldPath!);
                if (!String.IsNullOrEmpty(NewPath) && !String.Equals(NewPath, OldPath, StringComparison.Ordinal)) paths.Add(NewPath!);
                return paths;
            }
        }

        /// <summary>
        /// Path used for reporting: the new path, or the old path for a deletion.
        /// </summary>
        public string DisplayPath => !String.IsNullOrEmpty(NewPath) ? NewPath! : (OldPath ?? String.Empty);

        #endregion
    }
}
