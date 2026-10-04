namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A collapsible sidebar section (OPERATIONS, DELIVERY, BUILD, CONFIGURATION, ACTIVITY, SYSTEM).
    /// </summary>
    public class NavSection
    {
        #region Public-Members

        /// <summary>
        /// Stable key (also the collapse-state key), for example <c>operations</c>.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English label (catalog key), for example OPERATIONS.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Path prefixes that belong to the section. Never null.
        /// </summary>
        public IReadOnlyList<string> Matchers { get; }

        /// <summary>
        /// Items. Never null.
        /// </summary>
        public IReadOnlyList<NavItem> Items { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">Label.</param>
        /// <param name="matchers">Path prefixes.</param>
        /// <param name="items">Items.</param>
        public NavSection(string key, string label, IEnumerable<string> matchers, IEnumerable<NavItem> items)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Matchers = (matchers ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
            Items = (items ?? Enumerable.Empty<NavItem>()).ToList().AsReadOnly();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when a path belongs to this section.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True when a matcher prefixes the path.</returns>
        public bool Matches(string? path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            return Matchers.Any(m => path!.Equals(m, StringComparison.OrdinalIgnoreCase) || path.StartsWith(m + "/", StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
