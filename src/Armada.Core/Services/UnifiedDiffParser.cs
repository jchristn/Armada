namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Parses git unified diff text into typed file sections. Hunk bodies are consumed by the line counts in
    /// their @@ headers, so content lines that look like headers (for example an added line whose text starts
    /// with "++ ", which renders as "+++ ...") are always treated as content. File paths come from the
    /// structural headers (diff --git, rename/copy from/to, ---/+++ outside hunks) with C-quoting decoded,
    /// so deletions, pure renames, mode-only and binary changes all report their paths.
    /// </summary>
    public static class UnifiedDiffParser
    {
        #region Public-Methods

        /// <summary>
        /// Parse unified diff text.
        /// </summary>
        /// <param name="diffText">Diff text (null or empty yields no files).</param>
        /// <returns>One entry per file section, in diff order.</returns>
        public static List<UnifiedDiffFile> Parse(string? diffText)
        {
            return ParseCore(diffText, null);
        }

        /// <summary>
        /// Parse unified diff text and classify every line (one entry per line of the text split on '\n').
        /// </summary>
        /// <param name="diffText">Diff text (null or empty yields no files and no lines).</param>
        /// <param name="lineKinds">Structural kind of each line, in order.</param>
        /// <returns>One entry per file section, in diff order.</returns>
        public static List<UnifiedDiffFile> Parse(string? diffText, out List<UnifiedDiffLineKindEnum> lineKinds)
        {
            lineKinds = new List<UnifiedDiffLineKindEnum>();
            return ParseCore(diffText, lineKinds);
        }

        #endregion

        #region Private-Methods

        private static List<UnifiedDiffFile> ParseCore(string? diffText, List<UnifiedDiffLineKindEnum>? kinds)
        {
            List<UnifiedDiffFile> files = new List<UnifiedDiffFile>();
            if (String.IsNullOrEmpty(diffText)) return files;

            string[] lines = diffText!.Split('\n');
            UnifiedDiffFile? current = null;
            bool currentHasHunk = false;
            int remainingOld = 0;
            int remainingNew = 0;
            int newLine = 0;

            int index = 0;
            while (index < lines.Length)
            {
                string raw = lines[index];

                if (current != null && (remainingOld > 0 || remainingNew > 0))
                {
                    string body = raw.EndsWith("\r", StringComparison.Ordinal) ? raw.Substring(0, raw.Length - 1) : raw;
                    char marker = body.Length == 0 ? ' ' : body[0];
                    if (marker == '+' && remainingNew > 0)
                    {
                        kinds?.Add(UnifiedDiffLineKindEnum.Added);
                        current.AddedLines.Add(new UnifiedDiffLine { NewLineNumber = newLine, Content = body.Length > 0 ? body.Substring(1) : String.Empty });
                        current.AddedLineCount++;
                        remainingNew--;
                        newLine++;
                        index++;
                        continue;
                    }
                    if (marker == '-' && remainingOld > 0)
                    {
                        kinds?.Add(UnifiedDiffLineKindEnum.Deleted);
                        current.DeletedLineCount++;
                        remainingOld--;
                        index++;
                        continue;
                    }
                    if (marker == ' ' && remainingOld > 0 && remainingNew > 0)
                    {
                        kinds?.Add(UnifiedDiffLineKindEnum.Context);
                        remainingOld--;
                        remainingNew--;
                        newLine++;
                        index++;
                        continue;
                    }
                    if (marker == '\\')
                    {
                        kinds?.Add(UnifiedDiffLineKindEnum.NoNewline);
                        index++;
                        continue;
                    }

                    // Malformed or truncated hunk: stop consuming and treat this line as a header.
                    remainingOld = 0;
                    remainingNew = 0;
                }

                string line = raw.EndsWith("\r", StringComparison.Ordinal) ? raw.Substring(0, raw.Length - 1) : raw;

                if (line.StartsWith("\\", StringComparison.Ordinal))
                {
                    kinds?.Add(UnifiedDiffLineKindEnum.NoNewline);
                    index++;
                    continue;
                }

                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    current = new UnifiedDiffFile();
                    current.StartLineIndex = index;
                    currentHasHunk = false;
                    files.Add(current);
                    ParseDiffGitHeader(line.Substring("diff --git ".Length), current);
                    kinds?.Add(UnifiedDiffLineKindEnum.FileHeader);
                    index++;
                    continue;
                }

                if (line.StartsWith("--- ", StringComparison.Ordinal) &&
                    index + 1 < lines.Length && lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal))
                {
                    // Plain unified diff without a diff --git header, or a second file in one.
                    if (current == null || currentHasHunk)
                    {
                        current = new UnifiedDiffFile();
                        current.StartLineIndex = index;
                        currentHasHunk = false;
                        files.Add(current);
                    }

                    string? oldPath = ParseHeaderPath(TrimCr(line.Substring(4)), "a/");
                    string? newPath = ParseHeaderPath(TrimCr(lines[index + 1].Substring(4)), "b/");
                    current.OldPath = oldPath;
                    current.NewPath = newPath;
                    if (oldPath == null && newPath != null) current.Kind = GitChangeKindEnum.Added;
                    else if (newPath == null && oldPath != null) current.Kind = GitChangeKindEnum.Deleted;
                    kinds?.Add(UnifiedDiffLineKindEnum.Meta);
                    kinds?.Add(UnifiedDiffLineKindEnum.Meta);
                    index += 2;
                    continue;
                }

                if (current == null)
                {
                    kinds?.Add(UnifiedDiffLineKindEnum.Other);
                    index++;
                    continue;
                }

                if (line.StartsWith("@@ ", StringComparison.Ordinal))
                {
                    if (TryParseHunkHeader(line, out int oldCount, out int newStart, out int newCount))
                    {
                        remainingOld = oldCount;
                        remainingNew = newCount;
                        newLine = newStart;
                        currentHasHunk = true;
                    }
                    kinds?.Add(UnifiedDiffLineKindEnum.HunkHeader);
                    index++;
                    continue;
                }

                if (!currentHasHunk)
                {
                    ApplyExtendedHeader(line, current);
                    kinds?.Add(UnifiedDiffLineKindEnum.Meta);
                }
                else
                {
                    kinds?.Add(UnifiedDiffLineKindEnum.Other);
                }

                index++;
            }

            return files;
        }

        private static string TrimCr(string value)
        {
            return value.EndsWith("\r", StringComparison.Ordinal) ? value.Substring(0, value.Length - 1) : value;
        }

        private static void ApplyExtendedHeader(string line, UnifiedDiffFile file)
        {
            if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                file.Kind = GitChangeKindEnum.Added;
                file.OldPath = null;
            }
            else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                file.Kind = GitChangeKindEnum.Deleted;
                file.NewPath = null;
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                file.Kind = GitChangeKindEnum.Renamed;
                file.OldPath = NormalizePath(GitPathUnquoter.Unquote(line.Substring("rename from ".Length)));
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                file.Kind = GitChangeKindEnum.Renamed;
                file.NewPath = NormalizePath(GitPathUnquoter.Unquote(line.Substring("rename to ".Length)));
            }
            else if (line.StartsWith("copy from ", StringComparison.Ordinal))
            {
                file.Kind = GitChangeKindEnum.Copied;
                file.OldPath = NormalizePath(GitPathUnquoter.Unquote(line.Substring("copy from ".Length)));
            }
            else if (line.StartsWith("copy to ", StringComparison.Ordinal))
            {
                file.Kind = GitChangeKindEnum.Copied;
                file.NewPath = NormalizePath(GitPathUnquoter.Unquote(line.Substring("copy to ".Length)));
            }
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal) || String.Equals(line, "GIT binary patch", StringComparison.Ordinal))
            {
                file.IsBinary = true;
            }
        }

        private static void ParseDiffGitHeader(string rest, UnifiedDiffFile file)
        {
            // Quoted form: "a/x y" "b/x y" (either side may be quoted independently).
            string? first = null;
            string? second = null;
            int index = 0;
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                first = GitPathUnquoter.TryReadQuoted(rest, ref index);
                if (first != null && index < rest.Length && rest[index] == ' ')
                {
                    string remainder = rest.Substring(index + 1);
                    second = GitPathUnquoter.Unquote(remainder);
                }
            }
            else if (rest.EndsWith("\"", StringComparison.Ordinal))
            {
                int openQuote = rest.LastIndexOf(" \"", StringComparison.Ordinal);
                if (openQuote > 0)
                {
                    first = rest.Substring(0, openQuote);
                    second = GitPathUnquoter.Unquote(rest.Substring(openQuote + 1));
                }
            }
            else
            {
                // Unquoted: when both sides name the same path ("a/P b/P") the split point is exact.
                if (rest.Length >= 5 && (rest.Length - 1) % 2 == 0)
                {
                    int half = (rest.Length - 1) / 2;
                    string left = rest.Substring(0, half);
                    string right = rest.Substring(half + 1);
                    if (rest[half] == ' ' && left.Length > 2 && right.Length > 2 &&
                        String.Equals(left.Substring(2), right.Substring(2), StringComparison.Ordinal))
                    {
                        first = left;
                        second = right;
                    }
                }

                if (first == null)
                {
                    int split = rest.IndexOf(" b/", StringComparison.Ordinal);
                    if (split > 0)
                    {
                        first = rest.Substring(0, split);
                        second = rest.Substring(split + 1);
                    }
                }
            }

            if (first != null) file.OldPath = StripPrefix(first, "a/");
            if (second != null) file.NewPath = StripPrefix(second, "b/");
        }

        private static string? ParseHeaderPath(string token, string prefix)
        {
            string value = token;
            // git appends a tab before a timestamp only for paths containing spaces in some tools; drop it.
            int tab = value.IndexOf('\t');
            if (tab >= 0 && !value.StartsWith("\"", StringComparison.Ordinal)) value = value.Substring(0, tab);
            value = GitPathUnquoter.Unquote(value);
            if (String.Equals(value, "/dev/null", StringComparison.Ordinal)) return null;
            return StripPrefix(value, prefix);
        }

        private static string StripPrefix(string value, string prefix)
        {
            string path = value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
            return NormalizePath(path);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static bool TryParseHunkHeader(string line, out int oldCount, out int newStart, out int newCount)
        {
            oldCount = 0;
            newStart = 0;
            newCount = 0;

            // "@@ -oldStart[,oldCount] +newStart[,newCount] @@ optional section"
            int end = line.IndexOf(" @@", 3, StringComparison.Ordinal);
            if (end < 0) return false;
            string[] ranges = line.Substring(3, end - 3).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ranges.Length != 2 || !ranges[0].StartsWith("-", StringComparison.Ordinal) || !ranges[1].StartsWith("+", StringComparison.Ordinal))
                return false;

            if (!TryParseRange(ranges[0].Substring(1), out int _, out oldCount)) return false;
            if (!TryParseRange(ranges[1].Substring(1), out newStart, out newCount)) return false;
            return true;
        }

        private static bool TryParseRange(string range, out int start, out int count)
        {
            start = 0;
            count = 1;
            int comma = range.IndexOf(',');
            string startText = comma >= 0 ? range.Substring(0, comma) : range;
            if (!Int32.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out start)) return false;
            if (comma >= 0 && !Int32.TryParse(range.Substring(comma + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count)) return false;
            return true;
        }

        #endregion
    }
}
