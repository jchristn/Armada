namespace Armada.Tui.Screens
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Routing;

    /// <summary>
    /// Builds the screen for a route. Implemented screens register a builder by screen name; every other route gets a
    /// <see cref="PlaceholderScreen"/> (and hubs a <see cref="HubScreen"/> of placeholders). Later waves add builders.
    /// </summary>
    public class ScreenFactory
    {
        #region Private-Members

        private readonly Dictionary<string, Func<RouteMatch, TuiContext, ScreenBase>> _Builders = new Dictionary<string, Func<RouteMatch, TuiContext, ScreenBase>>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a builder for a screen name.
        /// </summary>
        /// <param name="screenName">Screen name (from the route table).</param>
        /// <param name="builder">Builder.</param>
        public void Register(string screenName, Func<RouteMatch, TuiContext, ScreenBase> builder)
        {
            _Builders[screenName] = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>
        /// True when a real screen is registered for a name.
        /// </summary>
        /// <param name="screenName">Screen name.</param>
        /// <returns>True when implemented.</returns>
        public bool IsImplemented(string screenName)
        {
            return _Builders.ContainsKey(screenName);
        }

        /// <summary>
        /// Build the screen for a route.
        /// </summary>
        /// <param name="match">Route match.</param>
        /// <param name="context">Context.</param>
        /// <returns>Screen.</returns>
        public ScreenBase Create(RouteMatch match, TuiContext context)
        {
            if (match.Route.Hub != null && match.Tab != null)
            {
                return new HubScreen(match, context, (m, tab) =>
                    _Builders.TryGetValue(tab.ScreenName, out Func<RouteMatch, TuiContext, ScreenBase>? tb)
                        ? tb(m, context)
                        : new PlaceholderScreen(m, context, tab.ScreenName, tab.Workstream, tab.Label));
            }

            if (_Builders.TryGetValue(match.Route.ScreenName, out Func<RouteMatch, TuiContext, ScreenBase>? builder)) return builder(match, context);
            if (ReferenceEquals(match.Route, RouteTable.NotFound))
                return new PlaceholderScreen(match, context, "NotFoundScreen", "-", "No screen at this path");
            return new PlaceholderScreen(match, context);
        }

        #endregion
    }
}
