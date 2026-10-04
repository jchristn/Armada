namespace Armada.Core.Services
{
    using System;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Builds self-contained Armada MCP server configuration documents for the various agent runtimes.
    /// These are used when launching a captain in an isolated configuration: because strict / scoped
    /// launches deliberately ignore the host user's globally installed MCP servers, the launch must be
    /// handed an explicit configuration that still points the agent at the Armada MCP endpoint. The
    /// schemas here mirror exactly what "armada mcp install" writes for each client so an isolated
    /// captain sees the identical Armada server. Side-effect free (pure string/JSON construction) so it
    /// can be unit tested in isolation.
    /// </summary>
    public static class ArmadaMcpConfigBuilder
    {
        #region Public-Methods

        /// <summary>
        /// The Armada MCP Streamable HTTP URL for a given port (matches the installer's endpoint).
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>The MCP URL, e.g. http://localhost:7891/mcp.</returns>
        public static string GetMcpUrl(int mcpPort)
        {
            return "http://localhost:" + mcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/mcp";
        }

        /// <summary>
        /// Build the keyed "mcpServers" document used by Claude Code, Gemini, and Cursor. The Armada
        /// server is registered under the "armada" key with the modern HTTP transport.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>An indented JSON document string.</returns>
        public static string BuildKeyedMcpServersJson(int mcpPort)
        {
            return BuildKeyedMcpServersJson(mcpPort, null);
        }

        /// <summary>
        /// Build the keyed "mcpServers" document used by Claude Code, Gemini, and Cursor, optionally carrying a session
        /// token as an <c>X-Token</c> header so every tool call reaches the Armada MCP server as that caller (for example
        /// a thread-scoped Ask Armada token).
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="sessionToken">Session token sent as X-Token on every request, or null for none.</param>
        /// <returns>An indented JSON document string.</returns>
        public static string BuildKeyedMcpServersJson(int mcpPort, string? sessionToken)
        {
            JsonObject server = new JsonObject
            {
                ["type"] = "http",
                ["url"] = GetMcpUrl(mcpPort),
            };
            if (!String.IsNullOrEmpty(sessionToken))
            {
                server["headers"] = new JsonObject { ["X-Token"] = sessionToken };
            }

            JsonObject root = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["armada"] = server,
                },
            };
            return root.ToJsonString(_IndentedOptions);
        }

        /// <summary>
        /// Build the "servers" array document used by Mux, which stores each server as an object carrying
        /// its own name plus a separate transport/url/mcpPath (a different shape from the keyed clients).
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>An indented JSON document string.</returns>
        public static string BuildMuxServersJson(int mcpPort)
        {
            JsonObject server = new JsonObject
            {
                ["name"] = "armada",
                ["transport"] = "http",
                ["url"] = "http://localhost:" + mcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["mcpPath"] = "/mcp",
            };
            JsonObject root = new JsonObject
            {
                ["servers"] = new JsonArray(server),
            };
            return root.ToJsonString(_IndentedOptions);
        }

        /// <summary>
        /// Build the TOML fragment used by Codex (~/.codex/config.toml) to register the Armada MCP server
        /// over HTTP. Codex reads its configuration from a CODEX_HOME-scoped config.toml.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>A TOML document string.</returns>
        public static string BuildCodexConfigToml(int mcpPort)
        {
            return BuildCodexConfigToml(mcpPort, null);
        }

        /// <summary>
        /// Build the Codex TOML fragment, optionally carrying a session token as an <c>X-Token</c> HTTP header.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="sessionToken">Session token sent as X-Token on every request, or null for none.</param>
        /// <returns>A TOML document string.</returns>
        public static string BuildCodexConfigToml(int mcpPort, string? sessionToken)
        {
            string toml = "[mcp_servers.armada]" + Environment.NewLine
                + "url = \"" + GetMcpUrl(mcpPort) + "\"" + Environment.NewLine;
            if (!String.IsNullOrEmpty(sessionToken))
            {
                toml += "http_headers = { \"X-Token\" = \"" + sessionToken.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" }" + Environment.NewLine;
            }

            return toml;
        }

        #endregion

        #region Private-Members

        private static readonly System.Text.Json.JsonSerializerOptions _IndentedOptions =
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        #endregion
    }
}
