namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Parsers for git's machine-readable output formats (NUL-terminated -z output and porcelain records).
    /// Pure and side-effect free.
    /// </summary>
    public static class GitMachineOutputParser
    {
        #region Public-Methods

        /// <summary>
        /// Split NUL-terminated output into its fields. A trailing terminator does not produce an empty field.
        /// </summary>
        /// <param name="output">Raw -z output.</param>
        /// <returns>The fields in order.</returns>
        public static List<string> SplitNul(string? output)
        {
            List<string> fields = new List<string>();
            if (String.IsNullOrEmpty(output)) return fields;
            string[] parts = output!.Split('\0');
            int count = parts.Length;
            if (count > 0 && parts[count - 1].Length == 0) count--;
            for (int i = 0; i < count; i++) fields.Add(parts[i]);
            return fields;
        }

        /// <summary>
        /// Split NUL-terminated path output (ls-files -z, diff --name-only -z) into non-empty paths.
        /// </summary>
        /// <param name="output">Raw -z output.</param>
        /// <returns>Paths with forward slashes.</returns>
        public static List<string> ParsePathListZ(string? output)
        {
            List<string> paths = new List<string>();
            foreach (string field in SplitNul(output))
            {
                if (field.Length == 0) continue;
                paths.Add(field.Replace('\\', '/'));
            }
            return paths;
        }

        /// <summary>
        /// Parse git diff --name-status -z output.
        /// </summary>
        /// <param name="output">Raw output.</param>
        /// <returns>One entry per changed path (renames and copies carry both paths).</returns>
        public static List<GitChangedFile> ParseNameStatusZ(string? output)
        {
            List<GitChangedFile> files = new List<GitChangedFile>();
            List<string> fields = SplitNul(output);
            int i = 0;
            while (i < fields.Count)
            {
                string status = fields[i++];
                if (status.Length == 0) continue;
                GitChangeKindEnum kind = KindFromStatusLetter(status[0]);
                if (kind == GitChangeKindEnum.Renamed || kind == GitChangeKindEnum.Copied)
                {
                    if (i + 1 >= fields.Count) break;
                    string oldPath = fields[i++];
                    string newPath = fields[i++];
                    files.Add(new GitChangedFile { Kind = kind, OldPath = Normalize(oldPath), Path = Normalize(newPath) });
                }
                else
                {
                    if (i >= fields.Count) break;
                    files.Add(new GitChangedFile { Kind = kind, Path = Normalize(fields[i++]) });
                }
            }
            return files;
        }

        /// <summary>
        /// Parse git diff --numstat -z output. Binary files report null counts and IsBinary = true.
        /// </summary>
        /// <param name="output">Raw output.</param>
        /// <returns>One entry per changed path with line counts.</returns>
        public static List<GitChangedFile> ParseNumstatZ(string? output)
        {
            List<GitChangedFile> files = new List<GitChangedFile>();
            List<string> fields = SplitNul(output);
            int i = 0;
            while (i < fields.Count)
            {
                string record = fields[i++];
                if (record.Length == 0) continue;

                int firstTab = record.IndexOf('\t');
                int secondTab = firstTab < 0 ? -1 : record.IndexOf('\t', firstTab + 1);
                if (firstTab < 0 || secondTab < 0) continue;

                string addedText = record.Substring(0, firstTab);
                string deletedText = record.Substring(firstTab + 1, secondTab - firstTab - 1);
                string pathText = record.Substring(secondTab + 1);

                GitChangedFile file = new GitChangedFile();
                if (pathText.Length == 0)
                {
                    // Rename or copy: the two paths follow as separate fields.
                    if (i + 1 >= fields.Count) break;
                    file.Kind = GitChangeKindEnum.Renamed;
                    file.OldPath = Normalize(fields[i++]);
                    file.Path = Normalize(fields[i++]);
                }
                else
                {
                    file.Path = Normalize(pathText);
                }

                bool addedParsed = Int32.TryParse(addedText, NumberStyles.None, CultureInfo.InvariantCulture, out int added);
                bool deletedParsed = Int32.TryParse(deletedText, NumberStyles.None, CultureInfo.InvariantCulture, out int deleted);
                if (addedParsed && deletedParsed)
                {
                    file.AddedLines = added;
                    file.DeletedLines = deleted;
                }
                else
                {
                    file.IsBinary = true;
                }

                files.Add(file);
            }
            return files;
        }

        /// <summary>
        /// Combine --name-status and --numstat results for the same range: kinds from name-status,
        /// line counts and the binary flag from numstat, matched by path.
        /// </summary>
        /// <param name="nameStatus">Parsed name-status entries.</param>
        /// <param name="numstat">Parsed numstat entries.</param>
        /// <returns>Merged entries; a path present in only one input is still reported.</returns>
        public static List<GitChangedFile> MergeNameStatusAndNumstat(List<GitChangedFile> nameStatus, List<GitChangedFile> numstat)
        {
            if (nameStatus == null) throw new ArgumentNullException(nameof(nameStatus));
            if (numstat == null) throw new ArgumentNullException(nameof(numstat));

            Dictionary<string, GitChangedFile> stats = new Dictionary<string, GitChangedFile>(StringComparer.Ordinal);
            foreach (GitChangedFile stat in numstat)
            {
                if (!stats.ContainsKey(stat.Path)) stats[stat.Path] = stat;
            }

            List<GitChangedFile> merged = new List<GitChangedFile>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GitChangedFile entry in nameStatus)
            {
                if (stats.TryGetValue(entry.Path, out GitChangedFile? stat))
                {
                    entry.AddedLines = stat.AddedLines;
                    entry.DeletedLines = stat.DeletedLines;
                    entry.IsBinary = stat.IsBinary;
                }
                seen.Add(entry.Path);
                merged.Add(entry);
            }

            foreach (GitChangedFile stat in numstat)
            {
                if (seen.Add(stat.Path)) merged.Add(stat);
            }

            return merged;
        }

        /// <summary>
        /// Parse git worktree list --porcelain output.
        /// </summary>
        /// <param name="output">Raw output.</param>
        /// <param name="nulTerminated">True when the output came from --porcelain -z (NUL-terminated fields).</param>
        /// <returns>One entry per worktree.</returns>
        public static List<GitWorktreeEntry> ParseWorktreeList(string? output, bool nulTerminated)
        {
            List<GitWorktreeEntry> entries = new List<GitWorktreeEntry>();
            if (String.IsNullOrEmpty(output)) return entries;

            string[] fields = nulTerminated
                ? output!.Split('\0')
                : output!.Replace("\r\n", "\n").Split('\n');

            GitWorktreeEntry? current = null;
            foreach (string field in fields)
            {
                if (field.Length == 0)
                {
                    if (current != null) entries.Add(current);
                    current = null;
                    continue;
                }

                if (field.StartsWith("worktree ", StringComparison.Ordinal))
                {
                    if (current != null) entries.Add(current);
                    current = new GitWorktreeEntry { Path = field.Substring("worktree ".Length) };
                    continue;
                }

                if (current == null) continue;
                if (field.StartsWith("HEAD ", StringComparison.Ordinal)) current.Head = field.Substring("HEAD ".Length);
                else if (field.StartsWith("branch ", StringComparison.Ordinal)) current.BranchRef = field.Substring("branch ".Length);
                else if (String.Equals(field, "bare", StringComparison.Ordinal)) current.IsBare = true;
                else if (String.Equals(field, "detached", StringComparison.Ordinal)) current.IsDetached = true;
            }

            if (current != null) entries.Add(current);
            return entries;
        }

        /// <summary>
        /// Map a git status letter (A, M, D, R, C, T, U) to a change kind.
        /// </summary>
        /// <param name="letter">Status letter.</param>
        /// <returns>The change kind.</returns>
        public static GitChangeKindEnum KindFromStatusLetter(char letter)
        {
            switch (letter)
            {
                case 'A': return GitChangeKindEnum.Added;
                case 'M': return GitChangeKindEnum.Modified;
                case 'D': return GitChangeKindEnum.Deleted;
                case 'R': return GitChangeKindEnum.Renamed;
                case 'C': return GitChangeKindEnum.Copied;
                case 'T': return GitChangeKindEnum.TypeChanged;
                case 'U': return GitChangeKindEnum.Unmerged;
                default: return GitChangeKindEnum.Unknown;
            }
        }

        #endregion

        #region Private-Methods

        private static string Normalize(string path)
        {
            return (path ?? String.Empty).Replace('\\', '/');
        }

        #endregion
    }
}
