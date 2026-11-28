namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// The completion line a work stage ends with (<c>[ARMADA:RESULT] COMPLETE</c>): which personas must write it, and
    /// the one decoration-tolerant matcher that Core and the API endpoint runtime both use to find it.
    /// </summary>
    public static class CompletionLine
    {
        #region Public-Members

        /// <summary>
        /// Environment variable the Admiral sets to "1" on a mission launch whose persona must end with the completion
        /// line. The API endpoint runtime reads it instead of searching the prompt for the marker.
        /// </summary>
        public const string RequiredEnvironmentVariable = "ARMADA_REQUIRES_COMPLETION_LINE";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a persona's contract ends with the completion line. Judges end with a verdict, Architects with a plan,
        /// and Recorders with a summary; every other persona, including a custom or empty one, ends with the completion line.
        /// </summary>
        /// <param name="persona">Mission persona, or null.</param>
        /// <returns>True when the completion line is required.</returns>
        public static bool IsRequiredFor(string? persona)
        {
            return !PersonaCatalog.Matches(persona, PersonaCatalog.Judge)
                && !PersonaCatalog.Matches(persona, PersonaCatalog.Architect)
                && !PersonaCatalog.Matches(persona, PersonaCatalog.Recorder);
        }

        /// <summary>
        /// Whether one line is the completion line, allowing surrounding bold, code-span, or quote decoration. More words
        /// on the line, or the marker quoted inside a sentence, do not count.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <returns>True for the completion line.</returns>
        public static bool IsCompletionLine(string? line)
        {
            return String.Equals(ProtocolLines.StripDecoration(line), NoOpCompletionDetector.CompletionMarker, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether output holds the completion line outside any fenced block. Fence state resets at the truncation marker.
        /// </summary>
        /// <param name="output">Stage output.</param>
        /// <returns>True when present.</returns>
        public static bool HasCompletionLine(string? output)
        {
            if (String.IsNullOrEmpty(output)) return false;
            bool inFence = false;
            foreach (string line in output.Replace("\r\n", "\n").Split('\n'))
            {
                if (ProtocolLines.IsTruncationMarker(line)) { inFence = false; continue; }
                if (ProtocolLines.IsFence(line)) { inFence = !inFence; continue; }
                if (!inFence && IsCompletionLine(line)) return true;
            }

            return false;
        }

        #endregion
    }
}
