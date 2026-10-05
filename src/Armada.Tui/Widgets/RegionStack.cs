namespace Armada.Tui.Widgets
{
    using System;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// Lays out a top-to-bottom stack of rows inside a region host (a screen or a nested panel) and keeps free the row
    /// that the box of each focus region needs (see <see cref="RegionFrames"/>). Boxes share edges: two regions in a
    /// row are separated by one line, a region at the top or bottom of the host uses the host's own edge, and plain
    /// content (titles, notes, footers) sits between the lines. The host's edges must themselves be box lines (true for
    /// a screen inside the main pane and for a panel placed with <see cref="Fill"/>). Not thread-safe.
    /// </summary>
    public sealed class RegionStack
    {
        #region Public-Members

        /// <summary>
        /// Host width.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Host height.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// The next free row.
        /// </summary>
        public int Y
        {
            get { return _Y; }
        }

        /// <summary>
        /// Rows left for content (after the box line a region placed last still needs).
        /// </summary>
        public int Remaining
        {
            get { return Math.Max(0, Height - _Y - (_State == StateAfterRegion ? 1 : 0)); }
        }

        #endregion

        #region Private-Members

        private const int StateAfterLine = 0;
        private const int StateAfterContent = 1;
        private const int StateAfterRegion = 2;

        private int _Y = 0;
        private int _State = StateAfterLine;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a host surface (the row above the first row is the host's top edge).
        /// </summary>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        public RegionStack(int width, int height)
        {
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reserve rows for plain content (not a focus region).
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <returns>The first row.</returns>
        public int Content(int rows)
        {
            if (_State == StateAfterRegion) _Y++;
            int top = _Y;
            _Y += Math.Max(0, rows);
            _State = StateAfterContent;
            return top;
        }

        /// <summary>
        /// Keep one row free between what came before and what comes next (a blank row after plain content, or the box
        /// line a region placed last needs); a region placed next uses that row as its top line.
        /// </summary>
        public void Spacer()
        {
            if (_State == StateAfterLine) return;
            _Y++;
            _State = StateAfterLine;
        }

        /// <summary>
        /// Reserve rows for a focus region, with the line above it when the row before is not one already.
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <returns>The region's rectangle (full width).</returns>
        public Rect Region(int rows)
        {
            if (_State != StateAfterLine) _Y++;
            Rect r = new Rect(0, _Y, Width, Math.Max(0, Math.Min(rows, Height - _Y)));
            _Y += Math.Max(0, rows);
            _State = StateAfterRegion;
            return r;
        }

        /// <summary>
        /// Reserve rows for a widget: a focus region when it is a Tab stop now, plain content otherwise.
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <param name="rows">Rows.</param>
        /// <returns>The rectangle (full width).</returns>
        public Rect Place(IWidget widget, int rows)
        {
            if (IsRegion(widget)) return Region(rows);
            int top = Content(rows);
            return new Rect(0, top, Width, Math.Max(0, Math.Min(rows, Height - top)));
        }

        /// <summary>
        /// The rows a widget would take if placed now with <paramref name="rows"/> rows, including the line it needs
        /// above it.
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <param name="rows">Rows.</param>
        /// <returns>Rows consumed.</returns>
        public int Cost(IWidget widget, int rows)
        {
            bool region = IsRegion(widget);
            int line = region ? (_State != StateAfterLine ? 1 : 0) : (_State == StateAfterRegion ? 1 : 0);
            return line + Math.Max(0, rows);
        }

        /// <summary>
        /// Give a widget every row that is left (down to the host's bottom edge).
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <returns>The rectangle (empty when nothing is left).</returns>
        public Rect Fill(IWidget widget)
        {
            int line = IsRegion(widget) ? (_State != StateAfterLine ? 1 : 0) : (_State == StateAfterRegion ? 1 : 0);
            int rows = Math.Max(0, Height - _Y - line);
            if (rows == 0) return Rect.Empty;
            return Place(widget, rows);
        }

        /// <summary>
        /// True when a widget is a focus region (a Tab stop now).
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <returns>True for a region.</returns>
        public static bool IsRegion(IWidget? widget)
        {
            return FocusScope.IsFocusStop(widget);
        }

        #endregion
    }
}
