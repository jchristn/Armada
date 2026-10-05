namespace Armada.Server
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using Armada.Server.RuntimeTools;
    using SyslogLogging;

    /// <summary>
    /// Discovers runtime-visible MCP servers and probes them for tool inventories. Every host touch point (config files,
    /// runtime CLIs, installed-package inventories, MCP server connections) goes through an
    /// <see cref="IRuntimeToolDiscoverySource"/>.
    /// </summary>
    internal sealed class CaptainRuntimeToolCatalogService
    {
        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly HarborConnectionManager? _HarborConnections;
        private readonly IRuntimeToolDiscoverySource _Discovery;
        private readonly int _McpPort;
        private readonly string _McpHost;
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="harborConnections">Harbor connection manager, or null in standalone mode.</param>
        /// <param name="discovery">Discovery source for host touch points; null uses <see cref="HostRuntimeToolDiscoverySource"/>.</param>
        /// <param name="mcpPort">The Admiral MCP port, used to recognize configured entries that point at Armada; 0 disables
        /// URL-based recognition (entries named "armada" are always recognized).</param>
        /// <param name="mcpHost">The host MCP clients must use to reach the Admiral (see
        /// <see cref="ArmadaMcpConfigBuilder.ClientHostFor"/>); null or empty means localhost.</param>
        public CaptainRuntimeToolCatalogService(
            LoggingModule logging,
            HarborConnectionManager? harborConnections = null,
            IRuntimeToolDiscoverySource? discovery = null,
            int mcpPort = 0,
            string? mcpHost = null)
        {
            _McpPort = mcpPort > 0 ? mcpPort : 0;
            _McpHost = String.IsNullOrWhiteSpace(mcpHost) ? ArmadaMcpConfigBuilder.DefaultHost : mcpHost!;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _HarborConnections = harborConnections;
            _Discovery = discovery ?? new HostRuntimeToolDiscoverySource(logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe the tool sources visible from a captain's runtime.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot, or null when the runtime has no inventory implementation.</returns>
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
                        Path.Combine(_Discovery.GetUserProfileDirectory(), ".claude.json"),
                        _Discovery.ReadBuiltInToolInventory(AgentRuntimeEnum.ClaudeCode),
                        token,
                        "Claude Code built-in tools are not currently enumerated by Armada.")
                        .ConfigureAwait(false);
                case AgentRuntimeEnum.Gemini:
                    return await DescribeConfiguredRuntimeAsync(
                        "Gemini CLI",
                        Path.Combine(_Discovery.GetUserProfileDirectory(), ".gemini", "settings.json"),
                        _Discovery.ReadBuiltInToolInventory(AgentRuntimeEnum.Gemini),
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
                        ToolsAccessible = true,
                        AvailabilityVerified = true,
                        AvailabilitySource = "api-endpoint-builtin-tools",
                        Summary = "API-endpoint captains run Armada's built-in coding tools (read, write, edit, search, run-process) in-process against a working directory. When Armada launches them for an Ask Armada turn or a mission, it also hands them its MCP endpoint and a scoped session token (ARMADA_MCP_URL and ARMADA_MCP_TOKEN), and the in-process loop connects to Armada's MCP server as an MCP client and adds Armada's tools to the built-in ones. They read no other MCP configuration, so there is no runtime MCP config file to inspect or connect."
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

        #endregion

        #region Private-Methods

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
            RuntimeBuiltInToolInventory? builtInInventory,
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
                probe = await _Discovery.ProbeMuxAsync(captain, host.Executor, host.WorkingDirectory, token).ConfigureAwait(false);
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
                snapshot.ArmadaToolCount = snapshot.Tools.Count(t => t.RegistrationSource != null && snapshot.ArmadaServerNames.Contains(t.RegistrationSource));
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
                        : snapshot.Servers.Count(s => s.SourceKind == CaptainToolSourceKindEnum.McpServer && s.Reachable) +
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
                        snapshot.Servers.Count(s => s.SourceKind == CaptainToolSourceKindEnum.McpServer && s.Reachable) +
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
                if (_McpPort > 0 && ArmadaMcpConfigBuilder.IsArmadaMcpUrl(server.Url, _McpPort, _McpHost))
                {
                    // localhost, 127.0.0.1 and ::1 on the MCP port are the same Armada server. The listener only answers
                    // the host it is bound with, so probe the entry at that host; an entry written with localhost is
                    // then still recognized when the Admiral is bound to 127.0.0.1 (Ask turns connect through a
                    // generated configuration that already uses the bound host).
                    snapshot.ArmadaServerNames.Add(server.Name);
                    capturedServer = server.WithUrl(ArmadaMcpConfigBuilder.RewriteArmadaMcpUrlHost(server.Url!, _McpPort, _McpHost));
                }

                CaptainToolServerSummary capturedSummary = serverSummary;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        List<McpRemoteTool> listed = await _Discovery.ListServerToolsAsync(capturedServer, token).ConfigureAwait(false);
                        List<CaptainToolSummary> tools = ApplyToolFilters(capturedServer, ToToolSummaries(listed, capturedServer.Name));

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
            RuntimeBuiltInToolInventory? builtInInventory,
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
            snapshot.ArmadaToolCount = snapshot.Tools.Count(t => t.RegistrationSource != null && snapshot.ArmadaServerNames.Contains(t.RegistrationSource));
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

        private static void ApplyRuntimeBuiltInInventory(RuntimeToolCatalogSnapshot snapshot, RuntimeBuiltInToolInventory? builtInInventory)
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
            RuntimeCommandResult listResult = await _Discovery.RunCodexAsync(
                new List<string> { "mcp", "list", "--json" },
                contextDirectory,
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
                RuntimeCommandResult getResult = await _Discovery.RunCodexAsync(
                    new List<string> { "mcp", "get", listedServer.Name, "--json" },
                    contextDirectory,
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
            if (String.IsNullOrWhiteSpace(configPath))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            string? json = await _Discovery.ReadConfigFileAsync(configPath, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(json))
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            RuntimeJsonMcpConfigFile? settings = JsonSerializer.Deserialize<RuntimeJsonMcpConfigFile>(json, _JsonOptions);
            if (settings?.McpServers == null || settings.McpServers.Count == 0)
            {
                return new List<RuntimeMcpServerDefinition>();
            }

            List<RuntimeMcpServerDefinition> servers = new List<RuntimeMcpServerDefinition>();
            foreach (KeyValuePair<string, RuntimeJsonMcpServerEntry> entry in settings.McpServers)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                RuntimeMcpServerDefinition server = new RuntimeMcpServerDefinition
                {
                    Name = entry.Key,
                    Enabled = true,
                    TransportType = NormalizeTransport(
                        entry.Value.Type
                        ?? entry.Value.Transport
                        ?? InferTransport(entry.Value)),
                    Url = entry.Value.Url ?? entry.Value.HttpUrl,
                    Command = entry.Value.Command,
                    Arguments = entry.Value.Args?.ToList() ?? new List<string>(),
                    Environment = entry.Value.Env != null && entry.Value.Env.Count > 0 ? entry.Value.Env : null,
                    Headers = entry.Value.Headers != null && entry.Value.Headers.Count > 0
                        ? entry.Value.Headers
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
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
            string? json = await _Discovery.ReadConfigFileAsync(configPath, token).ConfigureAwait(false);
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

        private static List<CaptainToolSummary> ToToolSummaries(List<McpRemoteTool> listed, string sourceName)
        {
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();
            if (listed == null)
            {
                return tools;
            }

            foreach (McpRemoteTool tool in listed)
            {
                if (tool == null || String.IsNullOrWhiteSpace(tool.Name))
                {
                    continue;
                }

                tools.Add(new CaptainToolSummary
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchemaJson = String.IsNullOrEmpty(tool.InputSchemaJson) ? null : tool.InputSchemaJson,
                    RegistrationSource = sourceName,
                    SourceKind = CaptainToolSourceKindEnum.McpServer
                });
            }

            return tools;
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

        private static string InferTransport(RuntimeJsonMcpServerEntry entry)
        {
            if (!String.IsNullOrWhiteSpace(entry.Command))
            {
                return "stdio";
            }

            if (!String.IsNullOrWhiteSpace(entry.Url) || !String.IsNullOrWhiteSpace(entry.HttpUrl))
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
                SourceKind = CaptainToolSourceKindEnum.McpServer,
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
                SourceKind = CaptainToolSourceKindEnum.RuntimeBuiltIn,
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
                SourceKind = CaptainToolSourceKindEnum.RuntimeBuiltIn,
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

        private string ResolveMuxConfigDirectory(MuxProbeResult? probe, MuxCaptainOptions? options)
        {
            if (!String.IsNullOrWhiteSpace(probe?.ConfigDirectory))
            {
                return probe.ConfigDirectory;
            }

            if (!String.IsNullOrWhiteSpace(options?.ConfigDirectory))
            {
                return options.ConfigDirectory!;
            }

            return Path.Combine(_Discovery.GetUserProfileDirectory(), ".mux");
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

        #endregion

        #region Private-Members-Types

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
            public HashSet<string> ArmadaServerNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "armada" };
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

        #endregion
    }
}
