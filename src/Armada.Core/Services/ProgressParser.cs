namespace Armada.Core.Services
{
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;

    /// <summary>
    /// Parses agent output for progress signals.
    /// Agents can emit lines like:
    ///   [ARMADA:PROGRESS] 75
    ///   [ARMADA:STATUS] Testing
    ///   [ARMADA:RESULT] COMPLETE
    ///   [ARMADA:VERDICT] PASS
    ///   [ARMADA:MESSAGE] Running unit tests now
    ///   [ARMADA:PAPERCUT] {"category":"MissingDoc","severity":"Low","title":"..."}
    /// </summary>
    public static class ProgressParser
    {
        #region Public-Members

        /// <summary>
        /// Parsed progress signal from agent output.
        /// </summary>
        public class ProgressSignal
        {
            /// <summary>
            /// Signal type: "progress", "status", "message", "result", or "verdict".
            /// </summary>
            public string Type { get; set; } = "";

            /// <summary>
            /// Signal value.
            /// </summary>
            public string Value { get; set; } = "";

            /// <summary>
            /// Parsed percentage (0-100) if type is "progress".
            /// </summary>
            public int? Percentage { get; set; } = null;

            /// <summary>
            /// Parsed mission status if type is "status".
            /// </summary>
            public MissionStatusEnum? MissionStatus { get; set; } = null;
        }

        #endregion

        #region Private-Members

        private static readonly Regex _SignalPattern = new Regex(
            @"^\s*\[ARMADA:(\w+)\]\s+(.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to parse a progress signal from an agent output record.
        /// Returns null if the record does not contain a signal.
        /// </summary>
        /// <remarks>
        /// A runtime record is not always a single line: a Codex <c>agent_message</c> or a Claude
        /// assistant text block can carry several physical lines in one record, and a captain's
        /// final answer commonly contains a protocol marker followed by prose. The marker only
        /// counts when it STARTS a physical line -- a signal embedded in the middle of a prose
        /// line is not a signal (for example an instruction file that documents the format).
        /// When a record holds several markers, a papercut wins: it is a report that must reach
        /// the store, while the other markers (result, status) are also re-detected from the
        /// mission log file at exit.
        /// </remarks>
        /// <param name="line">Agent output record.</param>
        /// <returns>Parsed signal or null.</returns>
        public static ProgressSignal? TryParse(string line)
        {
            if (String.IsNullOrEmpty(line)) return null;

            ProgressSignal? first = null;
            ProgressSignal? papercut = null;
            foreach (string physicalLine in line.Split('\n'))
            {
                ProgressSignal? parsed = TryParsePhysicalLine(physicalLine);
                if (parsed == null) continue;

                if (first == null) first = parsed;
                if (String.Equals(parsed.Type, "papercut", StringComparison.Ordinal)) papercut = parsed;
            }

            return papercut ?? first;
        }

        private static ProgressSignal? TryParsePhysicalLine(string line)
        {
            if (String.IsNullOrEmpty(line)) return null;

            Match match = _SignalPattern.Match(line);
            if (!match.Success) return null;

            string type = match.Groups[1].Value.ToLowerInvariant();
            string value = match.Groups[2].Value.Trim();

            ProgressSignal signal = new ProgressSignal
            {
                Type = type,
                Value = value
            };

            switch (type)
            {
                case "progress":
                    if (int.TryParse(value.TrimEnd('%'), out int pct))
                    {
                        signal.Percentage = Math.Clamp(pct, 0, 100);
                    }
                    break;

                case "status":
                    if (Enum.TryParse<MissionStatusEnum>(value, true, out MissionStatusEnum status))
                    {
                        signal.MissionStatus = status;
                    }
                    break;
            }

            return signal;
        }

        #endregion
    }
}
