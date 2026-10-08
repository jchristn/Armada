namespace Armada.Core.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The single list of public surfaces that ship as experimental at 1.0 and are therefore excluded from the
    /// compatibility promise in docs/COMPATIBILITY.md: REST routes, MCP tools, WebSocket endpoints, and settings keys.
    /// The Admiral prefixes the OpenAPI summary of every experimental route and the description of every experimental MCP
    /// tool with <see cref="Marker"/>; the API surface file (docs/api-surface-1.0.json) records the flag, and the contract
    /// test ignores removals and changes of experimental items. Graduating an item means removing it from this list (and
    /// regenerating the surface file) in a minor release.
    /// </summary>
    public static class ExperimentalSurface
    {
        #region Public-Members

        /// <summary>
        /// Prefix added to OpenAPI summaries and MCP tool descriptions of experimental items.
        /// </summary>
        public const string Marker = "[Experimental] ";

        /// <summary>
        /// OpenAPI tag added to experimental routes.
        /// </summary>
        public const string Tag = "Experimental";

        #endregion

        #region Private-Members

        // Harbor split mode (decision D3): the live link transport, the Harbor management surface that only serves it,
        // and the split-mode settings. Server rebuild/rollback: the Harbor-supervised cutover is not live-verified.
        private static readonly List<string> _RoutePrefixes = new List<string>
        {
            "/api/v1/harbors",
            "/api/v1/server/rebuild",
            "/api/v1/server/rollback"
        };

        private static readonly HashSet<string> _Tools = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_harbor",
            "get_harbor_metrics",
            "create_harbor",
            "update_harbor",
            "delete_harbor",
            "set_harbor_enabled"
        };

        // Settings keys use the camelCase JSON names of settings.json; a prefix covers the key and its children.
        private static readonly List<string> _SettingPrefixes = new List<string>
        {
            "harbor",
            "deploymentMode",
            "requireHarborForLaunch",
            "rebuildSupervisorHarborId",
            "rebuildSlotRetentionCount",
            "selfVesselId"
        };

        private static readonly HashSet<string> _WebSocketEndpoints = new HashSet<string>(StringComparer.Ordinal)
        {
            "harbor-link"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a REST route (method and template) is experimental.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="template">Route template, for example /api/v1/harbors/{id}.</param>
        /// <returns>True when experimental.</returns>
        public static bool IsExperimentalRoute(string method, string template)
        {
            if (String.IsNullOrEmpty(template)) return false;
            foreach (string prefix in _RoutePrefixes)
            {
                if (String.Equals(template, prefix, StringComparison.OrdinalIgnoreCase)) return true;
                if (template.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether an MCP tool is experimental.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>True when experimental.</returns>
        public static bool IsExperimentalTool(string toolName)
        {
            if (String.IsNullOrEmpty(toolName)) return false;
            return _Tools.Contains(toolName);
        }

        /// <summary>
        /// Whether a settings key (dotted camelCase path, for example harbor.linkPath) is experimental.
        /// </summary>
        /// <param name="key">Settings key.</param>
        /// <returns>True when experimental.</returns>
        public static bool IsExperimentalSetting(string key)
        {
            if (String.IsNullOrEmpty(key)) return false;
            foreach (string prefix in _SettingPrefixes)
            {
                if (String.Equals(key, prefix, StringComparison.Ordinal)) return true;
                if (key.StartsWith(prefix + ".", StringComparison.Ordinal)) return true;
                if (key.StartsWith(prefix + "[]", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a WebSocket endpoint (by its surface name, for example harbor-link) is experimental.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <returns>True when experimental.</returns>
        public static bool IsExperimentalWebSocketEndpoint(string endpointName)
        {
            if (String.IsNullOrEmpty(endpointName)) return false;
            return _WebSocketEndpoints.Contains(endpointName);
        }

        /// <summary>
        /// Prefix a summary or description with <see cref="Marker"/> unless it already carries it.
        /// </summary>
        /// <param name="text">Summary or description.</param>
        /// <returns>The marked text.</returns>
        public static string Mark(string? text)
        {
            string value = text ?? String.Empty;
            if (value.StartsWith(Marker, StringComparison.Ordinal)) return value;
            return Marker + value;
        }

        #endregion
    }
}
