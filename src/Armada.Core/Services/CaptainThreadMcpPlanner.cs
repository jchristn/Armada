namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Pure planner for an Ask Armada thread turn: binds a CLI captain's connection to Armada's MCP server to a
    /// thread-scoped session token (so mutating tool calls are held for approval) WITHOUT hiding the CLI's own login.
    /// Unlike <see cref="CaptainLaunchIsolationPlanner"/>, it never redirects HOME, CODEX_HOME, or a config directory;
    /// it uses each CLI's per-invocation override mechanism instead, so the user's credentials stay where the CLI
    /// expects them. The token itself travels in the <see cref="TokenEnvironmentVariable"/> environment variable and is
    /// referenced from the generated configuration wherever the CLI supports environment expansion, so it is not written
    /// to disk or placed on the command line.
    ///
    /// Strategy per runtime (verified against Codex 0.159, Gemini CLI 0.62, cursor-agent 2026.10.01, OpenCode 1.18, and
    /// Mux 1.1 source):
    /// - Claude Code: the strict per-launch MCP config of <see cref="CaptainLaunchIsolationPlanner"/> (its login does not
    ///   live in the settings it excludes).
    /// - Codex: <c>-c</c> overrides that disable every host Armada entry and add a fresh streamable HTTP entry whose
    ///   <c>env_http_headers</c> sends X-Token from the environment. Codex merges <c>-c</c> into an existing table, so a
    ///   host entry is never reused. The entry sets <c>default_tools_approval_mode = "approve"</c>: 'codex exec' runs
    ///   with approval policy "never" and would otherwise refuse every MCP tool call; the human approval happens in
    ///   Armada's Ask gate instead.
    /// - Gemini: a workspace <c>.gemini/settings.json</c> in the throwaway working directory (workspace settings replace a
    ///   user server of the same name), <c>GEMINI_CLI_TRUST_WORKSPACE=true</c> (untrusted folders load no MCP servers), and
    ///   <c>--allowed-mcp-server-names armada</c> so no other Armada alias is reachable.
    /// - Cursor: a project <c>.cursor/mcp.json</c> in the throwaway working directory that redefines <c>armada</c> and every
    ///   host Armada alias as the scoped server (project entries replace global ones of the same name). The runtime's
    ///   existing <c>--force</c> also approves project MCP servers.
    /// - Mux: <c>--mcp-config</c> with a scoped server document (API-key auth in the X-Token header) plus
    ///   <c>--strict-mcp-config</c>; the endpoint configuration in the Mux config directory is untouched.
    /// - OpenCode: <c>OPENCODE_CONFIG_CONTENT</c> (merged over the user's configuration) that disables every host Armada
    ///   entry and adds a fresh remote entry. OpenCode deep-merges entries, so a host entry is never reused.
    /// </summary>
    public static class CaptainThreadMcpPlanner
    {
        #region Public-Members

        /// <summary>
        /// Environment variable that carries the thread-scoped session token to the captain process.
        /// </summary>
        public const string TokenEnvironmentVariable = "ARMADA_MCP_TOKEN";

        /// <summary>
        /// Default name of the scoped Armada server in generated configuration.
        /// </summary>
        public const string ServerName = "armada";

        /// <summary>
        /// Name used for the scoped server when the host configuration already has an entry called <see cref="ServerName"/>
        /// that cannot be replaced wholesale (Codex, OpenCode).
        /// </summary>
        public const string AlternateServerName = "armada_ask";

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when Ask thread turns of this runtime connect to Armada's MCP server with a thread-scoped token, so
        /// mutating Armada tool calls are held for approval.
        /// </summary>
        /// <param name="runtime">The captain's runtime.</param>
        /// <returns>True when Ask approval gating applies.</returns>
        public static bool SupportsApprovalGating(AgentRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                case AgentRuntimeEnum.Codex:
                case AgentRuntimeEnum.Gemini:
                case AgentRuntimeEnum.Cursor:
                case AgentRuntimeEnum.Mux:
                case AgentRuntimeEnum.OpenCode:
                case AgentRuntimeEnum.ApiEndpoint:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Build the thread-scoped MCP plan for a CLI runtime. Returns an empty plan when the request is incomplete (no
        /// token, invalid port, missing directories) or the runtime has no CLI launch (ApiEndpoint is bound in-process).
        /// </summary>
        /// <param name="request">Plan inputs.</param>
        /// <returns>The plan; never null.</returns>
        public static CaptainLaunchIsolationPlan Plan(CaptainThreadMcpPlanRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            CaptainLaunchIsolationPlan plan = new CaptainLaunchIsolationPlan();
            if (request.McpPort <= 0 || request.McpPort > 65535) return plan;
            if (String.IsNullOrWhiteSpace(request.SessionToken)) return plan;
            if (String.IsNullOrWhiteSpace(request.ScopedConfigDirectory)) return plan;
            if (String.IsNullOrWhiteSpace(request.WorkingDirectory)) return plan;

            string url = ArmadaMcpConfigBuilder.GetMcpUrl(request.McpPort, request.McpHost);

            switch (request.Runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    {
                        return CaptainLaunchIsolationPlanner.Plan(AgentRuntimeEnum.ClaudeCode, request.McpPort, request.ScopedConfigDirectory, request.SessionToken, request.McpHost);
                    }
                case AgentRuntimeEnum.Codex:
                    {
                        List<string> aliases = FindCodexArmadaServerNames(request.HostCodexConfigToml, request.McpPort, request.McpHost);
                        foreach (string alias in aliases)
                        {
                            if (!_TomlBareKey.IsMatch(alias)) continue;
                            plan.ExtraArguments.Add("-c");
                            plan.ExtraArguments.Add("mcp_servers." + alias + ".enabled=false");
                        }

                        string name = ChooseFreshName(aliases);
                        plan.ExtraArguments.Add("-c");
                        plan.ExtraArguments.Add("mcp_servers." + name + "={ url = \"" + url + "\", env_http_headers = { \"X-Token\" = \"" + TokenEnvironmentVariable + "\" }, default_tools_approval_mode = \"approve\" }");
                        break;
                    }
                case AgentRuntimeEnum.Gemini:
                    {
                        if (!request.AllowWorkingDirectoryFiles) return plan;
                        JsonObject server = new JsonObject
                        {
                            ["httpUrl"] = url,
                            ["headers"] = new JsonObject { ["X-Token"] = "$" + TokenEnvironmentVariable },
                        };
                        JsonObject root = new JsonObject { ["mcpServers"] = new JsonObject { [ServerName] = server } };
                        plan.FilesToWrite.Add(new IsolationConfigFile(Path.Combine(".gemini", "settings.json"), root.ToJsonString(_Indented)) { RelativeToWorkingDirectory = true });
                        plan.ExtraArguments.Add("--allowed-mcp-server-names");
                        plan.ExtraArguments.Add(ServerName);
                        plan.EnvironmentOverrides["GEMINI_CLI_TRUST_WORKSPACE"] = "true";
                        break;
                    }
                case AgentRuntimeEnum.Cursor:
                    {
                        if (!request.AllowWorkingDirectoryFiles) return plan;
                        List<string> names = new List<string> { ServerName };
                        foreach (string alias in FindKeyedArmadaServerNames(request.HostCursorMcpJson, request.McpPort, request.McpHost))
                        {
                            if (!names.Contains(alias)) names.Add(alias);
                        }

                        JsonObject servers = new JsonObject();
                        foreach (string name in names)
                        {
                            servers[name] = new JsonObject
                            {
                                ["url"] = url,
                                ["headers"] = new JsonObject { ["X-Token"] = "${env:" + TokenEnvironmentVariable + "}" },
                            };
                        }

                        JsonObject root = new JsonObject { ["mcpServers"] = servers };
                        plan.FilesToWrite.Add(new IsolationConfigFile(Path.Combine(".cursor", "mcp.json"), root.ToJsonString(_Indented)) { RelativeToWorkingDirectory = true });
                        break;
                    }
                case AgentRuntimeEnum.Mux:
                    {
                        JsonObject server = new JsonObject
                        {
                            ["name"] = ServerName,
                            ["transport"] = "http",
                            ["url"] = ArmadaMcpConfigBuilder.GetMcpBaseUrl(request.McpPort, request.McpHost),
                            ["mcpPath"] = "/mcp",
                            ["auth"] = new JsonObject
                            {
                                ["type"] = "apikey",
                                ["apiKeyHeader"] = "X-Token",
                                ["apiKeyValue"] = "${" + TokenEnvironmentVariable + "}",
                            },
                        };
                        JsonObject root = new JsonObject { ["servers"] = new JsonArray(server) };
                        plan.FilesToWrite.Add(new IsolationConfigFile("mcp-servers.json", root.ToJsonString(_Indented)));
                        plan.ExtraArguments.Add("--mcp-config");
                        plan.ExtraArguments.Add(Path.Combine(request.ScopedConfigDirectory, "mcp-servers.json"));
                        plan.ExtraArguments.Add("--strict-mcp-config");
                        break;
                    }
                case AgentRuntimeEnum.OpenCode:
                    {
                        List<string> aliases = new List<string>();
                        List<string> documents = new List<string>(request.HostOpenCodeConfigDocuments);
                        if (!String.IsNullOrWhiteSpace(request.ExistingOpenCodeConfigContent)) documents.Add(request.ExistingOpenCodeConfigContent!);
                        foreach (string document in documents)
                        {
                            foreach (string alias in FindKeyedArmadaServerNames(document, request.McpPort, request.McpHost))
                            {
                                if (!aliases.Contains(alias)) aliases.Add(alias);
                            }
                        }

                        JsonObject root = ParseObjectOrEmpty(request.ExistingOpenCodeConfigContent);
                        JsonObject mcp = root["mcp"] as JsonObject ?? new JsonObject();
                        root["mcp"] = mcp;
                        foreach (string alias in aliases)
                        {
                            JsonObject entry = mcp[alias] as JsonObject ?? new JsonObject();
                            entry["enabled"] = false;
                            mcp[alias] = entry;
                        }

                        string name = ChooseFreshName(aliases);
                        mcp[name] = new JsonObject
                        {
                            ["type"] = "remote",
                            ["url"] = url,
                            ["headers"] = new JsonObject { ["X-Token"] = "{env:" + TokenEnvironmentVariable + "}" },
                            ["enabled"] = true,
                        };
                        plan.EnvironmentOverrides["OPENCODE_CONFIG_CONTENT"] = root.ToJsonString();
                        break;
                    }
                default:
                    return plan;
            }

            plan.EnvironmentOverrides[TokenEnvironmentVariable] = request.SessionToken;
            return plan;
        }

        /// <summary>
        /// Find the names of the MCP servers in a Codex <c>config.toml</c> that point at Armada: any server whose name
        /// contains "armada", whose URL targets the Admiral MCP port, or whose command runs the Armada CLI or Helm.
        /// Handles <c>[mcp_servers.name]</c> tables (including sub-tables such as <c>.env</c>) and inline entries under a
        /// <c>[mcp_servers]</c> table. Unparseable input yields an empty list.
        /// </summary>
        /// <param name="toml">The configuration text, or null.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>Distinct server names in file order.</returns>
        public static List<string> FindCodexArmadaServerNames(string? toml, int mcpPort)
        {
            return FindCodexArmadaServerNames(toml, mcpPort, ArmadaMcpConfigBuilder.DefaultHost);
        }

        /// <summary>
        /// Find the names of the MCP servers in a Codex <c>config.toml</c> that point at Armada, recognizing URLs on the
        /// MCP port at any loopback host (localhost, 127.0.0.1, ::1) or at the configured client host as the same server.
        /// </summary>
        /// <param name="toml">The configuration text, or null.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="mcpHost">The configured client host (see <see cref="ArmadaMcpConfigBuilder.ClientHostFor"/>).</param>
        /// <returns>Distinct server names in file order.</returns>
        public static List<string> FindCodexArmadaServerNames(string? toml, int mcpPort, string mcpHost)
        {
            List<string> names = new List<string>();
            if (String.IsNullOrWhiteSpace(toml)) return names;

            Dictionary<string, string> bodies = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            string? current = null;
            bool inServersTable = false;

            string[] lines = toml!.Replace("\r\n", "\n").Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    current = null;
                    inServersTable = false;
                    Match serverHeader = _CodexServerHeader.Match(line);
                    if (serverHeader.Success)
                    {
                        current = serverHeader.Groups["quoted"].Success ? serverHeader.Groups["quoted"].Value : serverHeader.Groups["bare"].Value;
                        AddBody(bodies, order, current, String.Empty);
                    }
                    else if (_CodexServersTable.IsMatch(line))
                    {
                        inServersTable = true;
                    }

                    continue;
                }

                if (current != null)
                {
                    AddBody(bodies, order, current, line);
                }
                else if (inServersTable)
                {
                    Match inline = _CodexInlineServer.Match(line);
                    if (inline.Success)
                    {
                        string name = inline.Groups["quoted"].Success ? inline.Groups["quoted"].Value : inline.Groups["bare"].Value;
                        AddBody(bodies, order, name, inline.Groups["rest"].Value);
                    }
                }
            }

            foreach (string name in order)
            {
                if (IsArmadaServer(name, bodies[name], mcpPort, mcpHost)) names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// Find the names of the MCP servers in a keyed JSON or JSONC client configuration (OpenCode <c>mcp</c>, or
        /// <c>mcpServers</c> for Cursor, Gemini, and Claude Code) that point at Armada, using the same rules as
        /// <see cref="FindCodexArmadaServerNames(string, int)"/>. Unparseable input yields an empty list.
        /// </summary>
        /// <param name="json">The configuration text, or null.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <returns>Distinct server names.</returns>
        public static List<string> FindKeyedArmadaServerNames(string? json, int mcpPort)
        {
            return FindKeyedArmadaServerNames(json, mcpPort, ArmadaMcpConfigBuilder.DefaultHost);
        }

        /// <summary>
        /// Find the names of the MCP servers in a keyed JSON or JSONC client configuration that point at Armada,
        /// recognizing URLs on the MCP port at any loopback host or at the configured client host as the same server.
        /// </summary>
        /// <param name="json">The configuration text, or null.</param>
        /// <param name="mcpPort">The Admiral MCP port.</param>
        /// <param name="mcpHost">The configured client host (see <see cref="ArmadaMcpConfigBuilder.ClientHostFor"/>).</param>
        /// <returns>Distinct server names.</returns>
        public static List<string> FindKeyedArmadaServerNames(string? json, int mcpPort, string mcpHost)
        {
            List<string> names = new List<string>();
            if (String.IsNullOrWhiteSpace(json)) return names;

            HostMcpConfigDocument? document = null;
            try
            {
                document = JsonSerializer.Deserialize<HostMcpConfigDocument>(json!, _Lenient);
            }
            catch (JsonException)
            {
                return names;
            }

            if (document == null) return names;

            List<Dictionary<string, object?>?> maps = new List<Dictionary<string, object?>?> { document.Mcp, document.McpServers };
            foreach (Dictionary<string, object?>? map in maps)
            {
                if (map == null) continue;
                foreach (KeyValuePair<string, object?> entry in map)
                {
                    string body = entry.Value?.ToString() ?? String.Empty;
                    if (IsArmadaServer(entry.Key, body, mcpPort, mcpHost) && !names.Contains(entry.Key)) names.Add(entry.Key);
                }
            }

            return names;
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Indented = new JsonSerializerOptions { WriteIndented = true };

        private static readonly JsonSerializerOptions _Lenient = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = false,
        };

        private static readonly Regex _TomlBareKey = new Regex("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        private static readonly Regex _CodexServerHeader = new Regex(
            "^\\[\\s*mcp_servers\\s*\\.\\s*(?:\"(?<quoted>[^\"]+)\"|(?<bare>[A-Za-z0-9_-]+))\\s*(?:\\..*)?\\]\\s*(?:#.*)?$",
            RegexOptions.Compiled);

        private static readonly Regex _CodexServersTable = new Regex("^\\[\\s*mcp_servers\\s*\\]\\s*(?:#.*)?$", RegexOptions.Compiled);

        private static readonly Regex _CodexInlineServer = new Regex(
            "^(?:\"(?<quoted>[^\"]+)\"|(?<bare>[A-Za-z0-9_-]+))\\s*=\\s*(?<rest>\\{.*)$",
            RegexOptions.Compiled);

        private static readonly Regex _HttpUrl = new Regex("https?://[^\\s\"'<>{},]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _ArmadaCommand = new Regex(
            "(?:^|[\"'\\s/\\\\])armada(?:\\.exe|\\.cmd)?[\"']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        #endregion

        #region Private-Methods

        private static void AddBody(Dictionary<string, string> bodies, List<string> order, string name, string text)
        {
            if (!bodies.ContainsKey(name))
            {
                bodies[name] = String.Empty;
                order.Add(name);
            }

            bodies[name] = bodies[name] + "\n" + text;
        }

        private static bool IsArmadaServer(string name, string body, int mcpPort, string mcpHost)
        {
            if (name.IndexOf("armada", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (String.IsNullOrEmpty(body)) return false;
            if (mcpPort > 0)
            {
                foreach (Match urlMatch in _HttpUrl.Matches(body))
                {
                    string candidate = urlMatch.Value;
                    if (ArmadaMcpConfigBuilder.IsArmadaMcpUrl(candidate, mcpPort, mcpHost)) return true;
                    if (IsMcpPathOnPort(candidate, mcpPort)) return true;
                }
            }

            if (body.IndexOf("Armada.Helm", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return _ArmadaCommand.IsMatch(body);
        }

        private static bool IsMcpPathOnPort(string url, int mcpPort)
        {
            // Any host serving /mcp on the Admiral MCP port (the rule before loopback aliases were recognized).
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
            if (uri.Port != mcpPort) return false;
            return String.Equals(uri.AbsolutePath.TrimEnd('/'), "/mcp", StringComparison.OrdinalIgnoreCase);
        }

        private static string ChooseFreshName(List<string> taken)
        {
            if (!taken.Contains(ServerName)) return ServerName;
            string candidate = AlternateServerName;
            int suffix = 2;
            while (taken.Contains(candidate))
            {
                candidate = AlternateServerName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

        private static JsonObject ParseObjectOrEmpty(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new JsonObject();
            try
            {
                JsonNode? node = JsonNode.Parse(json!, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return node as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                return new JsonObject();
            }
        }

        #endregion
    }
}
