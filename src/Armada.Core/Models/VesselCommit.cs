namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One commit in a vessel's history with what it changed.
    /// </summary>
    public class VesselCommit
    {
        #region Public-Members

        /// <summary>
        /// Full commit SHA.
        /// </summary>
        public string Sha { get; set; } = String.Empty;

        /// <summary>
        /// Abbreviated SHA (git's short form).
        /// </summary>
        public string ShortSha { get; set; } = String.Empty;

        /// <summary>
        /// First line of the commit message.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Rest of the commit message after the subject (trimmed), or empty.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Author name.
        /// </summary>
        public string AuthorName { get; set; } = String.Empty;

        /// <summary>
        /// Author email.
        /// </summary>
        public string AuthorEmail { get; set; } = String.Empty;

        /// <summary>
        /// Author date, UTC.
        /// </summary>
        public DateTime AuthoredUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Committer name.
        /// </summary>
        public string CommitterName { get; set; } = String.Empty;

        /// <summary>
        /// Committer email.
        /// </summary>
        public string CommitterEmail { get; set; } = String.Empty;

        /// <summary>
        /// Commit date, UTC. History is ordered and filtered (before, heatmap days) by this date.
        /// </summary>
        public DateTime CommittedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Parent SHAs (two or more for a merge). Never null.
        /// </summary>
        public List<string> ParentShas { get; set; } = new List<string>();

        /// <summary>
        /// True when the commit has more than one parent. File stats for a merge are against its first parent.
        /// </summary>
        public bool IsMerge { get; set; } = false;

        /// <summary>
        /// Number of files changed (all of them, even when Files is truncated).
        /// </summary>
        public int FilesChanged { get; set; } = 0;

        /// <summary>
        /// Total added lines (binary files count as zero).
        /// </summary>
        public int AddedLines { get; set; } = 0;

        /// <summary>
        /// Total deleted lines (binary files count as zero).
        /// </summary>
        public int DeletedLines { get; set; } = 0;

        /// <summary>
        /// Changed files, at most 200. Never null.
        /// </summary>
        public List<GitChangedFile> Files { get; set; } = new List<GitChangedFile>();

        /// <summary>
        /// True when the commit changed more files than Files lists.
        /// </summary>
        public bool FilesTruncated { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommit()
        {
        }

        #endregion
    }
}
