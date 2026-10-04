namespace Armada.Tui.Routing
{
    using System;

    /// <summary>
    /// One sidebar destination (the dashboard's <c>navConfig.tsx</c> items, same labels).
    /// </summary>
    public class NavItem
    {
        #region Public-Members

        /// <summary>
        /// Target path.
        /// </summary>
        public string To { get; }

        /// <summary>
        /// English label (catalog key).
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// English tooltip, shown in the status bar when the item is focused.
        /// </summary>
        public string Tooltip { get; }

        /// <summary>
        /// Single ASCII glyph shown when the sidebar is collapsed to icons.
        /// </summary>
        public string Icon { get; }

        /// <summary>
        /// Go-to chord suffix (the key after <c>g</c>), or null.
        /// </summary>
        public string? GoKey { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="to">Target path.</param>
        /// <param name="label">English label.</param>
        /// <param name="tooltip">English tooltip.</param>
        /// <param name="icon">ASCII glyph.</param>
        /// <param name="goKey">Go-to key, or null.</param>
        public NavItem(string to, string label, string tooltip, string icon, string? goKey = null)
        {
            To = to ?? throw new ArgumentNullException(nameof(to));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Tooltip = tooltip ?? "";
            Icon = icon ?? "*";
            GoKey = goKey;
        }

        #endregion
    }
}
