namespace Armada.Core.Hosting
{
    using System.Collections.Generic;

    /// <summary>
    /// The Harbor menus, defined once: the macOS application menu, the window menu (the macOS menu bar, or the
    /// in-window menu bar on Windows and Linux), and the tray menu. Every entry point opens the same three windows: the
    /// main window, Status (with its Logs tab), and Settings.
    /// </summary>
    public static class HarborMenuLayout
    {
        #region Public-Methods

        /// <summary>
        /// The macOS application menu (the bold "Armada Harbor" menu). The platform appends Services, Hide, and Quit.
        /// </summary>
        /// <returns>Entries.</returns>
        public static List<HarborMenuEntry> ApplicationMenu()
        {
            return new List<HarborMenuEntry>
            {
                HarborMenuEntry.Item("About Armada Harbor", HarborMenuCommandEnum.About),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Settings...", HarborMenuCommandEnum.Settings, HarborMenuKeyEnum.Comma)
            };
        }

        /// <summary>
        /// The window menu bar: Harbor, View, Window (macOS only), and Help. Without an application menu (Windows and
        /// Linux), Settings and Quit go in the Harbor menu and About in Help.
        /// </summary>
        /// <param name="isMacOS">True on macOS.</param>
        /// <returns>Entries (submenus).</returns>
        public static List<HarborMenuEntry> WindowMenu(bool isMacOS)
        {
            List<HarborMenuEntry> harbor = new List<HarborMenuEntry>
            {
                HarborMenuEntry.Item("Connect", HarborMenuCommandEnum.Connect),
                HarborMenuEntry.Item("Disconnect", HarborMenuCommandEnum.Disconnect),
                HarborMenuEntry.Item("Reconnect", HarborMenuCommandEnum.Reconnect, HarborMenuKeyEnum.R),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Copy Harbor ID", HarborMenuCommandEnum.CopyHarborId),
                HarborMenuEntry.Item("Copy MCP URL", HarborMenuCommandEnum.CopyMcpUrl),
                HarborMenuEntry.Item("Open Harbor Folder", HarborMenuCommandEnum.OpenHarborFolder)
            };

            if (!isMacOS)
            {
                harbor.Add(HarborMenuEntry.Separator());
                harbor.Add(HarborMenuEntry.Item("Settings...", HarborMenuCommandEnum.Settings, HarborMenuKeyEnum.Comma));
                harbor.Add(HarborMenuEntry.Separator());
                harbor.Add(HarborMenuEntry.Item("Quit", HarborMenuCommandEnum.Quit, HarborMenuKeyEnum.Q));
            }

            List<HarborMenuEntry> view = new List<HarborMenuEntry>
            {
                HarborMenuEntry.Item("Status", HarborMenuCommandEnum.Status, HarborMenuKeyEnum.I),
                HarborMenuEntry.Item("Logs", HarborMenuCommandEnum.Logs, HarborMenuKeyEnum.L),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Open Dashboard", HarborMenuCommandEnum.OpenDashboard, HarborMenuKeyEnum.D)
            };

            List<HarborMenuEntry> help = new List<HarborMenuEntry>
            {
                HarborMenuEntry.Item("Armada Documentation", HarborMenuCommandEnum.Documentation),
                HarborMenuEntry.Item("Copy Diagnostics", HarborMenuCommandEnum.CopyDiagnostics)
            };

            if (!isMacOS)
            {
                help.Add(HarborMenuEntry.Separator());
                help.Add(HarborMenuEntry.Item("About Armada Harbor", HarborMenuCommandEnum.About));
            }

            List<HarborMenuEntry> menu = new List<HarborMenuEntry>
            {
                HarborMenuEntry.Submenu("Harbor", harbor),
                HarborMenuEntry.Submenu("View", view)
            };

            if (isMacOS)
            {
                menu.Add(HarborMenuEntry.Submenu("Window", new List<HarborMenuEntry>
                {
                    HarborMenuEntry.Item("Minimize", HarborMenuCommandEnum.MinimizeWindow, HarborMenuKeyEnum.M),
                    HarborMenuEntry.Item("Close", HarborMenuCommandEnum.CloseWindow, HarborMenuKeyEnum.W),
                    HarborMenuEntry.Separator(),
                    HarborMenuEntry.Item("Armada Harbor", HarborMenuCommandEnum.ShowWindow)
                }));
            }

            menu.Add(HarborMenuEntry.Submenu("Help", help));
            return menu;
        }

        /// <summary>
        /// The tray (menu bar extra) menu: the link status, the three windows, connect and disconnect, the dashboard,
        /// and Quit. Tray items carry no shortcuts: those work only while a Harbor window is focused.
        /// </summary>
        /// <returns>Entries.</returns>
        public static List<HarborMenuEntry> TrayMenu()
        {
            return new List<HarborMenuEntry>
            {
                HarborMenuEntry.StatusLine(),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Open Armada Harbor", HarborMenuCommandEnum.ShowWindow),
                HarborMenuEntry.Item("Status", HarborMenuCommandEnum.Status),
                HarborMenuEntry.Item("Logs", HarborMenuCommandEnum.Logs),
                HarborMenuEntry.Item("Settings...", HarborMenuCommandEnum.Settings),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Connect", HarborMenuCommandEnum.Connect),
                HarborMenuEntry.Item("Disconnect", HarborMenuCommandEnum.Disconnect),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Open Dashboard", HarborMenuCommandEnum.OpenDashboard),
                HarborMenuEntry.Separator(),
                HarborMenuEntry.Item("Quit Armada Harbor", HarborMenuCommandEnum.Quit)
            };
        }

        #endregion
    }
}
