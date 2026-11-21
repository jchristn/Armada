namespace Armada.Core.ApiSurface
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The single list of public surfaces that were retired because they returned an entire collection in one response
    /// (DEPRECATION.md, Removed). None of them is registered any more. A request for a retired REST route is answered
    /// with HTTP 410 Gone and a message naming the paged replacement; retired MCP tools and WebSocket commands are simply
    /// not registered.
    /// </summary>
    public static class RetiredSurface
    {
        #region Public-Members

        /// <summary>
        /// Release that retired the items.
        /// </summary>
        public const string RetiredIn = "v1.3.0";

        #endregion

        #region Private-Members

        // "METHOD template" to its paged replacement.
        private static readonly Dictionary<string, string> _Routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET /api/v1/harbors"] = "POST /api/v1/harbors/enumerate",
            ["GET /api/v1/model-endpoints"] = "POST /api/v1/model-endpoints/enumerate",
            ["GET /api/v1/planning-sessions"] = "POST /api/v1/planning-sessions/enumerate",
            ["GET /api/v1/objectives/{id}/refinement-sessions"] = "POST /api/v1/objectives/{id}/refinement-sessions/enumerate",
            ["GET /api/v1/backlog/{id}/refinement-sessions"] = "POST /api/v1/backlog/{id}/refinement-sessions/enumerate",
            ["GET /api/v1/inbox"] = "POST /api/v1/inbox/enumerate",
            ["GET /api/v1/cli-permissions/rules"] = "POST /api/v1/cli-permissions/rules/enumerate",
            ["GET /api/v1/cli-permissions/requests"] = "POST /api/v1/cli-permissions/requests/enumerate",
            ["GET /api/v1/push/devices"] = "POST /api/v1/push/devices/enumerate",
            ["GET /api/v1/releases/{id}/github/pull-requests"] = "POST /api/v1/releases/{id}/github/pull-requests/enumerate",
            ["GET /api/v1/vessels/{id}/branches"] = "POST /api/v1/vessels/{id}/branches/enumerate",
            ["GET /api/v1/signals/recipient/{captainId}"] = "POST /api/v1/signals/enumerate (toCaptainId, unreadOnly)",
            ["GET /api/v1/signals/recent"] = "POST /api/v1/signals/enumerate",
        };

        // MCP tool name to its paged replacement.
        private static readonly Dictionary<string, string> _Tools = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inbox"] = "enumerate with entityType inbox",
            ["list_cli_permission_rules"] = "enumerate with entityType cli_permission_rules",
            ["list_cli_permission_requests"] = "enumerate with entityType cli_permission_requests",
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every retired REST route, as "METHOD template".
        /// </summary>
        /// <returns>The routes.</returns>
        public static IReadOnlyCollection<string> Routes()
        {
            return _Routes.Keys;
        }

        /// <summary>
        /// Every retired MCP tool.
        /// </summary>
        /// <returns>The tool names.</returns>
        public static IReadOnlyCollection<string> Tools()
        {
            return _Tools.Keys;
        }

        /// <summary>
        /// The replacement for a retired REST route template, or null when the route is not retired.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="template">Route template, for example /api/v1/inbox.</param>
        /// <returns>The replacement, or null.</returns>
        public static string? RouteReplacement(string method, string template)
        {
            if (String.IsNullOrEmpty(method) || String.IsNullOrEmpty(template)) return null;
            return _Routes.TryGetValue(method.ToUpperInvariant() + " " + template, out string? replacement) ? replacement : null;
        }

        /// <summary>
        /// The replacement for a retired MCP tool, or null when the tool is not retired.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>The replacement, or null.</returns>
        public static string? ToolReplacement(string toolName)
        {
            if (String.IsNullOrEmpty(toolName)) return null;
            return _Tools.TryGetValue(toolName, out string? replacement) ? replacement : null;
        }

        /// <summary>
        /// Match a concrete request against the retired routes. A <c>{name}</c> template segment matches any one
        /// non-empty path segment; a trailing slash is ignored.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path without the querystring, for example /api/v1/vessels/vsl_1/branches.</param>
        /// <returns>The retired "METHOD template" and its replacement, or null when the request is not for a retired route.</returns>
        public static RetiredRouteMatch? Match(string? method, string? path)
        {
            if (String.IsNullOrEmpty(method) || String.IsNullOrEmpty(path)) return null;
            string[] requested = Segments(path);
            foreach (KeyValuePair<string, string> entry in _Routes)
            {
                int space = entry.Key.IndexOf(' ');
                if (!String.Equals(entry.Key.Substring(0, space), method, StringComparison.OrdinalIgnoreCase)) continue;
                string[] template = Segments(entry.Key.Substring(space + 1));
                if (template.Length != requested.Length) continue;
                bool matched = true;
                for (int i = 0; i < template.Length && matched; i++)
                {
                    bool parameter = template[i].StartsWith("{", StringComparison.Ordinal) && template[i].EndsWith("}", StringComparison.Ordinal);
                    if (!parameter && !String.Equals(template[i], requested[i], StringComparison.OrdinalIgnoreCase)) matched = false;
                }

                if (matched) return new RetiredRouteMatch(entry.Key, entry.Value);
            }

            return null;
        }

        /// <summary>
        /// The message a retired REST route answers with.
        /// </summary>
        /// <param name="match">Matched retired route.</param>
        /// <returns>The message.</returns>
        /// <exception cref="ArgumentNullException">match is null.</exception>
        public static string GoneMessage(RetiredRouteMatch match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return match.Route + " was retired because it returned an entire collection; use " + match.Replacement + ", which returns one page with the total.";
        }

        #endregion

        #region Private-Methods

        private static string[] Segments(string path)
        {
            return path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        }

        #endregion
    }
}
