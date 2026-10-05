namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// One KPI or call-to-action tile of an <see cref="OpsTileRow"/>: a label, a value, detail lines, and an optional
    /// action (the dashboard's clickable cards).
    /// </summary>
    public class OpsTile
    {
        #region Public-Members

        /// <summary>
        /// Label (translated).
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Value (shown bold), or empty.
        /// </summary>
        public string Value { get; set; } = "";

        /// <summary>
        /// Value style, or null for the accent style.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? ValueStyle { get; set; } = null;

        /// <summary>
        /// Detail text (translated), shown under the value.
        /// </summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// Action on Enter or click, or null.
        /// </summary>
        public Action? Action { get; set; } = null;

        /// <summary>
        /// Key hint shown in the tile, or null.
        /// </summary>
        public string? KeyHint { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">Label.</param>
        /// <param name="value">Value.</param>
        /// <param name="detail">Detail.</param>
        /// <param name="action">Action, or null.</param>
        public OpsTile(string label, string value, string detail, Action? action)
        {
            Label = label ?? "";
            Value = value ?? "";
            Detail = detail ?? "";
            Action = action;
        }

        #endregion
    }
}
