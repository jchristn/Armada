namespace Armada.Tui.Widgets
{
    using System.Collections.Generic;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Status bar hints for a filter row (<see cref="FilterBar"/> and the Operations filter bar) by the kind of field
    /// that has focus: a text filter takes typed text, so its first hint is how to get back to the list; a select
    /// opens with <c>Enter</c>; a toggle changes with <c>Space</c>. Stateless and thread-safe.
    /// </summary>
    public static class FilterRowHints
    {
        #region Public-Methods

        /// <summary>
        /// Hints for the focused field of a filter row.
        /// </summary>
        /// <param name="focused">Focused field, or null.</param>
        /// <param name="exitLabel">English description of where <c>Esc</c> goes (for example "Back to the list").</param>
        /// <returns>Hints.</returns>
        public static List<KeyHint> For(IWidget? focused, string exitLabel)
        {
            List<KeyHint> hints = new List<KeyHint>();
            if (focused is ITextEntry entry && entry.AcceptsText)
            {
                hints.Add(new KeyHint("Esc", exitLabel));
                hints.Add(new KeyHint("Tab", "Next filter"));
                return hints;
            }

            if (focused is SelectField<string>) hints.Add(new KeyHint("Enter", "Choose"));
            else if (focused is TriStateField || focused is CheckField) hints.Add(new KeyHint("Space", "Change"));
            hints.Add(new KeyHint("Esc", exitLabel));
            hints.Add(new KeyHint("Tab", "Next filter"));
            return hints;
        }

        #endregion
    }
}
