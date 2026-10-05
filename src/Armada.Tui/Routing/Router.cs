namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Resolves paths against <see cref="RouteTable"/> (following the dashboard's redirects) and keeps back and forward
    /// history. Deep links such as <c>/vessels/health?overall=Fail</c> keep their query. Call on the UI loop thread.
    /// </summary>
    public class Router
    {
        #region Public-Members

        /// <summary>
        /// Current location, or null before the first navigation.
        /// </summary>
        public RouteMatch? Current { get; private set; } = null;

        /// <summary>
        /// True when <see cref="Back"/> can move.
        /// </summary>
        public bool CanGoBack
        {
            get { return _Back.Count > 0; }
        }

        /// <summary>
        /// True when <see cref="Forward"/> can move.
        /// </summary>
        public bool CanGoForward
        {
            get { return _Forward.Count > 0; }
        }

        /// <summary>
        /// Maximum history entries kept in each direction. Default 100; clamped to 1..1000.
        /// </summary>
        public int HistoryLimit
        {
            get { return _HistoryLimit; }
            set { _HistoryLimit = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Raised after the location changes.
        /// </summary>
        public event EventHandler<RouteMatch>? Navigated;

        /// <summary>
        /// Back stack (most recent last), for diagnostics and tests.
        /// </summary>
        public IReadOnlyList<string> BackStack
        {
            get { return _Back.ToList(); }
        }

        #endregion

        #region Private-Members

        private readonly List<string> _Back = new List<string>();
        private readonly List<string> _Forward = new List<string>();
        private int _HistoryLimit = 100;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a path (with optional query) to a route, following redirects (up to 5 hops). Unknown paths resolve
        /// to <see cref="RouteTable.NotFound"/>.
        /// </summary>
        /// <param name="target">Path and query, for example <c>/missions?tab=voyages</c>.</param>
        /// <returns>The match. Never null.</returns>
        public static RouteMatch Resolve(string? target)
        {
            string current = String.IsNullOrWhiteSpace(target) ? "/" : target!.Trim();
            Dictionary<string, string> carriedQuery = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int hop = 0; hop < 5; hop++)
            {
                Split(current, out string path, out Dictionary<string, string> query);
                foreach (KeyValuePair<string, string> kvp in carriedQuery)
                {
                    if (!query.ContainsKey(kvp.Key)) query[kvp.Key] = kvp.Value;
                }

                List<string> segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                RouteDefinition? best = null;
                Dictionary<string, string> bestParams = new Dictionary<string, string>(StringComparer.Ordinal);
                int bestScore = -1;
                Dictionary<string, string> scratch = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (RouteDefinition route in RouteTable.All)
                {
                    int score = route.Match(segments, scratch);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = route;
                        bestParams = new Dictionary<string, string>(scratch, StringComparer.Ordinal);
                    }
                }

                if (best == null) return new RouteMatch(RouteTable.NotFound, path, new Dictionary<string, string>(), query);
                if (best.RedirectTo == null) return new RouteMatch(best, "/" + String.Join("/", segments), bestParams, query);

                carriedQuery = query;
                current = best.RedirectTo;
            }

            return new RouteMatch(RouteTable.NotFound, current, new Dictionary<string, string>(), new Dictionary<string, string>());
        }

        /// <summary>
        /// Navigate to a target, pushing the current location onto the back stack and clearing forward history.
        /// Navigating to the current location is a no-op.
        /// </summary>
        /// <param name="target">Path and query.</param>
        /// <param name="replace">Replace the current entry instead of pushing it.</param>
        /// <returns>The new location.</returns>
        public RouteMatch Navigate(string? target, bool replace = false)
        {
            RouteMatch match = Resolve(target);
            if (Current != null && String.Equals(Current.FullPath, match.FullPath, StringComparison.Ordinal)) return Current;
            if (Current != null && !replace)
            {
                Push(_Back, Current.FullPath);
                _Forward.Clear();
            }

            SetCurrent(match);
            return match;
        }

        /// <summary>
        /// Go back one entry.
        /// </summary>
        /// <returns>True when moved.</returns>
        public bool Back()
        {
            if (_Back.Count == 0) return false;
            string target = _Back[_Back.Count - 1];
            _Back.RemoveAt(_Back.Count - 1);
            if (Current != null) Push(_Forward, Current.FullPath);
            SetCurrent(Resolve(target));
            return true;
        }

        /// <summary>
        /// Go forward one entry.
        /// </summary>
        /// <returns>True when moved.</returns>
        public bool Forward()
        {
            if (_Forward.Count == 0) return false;
            string target = _Forward[_Forward.Count - 1];
            _Forward.RemoveAt(_Forward.Count - 1);
            if (Current != null) Push(_Back, Current.FullPath);
            SetCurrent(Resolve(target));
            return true;
        }

        /// <summary>
        /// Switch the hub tab of the current location (replaces the entry so tab flips do not flood history).
        /// </summary>
        /// <param name="tabKey">Tab key.</param>
        /// <returns>The new location, or null when the current route is not a hub.</returns>
        public RouteMatch? SelectTab(string tabKey)
        {
            if (Current == null || Current.Route.Hub == null) return null;
            Dictionary<string, string> query = new Dictionary<string, string>(Current.Query, StringComparer.Ordinal);
            query[Current.Route.Hub.QueryParam] = tabKey;
            string path = Current.Route.DefaultTab != null ? StripLast(Current.Path) : Current.Path;
            return Navigate(path + RouteMatch.BuildQuery(query), true);
        }

        /// <summary>
        /// Replace the current location without raising <see cref="Navigated"/>, for screens that mirror their own
        /// state (filters, sort, page) into the query so Back, Forward, and deep links restore it. The screen is not
        /// rebuilt and history is not pushed.
        /// </summary>
        /// <param name="target">Path and query.</param>
        /// <returns>The new location.</returns>
        public RouteMatch ReplaceQuietly(string? target)
        {
            RouteMatch match = Resolve(target);
            Current = match;
            return match;
        }

        /// <summary>
        /// Clear history and location (sign-out).
        /// </summary>
        public void Reset()
        {
            _Back.Clear();
            _Forward.Clear();
            Current = null;
        }

        #endregion

        #region Private-Methods

        private void SetCurrent(RouteMatch match)
        {
            Current = match;
            EventHandler<RouteMatch>? handler = Navigated;
            if (handler != null) handler(this, match);
        }

        private void Push(List<string> stack, string value)
        {
            stack.Add(value);
            while (stack.Count > _HistoryLimit) stack.RemoveAt(0);
        }

        private static string StripLast(string path)
        {
            int idx = path.LastIndexOf('/');
            return idx > 0 ? path.Substring(0, idx) : path;
        }

        private static void Split(string target, out string path, out Dictionary<string, string> query)
        {
            query = new Dictionary<string, string>(StringComparer.Ordinal);
            int q = target.IndexOf('?');
            path = q >= 0 ? target.Substring(0, q) : target;
            if (!path.StartsWith("/")) path = "/" + path;
            if (path.Length > 1) path = path.TrimEnd('/');
            if (q < 0) return;
            foreach (string pair in target.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(eq >= 0 ? pair.Substring(0, eq) : pair);
                string value = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')) : "";
                if (key.Length > 0) query[key] = value;
            }
        }

        #endregion
    }
}
