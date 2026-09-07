namespace Armada.Server
{
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Discovers runtime-visible MCP servers and probes them for tool inventories.
    /// </summary>
    internal sealed class CaptainRuntimeToolCatalogService
    {
        private readonly LoggingModule _Logging;
        private readonly HarborConnectionManager? _HarborConnections;
        private readonly HttpClient _HttpClient = new HttpClient();
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public CaptainRuntimeToolCatalogService(LoggingModule logging, HarborConnectionManager? harborConnections = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _HarborConnections = harborConnections;
        }

        public async Task<RuntimeToolCatalogSnapshot?> TryDescribeAsync(Captain captain, DatabaseDriver database, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (database == null) throw new ArgumentNullException(nameof(database));

            string? contextDirectory = await ResolveContextDirectoryAsync(captain, database).ConfigureAwait(false);

            switch (captain.Runtime)
            {
                case AgentRuntimeEnum.Codex:
                    return await DescribeCodexAsync(contextDirectory, token).ConfigureAwait(false);
                case AgentRuntimeEnum.ClaudeCode:
                    return await DescribeConfiguredRuntimeAsync(
                        "Claude Code",
                        GetClaudeConfigPath(),
                        TryLoadClaudeBuiltInInventory(),
                        token,
                        "Claude Code built-in tools are not currently enumerated by Armada.")
                        .ConfigureAwait(false);
                case AgentRuntimeEnum.Gemini:
                    return await DescribeConfiguredRuntimeAsync(
                        "Gemini CLI",
                        GetGeminiConfigPath(),
                        TryLoadGeminiBuiltInInventory(),
                        token,
                        "Gemini built-in tools are not currently enumerated by Armada.")
                        .ConfigureAwait(false);
                case AgentRuntimeEnum.Cursor:
                    if (String.IsNullOrWhiteSpace(contextDirectory))
                    {
                        return new RuntimeToolCatalogSnapshot
                        {
                            AvailabilityVerified = true,
                            AvailabilitySource = "cursor-project-config-missing",
                            Summary = "Cursor MCP configuration is project-scoped, and this captain does not currently expose a workspace path Armada can inspect. Cursor built-in tools are not currently enumerated by Armada."
                        };
                    }

                    return await DescribeConfiguredRuntimeAsync(
                        "Cursor",
                        Path.Combine(contextDirectory, ".cursor", "mcp.json"),
                        null,
                        token,
                        "Cursor built-in tools are not currently enumerated by Armada.")
                        .ConfigureAwait(false);
                case AgentRuntimeEnum.Mux:
                    return await DescribeMuxAsync(captain, database, token).ConfigureAwait(false);
                case AgentRuntimeEnum.ApiEndpoint:
                    return new RuntimeToolCatalogSnapshot
                    {
                        AvailabilityVerified = true,
                        AvailabilitySource = "api-endpoint-builtin-tools",
                        Summary = "API-endpoint captains run Armada's built-in coding tools (read, write, edit, search, run-process) in-process against a working directory. They do not act as an MCP client, so Armada's fleet, mission, and voyage orchestration tools are not exposed to them and there is no runtime MCP config to connect."
                    };
                case AgentRuntimeEnum.Custom:
                    return new RuntimeToolCatalogSnapshot
                    {
                        AvailabilityVerified = false,
                        AvailabilitySource = "unsupported-runtime",
                        Summary = "Custom captains are not introspected by Armada because there is no shared runtime contract Armada can probe for tool inventory."
                    };
                default:
                    return new RuntimeToolCatalogSnapshot
                    {
                        AvailabilityVerified = false,
                        AvailabilitySource = "unsupported-runtime",
                        Summary = "Armada does not currently have a runtime-specific tool inventory implementation for this captain."
                    };
            }
        }

        private async Task<RuntimeToolCatalogSnapshot> DescribeCodexAsync(string? contextDirectory, CancellationToken token)
        {
            RuntimeToolCatalogSnapshot snapshot = new RuntimeToolCatalogSnapshot
            {
                AvailabilitySource = "codex-mcp-probe"
            };

            try
            {
                List<RuntimeMcpServerDefinition> servers = await GetCodexServersAsync(contextDirectory, token).ConfigureAwait(false);
                return await ProbeConfiguredSourcesAsync(
                    "Codex",
                    servers,
                    null,
                    "Codex built-in tools are not currently enumerated by Armada.",
                    snapshot,
                    token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                snapshot.AvailabilityVerified = false;
                snapshot.Summary = "Armada could not inspect Codex MCP servers for this captain: " + ex.Message;
                return snapshot;
            }
        }

        private async Task<RuntimeToolCatalogSnapshot> DescribeConfiguredRuntimeAsync(
            string runtimeName,
            string configPath,
            RuntimeBuiltInInventory? builtInInventory,
            CancellationToken token,
            string builtInFallbackNote)
        {
            RuntimeToolCatalogSnapshot snapshot = new RuntimeToolCatalogSnapshot
            {
                AvailabilitySource = runtimeName.ToLowerInvariant().Replace(" ", "-") + "-mcp-probe"
            };

            try
            {
                List<RuntimeMcpServerDefinition> servers = await ReadJsonConfiguredServersAsync(configPath, token).ConfigureAwait(false);
                return await ProbeConfiguredSourcesAsync(
                    runtimeName,
                    servers,
                    builtInInventory,
                    builtInFallbackNote,
                    snapshot,
                    token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                snapshot.AvailabilityVerified = false;
                snapshot.Summary = "Armada could not inspect " + runtimeName + " MCP servers for this captain: " + ex.Message;
                return snapshot;
            }
        }

        private async Task<RuntimeToolCatalogSnapshot> DescribeMuxAsync(Captain captain, DatabaseDriver database, CancellationToken token)
        {
            RuntimeToolCatalogSnapshot snapshot = new RuntimeToolCatalogSnapshot
            {
                AvailabilitySource = "mux-runtime-probe"
            };

            MuxCaptainOptions? options = CaptainRuntimeOptions.GetMuxOptions(captain);

            // Resolve where mux should actually run. When the captain's dock is owned by a connected Harbor,
            // run 'mux probe' on that Harbor over its link (via the remote executor) so the probe reflects the
            // host where mux, its config directory, and its provider auth live -- not the Admiral, which in
            // split mode may have neither mux installed nor the captain's config.
            RuntimeHostContext host = await ResolveHostContextAsync(captain, database).ConfigureAwait(false);

            // The model-endpoint probe ('mux probe --require-tools') performs a live LLM inference round-trip
            // and can be slow or fail for reasons entirely unrelated to MCP connectivity: a cold model, network
            // latency, or a briefly unavailable endpoint. It must NOT gate the MCP server inventory -- whether a
            // captain can reach Armada over MCP depends only on the configured MCP servers, which are read and
            // probed below. So a probe failure (including a timeout) degrades only the built-in tool count; it
            // never discards the MCP server probe that determines the "connected to Armada" state.
            MuxProbeResult? probe = null;
            string? probeError = null;
            try
            {
                MuxCliService muxCli = new MuxCliService(_Logging, host.Executor);
                probe = await muxCli.ProbeAsync(captain, host.WorkingDirectory, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                probeError = ex.Message;
                _Logging.Debug("[CaptainRuntimeToolCatalogService] mux model-endpoint probe failed for captain " +
                    captain.Id + "; continuing with MCP server inventory only: " + ex.Message);
            }

            try
            {
                string configDirectory = ResolveMuxConfigDirectory(probe, options);

                int builtInToolCount = probe != null ? Math.Max(0, probe.BuiltInToolCount) : 0;
                if (probe != null && (probe.ToolsEnabled || builtInToolCount > 0))
                {
                    snapshot.Servers.Add(CreateMuxBuiltInSummary(probe, builtInToolCount));
                }

                // In split mode the config directory and any stdio MCP servers live on the Harbor, so the
                // Admiral cannot read mcp-servers.json or probe those servers directly. Reflect what the
                // Harbor-side probe reported (built-in tools, whether MCP is configured, server count) and
                // note that per-server tool enumeration over the link is not yet available.
                if (host.IsRemote)
                {
                    return BuildRemoteMuxSnapshot(snapshot, probe, probeError, builtInToolCount, host);
                }

                List<RuntimeMcpServerDefinition> servers = await ReadMuxConfiguredServersAsync(configDirectory, token).ConfigureAwait(false);
                snapshot.ConfiguredServerCount = servers.Count + snapshot.Servers.Count;

                await ProbeServersConcurrentlyAsync(servers, snapshot, token).ConfigureAwait(false);

                int reachableSources = snapshot.Servers.Count(s => s.Reachable);
                snapshot.ReachableServerCount = reachableSources;
                snapshot.ToolsAccessible = builtInToolCount > 0 || snapshot.Tools.Count > 0;
                snapshot.ArmadaToolCount = snapshot.Tools.Count(t => String.Equals(t.RegistrationSource, "armada", StringComparison.OrdinalIgnoreCase));
                snapshot.EffectiveToolCount = builtInToolCount + snapshot.Tools.Count;
                snapshot.AvailabilityVerified = (probe?.Success ?? false) || snapshot.Servers.Count > 0;

                bool probeSucceeded = probe?.Success ?? false;
                string endpointName = probe?.EndpointName ?? String.Empty;

                if (!probeSucceeded)
                {
                    // The model-endpoint probe was slow, failed, or timed out. This is independent of MCP, so
                    // report the MCP server inventory that was actually gathered rather than declaring the
                    // captain unable to inspect tools (which the UI reads as "not connected to Armada").
                    string probeDetail = !String.IsNullOrWhiteSpace(probeError)
                        ? probeError!
                        : FirstNonEmptyLine(probe?.ErrorMessage, probe?.ErrorCode);
                    string mcpSummary = servers.Count == 0
                        ? "No external MCP servers are configured for the active Mux config directory."
                        : snapshot.Servers.Count(s => s.SourceKind == "McpServer" && s.Reachable) +
                          " of " + servers.Count + " configured MCP server(s) responded and exposed " +
                          snapshot.Tools.Count + " named tool(s).";
                    snapshot.Summary = "Mux model-endpoint probe did not complete (" +
                        (String.IsNullOrWhiteSpace(probeDetail) ? "endpoint unavailable or slow" : probeDetail) +
                        "); this does not affect MCP connectivity. " + mcpSummary;
                }
                else if (servers.Count == 0)
                {
                    snapshot.Summary = "Mux endpoint '" + endpointName + "' reports " + builtInToolCount +
                        " built-in tool(s). No external MCP servers are configured for the active Mux config directory, and Mux does not currently expose individual built-in tool names.";
                }
                else
                {
                    snapshot.Summary = "Mux endpoint '" + endpointName + "' reports " + builtInToolCount +
                        " built-in tool(s) and " + servers.Count + " configured MCP server(s); " +
                        snapshot.Servers.Count(s => s.SourceKind == "McpServer" && s.Reachable) +
                        " MCP server(s) responded and exposed " + snapshot.Tools.Count +
                        " named tool(s). Configured MCP servers that did not respond may simply be offline at query time. Mux does not currently expose individual built-in tool names.";
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.AvailabilityVerified = false;
                snapshot.Summary = "Armada could not inspect Mux tools for this captain: " + ex.Message;
                return snapshot;
            }
        }

        /// <summary>
        /// Resolve where a captain's runtime CLI should be executed for probing. When the captain's current
        /// dock is owned by a Harbor that is currently linked, return a remote executor targeting that Harbor
        /// (so the probe runs on the Harbor host) plus the dock's worktree path. Otherwise return a local
        /// executor. The local path preserves the standalone behavior exactly.
        /// </summary>
        private async Task<RuntimeHostContext> ResolveHostContextAsync(Captain captain, DatabaseDriver database)
        {
            if (_HarborConnections != null && !String.IsNullOrWhiteSpace(captain.CurrentDockId))
            {
                Dock? dock = await database.Docks.ReadAsync(captain.CurrentDockId).ConfigureAwait(false);
                if (dock != null
                    && !String.IsNullOrWhiteSpace(dock.HarborId)
                    && _HarborConnections.IsConnected(dock.HarborId!))
                {
                    return new RuntimeHostContext
                    {
                        Executor = new RemoteHostCommandExecutor(_HarborConnections, dock.HarborId!),
                        WorkingDirectory = dock.WorktreePath,
                        IsRemote = true,
                        HarborId = dock.HarborId
                    };
                }
            }

            return new RuntimeHostContext
            {
                Executor = new LocalHostCommandExecutor(),
                WorkingDirectory = null,
                IsRemote = false,
                HarborId = null
            };
        }

        /// <summary>
        /// Build the Mux snapshot for a captain whose probe ran on a Harbor. The Admiral cannot read the
        /// Harbor's mcp-servers.json or probe its stdio MCP servers directly, so the snapshot reflects the
        /// summary counts the Harbor-side probe returned. Per-server tool enumeration (which would confirm the
        /// Armada MCP server specifically) requires proxying MCP over the Harbor link, which is a follow-up;
        /// until then ArmadaToolCount is left at 0 (unknown) rather than inferred from the raw server count.
        /// </summary>
        private RuntimeToolCatalogSnapshot BuildRemoteMuxSnapshot(
            RuntimeToolCatalogSnapshot snapshot,
            MuxProbeResult? probe,
            string? probeError,
            int builtInToolCount,
            RuntimeHostContext host)
        {
            bool probeSucceeded = probe?.Success ?? false;
            int mcpServerCount = probe != null ? Math.Max(0, probe.McpServerCount) : 0;
            bool mcpConfigured = probe?.McpConfigured ?? false;

            snapshot.AvailabilitySource = "mux-harbor-probe";
            snapshot.ConfiguredServerCount = mcpServerCount + snapshot.Servers.Count;
            snapshot.ReachableServerCount = snapshot.Servers.Count(s => s.Reachable);
            snapshot.ToolsAccessible = builtInToolCount > 0 || mcpConfigured;
            snapshot.ArmadaToolCount = 0;
            snapshot.EffectiveToolCount = builtInToolCount;
            snapshot.AvailabilityVerified = probeSucceeded || mcpConfigured || snapshot.Servers.Count > 0;

            string harborLabel = String.IsNullOrWhiteSpace(host.HarborId) ? "a Harbor" : "Harbor " + host.HarborId;
            string mcpClause = mcpConfigured
                ? mcpServerCount + " MCP server(s) are configured on the Harbor"
                : "no MCP servers are configured on the Harbor";

            if (!probeSucceeded)
            {
                string probeDetail = !String.IsNullOrWhiteSpace(probeError)
                    ? probeError!
                    : FirstNonEmptyLine(probe?.ErrorMessage, probe?.ErrorCode);
                snapshot.Summary = "Mux runs on " + harborLabel + "; the model-endpoint probe did not complete (" +
                    (String.IsNullOrWhiteSpace(probeDetail) ? "endpoint unavailable or slow" : probeDetail) +
                    "). " + char.ToUpperInvariant(mcpClause[0]) + mcpClause.Substring(1) +
                    ". Per-server tool enumeration over the Harbor link is not yet available.";
            }
            else
            {
                snapshot.Summary = "Mux endpoint '" + (probe?.EndpointName ?? String.Empty) + "' runs on " +
                    harborLabel + " and reports " + builtInToolCount + " built-in tool(s); " + mcpClause +
                    ". Individual MCP tool names are not enumerated over the Harbor link yet, so Armada tool " +
                    "visibility cannot be confirmed from the Admiral.";
            }

            return snapshot;
        }

        /// <summary>
        /// Add each configured server to the snapshot and probe them for tools concurrently, so one
        /// offline server does not serialize the others behind its connection timeout. Each server's
        /// summary is updated in place; the collected tools are appended after all probes complete.
        /// </summary>
        private async Task ProbeServersConcurrentlyAsync(
            List<RuntimeMcpServerDefinition> servers,
            RuntimeToolCatalogSnapshot snapshot,
            CancellationToken token)
        {
            List<Task<List<CaptainToolSummary>>> tasks = new List<Task<List<CaptainToolSummary>>>();

            foreach (RuntimeMcpServerDefinition server in servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                CaptainToolServerSummary serverSummary = CreateConfiguredServerSummary(server);
                snapshot.Servers.Add(serverSummary);

                if (!server.Enabled)
                {
                    continue;
                }

                RuntimeMcpServerDefinition capturedServer = server;
                CaptainToolServerSummary capturedSummary = serverSummary;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        List<CaptainToolSummary> tools = await ProbeServerToolsAsync(capturedServer, token).ConfigureAwait(false);
                        tools = ApplyToolFilters(capturedServer, tools);

                        capturedSummary.Reachable = true;
                        capturedSummary.ToolCount = tools.Count;
                        capturedSummary.Status = "Reachable";
                        return tools;
                    }
                    catch (Exception ex)
                    {
                        capturedSummary.Reachable = false;
                        capturedSummary.Status = "Unreachable at query time";
                        capturedSummary.ErrorMessage = ex.Message;
                        return new List<CaptainToolSummary>();
                    }
                }, token));
            }

            List<CaptainToolSummary>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (List<CaptainToolSummary> tools in results)
            {
                snapshot.Tools.AddRange(tools);
            }
        }

        private async Task<RuntimeToolCatalogSnapshot> ProbeConfiguredSourcesAsync(
            string runtimeName,
            List<RuntimeMcpServerDefinition> servers,
            RuntimeBuiltInInventory? builtInInventory,
            string builtInFallbackNote,
            RuntimeToolCatalogSnapshot snapshot,
            CancellationToken token)
        {
            snapshot.AvailabilityVerified = true;
            ApplyRuntimeBuiltInInventory(snapshot, builtInInventory);

            await ProbeServersConcurrentlyAsync(servers, snapshot, token).ConfigureAwait(false);

            snapshot.ConfiguredServerCount = snapshot.Servers.Count;
            snapshot.ReachableServerCount = snapshot.Servers.Count(s => s.Reachable);
            snapshot.ToolsAccessible = snapshot.Tools.Count > 0;
            snapshot.ArmadaToolCount = snapshot.Tools.Count(t => String.Equals(t.RegistrationSource, "armada", StringComparison.OrdinalIgnoreCase));
            snapshot.EffectiveToolCount = snapshot.Tools.Count;
            string builtInNote = builtInInventory != null && builtInInventory.Tools.Count > 0
                ? builtInInventory.Note
                : builtInFallbackNote;

            if (servers.Count == 0)
            {
                snapshot.Summary = runtimeName + " has no configured MCP servers for this captain context. " + builtInNote;
            }
            else if (snapshot.ReachableServerCount == 0)
            {
                snapshot.Summary = runtimeName + " reports " + servers.Count + " configured MCP server(s), but none responded to tool discovery at query time. Some configured MCP servers may simply be offline. " + builtInNote;
            }
            else if (snapshot.ReachableServerCount < servers.Count)
            {
                snapshot.Summary = runtimeName + " reports " + servers.Count + " configured MCP server(s); " +
                    snapshot.ReachableServerCount + " responded and exposed " + snapshot.Tools.Count +
                    " tool(s). Remaining configured MCP servers did not respond at query time and may simply be offline. " + builtInNote;
            }
            else
            {
                snapshot.Summary = runtimeName + " reports " + servers.Count + " configured MCP server(s); " +
                    snapshot.ReachableServerCount + " responded and exposed " + snapshot.Tools.Count + " tool(s). " + builtInNote;
            }

            return snapshot;
        }

        private static void ApplyRuntimeBuiltInInventory(RuntimeToolCatalogSnapshot snapshot, RuntimeBuiltInInventory? builtInInventory)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (builtInInventory == null || builtInInventory.Tools.Count < 1)
            {
                return;
            }

            snapshot.Servers.Add(CreateRuntimeBuiltInSummary(
                builtInInventory.SourceName,
                builtInInventory.Target,
                builtInInventory.Tools.Count));

            snapshot.Tools.AddRange(
                builtInInventory.Tools
                    .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase));
        }

        private async Task<List<RuntimeMcpServerDefinition>> GetCodexServersAsync(string? contextDirectory, CancellationToken token)
        {
            string codexCommand = ResolveCodexCommand();
            CommandExecutionResult listResult = await RunProcessAsync(
                codexCommand,
                new[] { "mcp", "list", "--json" },
                contextDirectory,
                null,
                TimeSpan.FromSeconds(15),
                token).ConfigureAwait(false);

            if (listResult.ExitCode != 0)
            {
                throw new InvalidOperationException(FirstNonEmptyLine(listResult.Stderr, listResult.Stdout));
            }

            List<CodexMcpServerListEntry>? listedServers = JsonSerializer.Deserialize<List<CodexMcpServerListEntry>>(listResult.Stdout, _JsonOptions);
            if (listedServers == null)
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            List<RuntimeMcpServerDefinition> results = new List<RuntimeMcpServerDefinition>();
            foreach (CodexMcpServerListEntry listedServer in listedServers)
            {
                CommandExecutionResult getResult = await RunProcessAsync(
                    codexCommand,
                    new[] { "mcp", "get", listedServer.Name, "--json" },
                    contextDirectory,
                    null,
                    TimeSpan.FromSeconds(15),
                    token).ConfigureAwait(false);

                if (getResult.ExitCode != 0)
                {
                    throw new InvalidOperationException("codex mcp get " + listedServer.Name + " failed: " + FirstNonEmptyLine(getResult.Stderr, getResult.Stdout));
                }

                CodexMcpServerDetail? detail = JsonSerializer.Deserialize<CodexMcpServerDetail>(getResult.Stdout, _JsonOptions);
                if (detail == null || detail.Transport == null)
                {
                    continue;
                }

                RuntimeMcpServerDefinition server = new RuntimeMcpServerDefinition
                {
                    Name = detail.Name,
                    Enabled = detail.Enabled,
                    TransportType = NormalizeTransport(detail.Transport.Type),
                    Url = detail.Transport.Url,
                    Command = detail.Transport.Command,
                    Arguments = detail.Transport.Args ?? new List<string>(),
                    WorkingDirectory = String.IsNullOrWhiteSpace(detail.Transport.Cwd) ? contextDirectory : detail.Transport.Cwd,
                    Environment = detail.Transport.Env,
                    Headers = BuildHttpHeaders(detail.Transport),
                    EnabledTools = detail.EnabledTools ?? new List<string>(),
                    DisabledTools = detail.DisabledTools ?? new List<string>(),
                    StartupTimeout = TimeSpan.FromSeconds(Math.Max(4, detail.StartupTimeoutSec ?? 6)),
                    ToolTimeout = TimeSpan.FromSeconds(Math.Max(4, detail.ToolTimeoutSec ?? 6))
                };
                server.Target = BuildTarget(server);
                results.Add(server);
            }

            return results;
        }

        private async Task<List<RuntimeMcpServerDefinition>> ReadJsonConfiguredServersAsync(string configPath, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            string json = await File.ReadAllTextAsync(configPath, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(json))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            ClaudeCodeSettings? settings = JsonSerializer.Deserialize<ClaudeCodeSettings>(json, _JsonOptions);
            if (settings?.McpServers == null || settings.McpServers.Count == 0)
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            List<RuntimeMcpServerDefinition> servers = new List<RuntimeMcpServerDefinition>();
            foreach (KeyValuePair<string, McpServerEntry> entry in settings.McpServers)
            {
                RuntimeMcpServerDefinition server = new RuntimeMcpServerDefinition
                {
                    Name = entry.Key,
                    Enabled = true,
                    TransportType = NormalizeTransport(
                        entry.Value.Type
                        ?? GetExtensionString(entry.Value, "transport")
                        ?? InferTransport(entry.Value)),
                    Url = entry.Value.Url ?? GetExtensionString(entry.Value, "httpUrl"),
                    Command = entry.Value.Command,
                    Arguments = entry.Value.Args?.ToList() ?? new List<string>(),
                    Environment = GetExtensionStringDictionary(entry.Value, "env"),
                    Headers = GetExtensionStringDictionary(entry.Value, "headers")
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    EnabledTools = new List<string>(),
                    DisabledTools = new List<string>(),
                    StartupTimeout = TimeSpan.FromSeconds(6),
                    ToolTimeout = TimeSpan.FromSeconds(6)
                };
                server.Target = BuildTarget(server);
                servers.Add(server);
            }

            return servers;
        }

        private async Task<List<RuntimeMcpServerDefinition>> ReadMuxConfiguredServersAsync(string configDirectory, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(configDirectory))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            string configPath = Path.Combine(configDirectory, "mcp-servers.json");
            if (!File.Exists(configPath))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            string json = await File.ReadAllTextAsync(configPath, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(json))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            MuxMcpServersFile? file = JsonSerializer.Deserialize<MuxMcpServersFile>(json, _JsonOptions);
            if (file?.Servers == null || file.Servers.Count == 0)
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            List<RuntimeMcpServerDefinition> servers = new List<RuntimeMcpServerDefinition>();
            foreach (MuxMcpServerConfig muxServer in file.Servers)
            {
                string transport = NormalizeTransport(String.IsNullOrWhiteSpace(muxServer.Transport) ? "stdio" : muxServer.Transport);
                string? url = null;
                if (transport == "http" || transport == "streamable_http")
                {
                    string trimmedBaseUrl = muxServer.Url?.Trim() ?? String.Empty;
                    string mcpPath = String.IsNullOrWhiteSpace(muxServer.McpPath) ? "/mcp" : muxServer.McpPath.Trim();
                    url = CombineHttpUrl(trimmedBaseUrl, mcpPath);
                    transport = "streamable_http";
                }

                RuntimeMcpServerDefinition server = new RuntimeMcpServerDefinition
                {
                    Name = muxServer.Name,
                    Enabled = true,
                    TransportType = transport,
                    Url = url,
                    Command = String.IsNullOrWhiteSpace(muxServer.Command) ? null : muxServer.Command.Trim(),
                    Arguments = muxServer.Args?.Where(arg => !String.IsNullOrWhiteSpace(arg)).ToList() ?? new List<string>(),
                    Environment = muxServer.Env?.ToDictionary(
                        kvp => kvp.Key,
                        kvp => ExpandEnvironmentReference(kvp.Value),
                        StringComparer.OrdinalIgnoreCase),
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    EnabledTools = new List<string>(),
                    DisabledTools = new List<string>(),
                    StartupTimeout = TimeSpan.FromSeconds(6),
                    ToolTimeout = TimeSpan.FromSeconds(6)
                };
                server.Target = BuildTarget(server);
                servers.Add(server);
            }

            return servers;
        }

        private async Task<List<CaptainToolSummary>> ProbeServerToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token)
        {
            switch (server.TransportType)
            {
                case "stdio":
                    return await ProbeStdioToolsAsync(server, token).ConfigureAwait(false);
                case "streamable_http":
                case "http":
                    return await ProbeHttpToolsAsync(server, token).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException("Unsupported MCP transport: " + server.TransportType);
            }
        }

        private async Task<List<CaptainToolSummary>> ProbeHttpToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(server.Url))
            {
                throw new InvalidOperationException("HTTP MCP server is missing a URL.");
            }

            string? sessionId = null;
            string initializePayload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "armada",
                        version = "0.8.0"
                    }
                }
            });

            using (HttpRequestMessage initializeRequest = BuildHttpRequest(server, initializePayload, sessionId))
            using (HttpResponseMessage initializeResponse = await _HttpClient.SendAsync(initializeRequest, token).ConfigureAwait(false))
            {
                string initializeContent = NormalizeSseJson(await initializeResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                if (!initializeResponse.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(FirstNonEmptyLine(initializeContent, initializeResponse.ReasonPhrase));
                }

                if (initializeResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
                {
                    sessionId = values.FirstOrDefault();
                }

                EnsureJsonRpcSuccess(initializeContent, 1);
            }

            string initializedPayload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });

            using (HttpRequestMessage initializedRequest = BuildHttpRequest(server, initializedPayload, sessionId))
            using (HttpResponseMessage initializedResponse = await _HttpClient.SendAsync(initializedRequest, token).ConfigureAwait(false))
            {
                if (!initializedResponse.IsSuccessStatusCode && initializedResponse.StatusCode != System.Net.HttpStatusCode.Accepted)
                {
                    string message = await initializedResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    throw new InvalidOperationException(FirstNonEmptyLine(message, initializedResponse.ReasonPhrase));
                }
            }

            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();
            string? cursor = null;
            int requestId = 2;

            do
            {
                object parameters = cursor == null ? new { } : new { cursor };
                string payload = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method = "tools/list",
                    @params = parameters
                });

                using HttpRequestMessage request = BuildHttpRequest(server, payload, sessionId);
                using HttpResponseMessage response = await _HttpClient.SendAsync(request, token).ConfigureAwait(false);
                string content = NormalizeSseJson(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(FirstNonEmptyLine(content, response.ReasonPhrase));
                }

                using JsonDocument document = EnsureJsonRpcSuccess(content, requestId);
                tools.AddRange(ParseToolList(document, server.Name));
                cursor = ExtractNextCursor(document);
                requestId++;
            }
            while (!String.IsNullOrWhiteSpace(cursor));

            return tools;
        }

        private async Task<List<CaptainToolSummary>> ProbeStdioToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token)
        {
            Exception? jsonLineFailure = null;

            try
            {
                return await ProbeStdioToolsAsync(server, RpcWireProtocol.JsonLine, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                jsonLineFailure = ex;
            }

            try
            {
                return await ProbeStdioToolsAsync(server, RpcWireProtocol.ContentLength, token).ConfigureAwait(false);
            }
            catch (Exception framedFailure)
            {
                string message =
                    "JSON-line probe failed: " + FirstNonEmptyLine(jsonLineFailure?.Message, null) + " " +
                    "Content-Length probe failed: " + FirstNonEmptyLine(framedFailure.Message, null);
                throw new InvalidOperationException(message.Trim());
            }
        }

        private async Task<List<CaptainToolSummary>> ProbeStdioToolsAsync(
            RuntimeMcpServerDefinition server,
            RpcWireProtocol protocol,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(server.Command))
            {
                throw new InvalidOperationException("STDIO MCP server is missing a command.");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = server.Command!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in server.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!String.IsNullOrWhiteSpace(server.WorkingDirectory) && Directory.Exists(server.WorkingDirectory))
            {
                startInfo.WorkingDirectory = server.WorkingDirectory;
            }

            if (server.Environment != null)
            {
                foreach (KeyValuePair<string, string> entry in server.Environment)
                {
                    if (!String.IsNullOrWhiteSpace(entry.Key))
                    {
                        startInfo.Environment[entry.Key] = entry.Value;
                    }
                }
            }

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start MCP stdio server process.");
            }

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(server.StartupTimeout + server.ToolTimeout);

            Stream input = process.StandardInput.BaseStream;
            Stream output = process.StandardOutput.BaseStream;
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await WriteRpcMessageAsync(
                    input,
                    JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = "initialize",
                        @params = new
                        {
                            protocolVersion = "2025-03-26",
                            capabilities = new { },
                            clientInfo = new
                            {
                                name = "armada",
                                version = "0.8.0"
                            }
                        }
                    }),
                    protocol,
                    timeoutCts.Token).ConfigureAwait(false);

                using JsonDocument initializeResponse = await ReadRpcResponseAsync(output, 1, protocol, timeoutCts.Token).ConfigureAwait(false);
                EnsureJsonRpcSuccess(initializeResponse);

                await WriteRpcMessageAsync(
                    input,
                    JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        method = "notifications/initialized",
                        @params = new { }
                    }),
                    protocol,
                    timeoutCts.Token).ConfigureAwait(false);

                List<CaptainToolSummary> tools = new List<CaptainToolSummary>();
                string? cursor = null;
                int requestId = 2;

                do
                {
                    object parameters = cursor == null ? new { } : new { cursor };
                    await WriteRpcMessageAsync(
                        input,
                        JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            id = requestId,
                            method = "tools/list",
                            @params = parameters
                        }),
                        protocol,
                        timeoutCts.Token).ConfigureAwait(false);

                    using JsonDocument toolsResponse = await ReadRpcResponseAsync(output, requestId, protocol, timeoutCts.Token).ConfigureAwait(false);
                    EnsureJsonRpcSuccess(toolsResponse);
                    tools.AddRange(ParseToolList(toolsResponse, server.Name));
                    cursor = ExtractNextCursor(toolsResponse);
                    requestId++;
                }
                while (!String.IsNullOrWhiteSpace(cursor));

                return tools;
            }
            catch (Exception ex)
            {
                string stderr = await stderrTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    GetProtocolDisplayName(protocol) + ": " + FirstNonEmptyLine(ex.Message, stderr));
            }
            finally
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }
            }
        }

        private static List<CaptainToolSummary> ApplyToolFilters(RuntimeMcpServerDefinition server, List<CaptainToolSummary> tools)
        {
            IEnumerable<CaptainToolSummary> filtered = tools;

            if (server.EnabledTools.Count > 0)
            {
                HashSet<string> enabled = new HashSet<string>(server.EnabledTools, StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(tool => enabled.Contains(tool.Name));
            }

            if (server.DisabledTools.Count > 0)
            {
                HashSet<string> disabled = new HashSet<string>(server.DisabledTools, StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(tool => !disabled.Contains(tool.Name));
            }

            return filtered
                .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private HttpRequestMessage BuildHttpRequest(RuntimeMcpServerDefinition server, string payload, string? sessionId)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, server.Url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            // MCP Streamable HTTP servers (including Armada's own Voltaic server) require the client to
            // accept BOTH application/json and text/event-stream; sending only application/json is
            // rejected with "requires Accept: application/json, text/event-stream".
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");

            if (!String.IsNullOrWhiteSpace(sessionId))
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            }

            foreach (KeyValuePair<string, string> header in server.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        /// <summary>
        /// Streamable HTTP MCP servers may return a single JSON body or an SSE stream (text/event-stream)
        /// framed as "data: {json}" lines. Return the JSON payload in either case so the caller can parse it.
        /// </summary>
        private static string NormalizeSseJson(string content)
        {
            if (String.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return content;
            }

            StringBuilder builder = new StringBuilder();
            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(line.Substring(5).TrimStart());
                }
            }

            string joined = builder.ToString().Trim();
            return joined.Length > 0 ? joined : content;
        }

        private async Task<CommandExecutionResult> RunProcessAsync(
            string command,
            IEnumerable<string> arguments,
            string? workingDirectory,
            IDictionary<string, string>? environment,
            TimeSpan timeout,
            CancellationToken token)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!String.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> entry in environment)
                {
                    if (!String.IsNullOrWhiteSpace(entry.Key))
                    {
                        startInfo.Environment[entry.Key] = entry.Value;
                    }
                }
            }

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start process: " + command);
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }

                throw new TimeoutException(command + " timed out after " + timeout.TotalSeconds.ToString("0") + " seconds.");
            }

            return new CommandExecutionResult
            {
                ExitCode = process.ExitCode,
                Stdout = (await stdoutTask.ConfigureAwait(false)).Trim(),
                Stderr = (await stderrTask.ConfigureAwait(false)).Trim()
            };
        }

        private static async Task WriteRpcMessageAsync(Stream input, string json, RpcWireProtocol protocol, CancellationToken token)
        {
            if (protocol == RpcWireProtocol.JsonLine)
            {
                byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
                await input.WriteAsync(payload, token).ConfigureAwait(false);
                await input.FlushAsync(token).ConfigureAwait(false);
                return;
            }

            byte[] framedPayload = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + framedPayload.Length + "\r\n\r\n");
            await input.WriteAsync(header, token).ConfigureAwait(false);
            await input.WriteAsync(framedPayload, token).ConfigureAwait(false);
            await input.FlushAsync(token).ConfigureAwait(false);
        }

        private async Task<JsonDocument> ReadRpcResponseAsync(Stream output, int requestId, RpcWireProtocol protocol, CancellationToken token)
        {
            while (true)
            {
                string message = await ReadRpcMessageAsync(output, protocol, token).ConfigureAwait(false);
                JsonDocument? document = TryParseJsonDocument(message);
                if (document == null)
                {
                    continue;
                }

                if (!document.RootElement.TryGetProperty("id", out JsonElement idElement))
                {
                    document.Dispose();
                    continue;
                }

                if (idElement.ValueKind == JsonValueKind.Number && idElement.GetInt32() == requestId)
                {
                    return document;
                }

                if (idElement.ValueKind == JsonValueKind.String && Int32.TryParse(idElement.GetString(), out int parsedId) && parsedId == requestId)
                {
                    return document;
                }

                document.Dispose();
            }
        }

        private static async Task<string> ReadRpcMessageAsync(Stream output, RpcWireProtocol protocol, CancellationToken token)
        {
            return protocol switch
            {
                RpcWireProtocol.JsonLine => await ReadJsonLineMessageAsync(output, token).ConfigureAwait(false),
                _ => await ReadContentLengthMessageAsync(output, token).ConfigureAwait(false)
            };
        }

        private static async Task<string> ReadJsonLineMessageAsync(Stream output, CancellationToken token)
        {
            while (true)
            {
                List<byte> lineBytes = new List<byte>();
                byte[] buffer = new byte[1];

                while (true)
                {
                    int bytesRead = await output.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (bytesRead <= 0)
                    {
                        throw new InvalidOperationException("MCP server closed the stream before replying.");
                    }

                    if (buffer[0] == '\n')
                    {
                        break;
                    }

                    if (buffer[0] != '\r')
                    {
                        lineBytes.Add(buffer[0]);
                    }
                }

                string message = Encoding.UTF8.GetString(lineBytes.ToArray()).Trim();
                if (!String.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }

        private static async Task<string> ReadContentLengthMessageAsync(Stream output, CancellationToken token)
        {
            List<byte> headerBytes = new List<byte>();
            byte[] buffer = new byte[1];

            while (true)
            {
                int bytesRead = await output.ReadAsync(buffer, token).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    throw new InvalidOperationException("MCP server closed the stream before replying.");
                }

                headerBytes.Add(buffer[0]);

                int count = headerBytes.Count;
                if (count >= 4 &&
                    headerBytes[count - 4] == '\r' &&
                    headerBytes[count - 3] == '\n' &&
                    headerBytes[count - 2] == '\r' &&
                    headerBytes[count - 1] == '\n')
                {
                    break;
                }
            }

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            int contentLength = ParseContentLength(headerText);
            byte[] payload = new byte[contentLength];
            int offset = 0;

            while (offset < contentLength)
            {
                int bytesRead = await output.ReadAsync(payload.AsMemory(offset, contentLength - offset), token).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    throw new InvalidOperationException("MCP server closed the stream during message payload.");
                }

                offset += bytesRead;
            }

            return Encoding.UTF8.GetString(payload);
        }

        private static JsonDocument? TryParseJsonDocument(string message)
        {
            string trimmed = message?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            try
            {
                return JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int ParseContentLength(string headers)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring("Content-Length:".Length).Trim();
                    if (Int32.TryParse(value, out int contentLength))
                    {
                        return contentLength;
                    }
                }
            }

            throw new InvalidOperationException("MCP response did not include a valid Content-Length header.");
        }

        private static List<CaptainToolSummary> ParseToolList(JsonDocument document, string sourceName)
        {
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            if (!document.RootElement.TryGetProperty("result", out JsonElement resultElement) ||
                !resultElement.TryGetProperty("tools", out JsonElement toolsElement) ||
                toolsElement.ValueKind != JsonValueKind.Array)
            {
                return tools;
            }

            foreach (JsonElement toolElement in toolsElement.EnumerateArray())
            {
                string name = toolElement.TryGetProperty("name", out JsonElement nameElement)
                    ? (nameElement.GetString() ?? String.Empty)
                    : String.Empty;

                if (String.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string description = toolElement.TryGetProperty("description", out JsonElement descriptionElement)
                    ? (descriptionElement.GetString() ?? String.Empty)
                    : String.Empty;

                string? inputSchemaJson = toolElement.TryGetProperty("inputSchema", out JsonElement schemaElement)
                    ? schemaElement.GetRawText()
                    : null;

                tools.Add(new CaptainToolSummary
                {
                    Name = name,
                    Description = description,
                    InputSchemaJson = inputSchemaJson,
                    RegistrationSource = sourceName,
                    SourceKind = "McpServer"
                });
            }

            return tools;
        }

        private static string? ExtractNextCursor(JsonDocument document)
        {
            if (!document.RootElement.TryGetProperty("result", out JsonElement resultElement))
            {
                return null;
            }

            if (resultElement.TryGetProperty("nextCursor", out JsonElement nextCursorElement) && nextCursorElement.ValueKind == JsonValueKind.String)
            {
                return nextCursorElement.GetString();
            }

            if (resultElement.TryGetProperty("next_cursor", out JsonElement snakeCursorElement) && snakeCursorElement.ValueKind == JsonValueKind.String)
            {
                return snakeCursorElement.GetString();
            }

            return null;
        }

        private static JsonDocument EnsureJsonRpcSuccess(string content, int expectedId)
        {
            JsonDocument document = JsonDocument.Parse(content);

            if (!document.RootElement.TryGetProperty("id", out JsonElement idElement))
            {
                document.Dispose();
                throw new InvalidOperationException("MCP response did not include an id.");
            }

            bool idMatches = (idElement.ValueKind == JsonValueKind.Number && idElement.GetInt32() == expectedId)
                || (idElement.ValueKind == JsonValueKind.String && Int32.TryParse(idElement.GetString(), out int parsed) && parsed == expectedId);

            if (!idMatches)
            {
                document.Dispose();
                throw new InvalidOperationException("MCP response id did not match the request.");
            }

            EnsureJsonRpcSuccess(document);
            return document;
        }

        private static void EnsureJsonRpcSuccess(JsonDocument document)
        {
            if (!document.RootElement.TryGetProperty("error", out JsonElement errorElement))
            {
                return;
            }

            string message = errorElement.TryGetProperty("message", out JsonElement messageElement)
                ? (messageElement.GetString() ?? errorElement.GetRawText())
                : errorElement.GetRawText();
            throw new InvalidOperationException(message);
        }

        private async Task<string?> ResolveContextDirectoryAsync(Captain captain, DatabaseDriver database)
        {
            if (!String.IsNullOrWhiteSpace(captain.CurrentDockId))
            {
                Dock? dock = await database.Docks.ReadAsync(captain.CurrentDockId).ConfigureAwait(false);
                if (dock != null && !String.IsNullOrWhiteSpace(dock.WorktreePath) && Directory.Exists(dock.WorktreePath))
                {
                    return dock.WorktreePath;
                }
            }

            if (!String.IsNullOrWhiteSpace(captain.CurrentMissionId))
            {
                Mission? mission = await database.Missions.ReadAsync(captain.CurrentMissionId).ConfigureAwait(false);
                if (mission != null && !String.IsNullOrWhiteSpace(mission.VesselId))
                {
                    Vessel? vessel = await database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
                    if (vessel != null)
                    {
                        string? candidate = vessel.WorkingDirectory;
                        if (!String.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                        {
                            return candidate;
                        }

                        candidate = vessel.LocalPath;
                        if (!String.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        private static string ResolveCodexCommand()
        {
            if (!OperatingSystem.IsWindows())
            {
                return "codex";
            }

            string candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                "codex.cmd");

            return File.Exists(candidate) ? candidate : "codex.cmd";
        }

        private static string GetClaudeConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
        }

        private static string GetGeminiConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "settings.json");
        }

        private static RuntimeBuiltInInventory? TryLoadClaudeBuiltInInventory()
        {
            string sdkToolsPath = GetClaudeSdkToolsPath();
            if (!File.Exists(sdkToolsPath))
            {
                return null;
            }

            string contents = File.ReadAllText(sdkToolsPath);
            MatchCollection matches = Regex.Matches(contents, @"^\s*\|\s*([A-Za-z0-9]+Input)\s*$", RegexOptions.Multiline);

            if (matches.Count < 1)
            {
                return null;
            }

            const string sourceName = "Built-In";
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            foreach (Match match in matches)
            {
                string inputName = match.Groups[1].Value;
                if (!seen.Add(inputName))
                {
                    continue;
                }

                ClaudeBuiltInToolDefinition definition = GetClaudeBuiltInToolDefinition(inputName);
                tools.Add(CreateRuntimeBuiltInTool(definition.Name, definition.Description, sourceName));
            }

            return new RuntimeBuiltInInventory
            {
                SourceName = sourceName,
                Target = "Installed CLI schema",
                Note = "Armada enumerated " + tools.Count + " Claude Code built-in tool(s) from the installed CLI schema.",
                Tools = tools
            };
        }

        private static RuntimeBuiltInInventory? TryLoadGeminiBuiltInInventory()
        {
            string corePackagePath = GetGeminiCliCorePackagePath();
            if (String.IsNullOrWhiteSpace(corePackagePath))
            {
                return null;
            }

            string baseDeclarationsPath = Path.Combine(corePackagePath, "dist", "src", "tools", "definitions", "base-declarations.js");
            string toolNamesPath = Path.Combine(corePackagePath, "dist", "src", "tools", "tool-names.js");
            string docsPath = Path.Combine(corePackagePath, "dist", "docs", "reference", "tools.md");

            if (!File.Exists(baseDeclarationsPath) || !File.Exists(toolNamesPath))
            {
                return null;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(
                File.ReadAllText(baseDeclarationsPath),
                @"export const [A-Z0-9_]+_TOOL_NAME = '([^']+)';"))
            {
                names.Add(match.Groups[1].Value);
            }

            foreach (Match match in Regex.Matches(
                File.ReadAllText(toolNamesPath),
                @"export const TRACKER_[A-Z0-9_]+_TOOL_NAME = '([^']+)';"))
            {
                names.Add(match.Groups[1].Value);
            }

            Dictionary<string, string> descriptions = File.Exists(docsPath)
                ? ParseGeminiToolDescriptions(docsPath)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            const string sourceName = "Gemini CLI Built-In Tools";
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            foreach (string name in names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string description = descriptions.TryGetValue(name, out string? documented)
                    ? documented
                    : GetGeminiFallbackDescription(name);
                tools.Add(CreateRuntimeBuiltInTool(name, description, sourceName));
            }

            if (descriptions.TryGetValue("complete_task", out string? completeTaskDescription))
            {
                tools.Add(CreateRuntimeBuiltInTool("complete_task", completeTaskDescription, sourceName));
            }

            return new RuntimeBuiltInInventory
            {
                SourceName = sourceName,
                Target = "Installed CLI package",
                Note = "Armada enumerated " + tools.Count + " Gemini CLI built-in tool(s) from the installed CLI package.",
                Tools = tools
            };
        }

        private static Dictionary<string, string> ParseGeminiToolDescriptions(string docsPath)
        {
            Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in File.ReadLines(docsPath))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal) || line.StartsWith("| :", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] cells = line.Split('|');
                if (cells.Length < 5)
                {
                    continue;
                }

                string? toolName = ExtractGeminiToolName(cells[2].Trim());
                if (String.IsNullOrWhiteSpace(toolName))
                {
                    continue;
                }

                string description = cells[4].Trim().Replace("<br><br>", " ", StringComparison.Ordinal);
                int parameterIndex = description.IndexOf("**Parameters:**", StringComparison.Ordinal);
                if (parameterIndex >= 0)
                {
                    description = description.Substring(0, parameterIndex).Trim();
                }

                if (!String.IsNullOrWhiteSpace(description))
                {
                    descriptions[toolName] = description;
                }
            }

            descriptions["tracker_create_task"] = "Creates a new tracker task in Gemini's built-in task graph.";
            descriptions["tracker_update_task"] = "Updates an existing tracker task in Gemini's built-in task graph.";
            descriptions["tracker_get_task"] = "Reads one tracker task from Gemini's built-in task graph.";
            descriptions["tracker_list_tasks"] = "Lists tracker tasks from Gemini's built-in task graph.";
            descriptions["tracker_add_dependency"] = "Adds a dependency edge between Gemini tracker tasks.";
            descriptions["tracker_visualize"] = "Renders Gemini's built-in task graph and dependencies.";

            return descriptions;
        }

        private static string? ExtractGeminiToolName(string toolCell)
        {
            if (String.IsNullOrWhiteSpace(toolCell))
            {
                return null;
            }

            if (toolCell.StartsWith("[`", StringComparison.Ordinal))
            {
                int end = toolCell.IndexOf("`]", StringComparison.Ordinal);
                if (end > 2)
                {
                    return toolCell.Substring(2, end - 2).Trim();
                }
            }

            if (toolCell.StartsWith("`", StringComparison.Ordinal) && toolCell.EndsWith("`", StringComparison.Ordinal) && toolCell.Length > 2)
            {
                return toolCell.Substring(1, toolCell.Length - 2).Trim();
            }

            return null;
        }

        private static string GetGeminiFallbackDescription(string name)
        {
            return name switch
            {
                "tracker_create_task" => "Creates a new tracker task in Gemini's built-in task graph.",
                "tracker_update_task" => "Updates an existing tracker task in Gemini's built-in task graph.",
                "tracker_get_task" => "Reads one tracker task from Gemini's built-in task graph.",
                "tracker_list_tasks" => "Lists tracker tasks from Gemini's built-in task graph.",
                "tracker_add_dependency" => "Adds a dependency edge between Gemini tracker tasks.",
                "tracker_visualize" => "Renders Gemini's built-in task graph and dependencies.",
                _ => "Gemini CLI built-in tool."
            };
        }

        private static ClaudeBuiltInToolDefinition GetClaudeBuiltInToolDefinition(string inputName)
        {
            return inputName switch
            {
                "AgentInput" => new ClaudeBuiltInToolDefinition("Agent", "Spawns or resumes specialized subagents."),
                "AskUserQuestionInput" => new ClaudeBuiltInToolDefinition("AskUserQuestion", "Asks the user follow-up questions when more input is needed."),
                "BashInput" => new ClaudeBuiltInToolDefinition("Bash", "Runs shell commands in the current workspace."),
                "ConfigInput" => new ClaudeBuiltInToolDefinition("Config", "Reads or updates Claude Code runtime settings."),
                "EnterWorktreeInput" => new ClaudeBuiltInToolDefinition("EnterWorktree", "Creates or enters an isolated git worktree."),
                "ExitPlanModeInput" => new ClaudeBuiltInToolDefinition("ExitPlanMode", "Finalizes a plan and requests approval to implement it."),
                "ExitWorktreeInput" => new ClaudeBuiltInToolDefinition("ExitWorktree", "Leaves or removes an isolated git worktree."),
                "FileEditInput" => new ClaudeBuiltInToolDefinition("Edit", "Replaces exact text inside an existing file."),
                "FileReadInput" => new ClaudeBuiltInToolDefinition("Read", "Reads file contents, including notebooks and PDFs."),
                "FileWriteInput" => new ClaudeBuiltInToolDefinition("Write", "Writes file contents from scratch."),
                "GlobInput" => new ClaudeBuiltInToolDefinition("Glob", "Finds files by glob pattern."),
                "GrepInput" => new ClaudeBuiltInToolDefinition("Grep", "Searches file contents with ripgrep."),
                "ListMcpResourcesInput" => new ClaudeBuiltInToolDefinition("ListMcpResources", "Lists MCP resources exposed by configured servers."),
                "McpInput" => new ClaudeBuiltInToolDefinition("Mcp", "Invokes a tool through a configured MCP server."),
                "NotebookEditInput" => new ClaudeBuiltInToolDefinition("NotebookEdit", "Edits notebook cells in Jupyter notebooks."),
                "ReadMcpResourceInput" => new ClaudeBuiltInToolDefinition("ReadMcpResource", "Reads one resource from a configured MCP server."),
                "SubscribeMcpResourceInput" => new ClaudeBuiltInToolDefinition("SubscribeMcpResource", "Subscribes to change notifications for an MCP resource."),
                "SubscribePollingInput" => new ClaudeBuiltInToolDefinition("SubscribePolling", "Polls an MCP tool or resource on a recurring interval."),
                "TaskOutputInput" => new ClaudeBuiltInToolDefinition("TaskOutput", "Reads or waits on output from a background task."),
                "TaskStopInput" => new ClaudeBuiltInToolDefinition("TaskStop", "Stops a running background task."),
                "TodoWriteInput" => new ClaudeBuiltInToolDefinition("TodoWrite", "Maintains Claude Code's internal task list."),
                "UnsubscribeMcpResourceInput" => new ClaudeBuiltInToolDefinition("UnsubscribeMcpResource", "Stops an MCP resource subscription."),
                "UnsubscribePollingInput" => new ClaudeBuiltInToolDefinition("UnsubscribePolling", "Stops a recurring MCP polling subscription."),
                "WebFetchInput" => new ClaudeBuiltInToolDefinition("WebFetch", "Fetches and processes a specific URL."),
                "WebSearchInput" => new ClaudeBuiltInToolDefinition("WebSearch", "Searches the web for up-to-date information."),
                _ => new ClaudeBuiltInToolDefinition(
                    inputName.EndsWith("Input", StringComparison.Ordinal)
                        ? inputName.Substring(0, inputName.Length - "Input".Length)
                        : inputName,
                    "Claude Code built-in tool.")
            };
        }

        private static string GetClaudeSdkToolsPath()
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm",
                    "node_modules",
                    "@anthropic-ai",
                    "claude-code",
                    "sdk-tools.d.ts");
            }

            return Path.Combine(
                "/usr",
                "local",
                "lib",
                "node_modules",
                "@anthropic-ai",
                "claude-code",
                "sdk-tools.d.ts");
        }

        private static string GetGeminiCliCorePackagePath()
        {
            if (OperatingSystem.IsWindows())
            {
                string candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm",
                    "node_modules",
                    "@google",
                    "gemini-cli",
                    "node_modules",
                    "@google",
                    "gemini-cli-core");

                return Directory.Exists(candidate) ? candidate : String.Empty;
            }

            string unixCandidate = Path.Combine(
                "/usr",
                "local",
                "lib",
                "node_modules",
                "@google",
                "gemini-cli",
                "node_modules",
                "@google",
                "gemini-cli-core");

            return Directory.Exists(unixCandidate) ? unixCandidate : String.Empty;
        }

        private static string NormalizeTransport(string? transport)
        {
            if (String.IsNullOrWhiteSpace(transport))
            {
                return "unknown";
            }

            return transport switch
            {
                "http" => "streamable_http",
                "streamable-http" => "streamable_http",
                _ => transport.Trim()
            };
        }

        private static string InferTransport(McpServerEntry entry)
        {
            if (!String.IsNullOrWhiteSpace(entry.Command))
            {
                return "stdio";
            }

            if (!String.IsNullOrWhiteSpace(entry.Url) || !String.IsNullOrWhiteSpace(GetExtensionString(entry, "httpUrl")))
            {
                return "streamable_http";
            }

            return "unknown";
        }

        private static string BuildTarget(RuntimeMcpServerDefinition server)
        {
            if (!String.IsNullOrWhiteSpace(server.Url))
            {
                return SanitizeUrl(server.Url) ?? String.Empty;
            }

            if (!String.IsNullOrWhiteSpace(server.Command))
            {
                return server.Command!.Trim();
            }

            return String.Empty;
        }

        private static CaptainToolServerSummary CreateConfiguredServerSummary(RuntimeMcpServerDefinition server)
        {
            return new CaptainToolServerSummary
            {
                Name = server.Name,
                SourceKind = "McpServer",
                Transport = server.TransportType,
                Target = server.Target,
                Url = SanitizeUrl(server.Url),
                Command = String.IsNullOrWhiteSpace(server.Command) ? null : server.Command.Trim(),
                WorkingDirectory = String.IsNullOrWhiteSpace(server.WorkingDirectory) ? null : server.WorkingDirectory.Trim(),
                Enabled = server.Enabled,
                Reachable = false,
                ToolCount = 0,
                HeaderCount = server.Headers?.Count ?? 0,
                EnvironmentVariableCount = server.Environment?.Count ?? 0,
                EnabledToolFilterCount = server.EnabledTools?.Count ?? 0,
                DisabledToolFilterCount = server.DisabledTools?.Count ?? 0,
                StartupTimeoutSeconds = (int)Math.Max(0, Math.Round(server.StartupTimeout.TotalSeconds)),
                ToolTimeoutSeconds = (int)Math.Max(0, Math.Round(server.ToolTimeout.TotalSeconds)),
                Status = server.Enabled ? "Pending probe" : "Disabled"
            };
        }

        private static CaptainToolServerSummary CreateMuxBuiltInSummary(MuxProbeResult probe, int builtInToolCount)
        {
            return new CaptainToolServerSummary
            {
                Name = "Mux Built-In Tools",
                SourceKind = "RuntimeBuiltIn",
                Transport = "mux",
                Target = BuildMuxBuiltInTarget(probe),
                Url = SanitizeUrl(probe.BaseUrl),
                Enabled = probe.ToolsEnabled,
                Reachable = probe.Success && probe.ToolsEnabled,
                ToolCount = builtInToolCount,
                HeaderCount = 0,
                EnvironmentVariableCount = 0,
                EnabledToolFilterCount = 0,
                DisabledToolFilterCount = 0,
                StartupTimeoutSeconds = 0,
                ToolTimeoutSeconds = 0,
                Status = probe.Success
                    ? (builtInToolCount > 0 ? "Available (names unavailable)" : "Available")
                    : "Unreachable at query time",
                ErrorMessage = probe.Success ? null : probe.ErrorMessage
            };
        }

        private static CaptainToolServerSummary CreateRuntimeBuiltInSummary(string sourceName, string target, int toolCount)
        {
            return new CaptainToolServerSummary
            {
                Name = sourceName,
                SourceKind = "RuntimeBuiltIn",
                Transport = "internal",
                Target = target,
                Enabled = true,
                Reachable = true,
                ToolCount = toolCount,
                HeaderCount = 0,
                EnvironmentVariableCount = 0,
                EnabledToolFilterCount = 0,
                DisabledToolFilterCount = 0,
                StartupTimeoutSeconds = 0,
                ToolTimeoutSeconds = 0,
                Status = "Available"
            };
        }

        private static CaptainToolSummary CreateRuntimeBuiltInTool(string name, string description, string sourceName)
        {
            return new CaptainToolSummary
            {
                Name = name,
                Description = description,
                RegistrationSource = sourceName,
                SourceKind = "RuntimeBuiltIn"
            };
        }

        private static string ResolveMuxConfigDirectory(MuxProbeResult? probe, MuxCaptainOptions? options)
        {
            if (!String.IsNullOrWhiteSpace(probe?.ConfigDirectory))
            {
                return probe.ConfigDirectory;
            }

            if (!String.IsNullOrWhiteSpace(options?.ConfigDirectory))
            {
                return options.ConfigDirectory!;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mux");
        }

        private static string BuildMuxBuiltInTarget(MuxProbeResult probe)
        {
            List<string> parts = new List<string>();

            if (!String.IsNullOrWhiteSpace(probe.EndpointName))
            {
                parts.Add(probe.EndpointName);
            }

            if (!String.IsNullOrWhiteSpace(probe.AdapterType))
            {
                parts.Add(probe.AdapterType);
            }

            if (!String.IsNullOrWhiteSpace(probe.Model))
            {
                parts.Add(probe.Model);
            }

            if (!String.IsNullOrWhiteSpace(probe.BaseUrl))
            {
                parts.Add(SanitizeUrl(probe.BaseUrl) ?? probe.BaseUrl);
            }

            return parts.Count > 0 ? String.Join(" | ", parts) : "Mux runtime";
        }

        private static string? SanitizeUrl(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
            {
                return trimmed;
            }

            UriBuilder builder = new UriBuilder(uri)
            {
                UserName = String.Empty,
                Password = String.Empty,
                Query = String.Empty,
                Fragment = String.Empty
            };

            return builder.Uri.GetLeftPart(UriPartial.Path);
        }

        private static string CombineHttpUrl(string baseUrl, string mcpPath)
        {
            if (String.IsNullOrWhiteSpace(baseUrl))
            {
                return String.Empty;
            }

            string normalizedBase = baseUrl.TrimEnd('/');
            string normalizedPath = String.IsNullOrWhiteSpace(mcpPath) ? "/mcp" : mcpPath.Trim();
            if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
            {
                normalizedPath = "/" + normalizedPath;
            }

            return normalizedBase + normalizedPath;
        }

        private static string ExpandEnvironmentReference(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return String.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal) && trimmed.Length > 3)
            {
                string variableName = trimmed.Substring(2, trimmed.Length - 3);
                return Environment.GetEnvironmentVariable(variableName) ?? String.Empty;
            }

            return trimmed;
        }

        private static Dictionary<string, string> BuildHttpHeaders(CodexMcpTransport transport)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (transport.HttpHeaders != null)
            {
                foreach (KeyValuePair<string, string> header in transport.HttpHeaders)
                {
                    headers[header.Key] = header.Value;
                }
            }

            if (transport.EnvHttpHeaders != null)
            {
                foreach (KeyValuePair<string, string> entry in transport.EnvHttpHeaders)
                {
                    string? value = Environment.GetEnvironmentVariable(entry.Value);
                    if (!String.IsNullOrWhiteSpace(value))
                    {
                        headers[entry.Key] = value;
                    }
                }
            }

            if (!String.IsNullOrWhiteSpace(transport.BearerTokenEnvVar))
            {
                string? bearerToken = Environment.GetEnvironmentVariable(transport.BearerTokenEnvVar);
                if (!String.IsNullOrWhiteSpace(bearerToken))
                {
                    headers["Authorization"] = "Bearer " + bearerToken;
                }
            }

            return headers;
        }

        private static string? GetExtensionString(McpServerEntry entry, string key)
        {
            if (entry.AdditionalProperties == null || !entry.AdditionalProperties.TryGetValue(key, out object? value) || value == null)
            {
                return null;
            }

            if (value is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            }

            return value.ToString();
        }

        private static Dictionary<string, string>? GetExtensionStringDictionary(McpServerEntry entry, string key)
        {
            if (entry.AdditionalProperties == null || !entry.AdditionalProperties.TryGetValue(key, out object? value) || value == null)
            {
                return null;
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                Dictionary<string, string> results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        results[property.Name] = property.Value.GetString() ?? String.Empty;
                    }
                }

                return results.Count > 0 ? results : null;
            }

            return null;
        }

        private static string FirstNonEmptyLine(string? primary, string? secondary)
        {
            foreach (string source in new[] { primary ?? String.Empty, secondary ?? String.Empty })
            {
                foreach (string line in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!String.IsNullOrWhiteSpace(trimmed))
                    {
                        return trimmed;
                    }
                }
            }

            return String.Empty;
        }

        private static string GetProtocolDisplayName(RpcWireProtocol protocol)
        {
            return protocol switch
            {
                RpcWireProtocol.JsonLine => "json-line",
                _ => "content-length"
            };
        }

        private sealed class CommandExecutionResult
        {
            public int ExitCode { get; set; } = 0;
            public string Stdout { get; set; } = String.Empty;
            public string Stderr { get; set; } = String.Empty;
        }

        /// <summary>
        /// Where a captain's runtime CLI should be executed for probing, plus the working directory and
        /// (in split mode) the owning Harbor.
        /// </summary>
        private sealed class RuntimeHostContext
        {
            public IHostCommandExecutor Executor { get; set; } = new LocalHostCommandExecutor();
            public string? WorkingDirectory { get; set; } = null;
            public bool IsRemote { get; set; } = false;
            public string? HarborId { get; set; } = null;
        }

        private enum RpcWireProtocol
        {
            JsonLine,
            ContentLength
        }

        internal sealed class RuntimeToolCatalogSnapshot
        {
            public bool ToolsAccessible { get; set; } = false;
            public bool AvailabilityVerified { get; set; } = false;
            public string AvailabilitySource { get; set; } = String.Empty;
            public string Summary { get; set; } = String.Empty;
            public int ArmadaToolCount { get; set; } = 0;
            public int ConfiguredServerCount { get; set; } = 0;
            public int ReachableServerCount { get; set; } = 0;
            public int EffectiveToolCount { get; set; } = 0;
            public List<CaptainToolServerSummary> Servers { get; set; } = new List<CaptainToolServerSummary>();
            public List<CaptainToolSummary> Tools { get; set; } = new List<CaptainToolSummary>();
        }

        private sealed class RuntimeMcpServerDefinition
        {
            public string Name { get; set; } = String.Empty;
            public bool Enabled { get; set; } = true;
            public string TransportType { get; set; } = String.Empty;
            public string? Url { get; set; } = null;
            public string? Command { get; set; } = null;
            public List<string> Arguments { get; set; } = new List<string>();
            public string? WorkingDirectory { get; set; } = null;
            public Dictionary<string, string>? Environment { get; set; } = null;
            public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<string> EnabledTools { get; set; } = new List<string>();
            public List<string> DisabledTools { get; set; } = new List<string>();
            public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(15);
            public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(15);
            public string Target { get; set; } = String.Empty;
        }

        private sealed class MuxMcpServersFile
        {
            [JsonPropertyName("servers")]
            public List<MuxMcpServerConfig>? Servers { get; set; } = null;
        }

        private sealed class MuxMcpServerConfig
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = String.Empty;

            [JsonPropertyName("transport")]
            public string? Transport { get; set; } = null;

            [JsonPropertyName("command")]
            public string? Command { get; set; } = null;

            [JsonPropertyName("args")]
            public List<string>? Args { get; set; } = null;

            [JsonPropertyName("env")]
            public Dictionary<string, string>? Env { get; set; } = null;

            [JsonPropertyName("url")]
            public string? Url { get; set; } = null;

            [JsonPropertyName("mcpPath")]
            public string? McpPath { get; set; } = null;
        }

        private sealed class CodexMcpServerListEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = String.Empty;
        }

        private sealed class CodexMcpServerDetail
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = String.Empty;

            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; } = true;

            [JsonPropertyName("transport")]
            public CodexMcpTransport? Transport { get; set; } = null;

            [JsonPropertyName("enabled_tools")]
            public List<string>? EnabledTools { get; set; } = null;

            [JsonPropertyName("disabled_tools")]
            public List<string>? DisabledTools { get; set; } = null;

            [JsonPropertyName("startup_timeout_sec")]
            public int? StartupTimeoutSec { get; set; } = null;

            [JsonPropertyName("tool_timeout_sec")]
            public int? ToolTimeoutSec { get; set; } = null;
        }

        private sealed class CodexMcpTransport
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = String.Empty;

            [JsonPropertyName("url")]
            public string? Url { get; set; } = null;

            [JsonPropertyName("command")]
            public string? Command { get; set; } = null;

            [JsonPropertyName("args")]
            public List<string>? Args { get; set; } = null;

            [JsonPropertyName("env")]
            public Dictionary<string, string>? Env { get; set; } = null;

            [JsonPropertyName("cwd")]
            public string? Cwd { get; set; } = null;

            [JsonPropertyName("bearer_token_env_var")]
            public string? BearerTokenEnvVar { get; set; } = null;

            [JsonPropertyName("http_headers")]
            public Dictionary<string, string>? HttpHeaders { get; set; } = null;

            [JsonPropertyName("env_http_headers")]
            public Dictionary<string, string>? EnvHttpHeaders { get; set; } = null;
        }

        private sealed class RuntimeBuiltInInventory
        {
            public string SourceName { get; set; } = String.Empty;
            public string Target { get; set; } = String.Empty;
            public string Note { get; set; } = String.Empty;
            public List<CaptainToolSummary> Tools { get; set; } = new List<CaptainToolSummary>();
        }

        private sealed record ClaudeBuiltInToolDefinition(string Name, string Description);
    }
}
