namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Avalonia.Controls;
    using Avalonia.Input;

    /// <summary>
    /// Builds the Harbor menus from one definition: the macOS application menu, the window menu (the macOS menu bar,
    /// or the in-window menu bar on Windows and Linux), and the tray menu. A native menu item can belong to only one
    /// menu, so each call builds fresh items; the builder remembers them all so <see cref="Refresh"/> can update
    /// enabled state and the tray status line everywhere at once.
    /// </summary>
    public class HarborMenuBuilder
    {
        #region Private-Members

        private readonly IHarborMenuHost _Host;
        private readonly bool _IsMacOS;
        private readonly List<NativeMenuItem> _CommandItems = new List<NativeMenuItem>();
        private readonly Dictionary<NativeMenuItem, HarborMenuCommandEnum> _Commands = new Dictionary<NativeMenuItem, HarborMenuCommandEnum>();
        private readonly List<NativeMenuItem> _StatusItems = new List<NativeMenuItem>();
        private bool _BuildingTray = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="host">Carries out the commands.</param>
        public HarborMenuBuilder(IHarborMenuHost host)
        {
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            _IsMacOS = OperatingSystem.IsMacOS();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The macOS application menu (the bold "Armada Harbor" menu). Avalonia appends the standard Services, Hide,
        /// and Quit items after these.
        /// </summary>
        /// <returns>The menu.</returns>
        public NativeMenu BuildApplicationMenu()
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Item("About Armada Harbor", HarborMenuCommandEnum.About));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Settings...", HarborMenuCommandEnum.Settings, Key.OemComma));
            return menu;
        }

        /// <summary>
        /// The window's menu bar: Harbor, Armada, Logs, (Window on macOS), Help. On Windows and Linux, where there is
        /// no application menu, Settings and Quit go in the Harbor menu and About goes in Help.
        /// </summary>
        /// <returns>The menu.</returns>
        public NativeMenu BuildWindowMenu()
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Submenu("Harbor", BuildHarborMenu(!_IsMacOS)));
            menu.Items.Add(Submenu("Armada", BuildArmadaMenu()));
            menu.Items.Add(Submenu("Logs", BuildLogsMenu()));
            if (_IsMacOS) menu.Items.Add(Submenu("Window", BuildWindowControlMenu()));
            menu.Items.Add(Submenu("Help", BuildHelpMenu(!_IsMacOS)));
            return menu;
        }

        /// <summary>
        /// The tray (menu bar extra) menu: link status, the window, the Harbor, Armada, and Logs submenus, About, and
        /// Quit.
        /// </summary>
        /// <returns>The menu.</returns>
        public NativeMenu BuildTrayMenu()
        {
            NativeMenu menu = new NativeMenu();
            _BuildingTray = true;

            NativeMenuItem status = new NativeMenuItem { Header = _Host.StatusLine, IsEnabled = false };
            _StatusItems.Add(status);
            menu.Items.Add(status);
            menu.Items.Add(new NativeMenuItemSeparator());

            menu.Items.Add(Item("Open Armada Harbor", HarborMenuCommandEnum.ShowWindow));
            menu.Items.Add(Submenu("Harbor", BuildHarborMenu(false)));
            menu.Items.Add(Submenu("Armada", BuildArmadaMenu()));
            menu.Items.Add(Submenu("Logs", BuildLogsMenu()));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("About Armada Harbor", HarborMenuCommandEnum.About));
            menu.Items.Add(Item("Quit Armada Harbor", HarborMenuCommandEnum.Quit));
            _BuildingTray = false;
            return menu;
        }

        /// <summary>
        /// Stop tracking a menu built earlier (its window closed), so <see cref="Refresh"/> no longer updates it.
        /// </summary>
        /// <param name="menu">Menu returned by one of the Build methods.</param>
        public void Release(NativeMenu? menu)
        {
            if (menu == null) return;
            foreach (NativeMenuItemBase entry in menu.Items)
            {
                if (entry is not NativeMenuItem item) continue;
                _Commands.Remove(item);
                _CommandItems.Remove(item);
                _StatusItems.Remove(item);
                Release(item.Menu);
            }
        }

        /// <summary>
        /// Re-read enabled state and the status line from the host and apply them to every menu built so far.
        /// </summary>
        public void Refresh()
        {
            foreach (NativeMenuItem item in _CommandItems)
            {
                HarborMenuCommandEnum command = _Commands[item];
                bool enabled = _Host.CanExecute(command);
                if (item.IsEnabled != enabled) item.IsEnabled = enabled;
                string? tip = enabled ? null : _Host.DisabledReason(command);
                if (!String.Equals(item.ToolTip, tip, StringComparison.Ordinal)) item.ToolTip = tip;
            }

            string status = _Host.StatusLine;
            foreach (NativeMenuItem item in _StatusItems)
            {
                if (!String.Equals(item.Header, status, StringComparison.Ordinal)) item.Header = status;
            }
        }

        #endregion

        #region Private-Methods

        private NativeMenu BuildHarborMenu(bool includeAppItems)
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Item("Connect", HarborMenuCommandEnum.Connect));
            menu.Items.Add(Item("Disconnect", HarborMenuCommandEnum.Disconnect));
            menu.Items.Add(Item("Reconnect", HarborMenuCommandEnum.Reconnect, Key.R));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Harbor Settings...", HarborMenuCommandEnum.HarborSettings));
            if (includeAppItems) menu.Items.Add(Item("Settings...", HarborMenuCommandEnum.Settings, Key.OemComma));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Copy Harbor ID", HarborMenuCommandEnum.CopyHarborId));
            menu.Items.Add(Item("Copy MCP URL", HarborMenuCommandEnum.CopyMcpUrl));
            menu.Items.Add(Item("Open Harbor Folder", HarborMenuCommandEnum.OpenHarborFolder));
            if (includeAppItems)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(Item("Quit", HarborMenuCommandEnum.Quit, Key.Q));
            }

            return menu;
        }

        private NativeMenu BuildArmadaMenu()
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Item("Status...", HarborMenuCommandEnum.Status));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Armada Settings...", HarborMenuCommandEnum.ArmadaSettings));
            menu.Items.Add(Item("TUI Settings...", HarborMenuCommandEnum.TuiSettings));
            menu.Items.Add(Item("Backups...", HarborMenuCommandEnum.Backups));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Open Data Folder", HarborMenuCommandEnum.OpenDataFolder));
            menu.Items.Add(Item("Open Dashboard", HarborMenuCommandEnum.OpenDashboard, Key.D));
            return menu;
        }

        private NativeMenu BuildLogsMenu()
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Item("Admiral Log (Today)", HarborMenuCommandEnum.OpenAdmiralLog, Key.L));
            menu.Items.Add(Item("Harbor Log", HarborMenuCommandEnum.OpenHarborLog));
            menu.Items.Add(Item("Mission Log...", HarborMenuCommandEnum.MissionLog));
            menu.Items.Add(Item("Log Browser...", HarborMenuCommandEnum.LogBrowser));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Open Logs Folder", HarborMenuCommandEnum.OpenLogsFolder));
            return menu;
        }

        private NativeMenu BuildWindowControlMenu()
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Item("Minimize", HarborMenuCommandEnum.MinimizeWindow, Key.M));
            menu.Items.Add(Item("Close", HarborMenuCommandEnum.CloseWindow, Key.W));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(Item("Armada Harbor", HarborMenuCommandEnum.ShowWindow));
            return menu;
        }

        private NativeMenu BuildHelpMenu(bool includeAbout)
        {
            NativeMenu menu = new NativeMenu();
            menu.Items.Add(Item("Armada Documentation", HarborMenuCommandEnum.Documentation));
            menu.Items.Add(Item("Copy Diagnostics", HarborMenuCommandEnum.CopyDiagnostics));
            if (includeAbout)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(Item("About Armada Harbor", HarborMenuCommandEnum.About));
            }

            return menu;
        }

        private NativeMenuItem Item(string header, HarborMenuCommandEnum command, Key? key = null)
        {
            NativeMenuItem item = new NativeMenuItem { Header = header };
            if (key.HasValue && !_BuildingTray)
            {
                // Command on macOS, Control elsewhere. Tray items get none: the shortcuts work only while a Harbor
                // window is focused, so showing them in the tray would mislead.
                item.Gesture = new KeyGesture(key.Value, _IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control);
            }

            item.Click += (sender, args) =>
            {
                if (_Host.CanExecute(command)) _Host.Execute(command);
            };

            _CommandItems.Add(item);
            _Commands[item] = command;
            item.IsEnabled = _Host.CanExecute(command);
            if (!item.IsEnabled) item.ToolTip = _Host.DisabledReason(command);
            return item;
        }

        private static NativeMenuItem Submenu(string header, NativeMenu menu)
        {
            return new NativeMenuItem { Header = header, Menu = menu };
        }

        #endregion
    }
}
