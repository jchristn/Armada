namespace Armada.Tui.Input
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A command registered once with <see cref="CommandService"/> and surfaced in the menu bar, the command palette,
    /// the help overlay, and key bindings. Screen-scoped commands carry the screen id in <see cref="Scope"/>.
    /// </summary>
    public class ArmadaCommand
    {
        #region Public-Members

        /// <summary>
        /// Stable id, for example <c>go.missions</c> or <c>view.theme.dark</c>.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// English title (catalog key).
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Menu the command appears under.
        /// </summary>
        public CommandMenuEnum Menu { get; set; } = CommandMenuEnum.None;

        /// <summary>
        /// Help overlay group (English), for example Global or Lists. Defaults to the menu name.
        /// </summary>
        public string Group { get; set; } = "";

        /// <summary>
        /// Key bindings. Never null.
        /// </summary>
        public List<KeyGesture> Gestures { get; set; } = new List<KeyGesture>();

        /// <summary>
        /// Handler, run on the UI loop thread.
        /// </summary>
        public Action Handler { get; set; }

        /// <summary>
        /// Enablement predicate, or null for always enabled.
        /// </summary>
        public Func<bool>? IsEnabled { get; set; } = null;

        /// <summary>
        /// Visibility predicate (role gating), or null for always visible.
        /// </summary>
        public Func<bool>? IsVisible { get; set; } = null;

        /// <summary>
        /// Slash aliases for the palette (for example <c>/dispatch</c>). Never null.
        /// </summary>
        public List<string> SlashAliases { get; set; } = new List<string>();

        /// <summary>
        /// Owning screen id for screen-scoped commands, or null for global commands.
        /// </summary>
        public string? Scope { get; set; } = null;

        /// <summary>
        /// Show the command in the palette. Default true.
        /// </summary>
        public bool InPalette { get; set; } = true;

        /// <summary>
        /// True when the command's key binding should be dispatched globally (false for bindings that are only
        /// documented, because a widget handles them, such as grid <c>Space</c>).
        /// </summary>
        public bool Dispatch { get; set; } = true;

        /// <summary>
        /// Current enablement.
        /// </summary>
        public bool Enabled
        {
            get { return IsEnabled == null || IsEnabled(); }
        }

        /// <summary>
        /// Current visibility.
        /// </summary>
        public bool Visible
        {
            get { return IsVisible == null || IsVisible(); }
        }

        /// <summary>
        /// Label of the first binding, or empty.
        /// </summary>
        public string KeyLabel
        {
            get { return Gestures.Count > 0 ? Gestures[0].ToLabel() : ""; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="title">English title.</param>
        /// <param name="menu">Menu.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="gestures">Key bindings (gesture text), optional.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> or <paramref name="handler"/> is null.</exception>
        public ArmadaCommand(string id, string title, CommandMenuEnum menu, Action handler, params string[] gestures)
        {
            Id = String.IsNullOrEmpty(id) ? throw new ArgumentNullException(nameof(id)) : id;
            Title = title ?? id;
            Menu = menu;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Group = menu.ToString();
            if (gestures != null) Gestures = gestures.Where(g => !String.IsNullOrEmpty(g)).Select(g => new KeyGesture(g)).ToList();
        }

        #endregion
    }
}
