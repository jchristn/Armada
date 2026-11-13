namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A captain of any persona that cannot proceed because of an external prerequisite (for example work another mission
    /// has not landed yet) or a contradictory brief reports it with the standalone <see cref="VerifierContract.BlockedMarker"/>
    /// line, or with an <c>[ARMADA:MESSAGE] BLOCKED: ...</c> line, plus the reason. Outside the verifier stages (whose
    /// BLOCKED sends the work back to the Worker, see <see cref="VerifierContract"/>), such a run with no work on its branch
    /// is a reported blocker, not an unfinished completion: it is not re-dispatched and fails with the captain's reason.
    /// </summary>
    public static class CaptainBlockedContract
    {
        #region Public-Members

        /// <summary>
        /// Event type recorded when a mission is failed because its captain reported BLOCKED.
        /// </summary>
        public const string EventType = "mission.blocked_by_captain";

        /// <summary>
        /// A result-style spelling some captains use for the blocked outcome; accepted as well as the canonical marker.
        /// </summary>
        public const string ResultAlias = "[ARMADA:RESULT] BLOCKED";

        /// <summary>
        /// Prefix of a progress message line.
        /// </summary>
        public const string MessagePrefix = "[ARMADA:MESSAGE]";

        /// <summary>
        /// The word a blocking progress message starts with, followed by a colon.
        /// </summary>
        public const string MessageBlockedWord = "BLOCKED:";

        /// <summary>
        /// Maximum length of the blocking reason copied into a mission's failure reason.
        /// </summary>
        public const int MaxReasonLength = 1000;

        /// <summary>
        /// Prefix of the failure reason of a mission whose captain reported BLOCKED.
        /// </summary>
        public const string FailureReasonPrefix = "Captain reported BLOCKED: ";

        /// <summary>
        /// The runtime-signal line that documents the blocked outcome for every persona (mission.progress_signals).
        /// </summary>
        public const string SignalLine =
            "- `" + VerifierContract.BlockedMarker + "` -- any persona: you cannot proceed because of an external prerequisite (for example work another mission has not landed yet) "
            + "or a contradictory brief. End with this standalone line instead of a result or verdict line, followed by one paragraph giving the reason; do not change files for work you cannot do. "
            + "Verifier stages that found a product defect use the same line with an `## Affected Case` section.\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an output reports a blocked outcome: a standalone blocked marker line (or its result-style alias), or
        /// an <c>[ARMADA:MESSAGE] BLOCKED:</c> line, outside fenced code blocks.
        /// </summary>
        /// <param name="output">The captain's output.</param>
        /// <returns>True when the captain reported that it is blocked.</returns>
        public static bool IsReported(string? output)
        {
            foreach (string line in ProtocolLines(output))
            {
                if (IsMarkerLine(line) || TryReadBlockingMessage(line, out _)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether an output also carries a completion or verdict line, in which case a blocking message was only
        /// progress chatter and the normal completion handling applies.
        /// </summary>
        /// <param name="output">The captain's output.</param>
        /// <returns>True when a standalone <c>[ARMADA:RESULT] COMPLETE</c> or any <c>[ARMADA:VERDICT]</c> line exists.</returns>
        public static bool HasCompletionOrVerdict(string? output)
        {
            foreach (string line in ProtocolLines(output))
            {
                if (String.Equals(line, NoOpCompletionDetector.CompletionMarker, StringComparison.Ordinal)) return true;
                if (line.StartsWith("[ARMADA:VERDICT]", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether an output carries any <c>[ARMADA:RESULT]</c>, <c>[ARMADA:VERDICT]</c> or stage-outcome line, in any form.
        /// </summary>
        /// <param name="output">The captain's output.</param>
        /// <returns>True when a result-like line is present.</returns>
        public static bool HasResultLine(string? output)
        {
            if (String.IsNullOrEmpty(output)) return false;
            return output.IndexOf("[ARMADA:RESULT]", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("[ARMADA:VERDICT]", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("[ARMADA:STAGE-OUTCOME]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The captain's blocking reason: the text of the last <c>[ARMADA:MESSAGE] BLOCKED:</c> line, otherwise the
        /// paragraph after the blocked marker line, otherwise the paragraph before it. Whitespace is collapsed and the
        /// result is bounded to <see cref="MaxReasonLength"/> characters.
        /// </summary>
        /// <param name="output">The captain's output.</param>
        /// <returns>The reason, or null when the output gives none.</returns>
        public static string? ExtractReason(string? output)
        {
            List<string> lines = ProtocolLines(output).ToList();
            string? message = null;
            foreach (string line in lines)
            {
                if (TryReadBlockingMessage(line, out string? text) && !String.IsNullOrWhiteSpace(text)) message = text;
            }

            if (message != null) return Bound(message);

            int marker = lines.FindLastIndex(IsMarkerLine);
            if (marker < 0) return null;

            string? after = Paragraph(lines, marker + 1, 1);
            if (!String.IsNullOrWhiteSpace(after)) return Bound(after!);
            string? before = Paragraph(lines, marker - 1, -1);
            return String.IsNullOrWhiteSpace(before) ? null : Bound(before!);
        }

        /// <summary>
        /// The failure reason recorded for a blocked mission.
        /// </summary>
        /// <param name="output">The captain's output.</param>
        /// <returns>The failure reason.</returns>
        public static string FailureReason(string? output)
        {
            return FailureReasonPrefix + (ExtractReason(output) ?? "(no reason given)");
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<string> ProtocolLines(string? output)
        {
            if (String.IsNullOrEmpty(output)) yield break;
            bool inFence = false;
            foreach (string raw in output.Replace("\r", String.Empty).Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (!inFence) yield return line;
            }
        }

        private static bool IsMarkerLine(string line)
        {
            return String.Equals(line, VerifierContract.BlockedMarker, StringComparison.Ordinal)
                || String.Equals(line, ResultAlias, StringComparison.Ordinal);
        }

        private static bool TryReadBlockingMessage(string line, out string? text)
        {
            text = null;
            if (!line.StartsWith(MessagePrefix, StringComparison.Ordinal)) return false;
            string rest = line.Substring(MessagePrefix.Length).TrimStart();
            if (!rest.StartsWith(MessageBlockedWord, StringComparison.Ordinal)) return false;
            text = rest.Substring(MessageBlockedWord.Length).Trim();
            return true;
        }

        private static string? Paragraph(List<string> lines, int start, int step)
        {
            List<string> collected = new List<string>();
            int i = start;

            // Skip blank lines next to the marker, then collect until the next blank or protocol line.
            while (i >= 0 && i < lines.Count && String.IsNullOrWhiteSpace(lines[i])) i += step;
            while (i >= 0 && i < lines.Count && !String.IsNullOrWhiteSpace(lines[i]) && !lines[i].StartsWith("[ARMADA:", StringComparison.Ordinal))
            {
                collected.Add(lines[i]);
                i += step;
            }

            if (collected.Count == 0) return null;
            if (step < 0) collected.Reverse();
            return String.Join(" ", collected);
        }

        private static string Bound(string text)
        {
            string collapsed = String.Join(" ", text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (collapsed.Length <= MaxReasonLength) return collapsed;
            return collapsed.Substring(0, MaxReasonLength - 3).TrimEnd() + "...";
        }

        #endregion
    }
}
