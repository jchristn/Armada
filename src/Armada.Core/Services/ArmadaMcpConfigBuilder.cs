namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
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
        #region Public-Members

        /// <summary>
        /// The client host used when none is supplied, and for listeners bound to every interface.
        /// </summary>
        public const string DefaultHost = "localhost";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The host an MCP client must use to reach a listener bound with the given REST hostname. The Admiral's MCP
        /// listener only answers requests whose Host matches the name it was bound with, so a listener bound to
        /// 127.0.0.1 must be addressed as 127.0.0.1 (localhost gets HTTP 404) and vice versa. Wildcard bindings
        /// (empty, "*", "+", "0.0.0.0", "::") accept any host and map to "localhost". An IPv6 literal is bracketed.
        /// </summary>
        /// <param name="restHostname">The configured REST hostname (Settings.Rest.Hostname), or null.</param>
        /// <returns>The host to place in generated client URLs.</returns>
        public static string ClientHostFor(string? restHostname)
        {
            if (String.IsNullOrWhiteSpace(restHostname)) return DefaultHost;
            string host = restHostname.Trim();
            if (host == "*" || host == "+") return DefaultHost;

            string unbracketed = StripBrackets(host);

            if (IPAddress.TryParse(unbracketed, out IPAddress? address))
            {
                if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return DefaultHost;
                if (address.AddressFamily == AddressFamily.InterNetworkV6) return "[" + unbracketed + "]";
                return unbracketed;
            }

            return host;
        }

        /// <summary>
        /// The startup warning for a REST hostname that is a loopback IP literal (127.0.0.0/8 or ::1), or null for any
        /// other hostname. Such a listener answers only that exact host, so MCP clients configured with "localhost" get
        /// HTTP 404 (or a refused connection); the message names the URL Armada generates for its clients.
        /// </summary>
        /// <param name="restHostname">The configured REST hostname (Settings.Rest.Hostname), or null.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>The warning text, or null when no warning applies.</returns>
        public static string? LoopbackLiteralHostWarning(string? restHostname, int mcpPort)
        {
            if (String.IsNullOrWhiteSpace(restHostname)) return null;
            if (!IPAddress.TryParse(StripBrackets(restHostname), out IPAddress? address)) return null;
            if (!IPAddress.IsLoopback(address)) return null;
            string url = GetMcpUrl(mcpPort, ClientHostFor(restHostname));
            return "rest.hostname is the loopback address " + restHostname.Trim() + ": the MCP listener only answers requests "
                + "addressed to that exact host, so MCP clients must use " + url + " (requests to localhost get HTTP 404). "
                + "Armada generates " + url + " for captains and 'armada mcp install'; re-run 'armada mcp install' if a client "
                + "was configured with localhost, or set rest.hostname to localhost.";
        }

        /// <summary>
        /// True when the host names this machine's loopback interface: "localhost", any 127.0.0.0/8 address, or ::1
        /// (bracketed or not). Used to recognize an Armada MCP entry whether it was written with localhost or with the
        /// loopback address the listener is bound to.
        /// </summary>
        /// <param name="host">A URL host, or null.</param>
        /// <returns>True for a loopback host.</returns>
        public static bool IsLoopbackHost(string? host)
        {
            if (String.IsNullOrWhiteSpace(host)) return false;
            string value = StripBrackets(host);
            if (String.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (IPAddress.TryParse(value, out IPAddress? address)) return IPAddress.IsLoopback(address);
            return false;
        }

        /// <summary>
        /// True when the URL addresses this Admiral's MCP listener: an absolute http(s) URL on the MCP port whose host is
        /// either a loopback host (localhost, 127.0.0.0/8, ::1) or the configured client host, and whose path is the MCP
        /// endpoint ("/mcp", the legacy "/rpc", or empty for clients such as Mux that store the path separately).
        /// localhost, 127.0.0.1 and ::1 on the MCP port are therefore the same Armada server, so a client entry written
        /// with localhost is still recognized when the listener is bound to 127.0.0.1.
        /// </summary>
        /// <param name="url">The URL to test, or null.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="mcpHost">The configured client host (see <see cref="ClientHostFor"/>).</param>
        /// <returns>True when the URL targets the Armada MCP listener.</returns>
        public static bool IsArmadaMcpUrl(string? url, int mcpPort, string mcpHost)
        {
            if (mcpPort <= 0 || String.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (uri.Port != mcpPort) return false;
            if (!IsMcpEndpointPath(uri.AbsolutePath)) return false;
            if (IsLoopbackHost(uri.Host)) return true;
            return String.Equals(StripBrackets(uri.Host), StripBrackets(mcpHost), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rewrite a URL recognized by <see cref="IsArmadaMcpUrl"/> so it addresses the listener at the configured client
        /// host, keeping its scheme, port, path, and query. Other URLs are returned unchanged.
        /// </summary>
        /// <param name="url">The URL to rewrite.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="mcpHost">The configured client host (see <see cref="ClientHostFor"/>).</param>
        /// <returns>The rewritten URL, or the input when it does not target the Armada MCP listener.</returns>
        public static string RewriteArmadaMcpUrlHost(string url, int mcpPort, string mcpHost)
        {
            if (!IsArmadaMcpUrl(url, mcpPort, mcpHost)) return url;
            Uri uri = new Uri(url.Trim(), UriKind.Absolute);
            string host = String.IsNullOrWhiteSpace(mcpHost) ? DefaultHost : mcpHost;
            if (String.Equals(StripBrackets(uri.Host), StripBrackets(host), StringComparison.OrdinalIgnoreCase)) return url;
            UriBuilder builder = new UriBuilder(uri) { Host = StripBrackets(host) };
            return builder.Uri.AbsoluteUri;
        }

        /// <summary>
        /// The Armada MCP Streamable HTTP URL for a given port on localhost (matches the installer's default endpoint).
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>The MCP URL, e.g. http://localhost:7891/mcp.</returns>
        public static string GetMcpUrl(int mcpPort)
        {
            return GetMcpUrl(mcpPort, DefaultHost);
        }

        /// <summary>
        /// The Armada MCP Streamable HTTP URL for a given port and client host.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="host">The client host (see <see cref="ClientHostFor"/>); empty means localhost.</param>
        /// <returns>The MCP URL, e.g. http://127.0.0.1:7891/mcp.</returns>
        public static string GetMcpUrl(int mcpPort, string host)
        {
            return GetMcpBaseUrl(mcpPort, host) + "/mcp";
        }

        /// <summary>
        /// The Armada MCP base URL (scheme, host, and port, without the /mcp path) used by clients such as Mux that
        /// store the path separately.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="host">The client host (see <see cref="ClientHostFor"/>); empty means localhost.</param>
        /// <returns>The base URL, e.g. http://localhost:7891.</returns>
        public static string GetMcpBaseUrl(int mcpPort, string host)
        {
            string effectiveHost = String.IsNullOrWhiteSpace(host) ? DefaultHost : host;
            return "http://" + effectiveHost + ":" + mcpPort.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Build the keyed "mcpServers" document used by Claude Code, Gemini, and Cursor. The Armada
        /// server is registered under the "armada" key with the modern HTTP transport.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>An indented JSON document string.</returns>
        public static string BuildKeyedMcpServersJson(int mcpPort)
        {
            return BuildKeyedMcpServersJson(mcpPort, null, DefaultHost);
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
            return BuildKeyedMcpServersJson(mcpPort, sessionToken, DefaultHost);
        }

        /// <summary>
        /// Build the keyed "mcpServers" document used by Claude Code, Gemini, and Cursor for a specific client host,
        /// optionally carrying a session token as an <c>X-Token</c> header.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="sessionToken">Session token sent as X-Token on every request, or null for none.</param>
        /// <param name="host">The client host (see <see cref="ClientHostFor"/>).</param>
        /// <returns>An indented JSON document string.</returns>
        public static string BuildKeyedMcpServersJson(int mcpPort, string? sessionToken, string host)
        {
            JsonObject server = new JsonObject
            {
                ["type"] = "http",
                ["url"] = GetMcpUrl(mcpPort, host),
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
            return BuildMuxServersJson(mcpPort, DefaultHost);
        }

        /// <summary>
        /// Build the Mux "servers" array document for a specific client host.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="host">The client host (see <see cref="ClientHostFor"/>).</param>
        /// <returns>An indented JSON document string.</returns>
        public static string BuildMuxServersJson(int mcpPort, string host)
        {
            JsonObject server = new JsonObject
            {
                ["name"] = "armada",
                ["transport"] = "http",
                ["url"] = GetMcpBaseUrl(mcpPort, host),
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
            return BuildCodexConfigToml(mcpPort, null, DefaultHost);
        }

        /// <summary>
        /// Build the Codex TOML fragment, optionally carrying a session token as an <c>X-Token</c> HTTP header.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="sessionToken">Session token sent as X-Token on every request, or null for none.</param>
        /// <returns>A TOML document string.</returns>
        public static string BuildCodexConfigToml(int mcpPort, string? sessionToken)
        {
            return BuildCodexConfigToml(mcpPort, sessionToken, DefaultHost);
        }

        /// <summary>
        /// Build the Codex TOML fragment for a specific client host, optionally carrying a session token as an
        /// <c>X-Token</c> HTTP header.
        /// </summary>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="sessionToken">Session token sent as X-Token on every request, or null for none.</param>
        /// <param name="host">The client host (see <see cref="ClientHostFor"/>).</param>
        /// <returns>A TOML document string.</returns>
        public static string BuildCodexConfigToml(int mcpPort, string? sessionToken, string host)
        {
            string toml = "[mcp_servers.armada]" + Environment.NewLine
                + "url = \"" + GetMcpUrl(mcpPort, host) + "\"" + Environment.NewLine;
            if (!String.IsNullOrEmpty(sessionToken))
            {
                toml += "http_headers = { \"X-Token\" = \"" + sessionToken.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" }" + Environment.NewLine;
            }

            return toml;
        }

        #endregion

        #region Private-Methods

        private static string StripBrackets(string? host)
        {
            if (String.IsNullOrEmpty(host)) return String.Empty;
            string value = host.Trim();
            if (value.Length > 2 && value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static bool IsMcpEndpointPath(string path)
        {
            string trimmed = (path ?? String.Empty).TrimEnd('/');
            return trimmed.Length == 0
                || String.Equals(trimmed, "/mcp", StringComparison.OrdinalIgnoreCase)
                || String.Equals(trimmed, "/rpc", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Members

        private static readonly System.Text.Json.JsonSerializerOptions _IndentedOptions =
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        #endregion
    }
}
