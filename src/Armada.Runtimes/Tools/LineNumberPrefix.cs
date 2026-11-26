namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Detects edit text that carries the line-number prefixes read_file adds for display (a right-aligned number
    /// six characters wide, then a tab). Some open models paste them into edits (RS-7.15). Detection is
    /// conservative so tab-separated data with numeric first columns is not rejected.
    /// </summary>
    public static class LineNumberPrefix
    {
        /// <summary>
        /// The error code edit tools return when edit text carries line-number prefixes.
        /// </summary>
        public const string ErrorCode = "line_number_prefix";

        private static readonly Regex _Prefix = new Regex(@"^(?<pad> *)(?<num>\d{1,9})(\t|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Check old_string: it is reported when it is not in the file as sent, every line carries a prefix, and the
        /// text with the prefixes removed is in the file.
        /// </summary>
        /// <param name="lfContent">The file content, LF-normalized.</param>
        /// <param name="lfOldString">The old_string, LF-normalized.</param>
        /// <returns>True when old_string carries read_file prefixes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static bool OldStringHasPrefixes(string lfContent, string lfOldString)
        {
            if (lfContent == null) throw new ArgumentNullException(nameof(lfContent));
            if (lfOldString == null) throw new ArgumentNullException(nameof(lfOldString));
            if (lfOldString.Length == 0 || lfContent.Contains(lfOldString, StringComparison.Ordinal)) return false;
            if (!AllLinesPrefixed(lfOldString, false, out List<long> _)) return false;
            string stripped = Strip(lfOldString);
            return stripped.Length > 0 && lfContent.Contains(stripped, StringComparison.Ordinal);
        }

        /// <summary>
        /// Check replacement text: it is reported when every line carries a six-wide read_file prefix, the numbers
        /// are consecutive, and the file itself has no such lines (so a file of numbered tab-separated rows is
        /// left alone).
        /// </summary>
        /// <param name="lfContent">The file content, LF-normalized, or empty for a new file.</param>
        /// <param name="lfNewText">The replacement text, LF-normalized.</param>
        /// <returns>True when the replacement text carries read_file prefixes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static bool NewTextHasPrefixes(string lfContent, string lfNewText)
        {
            if (lfContent == null) throw new ArgumentNullException(nameof(lfContent));
            if (lfNewText == null) throw new ArgumentNullException(nameof(lfNewText));
            if (!AllLinesPrefixed(lfNewText, true, out List<long> numbers)) return false;
            for (int i = 1; i < numbers.Count; i++)
            {
                if (numbers[i] != numbers[i - 1] + 1) return false;
            }

            foreach (string line in lfContent.Split('\n'))
            {
                if (_Prefix.IsMatch(line) && line.Contains('\t')) return false;
            }

            return true;
        }

        /// <summary>
        /// Remove a read_file prefix from the start of each line.
        /// </summary>
        /// <param name="lfText">LF-normalized text.</param>
        /// <returns>The text without prefixes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lfText"/> is null.</exception>
        public static string Strip(string lfText)
        {
            if (lfText == null) throw new ArgumentNullException(nameof(lfText));
            string[] lines = lfText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match match = _Prefix.Match(lines[i]);
                if (match.Success) lines[i] = lines[i].Substring(match.Length);
            }

            return String.Join("\n", lines);
        }

        /// <summary>
        /// The error message an edit tool returns, naming the argument that carried the prefixes.
        /// </summary>
        /// <param name="argumentName">The argument name, such as old_string.</param>
        /// <param name="resendArguments">The arguments to send again without prefixes.</param>
        /// <returns>A message the model can act on.</returns>
        public static string Message(string argumentName, string resendArguments = "old_string and new_string")
        {
            return argumentName + " starts each line with a read_file line number and tab (for example \"    12\\t\"). "
                + "read_file adds those numbers for display; they are not in the file. Send the call again with the leading number "
                + "and tab removed from every line of " + resendArguments + ".";
        }

        private static bool AllLinesPrefixed(string lfText, bool requireReadFileWidth, out List<long> numbers)
        {
            numbers = new List<long>();
            string[] lines = lfText.Split('\n');
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;
                Match match = _Prefix.Match(line);
                if (!match.Success) return false;
                int width = match.Groups["pad"].Length + match.Groups["num"].Length;
                if (requireReadFileWidth && (width != 6 || !line.Contains('\t'))) return false;
                numbers.Add(Int64.Parse(match.Groups["num"].Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            return numbers.Count > 0;
        }
    }
}
