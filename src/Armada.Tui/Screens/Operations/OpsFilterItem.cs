namespace Armada.Tui.Screens.Operations
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// One labeled field of an <see cref="OpsFilterBar"/>.
    /// </summary>
    public class OpsFilterItem
    {
        #region Public-Members

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Field widget.
        /// </summary>
        public IWidget Widget { get; }

        /// <summary>
        /// Field width in cells.
        /// </summary>
        public int Width { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="widget">Widget.</param>
        /// <param name="width">Width.</param>
        public OpsFilterItem(string label, IWidget widget, int width)
        {
            Label = label ?? "";
            Widget = widget ?? throw new ArgumentNullException(nameof(widget));
            Width = width;
        }

        #endregion
    }
}
