namespace Armada.Tui.Widgets
{
    using System;

    /// <summary>
    /// One choice in a picker or select field.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class SelectOption<T>
    {
        #region Public-Members

        /// <summary>
        /// Value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Label (already translated, or an entity name).
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Secondary text (an id, a description, a key binding), or empty.
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// Disabled options are shown but cannot be chosen.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Pinned options cannot be unchecked in a multi-select (for example a grid's identifying column).
        /// </summary>
        public bool Pinned { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="label">Label.</param>
        /// <param name="detail">Secondary text.</param>
        public SelectOption(T value, string label, string detail = "")
        {
            Value = value;
            Label = label ?? "";
            Detail = detail ?? "";
        }

        #endregion
    }
}
