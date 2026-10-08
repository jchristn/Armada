namespace Armada.Tui.Ask
{
    using System;

    /// <summary>
    /// One row of a captain reply's turn statistics: a stable key, the label (English; the caller translates it), and
    /// the formatted value. The same rows, keys, and formats as the dashboard's and the mobile app's lib/chatMetrics.
    /// </summary>
    public class AskTurnStatistic
    {
        #region Public-Members

        /// <summary>
        /// Stable key (timeToFirstToken, timeToFirstText, streaming, tokensPerSecond, tokens, inputTokens, cachedTokens,
        /// cost, total, toolCalls, toolTime).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label in English, for example "time to first token".
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Formatted value, for example "812ms", "4.21s", "~120", or "$0.0123"; "-" when unknown.
        /// </summary>
        public string Value { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTurnStatistic()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">Label in English.</param>
        /// <param name="value">Formatted value.</param>
        public AskTurnStatistic(string key, string label, string value)
        {
            Key = key ?? String.Empty;
            Label = label ?? String.Empty;
            Value = value ?? String.Empty;
        }

        #endregion
    }
}
