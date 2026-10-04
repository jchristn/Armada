namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A resolved navigation target: the route, its path parameters, the query, and (for hubs) the active tab.
    /// Immutable.
    /// </summary>
    public class RouteMatch
    {
        #region Public-Members

        /// <summary>
        /// Matched route.
        /// </summary>
        public RouteDefinition Route { get; }

        /// <summary>
        /// Normalized path without the query, for example <c>/missions/msn_1</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Path parameters. Never null.
        /// </summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>
        /// Query parameters. Never null.
        /// </summary>
        public IReadOnlyDictionary<string, string> Query { get; }

        /// <summary>
        /// Active hub tab, or null when the route is not a hub.
        /// </summary>
        public HubTab? Tab { get; }

        /// <summary>
        /// Path plus query, as stored in history (for example <c>/missions?tab=voyages</c>).
        /// </summary>
        public string FullPath { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="path">Path.</param>
        /// <param name="parameters">Path parameters.</param>
        /// <param name="query">Query.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="route"/> is null.</exception>
        public RouteMatch(RouteDefinition route, string path, IDictionary<string, string> parameters, IDictionary<string, string> query)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));
            Path = String.IsNullOrEmpty(path) ? "/" : path;
            Parameters = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            Query = new Dictionary<string, string>(query ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (route.Hub != null)
            {
                Query.TryGetValue(route.Hub.QueryParam, out string? key);
                Tab = route.Hub.Find(key) ?? route.Hub.Find(route.DefaultTab) ?? route.Hub.Tabs[0];
            }

            FullPath = Path + BuildQuery(Query);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A path parameter or null.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Value or null.</returns>
        public string? Param(string name)
        {
            return Parameters.TryGetValue(name, out string? value) ? value : null;
        }

        /// <summary>
        /// Build a query string from pairs (keys and values escaped, sorted for stable history entries).
        /// </summary>
        /// <param name="query">Pairs.</param>
        /// <returns>Query including the leading <c>?</c>, or empty.</returns>
        public static string BuildQuery(IReadOnlyDictionary<string, string> query)
        {
            if (query == null || query.Count == 0) return "";
            return "?" + String.Join("&", query.OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => Uri.EscapeDataString(k.Key) + "=" + Uri.EscapeDataString(k.Value ?? "")));
        }

        #endregion
    }
}
