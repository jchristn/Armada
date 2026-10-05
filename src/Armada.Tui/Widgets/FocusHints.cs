namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Status bar hints for the control that holds keyboard focus: key labels with English descriptions (the status bar
    /// translates them), most important first, and whether the control takes typed text. For a text-entry control the
    /// first hint says how to leave it and what that unlocks (for example <c>Esc</c> "Back to the list"). Not
    /// thread-safe.
    /// </summary>
    public class FocusHints
    {
        #region Public-Members

        /// <summary>
        /// True when the focused control takes typed text, so printable keys type instead of acting as shortcuts.
        /// </summary>
        public bool TextEntry { get; set; } = false;

        /// <summary>
        /// Hints (key label, English description) in display order. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> Keys { get; } = new List<KeyValuePair<string, string>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="textEntry">The focused control takes typed text.</param>
        public FocusHints(bool textEntry = false)
        {
            TextEntry = textEntry;
        }

        /// <summary>
        /// Hints for a control that does not take typed text.
        /// </summary>
        /// <param name="keys">Hints, or null.</param>
        /// <returns>Hints.</returns>
        public static FocusHints Of(IEnumerable<KeyValuePair<string, string>>? keys)
        {
            FocusHints hints = new FocusHints(false);
            if (keys != null) hints.Keys.AddRange(keys);
            return hints;
        }

        /// <summary>
        /// Hints for a text-entry control, starting with how to leave it.
        /// </summary>
        /// <param name="leaveKey">Key that leaves the control (for example <c>Esc</c> or <c>Tab</c>).</param>
        /// <param name="leaveLabel">English description of where that goes and what it unlocks.</param>
        /// <returns>Hints.</returns>
        public static FocusHints Typing(string leaveKey, string leaveLabel)
        {
            if (String.IsNullOrEmpty(leaveKey)) throw new ArgumentNullException(nameof(leaveKey));
            FocusHints hints = new FocusHints(true);
            hints.Add(leaveKey, leaveLabel ?? "");
            return hints;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append a hint.
        /// </summary>
        /// <param name="key">Key label.</param>
        /// <param name="label">English description.</param>
        /// <returns>This instance.</returns>
        public FocusHints Add(string key, string label)
        {
            Keys.Add(new KeyValuePair<string, string>(key ?? "", label ?? ""));
            return this;
        }

        /// <summary>
        /// Append hints.
        /// </summary>
        /// <param name="keys">Hints, or null.</param>
        /// <returns>This instance.</returns>
        public FocusHints AddRange(IEnumerable<KeyValuePair<string, string>>? keys)
        {
            if (keys != null) Keys.AddRange(keys);
            return this;
        }

        #endregion
    }
}
