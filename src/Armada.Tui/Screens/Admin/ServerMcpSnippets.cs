namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The MCP configuration snippets the dashboard's Server page shows for Claude Code, Codex, Gemini, and Cursor
    /// (HTTP pointing at the Admiral's MCP port, and STDIO through <c>armada mcp stdio</c>), byte for byte.
    /// </summary>
    public static class ServerMcpSnippets
    {
        #region Public-Members

        /// <summary>
        /// Client keys in display order.
        /// </summary>
        public static IReadOnlyList<string> Clients { get; } = new List<string> { "claude", "codex", "gemini", "cursor" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display title of a client.
        /// </summary>
        /// <param name="client">Client key.</param>
        /// <returns>Title.</returns>
        public static string Title(string client)
        {
            switch (client)
            {
                case "claude": return "Claude Code";
                case "codex": return "Codex";
                case "gemini": return "Gemini";
                case "cursor": return "Cursor";
                default: return client ?? "";
            }
        }

        /// <summary>
        /// Where the client reads its MCP configuration.
        /// </summary>
        /// <param name="client">Client key.</param>
        /// <returns>Location text.</returns>
        public static string Location(string client)
        {
            switch (client)
            {
                case "claude": return "~/.claude.json -> mcpServers.armada";
                case "codex": return "~/.codex/config.json -> mcpServers.armada";
                case "gemini": return "~/.gemini/settings.json -> mcpServers.armada";
                case "cursor": return ".cursor/mcp.json -> mcpServers.armada";
                default: return "";
            }
        }

        /// <summary>
        /// MCP JSON-RPC URL for a port.
        /// </summary>
        /// <param name="mcpPort">MCP port (7891 when not positive).</param>
        /// <returns>URL.</returns>
        public static string RpcUrl(int mcpPort)
        {
            return "http://localhost:" + (mcpPort > 0 ? mcpPort : 7891) + "/rpc";
        }

        /// <summary>
        /// HTTP configuration snippet.
        /// </summary>
        /// <param name="client">Client key.</param>
        /// <param name="mcpPort">MCP port.</param>
        /// <returns>Snippet.</returns>
        public static string Http(string client, int mcpPort)
        {
            string url = RpcUrl(mcpPort);
            switch (client)
            {
                case "gemini":
                    return Wrap("      \"httpUrl\": \"" + url + "\"");
                case "cursor":
                    return Wrap("      \"url\": \"" + url + "\"");
                default:
                    return Wrap("      \"type\": \"http\",\n      \"url\": \"" + url + "\"");
            }
        }

        /// <summary>
        /// STDIO configuration snippet (a command for Claude Code).
        /// </summary>
        /// <param name="client">Client key.</param>
        /// <returns>Snippet.</returns>
        public static string Stdio(string client)
        {
            string args = "      \"args\": [\n        \"mcp\",\n        \"stdio\"\n      ]";
            switch (client)
            {
                case "claude":
                    return "claude mcp add --scope user armada -- armada mcp stdio";
                case "cursor":
                    return Wrap("      \"command\": \"armada\",\n" + args);
                default:
                    return Wrap("      \"type\": \"stdio\",\n      \"command\": \"armada\",\n" + args);
            }
        }

        #endregion

        #region Private-Methods

        private static string Wrap(string body)
        {
            return "{\n  \"mcpServers\": {\n    \"armada\": {\n" + body + "\n    }\n  }\n}";
        }

        #endregion
    }
}
