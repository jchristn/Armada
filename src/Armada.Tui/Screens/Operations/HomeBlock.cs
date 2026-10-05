namespace Armada.Tui.Screens.Operations
{
    using System;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// One laid-out block of the Home page: either drawn text or a child widget, at a virtual row.
    /// </summary>
    public class HomeBlock
    {
        #region Public-Members

        /// <summary>
        /// Virtual top row.
        /// </summary>
        public int Top { get; set; } = 0;

        /// <summary>
        /// Rows.
        /// </summary>
        public int Height { get; set; } = 1;

        /// <summary>
        /// Left column.
        /// </summary>
        public int Left { get; set; } = 0;

        /// <summary>
        /// Right edge (exclusive), or 0 for the full width.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Row to keep visible when the widget has focus (its section heading).
        /// </summary>
        public int FocusTop { get; set; } = 0;

        /// <summary>
        /// Widget, or null.
        /// </summary>
        public IWidget? Widget { get; set; } = null;

        /// <summary>
        /// Drawing callback, or null.
        /// </summary>
        public Action<ISurface>? Draw { get; set; } = null;

        /// <summary>
        /// Right edge of the widget's focus region when it is wider than where the widget draws (a button row that
        /// shares its row with text inside the same box), or 0 for the drawn rectangle.
        /// </summary>
        public int RegionRight { get; set; } = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A drawn block.
        /// </summary>
        /// <param name="top">Top.</param>
        /// <param name="height">Rows.</param>
        /// <param name="draw">Callback.</param>
        /// <returns>Block.</returns>
        public static HomeBlock Drawn(int top, int height, Action<ISurface> draw)
        {
            HomeBlock b = new HomeBlock();
            b.Top = top;
            b.Height = height;
            b.FocusTop = top;
            b.Draw = draw;
            return b;
        }

        /// <summary>
        /// A widget block.
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <param name="top">Top.</param>
        /// <param name="height">Rows.</param>
        /// <param name="left">Left column.</param>
        /// <param name="width">Right edge, or 0 for full width.</param>
        /// <param name="focusTop">Heading row, or -1 for the top.</param>
        /// <returns>Block.</returns>
        public static HomeBlock For(IWidget widget, int top, int height, int left = 0, int width = 0, int focusTop = -1)
        {
            HomeBlock b = new HomeBlock();
            b.Widget = widget;
            b.Top = top;
            b.Height = Math.Max(1, height);
            b.Left = left;
            b.Width = width;
            b.FocusTop = focusTop >= 0 ? focusTop : top;
            return b;
        }

        #endregion
    }
}
