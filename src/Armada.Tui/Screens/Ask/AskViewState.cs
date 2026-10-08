namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Per-view display choices for the Ask transcript: which tool chips, thinking sections, turn statistics, and argument
    /// blocks are expanded, and which work card is highlighted after the work strip jumps to it. <see cref="Version"/> increases on
    /// every change so the layout cache can tell.
    /// </summary>
    public class AskViewState
    {
        #region Public-Members

        /// <summary>
        /// Block keys whose tool chips are expanded (arguments and results).
        /// </summary>
        public HashSet<string> ExpandedTools { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Block keys whose thinking section is expanded.
        /// </summary>
        public HashSet<string> ExpandedThinking { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Block keys of captain replies whose turn statistics panel is open (<c>i</c>).
        /// </summary>
        public HashSet<string> ExpandedStats { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Proposal ids whose arguments (and result) are expanded.
        /// </summary>
        public HashSet<string> ExpandedArguments { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Work id highlighted after a jump from the work strip, or null.
        /// </summary>
        public string? HighlightedWorkId { get; private set; } = null;

        /// <summary>
        /// When the highlight ends.
        /// </summary>
        public DateTime HighlightUntilUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Increases on every change.
        /// </summary>
        public long Version { get; private set; } = 0;

        /// <summary>
        /// Key of the block the focused transcript has selected (its pending card shows its keys inline), or null.
        /// </summary>
        public string? FocusedKey { get; private set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskViewState()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Toggle membership of a key in a set.
        /// </summary>
        /// <param name="set">Set.</param>
        /// <param name="key">Key.</param>
        /// <returns>True when now expanded.</returns>
        public bool Toggle(HashSet<string> set, string key)
        {
            Version++;
            if (set.Remove(key)) return false;
            set.Add(key);
            return true;
        }

        /// <summary>
        /// Record the block the focused transcript has selected (null when nothing is selected or the transcript does
        /// not have focus).
        /// </summary>
        /// <param name="key">Block key, or null.</param>
        public void Focus(string? key)
        {
            if (String.Equals(key, FocusedKey, StringComparison.Ordinal)) return;
            FocusedKey = key;
            Version++;
        }

        /// <summary>
        /// Highlight a work card for two seconds.
        /// </summary>
        /// <param name="workId">Tracked work id.</param>
        /// <param name="nowUtc">Now.</param>
        public void Highlight(string workId, DateTime nowUtc)
        {
            HighlightedWorkId = workId;
            HighlightUntilUtc = nowUtc.AddSeconds(2);
            Version++;
        }

        /// <summary>
        /// The highlighted work id at a time, or null after it expired.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Work id or null.</returns>
        public string? HighlightAt(DateTime nowUtc)
        {
            return HighlightedWorkId != null && nowUtc < HighlightUntilUtc ? HighlightedWorkId : null;
        }

        #endregion
    }
}
