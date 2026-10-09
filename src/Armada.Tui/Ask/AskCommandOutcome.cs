namespace Armada.Tui.Ask
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of a local command: <see cref="Ok"/> clears the composer; <see cref="Hint"/> (an English key with
    /// <see cref="HintArgs"/>) is shown above it.
    /// </summary>
    public class AskCommandOutcome
    {
        #region Public-Members

        /// <summary>
        /// The command ran (the composer clears).
        /// </summary>
        public bool Ok { get; set; } = true;

        /// <summary>
        /// English hint, or null.
        /// </summary>
        public string? Hint { get; set; } = null;

        /// <summary>
        /// Hint placeholders, or null.
        /// </summary>
        public Dictionary<string, object?>? HintArgs { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ok">The command ran.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <param name="hintArgs">Hint placeholders, or null.</param>
        public AskCommandOutcome(bool ok, string? hint = null, Dictionary<string, object?>? hintArgs = null)
        {
            Ok = ok;
            Hint = hint;
            HintArgs = hintArgs;
        }

        #endregion
    }
}
