namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One entry of a Harbor menu as <see cref="HarborMenuLayout"/> defines it: a command, a submenu, a separator, or
    /// the tray's link status line. Harbor turns these into native menu items.
    /// </summary>
    public class HarborMenuEntry
    {
        #region Public-Members

        /// <summary>
        /// Text of the item or submenu; empty for a separator or the status line.
        /// </summary>
        public string Header { get; set; } = String.Empty;

        /// <summary>
        /// Command the item runs; null for a submenu, separator, or status line.
        /// </summary>
        public HarborMenuCommandEnum? Command { get; set; } = null;

        /// <summary>
        /// Shortcut key, pressed with Command on macOS and Control elsewhere.
        /// </summary>
        public HarborMenuKeyEnum Key { get; set; } = HarborMenuKeyEnum.None;

        /// <summary>
        /// Items of a submenu; null for anything else.
        /// </summary>
        public List<HarborMenuEntry>? Children { get; set; } = null;

        /// <summary>
        /// True for a separator.
        /// </summary>
        public bool IsSeparator { get; set; } = false;

        /// <summary>
        /// True for the tray's disabled link status line, whose text the host supplies.
        /// </summary>
        public bool IsStatusLine { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborMenuEntry()
        {
        }

        /// <summary>
        /// A command item.
        /// </summary>
        /// <param name="header">Text.</param>
        /// <param name="command">Command.</param>
        /// <param name="key">Shortcut key.</param>
        /// <returns>The entry.</returns>
        public static HarborMenuEntry Item(string header, HarborMenuCommandEnum command, HarborMenuKeyEnum key = HarborMenuKeyEnum.None)
        {
            if (String.IsNullOrWhiteSpace(header)) throw new ArgumentNullException(nameof(header));
            return new HarborMenuEntry { Header = header, Command = command, Key = key };
        }

        /// <summary>
        /// A submenu.
        /// </summary>
        /// <param name="header">Text.</param>
        /// <param name="children">Items.</param>
        /// <returns>The entry.</returns>
        public static HarborMenuEntry Submenu(string header, List<HarborMenuEntry> children)
        {
            if (String.IsNullOrWhiteSpace(header)) throw new ArgumentNullException(nameof(header));
            return new HarborMenuEntry { Header = header, Children = children ?? throw new ArgumentNullException(nameof(children)) };
        }

        /// <summary>
        /// A separator.
        /// </summary>
        /// <returns>The entry.</returns>
        public static HarborMenuEntry Separator()
        {
            return new HarborMenuEntry { IsSeparator = true };
        }

        /// <summary>
        /// The tray's link status line.
        /// </summary>
        /// <returns>The entry.</returns>
        public static HarborMenuEntry StatusLine()
        {
            return new HarborMenuEntry { IsStatusLine = true };
        }

        #endregion
    }
}
