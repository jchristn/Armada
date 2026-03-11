namespace Armada.Core.Services
{
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;

    /// <summary>
    /// Parses agent output for progress signals.
    /// Agents can emit lines like:
    ///   [ARMADA:PROGRESS] 75
    ///   [ARMADA:STATUS] Testing
    ///   [ARMADA:MESSAGE] Running unit tests now
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
            /// Signal type: "progress", "status", or "message".
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
            @"\[ARMADA:(\w+)\]\s*(.+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to parse a progress signal from an agent output line.
        /// Returns null if the line does not contain a signal.
        /// </summary>
        /// <param name="line">Agent output line.</param>
        /// <returns>Parsed signal or null.</returns>
        public static ProgressSignal? TryParse(string line)
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
