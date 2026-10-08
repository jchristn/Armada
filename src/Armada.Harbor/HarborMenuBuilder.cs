namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Hosting;
    using Avalonia.Controls;
    using Avalonia.Input;

    /// <summary>
    /// Builds the native Harbor menus from one definition (<see cref="HarborMenuLayout"/>): the macOS application menu,
    /// the window menu (the macOS menu bar, or the in-window menu bar on Windows and Linux), and the tray menu. A native
    /// menu item can belong to only one menu, so each call builds fresh items; the builder remembers them all so
    /// <see cref="Refresh"/> can update enabled state and the tray status line everywhere at once.
    /// </summary>
    public class HarborMenuBuilder
    {
        #region Private-Members

        private readonly IHarborMenuHost _Host;
        private readonly bool _IsMacOS;
        private readonly List<NativeMenuItem> _CommandItems = new List<NativeMenuItem>();
        private readonly Dictionary<NativeMenuItem, HarborMenuCommandEnum> _Commands = new Dictionary<NativeMenuItem, HarborMenuCommandEnum>();
        private readonly List<NativeMenuItem> _StatusItems = new List<NativeMenuItem>();

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
            return Build(HarborMenuLayout.ApplicationMenu(), false);
        }

        /// <summary>
        /// The window's menu bar (see <see cref="HarborMenuLayout.WindowMenu"/>).
        /// </summary>
        /// <returns>The menu.</returns>
        public NativeMenu BuildWindowMenu()
        {
            return Build(HarborMenuLayout.WindowMenu(_IsMacOS), false);
        }

        /// <summary>
        /// The tray (menu bar extra) menu (see <see cref="HarborMenuLayout.TrayMenu"/>).
        /// </summary>
        /// <returns>The menu.</returns>
        public NativeMenu BuildTrayMenu()
        {
            return Build(HarborMenuLayout.TrayMenu(), true);
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

        private NativeMenu Build(List<HarborMenuEntry> entries, bool tray)
        {
            NativeMenu menu = new NativeMenu();
            foreach (HarborMenuEntry entry in entries)
            {
                if (entry.IsSeparator)
                {
                    menu.Items.Add(new NativeMenuItemSeparator());
                }
                else if (entry.IsStatusLine)
                {
                    NativeMenuItem status = new NativeMenuItem { Header = _Host.StatusLine, IsEnabled = false };
                    _StatusItems.Add(status);
                    menu.Items.Add(status);
                }
                else if (entry.Children != null)
                {
                    menu.Items.Add(new NativeMenuItem { Header = entry.Header, Menu = Build(entry.Children, tray) });
                }
                else if (entry.Command.HasValue)
                {
                    // Tray items get no shortcut: shortcuts work only while a Harbor window is focused.
                    menu.Items.Add(Item(entry.Header, entry.Command.Value, tray ? HarborMenuKeyEnum.None : entry.Key));
                }
            }

            return menu;
        }

        private static Key? ToKey(HarborMenuKeyEnum key)
        {
            switch (key)
            {
                case HarborMenuKeyEnum.Comma: return Key.OemComma;
                case HarborMenuKeyEnum.D: return Key.D;
                case HarborMenuKeyEnum.I: return Key.I;
                case HarborMenuKeyEnum.L: return Key.L;
                case HarborMenuKeyEnum.M: return Key.M;
                case HarborMenuKeyEnum.Q: return Key.Q;
                case HarborMenuKeyEnum.R: return Key.R;
                case HarborMenuKeyEnum.W: return Key.W;
                default: return null;
            }
        }

        private NativeMenuItem Item(string header, HarborMenuCommandEnum command, HarborMenuKeyEnum shortcut)
        {
            NativeMenuItem item = new NativeMenuItem { Header = header };
            Key? key = ToKey(shortcut);
            if (key.HasValue)
            {
                // Command on macOS, Control elsewhere.
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

        #endregion
    }
}
