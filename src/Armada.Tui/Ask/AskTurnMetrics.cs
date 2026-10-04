namespace Armada.Tui.Ask
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Timing of one captain turn as observed by the TUI: time to first token, approximate token rate and count (the
    /// server reports no token counts for Ask turns, so tokens are estimated at four characters each), and total time.
    /// </summary>
    public class AskTurnMetrics
    {
        #region Public-Members

        /// <summary>
        /// Milliseconds from the start of the turn to the first reply chunk, or null.
        /// </summary>
        public double? FirstTokenMs { get; set; } = null;

        /// <summary>
        /// Approximate tokens per second while streaming, or null.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Approximate tokens in the reply.
        /// </summary>
        public int ApproximateTokens { get; set; } = 0;

        /// <summary>
        /// Total milliseconds.
        /// </summary>
        public double TotalMs { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTurnMetrics()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Format a duration like the dashboard (<c>850ms</c>, <c>4.2s</c>, <c>12s</c>).
        /// </summary>
        /// <param name="ms">Milliseconds, or null.</param>
        /// <returns>Text, or empty.</returns>
        public static string FormatDuration(double? ms)
        {
            if (ms == null) return "";
            double v = ms.Value;
            if (v < 1000) return Math.Round(v).ToString(CultureInfo.InvariantCulture) + "ms";
            double s = v / 1000.0;
            return (v < 10000 ? s.ToString("0.0", CultureInfo.InvariantCulture) : s.ToString("0", CultureInfo.InvariantCulture)) + "s";
        }

        /// <summary>
        /// One-line summary, for example <c>first token 1.2s  ~38 tok/s  ~420 tokens  total 12s</c> (English words; the
        /// caller translates the labels).
        /// </summary>
        /// <param name="firstToken">Label for time to first token.</param>
        /// <param name="tokPerSec">Label for the rate unit.</param>
        /// <param name="tokens">Label for tokens.</param>
        /// <param name="total">Label for the total.</param>
        /// <returns>Text.</returns>
        public string Describe(string firstToken, string tokPerSec, string tokens, string total)
        {
            string text = "";
            if (FirstTokenMs != null) text += firstToken + " " + FormatDuration(FirstTokenMs) + "  ";
            if (TokensPerSecond != null) text += "~" + Math.Round(TokensPerSecond.Value).ToString(CultureInfo.InvariantCulture) + " " + tokPerSec + "  ";
            if (ApproximateTokens > 0) text += "~" + ApproximateTokens.ToString(CultureInfo.InvariantCulture) + " " + tokens + "  ";
            text += total + " " + FormatDuration(TotalMs);
            return text;
        }

        #endregion
    }
}
