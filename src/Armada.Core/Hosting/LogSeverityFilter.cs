namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Filters Armada log lines (admiral.log, harbor.log: "yyyy-MM-dd HH:mm:ss host Severity message") by minimum
    /// severity. A line without a severity (a stack trace, a wrapped message) belongs to the line before it and is kept
    /// or dropped with it.
    /// </summary>
    public static class LogSeverityFilter
    {
        #region Public-Members

        /// <summary>
        /// Severity names from least to most severe.
        /// </summary>
        public static readonly IReadOnlyList<string> Severities = new string[] { "Debug", "Info", "Warn", "Error", "Alert", "Critical", "Emergency" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rank of a severity name (0 for Debug), or -1 when it is not one.
        /// </summary>
        /// <param name="severity">Severity name, case-insensitive.</param>
        /// <returns>Rank, or -1.</returns>
        public static int Rank(string? severity)
        {
            if (String.IsNullOrEmpty(severity)) return -1;
            for (int i = 0; i < Severities.Count; i++)
            {
                if (String.Equals(Severities[i], severity, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        /// <summary>
        /// The severity rank a log line declares (its fourth field), or -1 when it has none.
        /// </summary>
        /// <param name="line">Log line.</param>
        /// <returns>Rank, or -1.</returns>
        public static int LineRank(string? line)
        {
            if (String.IsNullOrEmpty(line)) return -1;

            // date, time, host, severity: find the fourth space-separated field without splitting the whole line.
            int start = 0;
            for (int field = 0; field < 3; field++)
            {
                int space = line.IndexOf(' ', start);
                if (space < 0) return -1;
                start = space + 1;
            }

            if (start < 5 || !Char.IsDigit(line[0])) return -1;
            int end = line.IndexOf(' ', start);
            string token = end < 0 ? line.Substring(start) : line.Substring(start, end - start);
            return Rank(token);
        }

        /// <summary>
        /// Keep lines at or above a minimum severity, with their continuation lines.
        /// </summary>
        /// <param name="lines">Lines.</param>
        /// <param name="minimumRank">Minimum rank (see <see cref="Rank"/>); 0 or less keeps everything.</param>
        /// <param name="previousKept">Whether the line before the first one was kept (for appended text); null when
        /// unknown, which keeps leading continuation lines.</param>
        /// <returns>The kept lines.</returns>
        public static List<string> Filter(IEnumerable<string> lines, int minimumRank, bool? previousKept = null)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            List<string> kept = new List<string>();
            bool keep = previousKept ?? true;
            foreach (string line in lines)
            {
                if (minimumRank <= 0)
                {
                    kept.Add(line);
                    continue;
                }

                int rank = LineRank(line);
                if (rank >= 0) keep = rank >= minimumRank;
                if (keep) kept.Add(line);
            }

            return kept;
        }

        #endregion
    }
}
