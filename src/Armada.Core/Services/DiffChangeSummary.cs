namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Size and path facts for a mission's change, used by the dock-boundary scanner and the auto-land
    /// predicate. Built from a parsed unified diff and/or git's --name-status / --numstat output. Paths include
    /// both sides of a rename and deleted paths, so path rules see every path the change touches.
    /// </summary>
    public class DiffChangeSummary
    {
        #region Public-Members

        /// <summary>
        /// Every distinct repository-relative path the change touches (forward slashes).
        /// </summary>
        public List<string> ChangedPaths { get; set; } = new List<string>();

        /// <summary>
        /// Number of changed files (a rename counts once).
        /// </summary>
        public int FileCount { get; set; } = 0;

        /// <summary>
        /// Added plus deleted lines.
        /// </summary>
        public int ChangedLineCount { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a summary from unified diff text.
        /// </summary>
        /// <param name="diffText">Diff text; null or empty yields an empty summary.</param>
        /// <returns>The summary.</returns>
        public static DiffChangeSummary FromUnifiedDiff(string? diffText)
        {
            return FromUnifiedDiff(UnifiedDiffParser.Parse(diffText));
        }

        /// <summary>
        /// Build a summary from parsed unified diff sections.
        /// </summary>
        /// <param name="files">Parsed sections.</param>
        /// <returns>The summary.</returns>
        public static DiffChangeSummary FromUnifiedDiff(IEnumerable<UnifiedDiffFile> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            DiffChangeSummary summary = new DiffChangeSummary();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnifiedDiffFile file in files)
            {
                List<string> paths = file.Paths;
                if (paths.Count == 0) continue;
                summary.FileCount++;
                summary.ChangedLineCount += file.AddedLineCount + file.DeletedLineCount;
                foreach (string path in paths) AddPath(summary, seen, path);
            }
            return summary;
        }

        /// <summary>
        /// Build a summary from git --name-status / --numstat entries.
        /// </summary>
        /// <param name="files">Changed files.</param>
        /// <returns>The summary.</returns>
        public static DiffChangeSummary FromGitChanges(IEnumerable<GitChangedFile> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            DiffChangeSummary summary = new DiffChangeSummary();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GitChangedFile file in files)
            {
                if (String.IsNullOrEmpty(file.Path) && String.IsNullOrEmpty(file.OldPath)) continue;
                summary.FileCount++;
                summary.ChangedLineCount += (file.AddedLines ?? 0) + (file.DeletedLines ?? 0);
                if (!String.IsNullOrEmpty(file.OldPath)) AddPath(summary, seen, file.OldPath!);
                if (!String.IsNullOrEmpty(file.Path)) AddPath(summary, seen, file.Path);
            }
            return summary;
        }

        /// <summary>
        /// Combine two summaries of the same change conservatively: the union of paths and the larger
        /// file and line counts.
        /// </summary>
        /// <param name="first">First summary.</param>
        /// <param name="second">Second summary (may be null).</param>
        /// <returns>The combined summary.</returns>
        public static DiffChangeSummary Combine(DiffChangeSummary first, DiffChangeSummary? second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) return first;

            DiffChangeSummary combined = new DiffChangeSummary();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in first.ChangedPaths) AddPath(combined, seen, path);
            foreach (string path in second.ChangedPaths) AddPath(combined, seen, path);
            combined.FileCount = Math.Max(first.FileCount, second.FileCount);
            combined.ChangedLineCount = Math.Max(first.ChangedLineCount, second.ChangedLineCount);
            return combined;
        }

        #endregion

        #region Private-Methods

        private static void AddPath(DiffChangeSummary summary, HashSet<string> seen, string path)
        {
            string normalized = path.Replace('\\', '/').TrimStart('/');
            if (normalized.Length == 0) return;
            if (seen.Add(normalized)) summary.ChangedPaths.Add(normalized);
        }

        #endregion
    }
}
