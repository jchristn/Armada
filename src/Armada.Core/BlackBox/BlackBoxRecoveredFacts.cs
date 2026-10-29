namespace Armada.Core.BlackBox
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Facts a capture without inline context (the sweeper or a backfill) recovers from what Armada stored about the
    /// failure: the mission's FailureReason and the <c>captain.process_exited</c> event, whose texts carry the agent's
    /// exit code ("Agent process exited with code 134.", "Agent process 4242 exited with code 134 for captain ...").
    /// </summary>
    public static class BlackBoxRecoveredFacts
    {
        #region Private-Members

        private static readonly Regex _ExitCode = new Regex(@"\bexit(?:ed)?(?:\s+with)?(?:\s+exit)?\s+code\s*[:=]?\s*(-?\d{1,10})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The exit code a failure text names ("exited with code 134", "exit code: 1"), or null when it names none
        /// ("exited with code unknown").
        /// </summary>
        /// <param name="text">Failure reason or event message, or null.</param>
        /// <returns>Exit code, or null.</returns>
        public static int? ParseExitCode(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            Match match = _ExitCode.Match(text);
            if (!match.Success) return null;
            return Int32.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int code) ? code : (int?)null;
        }

        #endregion
    }
}
