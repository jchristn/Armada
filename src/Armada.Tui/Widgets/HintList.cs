namespace Armada.Tui.Widgets
{
    using System.Collections.Generic;
    using TUIKit.Input;

    /// <summary>
    /// A list of TUIKit <see cref="KeyHint"/> values with a chaining <see cref="Add(string, string)"/>, for building
    /// status bar hints inline (<c>HintList.Of("Esc", "Cancel").Add("Enter", "Save")</c>). Not thread-safe.
    /// </summary>
    public class HintList : List<KeyHint>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty list.
        /// </summary>
        public HintList()
        {
        }

        /// <summary>
        /// A list that starts with one hint.
        /// </summary>
        /// <param name="key">Key label, for example <c>Esc</c>.</param>
        /// <param name="description">English description.</param>
        /// <returns>List.</returns>
        public static HintList Of(string key, string description)
        {
            return new HintList().Add(key, description);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append a hint.
        /// </summary>
        /// <param name="key">Key label, for example <c>Ctrl+S</c>. Must not be null or empty.</param>
        /// <param name="description">English description.</param>
        /// <returns>This list.</returns>
        public HintList Add(string key, string description)
        {
            Add(new KeyHint(key, description ?? ""));
            return this;
        }

        #endregion
    }
}
