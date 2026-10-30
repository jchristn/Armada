namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Severity escalation of proposed Armada changes by recurrence: a failure mode seen once is Medium, 3 or more times
    /// in 30 days High, and 5 or more times or in 2 or more Armada versions Critical. A change keeps its own severity when
    /// that is higher. Pure and thread-safe.
    /// </summary>
    public static class BlackBoxResilienceSeverity
    {
        #region Public-Methods

        /// <summary>
        /// The severity recurrence alone calls for.
        /// </summary>
        /// <param name="occurrences30Days">Entries with the fingerprint in the last 30 days (values below 1 count as 1).</param>
        /// <param name="armadaVersions">Armada versions the fingerprint was seen in.</param>
        /// <returns>Medium, High or Critical.</returns>
        public static BlackBoxSeverityEnum FromRecurrence(int occurrences30Days, int armadaVersions)
        {
            int occurrences = Math.Max(1, occurrences30Days);
            if (occurrences >= 5 || armadaVersions >= 2) return BlackBoxSeverityEnum.Critical;
            if (occurrences >= 3) return BlackBoxSeverityEnum.High;
            return BlackBoxSeverityEnum.Medium;
        }

        /// <summary>
        /// Escalate a severity by recurrence: the higher of the base severity and the recurrence severity.
        /// </summary>
        /// <param name="baseSeverity">Base severity (Unknown counts as Low).</param>
        /// <param name="occurrences30Days">Entries with the fingerprint in the last 30 days.</param>
        /// <param name="armadaVersions">Armada versions the fingerprint was seen in.</param>
        /// <returns>The escalated severity.</returns>
        public static BlackBoxSeverityEnum Escalate(BlackBoxSeverityEnum baseSeverity, int occurrences30Days, int armadaVersions)
        {
            return Max(baseSeverity, FromRecurrence(occurrences30Days, armadaVersions));
        }

        /// <summary>
        /// The higher of two severities (Unknown counts as Low).
        /// </summary>
        /// <param name="a">First.</param>
        /// <param name="b">Second.</param>
        /// <returns>The higher one.</returns>
        public static BlackBoxSeverityEnum Max(BlackBoxSeverityEnum a, BlackBoxSeverityEnum b)
        {
            return Order(a) >= Order(b) ? Normalize(a) : Normalize(b);
        }

        /// <summary>
        /// Sort order of a severity: Low 0, Medium 1, High 2, Critical 3 (Unknown counts as Low).
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <returns>The order.</returns>
        public static int Order(BlackBoxSeverityEnum severity)
        {
            switch (severity)
            {
                case BlackBoxSeverityEnum.Critical: return 3;
                case BlackBoxSeverityEnum.High: return 2;
                case BlackBoxSeverityEnum.Medium: return 1;
                default: return 0;
            }
        }

        #endregion

        #region Private-Methods

        private static BlackBoxSeverityEnum Normalize(BlackBoxSeverityEnum severity)
        {
            return severity == BlackBoxSeverityEnum.Unknown ? BlackBoxSeverityEnum.Low : severity;
        }

        #endregion
    }
}
