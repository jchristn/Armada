namespace Armada.Tui.Shell
{
    using System;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Computes the shell's rectangles for a terminal size (W1.3): header, menu bar, sidebar, main, Ask dock, and status
    /// bar, with the breakpoints from the plan (sidebar collapses to icons below 110 columns and hides below 90; 80x24
    /// minimum). The sidebar, main, and dock rectangles include a one-cell focus border (see <see cref="FocusFrame"/>)
    /// that is reserved whether or not the pane has focus; the <c>Inner</c> rectangles are what the panes draw in.
    /// Immutable.
    /// </summary>
    public class ShellLayout
    {
        #region Public-Members

        /// <summary>
        /// Minimum supported size.
        /// </summary>
        public static Size MinimumSize { get; } = new Size(80, 24);

        /// <summary>
        /// Breakpoint.
        /// </summary>
        public LayoutModeEnum Mode { get; }

        /// <summary>
        /// Header rows.
        /// </summary>
        public Rect Header { get; }

        /// <summary>
        /// Menu bar row (its drop-downs draw below it).
        /// </summary>
        public Rect MenuBar { get; }

        /// <summary>
        /// Sidebar including its focus border (empty when hidden).
        /// </summary>
        public Rect Sidebar { get; }

        /// <summary>
        /// Main region including its focus border.
        /// </summary>
        public Rect Main { get; }

        /// <summary>
        /// Ask dock including its focus border (empty when hidden).
        /// </summary>
        public Rect Dock { get; }

        /// <summary>
        /// Sidebar content inside the focus border (empty when hidden).
        /// </summary>
        public Rect SidebarInner
        {
            get { return FocusFrame.Inner(Sidebar); }
        }

        /// <summary>
        /// Main content inside the focus border.
        /// </summary>
        public Rect MainInner
        {
            get { return FocusFrame.Inner(Main); }
        }

        /// <summary>
        /// Dock content inside the focus border (empty when hidden).
        /// </summary>
        public Rect DockInner
        {
            get { return FocusFrame.Inner(Dock); }
        }

        /// <summary>
        /// Status bar.
        /// </summary>
        public Rect StatusBar { get; }

        /// <summary>
        /// True when the sidebar shows icons only.
        /// </summary>
        public bool CompactSidebar { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Compute the layout.
        /// </summary>
        /// <param name="size">Terminal size.</param>
        /// <param name="headerRows">Header rows (1, or 2 with the proxy strip).</param>
        /// <param name="sidebarPreferred">User wants the sidebar (Ctrl+B).</param>
        /// <param name="dockVisible">Ask dock shown.</param>
        /// <param name="dockHeight">Dock content height (the border adds two rows).</param>
        /// <param name="narrowOverlay">Show the full sidebar even below 90 columns (Ctrl+B in narrow terminals).</param>
        public ShellLayout(Size size, int headerRows, bool sidebarPreferred, bool dockVisible, int dockHeight, bool narrowOverlay = false)
        {
            int w = size.Width;
            int h = size.Height;
            if (w < MinimumSize.Width || h < MinimumSize.Height)
            {
                Mode = LayoutModeEnum.TooSmall;
                Header = MenuBar = Sidebar = Main = Dock = StatusBar = Rect.Empty;
                return;
            }

            Mode = w >= 110 ? LayoutModeEnum.Wide : w >= 90 ? LayoutModeEnum.Compact : LayoutModeEnum.Narrow;
            int hr = Math.Clamp(headerRows, 1, 2);
            Header = new Rect(0, 0, w, hr);
            MenuBar = new Rect(0, hr, w, 1);
            StatusBar = new Rect(0, h - 1, w, 1);
            int bodyTop = hr + 1;
            int bodyHeight = Math.Max(1, h - bodyTop - 1);

            int sidebarWidth = 0;
            CompactSidebar = false;
            if (sidebarPreferred)
            {
                if (Mode == LayoutModeEnum.Wide) sidebarWidth = 22 + 2;
                else if (Mode == LayoutModeEnum.Compact)
                {
                    sidebarWidth = 4 + 2;
                    CompactSidebar = true;
                }
            }

            if (Mode == LayoutModeEnum.Narrow && narrowOverlay) sidebarWidth = 22 + 2;

            Sidebar = sidebarWidth > 0 ? new Rect(0, bodyTop, sidebarWidth, bodyHeight) : Rect.Empty;
            int mainLeft = sidebarWidth;
            int mainWidth = w - mainLeft;
            int dh = dockVisible ? Math.Clamp(dockHeight + 2, 6, Math.Max(6, bodyHeight / 2)) : 0;
            Dock = dh > 0 ? new Rect(mainLeft, bodyTop + bodyHeight - dh, mainWidth, dh) : Rect.Empty;
            Main = new Rect(mainLeft, bodyTop, mainWidth, bodyHeight - dh);
        }

        #endregion
    }
}
