namespace Armada.Tui.Modals
{
    using System;

    /// <summary>
    /// One command palette candidate: a command, a route (including hub tabs), or an entity jump.
    /// </summary>
    public class PaletteEntry
    {
        #region Public-Members

        /// <summary>
        /// Kind: command, route, or entity.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Title (translated).
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Secondary text (key binding, section, or path).
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// Action run on the UI loop when chosen.
        /// </summary>
        public Action Action { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="title">Title.</param>
        /// <param name="detail">Detail.</param>
        /// <param name="action">Action.</param>
        public PaletteEntry(string kind, string title, string detail, Action action)
        {
            Kind = kind ?? "command";
            Title = title ?? "";
            Detail = detail ?? "";
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        #endregion
    }
}
