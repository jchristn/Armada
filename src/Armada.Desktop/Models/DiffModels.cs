namespace Armada.Desktop.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Type of diff line for color coding.
    /// </summary>
    public enum DiffLineTypeEnum
    {
        /// <summary>Context (unchanged) line.</summary>
        Context,

        /// <summary>Added line (+).</summary>
        Addition,

        /// <summary>Removed line (-).</summary>
        Deletion,

        /// <summary>Hunk header (@@).</summary>
        Hunk
    }

    /// <summary>
    /// A single line in a diff hunk.
    /// </summary>
    public class DiffLine
    {
        /// <summary>Line type for coloring.</summary>
        public DiffLineTypeEnum LineType { get; set; } = DiffLineTypeEnum.Context;

        /// <summary>Old (left) line number, null for additions.</summary>
        public int? OldLineNumber { get; set; }

        /// <summary>New (right) line number, null for deletions.</summary>
        public int? NewLineNumber { get; set; }

        /// <summary>The prefix character (+, -, or space).</summary>
        public string Prefix { get; set; } = " ";

        /// <summary>Line content without the prefix.</summary>
        public string Content { get; set; } = "";

        /// <summary>Full raw line text.</summary>
        public string RawText { get; set; } = "";
    }

    /// <summary>
    /// A hunk within a diff file.
    /// </summary>
    public class DiffHunk
    {
        /// <summary>The @@ header line.</summary>
        public string Header { get; set; } = "";

        /// <summary>Lines within this hunk.</summary>
        public List<DiffLine> Lines { get; set; } = new List<DiffLine>();
    }

    /// <summary>
    /// A single file in a diff.
    /// </summary>
    public class DiffFile
    {
        /// <summary>File path/name.</summary>
        public string FileName { get; set; } = "";

        /// <summary>Number of added lines.</summary>
        public int Additions { get; set; }

        /// <summary>Number of deleted lines.</summary>
        public int Deletions { get; set; }

        /// <summary>Hunks in this file.</summary>
        public List<DiffHunk> Hunks { get; set; } = new List<DiffHunk>();

        /// <summary>Display-friendly additions count.</summary>
        public string AdditionsText => "+" + Additions;

        /// <summary>Display-friendly deletions count.</summary>
        public string DeletionsText => "-" + Deletions;

        /// <summary>Short filename (last path component).</summary>
        public string ShortFileName
        {
            get
            {
                int lastSlash = FileName.LastIndexOf('/');
                return lastSlash >= 0 ? FileName.Substring(lastSlash + 1) : FileName;
            }
        }

        /// <summary>Directory path (everything before last component).</summary>
        public string DirectoryPath
        {
            get
            {
                int lastSlash = FileName.LastIndexOf('/');
                return lastSlash >= 0 ? FileName.Substring(0, lastSlash + 1) : "";
            }
        }
    }

    /// <summary>
    /// Parses unified diff format into structured models.
    /// </summary>
    public class UnifiedDiffParser
    {
        private static readonly Regex _HunkHeaderRegex = new Regex(
            @"^@@\s+-(\d+)(?:,\d+)?\s+\+(\d+)(?:,\d+)?\s+@@",
            RegexOptions.Compiled);

        /// <summary>
        /// Parse a raw unified diff string into a list of DiffFile objects.
        /// </summary>
        /// <param name="rawDiff">Raw unified diff text.</param>
        /// <returns>List of parsed diff files.</returns>
        public static List<DiffFile> Parse(string rawDiff)
        {
            List<DiffFile> files = new List<DiffFile>();

            if (string.IsNullOrEmpty(rawDiff))
                return files;

            string[] lines = rawDiff.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            DiffFile? currentFile = null;
            DiffHunk? currentHunk = null;
            int oldLine = 0;
            int newLine = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // New file boundary
                if (line.StartsWith("diff --git "))
                {
                    currentFile = new DiffFile();
                    files.Add(currentFile);
                    currentHunk = null;

                    // Try to extract filename from "diff --git a/path b/path"
                    int bIndex = line.IndexOf(" b/");
                    if (bIndex >= 0)
                    {
                        currentFile.FileName = line.Substring(bIndex + 3);
                    }

                    continue;
                }

                if (currentFile == null)
                    continue;

                // Extract filename from +++ line (more reliable)
                if (line.StartsWith("+++ "))
                {
                    string path = line.Substring(4).Trim();
                    if (path.StartsWith("b/"))
                        path = path.Substring(2);
                    if (path != "/dev/null")
                        currentFile.FileName = path;
                    continue;
                }

                // Skip --- line
                if (line.StartsWith("--- "))
                {
                    continue;
                }

                // Skip index/mode lines
                if (line.StartsWith("index ") || line.StartsWith("old mode") ||
                    line.StartsWith("new mode") || line.StartsWith("new file") ||
                    line.StartsWith("deleted file") || line.StartsWith("similarity") ||
                    line.StartsWith("rename ") || line.StartsWith("copy "))
                {
                    continue;
                }

                // Hunk header
                if (line.StartsWith("@@"))
                {
                    currentHunk = new DiffHunk { Header = line };
                    currentFile.Hunks.Add(currentHunk);

                    Match match = _HunkHeaderRegex.Match(line);
                    if (match.Success)
                    {
                        oldLine = int.Parse(match.Groups[1].Value);
                        newLine = int.Parse(match.Groups[2].Value);
                    }

                    // Add the hunk header as a line for display
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LineType = DiffLineTypeEnum.Hunk,
                        Content = line,
                        RawText = line,
                        Prefix = "@@"
                    });

                    continue;
                }

                if (currentHunk == null)
                    continue;

                // Diff content lines
                if (line.StartsWith("+"))
                {
                    DiffLine diffLine = new DiffLine
                    {
                        LineType = DiffLineTypeEnum.Addition,
                        NewLineNumber = newLine,
                        Prefix = "+",
                        Content = line.Length > 1 ? line.Substring(1) : "",
                        RawText = line
                    };
                    currentHunk.Lines.Add(diffLine);
                    currentFile.Additions++;
                    newLine++;
                }
                else if (line.StartsWith("-"))
                {
                    DiffLine diffLine = new DiffLine
                    {
                        LineType = DiffLineTypeEnum.Deletion,
                        OldLineNumber = oldLine,
                        Prefix = "-",
                        Content = line.Length > 1 ? line.Substring(1) : "",
                        RawText = line
                    };
                    currentHunk.Lines.Add(diffLine);
                    currentFile.Deletions++;
                    oldLine++;
                }
                else if (line.StartsWith(" ") || line == "")
                {
                    DiffLine diffLine = new DiffLine
                    {
                        LineType = DiffLineTypeEnum.Context,
                        OldLineNumber = oldLine,
                        NewLineNumber = newLine,
                        Prefix = " ",
                        Content = line.Length > 1 ? line.Substring(1) : (line == "" ? "" : line),
                        RawText = line
                    };
                    currentHunk.Lines.Add(diffLine);
                    oldLine++;
                    newLine++;
                }
                else if (line.StartsWith("\\"))
                {
                    // "\ No newline at end of file" - add as context
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LineType = DiffLineTypeEnum.Context,
                        Prefix = "\\",
                        Content = line,
                        RawText = line
                    });
                }
            }

            return files;
        }
    }
}
