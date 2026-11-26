namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Shared line rules for reading protocol lines (verdicts, completion lines, plan fences) from stage output: which
    /// lines open or close a fenced block, where output truncation cut the text, and which decoration a model may wrap
    /// around a protocol line without changing its meaning.
    /// </summary>
    public static class ProtocolLines
    {
        #region Public-Members

        /// <summary>
        /// The line Armada writes where it cut captured output short. A fence left open before it was cut by truncation,
        /// not by the model, so fence state resets at this line.
        /// </summary>
        public const string TruncationMarker = "[...output truncated...]";

        #endregion

        #region Private-Members

        private static readonly char[] _LeadingDecoration = new char[] { ' ', '\t', '*', '`', '_', '>' };

        private static readonly char[] _TrailingDecoration = new char[] { ' ', '\t', '*', '`', '_', '.' };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a line opens or closes a fenced block (three backticks or three tildes).
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <returns>True for a fence line.</returns>
        public static bool IsFence(string? line)
        {
            if (line == null) return false;
            string trimmed = line.Trim();
            return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a line is the truncation marker Armada writes into cut output.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <returns>True for the truncation marker.</returns>
        public static bool IsTruncationMarker(string? line)
        {
            return line != null && String.Equals(line.Trim(), TruncationMarker, StringComparison.Ordinal);
        }

        /// <summary>
        /// Remove the bold, italic, code-span, and quote decoration a model may wrap around a protocol line, for example
        /// <c>**[ARMADA:RESULT] COMPLETE**</c> or <c>`[ARMADA:RESULT] COMPLETE`</c>.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <returns>The undecorated line; empty for null.</returns>
        public static string StripDecoration(string? line)
        {
            if (line == null) return String.Empty;
            return line.Trim().TrimStart(_LeadingDecoration).TrimEnd(_TrailingDecoration);
        }

        #endregion
    }
}
