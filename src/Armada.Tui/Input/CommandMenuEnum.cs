namespace Armada.Tui.Input
{
    /// <summary>
    /// Menu bar menu a command appears under.
    /// </summary>
    public enum CommandMenuEnum
    {
        /// <summary>
        /// File: server profiles, sign out, quit.
        /// </summary>
        File = 0,

        /// <summary>
        /// Go: every sidebar destination, back and forward.
        /// </summary>
        Go = 1,

        /// <summary>
        /// View: theme, sidebar, Ask dock, language, refresh.
        /// </summary>
        View = 2,

        /// <summary>
        /// Actions: the current screen's row, bulk, and screen actions.
        /// </summary>
        Actions = 3,

        /// <summary>
        /// Ask: conversations, approvals, notifications.
        /// </summary>
        Ask = 4,

        /// <summary>
        /// Help: keys, palette, about, documentation.
        /// </summary>
        Help = 5,

        /// <summary>
        /// Not shown in the menu bar (palette and help only).
        /// </summary>
        None = 6
    }
}
