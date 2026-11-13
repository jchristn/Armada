namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Shortens a failed stage check's output for the captain's brief without losing the names of the failing tests.
    /// The head and tail of the output are kept, and lines that test runners and compilers use to report a failure
    /// (dotnet test, xUnit, NUnit, the Touchstone console runner, TAP, Jest and Vitest, go test, pytest, cargo, MSBuild
    /// errors) are lifted into a "Failing tests" section that is never cut by the head and tail budget.
    /// </summary>
    public static class StageCheckOutputSummarizer
    {
        #region Public-Members

        /// <summary>
        /// Characters kept from the start of the output by default.
        /// </summary>
        public const int DefaultHeadChars = 1000;

        /// <summary>
        /// Characters kept from the end of the output by default.
        /// </summary>
        public const int DefaultTailChars = 2500;

        /// <summary>
        /// Most failure lines listed in the "Failing tests" section by default.
        /// </summary>
        public const int DefaultMaxFailingLines = 50;

        /// <summary>
        /// Longest single failure line kept; longer lines are cut so one runaway line cannot fill the section.
        /// </summary>
        public const int MaxFailingLineChars = 400;

        /// <summary>
        /// Heading of the section that lists failure lines.
        /// </summary>
        public const string FailingTestsHeading = "Failing tests:";

        #endregion

        #region Private-Members

        private static readonly Regex _CompilerError = new Regex(@":\s+error\s+[A-Za-z]+\d+\s*:", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _TouchstoneRow = new Regex(@"\s{2,}FAIL\s+\d+ms\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Shorten stage check output for a brief. Output within the head and tail budget is returned unchanged
        /// (trimmed); longer output keeps its head and tail and is preceded by a "Failing tests" section that lists
        /// every recognized failure line, up to <paramref name="maxFailingLines"/>.
        /// </summary>
        /// <param name="output">Combined stdout and stderr of the check.</param>
        /// <param name="headChars">Characters kept from the start.</param>
        /// <param name="tailChars">Characters kept from the end.</param>
        /// <param name="maxFailingLines">Most failure lines listed.</param>
        /// <returns>The shortened output.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a budget is negative.</exception>
        public static string Summarize(string? output, int headChars = DefaultHeadChars, int tailChars = DefaultTailChars, int maxFailingLines = DefaultMaxFailingLines)
        {
            if (headChars < 0) throw new ArgumentOutOfRangeException(nameof(headChars));
            if (tailChars < 0) throw new ArgumentOutOfRangeException(nameof(tailChars));
            if (maxFailingLines < 0) throw new ArgumentOutOfRangeException(nameof(maxFailingLines));

            string text = (output ?? String.Empty).Trim();
            if (text.Length <= headChars + tailChars) return text;

            StringBuilder builder = new StringBuilder();
            int totalFailing;
            List<string> failing = ExtractFailingLines(text, maxFailingLines, out totalFailing);
            if (failing.Count > 0)
            {
                builder.Append(FailingTestsHeading).Append('\n');
                foreach (string line in failing) builder.Append(line).Append('\n');
                if (totalFailing > failing.Count) builder.Append("(" + (totalFailing - failing.Count) + " more failure lines not shown)\n");
                builder.Append('\n').Append("Output (start and end):\n");
            }

            string head = CutAtLineEnd(text.Substring(0, headChars));
            string tail = CutAtLineStart(text.Substring(text.Length - tailChars));
            int omitted = text.Length - head.Length - tail.Length;
            builder.Append(head);
            if (head.Length > 0) builder.Append('\n');
            builder.Append("... [" + omitted + " characters omitted] ...\n");
            builder.Append(tail);
            return builder.ToString();
        }

        /// <summary>
        /// Extract the lines that report a failing test or a compiler error, in order and without duplicates.
        /// </summary>
        /// <param name="output">Check output.</param>
        /// <param name="maxLines">Most lines returned.</param>
        /// <param name="totalFound">Receives how many distinct failure lines the output holds.</param>
        /// <returns>The failure lines, trimmed of trailing whitespace and capped in length.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxLines"/> is negative.</exception>
        public static List<string> ExtractFailingLines(string? output, int maxLines, out int totalFound)
        {
            if (maxLines < 0) throw new ArgumentOutOfRangeException(nameof(maxLines));

            List<string> lines = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            totalFound = 0;
            if (String.IsNullOrEmpty(output)) return lines;

            string[] all = output!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool inTouchstoneFailures = false;
            bool takeNext = false;
            for (int i = 0; i < all.Length; i++)
            {
                string line = all[i].TrimEnd();
                bool keep = false;

                if (inTouchstoneFailures)
                {
                    if (line.Length == 0 || !Char.IsWhiteSpace(all[i], 0)) inTouchstoneFailures = false;
                    else keep = true;
                }

                if (takeNext)
                {
                    takeNext = false;
                    if (line.Trim().Length > 0) keep = true;
                }

                if (line.Trim() == "Failed Tests:")
                {
                    inTouchstoneFailures = true;
                    keep = true;
                }
                else if (IsFailureMarker(line))
                {
                    keep = true;
                    if (line.Trim() == "Error Message:") takeNext = true;
                }

                if (!keep) continue;
                if (line.Length > MaxFailingLineChars) line = line.Substring(0, MaxFailingLineChars) + "...";
                if (!seen.Add(line)) continue;
                totalFound++;
                if (lines.Count < maxLines) lines.Add(line);
            }

            return lines;
        }

        #endregion

        #region Private-Methods

        private static bool IsFailureMarker(string line)
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0) return false;
            if (trimmed.StartsWith("Failed ", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("Failed :", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("FAILED ", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("FAIL ", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("not ok ", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("--- FAIL:", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("✕ ", StringComparison.Ordinal) || trimmed.StartsWith("✗ ", StringComparison.Ordinal) || trimmed.StartsWith("× ", StringComparison.Ordinal)) return true;
            if (trimmed == "Error Message:") return true;
            if (line.Contains("[FAIL]", StringComparison.Ordinal)) return true;
            if (line.Contains("Assertion failed", StringComparison.Ordinal)) return true;
            if (line.EndsWith("... FAILED", StringComparison.Ordinal)) return true;
            if (_TouchstoneRow.IsMatch(line)) return true;
            if (_CompilerError.IsMatch(line)) return true;
            return false;
        }

        private static string CutAtLineEnd(string head)
        {
            int newline = head.LastIndexOf('\n');
            return newline > 0 ? head.Substring(0, newline) : head;
        }

        private static string CutAtLineStart(string tail)
        {
            int newline = tail.IndexOf('\n');
            return newline >= 0 && newline < tail.Length - 1 ? tail.Substring(newline + 1) : tail;
        }

        #endregion
    }
}
