namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A hub screen: a route whose content is a tab strip (the dashboard's Tabs pages), with the active tab in the
    /// query string (<c>?tab=</c>, or <c>?source=</c> for Activity).
    /// </summary>
    public class HubDefinition
    {
        #region Public-Members

        /// <summary>
        /// Query parameter naming the active tab. Default <c>tab</c>.
        /// </summary>
        public string QueryParam { get; }

        /// <summary>
        /// Tabs in display order. Never null or empty.
        /// </summary>
        public IReadOnlyList<HubTab> Tabs { get; }

        /// <summary>
        /// Default tab key (the first tab).
        /// </summary>
        public string DefaultTab
        {
            get { return Tabs[0].Key; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="queryParam">Query parameter name.</param>
        /// <param name="tabs">Tabs.</param>
        /// <exception cref="ArgumentException">Thrown when no tabs are given.</exception>
        public HubDefinition(string queryParam, params HubTab[] tabs)
        {
            QueryParam = String.IsNullOrEmpty(queryParam) ? "tab" : queryParam;
            if (tabs == null || tabs.Length == 0) throw new ArgumentException("A hub needs at least one tab.", nameof(tabs));
            Tabs = tabs.ToList().AsReadOnly();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a tab by key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>The tab, or null.</returns>
        public HubTab? Find(string? key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            return Tabs.FirstOrDefault(t => String.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
