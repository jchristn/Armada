namespace Armada.Tui.Shell
{
    /// <summary>
    /// Responsive breakpoint of the shell.
    /// </summary>
    public enum LayoutModeEnum
    {
        /// <summary>
        /// 110 columns or more: full sidebar.
        /// </summary>
        Wide = 0,

        /// <summary>
        /// 90 to 109 columns: icon sidebar.
        /// </summary>
        Compact = 1,

        /// <summary>
        /// Below 90 columns: sidebar hidden (Ctrl+B shows it).
        /// </summary>
        Narrow = 2,

        /// <summary>
        /// Below 80x24: the terminal-too-small screen.
        /// </summary>
        TooSmall = 3
    }
}
