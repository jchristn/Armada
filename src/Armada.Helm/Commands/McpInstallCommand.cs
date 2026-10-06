namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Net.Http;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Configure MCP integration for supported clients.
    /// </summary>
    [Description("Configure MCP integration for Claude Code, Codex, Gemini, Cursor, and Mux (when detected)")]
    public class McpInstallCommand : BaseCommand<McpInstallSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, McpInstallSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
            string mcpHost = ArmadaMcpConfigBuilder.ClientHostFor(armadaSettings.Rest.Hostname);
            string mcpUrl = McpConfigHelper.GetMcpUrl(armadaSettings.McpPort, mcpHost);

            // A remote target (--server, ARMADA_SERVER_URL, a remote profile) or an explicit --mcp-url writes the remote
            // HTTP endpoint with a bearer token; otherwise the local install is unchanged.
            AdmiralTarget admiral = GetTarget();
            bool remote = !admiral.IsLocal || !String.IsNullOrWhiteSpace(settings.McpUrl);
            List<McpConfigHelper.ConfigTarget> targets;
            if (remote)
            {
                int? advertisedMcpPort = null;
                if (String.IsNullOrWhiteSpace(settings.McpUrl) && !admiral.IsLocal)
                {
                    // The unauthenticated health endpoint reports the Admiral's MCP port.
                    try
                    {
                        HealthResponse? health = await GetAsync<HealthResponse>("/api/v1/status/health").ConfigureAwait(false);
                        advertisedMcpPort = health?.Ports?.Mcp;
                    }
                    catch (HttpRequestException)
                    {
                        advertisedMcpPort = null;
                    }
                }

                mcpUrl = ResolveRemoteMcpUrl(admiral, settings.McpUrl, advertisedMcpPort);
                string? token = admiral.Token;
                if (String.IsNullOrEmpty(token))
                {
                    Uri mcpUri = new Uri(mcpUrl);
                    if (!AdmiralTarget.IsLoopbackHost(mcpUri.Host))
                    {
                        throw new AdmiralTargetException(
                            AdmiralTargetErrorEnum.MissingToken,
                            "A remote Admiral's MCP endpoint requires a credential: pass --token <bearer>, set " + AdmiralTargetResolver.TokenEnvironmentVariable
                            + ", or store one with 'armada profile add <name> --server <url> --token <bearer>'.",
                            "mcp install");
                    }

                    targets = McpConfigHelper.BuildTargets(armadaSettings.McpPort, mcpHost);
                    targets = RewriteLocalTargets(targets, mcpUrl);
                }
                else
                {
                    McpConfigHelper.McpInstallPaths paths = McpConfigHelper.McpInstallPaths.Default();
                    targets = McpConfigHelper.BuildRemoteTargets(mcpUrl, token, paths, McpConfigHelper.IsMuxAvailable(), McpConfigHelper.IsOpenCodeAvailable());
                }
            }
            else
            {
                targets = McpConfigHelper.BuildTargets(armadaSettings.McpPort, mcpHost);
            }

            List<McpConfigHelper.InstructionTarget> instructionTargets = McpConfigHelper.BuildInstructionTargets();

            AnsiConsole.MarkupLine("[bold dodgerblue1]Armada MCP Install[/]");
            AnsiConsole.MarkupLine($"[dim]MCP endpoint:[/] [green]{Markup.Escape(mcpUrl)}[/]");
            if (remote)
            {
                AnsiConsole.MarkupLine("[dim]Admiral:[/] " + Markup.Escape(admiral.Describe()) + (String.IsNullOrWhiteSpace(settings.McpUrl) && !admiral.IsLocal ? " [dim](MCP URL derived from it; pass --mcp-url if your MCP endpoint differs)[/]" : ""));
                if (!String.IsNullOrEmpty(admiral.Token))
                    AnsiConsole.MarkupLine("[gold1]Note:[/] the Claude Code, Gemini, Cursor, Mux, and OpenCode configs will contain the bearer token in plain text; keep those files private. Codex reads it from " + AdmiralTargetResolver.TokenEnvironmentVariable + " at run time.");
                if (mcpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !AdmiralTarget.IsLoopbackHost(new Uri(mcpUrl).Host))
                    AnsiConsole.MarkupLine("[gold1]Warning:[/] the MCP endpoint is plain HTTP, so the token crosses the network unencrypted; use https:// through a TLS-terminating proxy.");
            }
            AnsiConsole.WriteLine();

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]Dry run:[/] no files will be modified.");
                AnsiConsole.WriteLine();
            }

            foreach (McpConfigHelper.ConfigTarget target in targets)
            {
                WriteClientHeader(target.ClientName);

                bool shouldWrite = settings.DryRun || settings.Yes || AnsiConsole.Confirm(
                    $"[dodgerblue1]Configure[/] [green]{target.ClientName}[/] at [green]{Markup.Escape(target.FilePath)}[/]?",
                    true);

                if (!shouldWrite)
                {
                    AnsiConsole.MarkupLine($"[yellow]{target.ClientName}[/]: skipped.");
                }
                else if (!settings.DryRun)
                {
                    McpConfigHelper.ApplyResult result = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
                    if (remote && (target.ManualConfig != null || (target.Kind == McpClientKindEnum.ClaudeCode && target.ArmadaConfig != null)))
                        McpConfigHelper.RestrictToOwner(target.FilePath);
                    WriteResult(result);
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]{target.ClientName}[/]: would configure [green]{Markup.Escape(target.FilePath)}[/].");
                }

                if (target.InstallAgent)
                {
                    bool shouldInstallAgent = settings.DryRun || settings.Yes || AnsiConsole.Confirm(
                        $"[dodgerblue1]Install/update[/] Claude Code Armada agent at [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/]?",
                        true);

                    if (!shouldInstallAgent)
                    {
                        AnsiConsole.MarkupLine("[yellow]Claude Code Agent[/]: skipped.");
                    }
                    else if (!settings.DryRun)
                    {
                        McpConfigHelper.ApplyResult agentResult = await McpConfigHelper.InstallClaudeAgentAsync().ConfigureAwait(false);
                        WriteResult(agentResult);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Claude Code Agent[/]: would configure [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/].");
                    }
                }

                WriteManualSection(target, armadaSettings.McpPort, mcpHost, remote);
            }

            foreach (McpConfigHelper.InstructionTarget target in instructionTargets)
            {
                WriteClientHeader(target.ClientName);

                bool shouldWrite = settings.DryRun || settings.Yes || AnsiConsole.Confirm(
                    $"[dodgerblue1]Install/update[/] Armada instructions for [green]{target.ClientName}[/] at [green]{Markup.Escape(target.FilePath)}[/]?",
                    true);

                if (!shouldWrite)
                {
                    AnsiConsole.MarkupLine($"[yellow]{target.ClientName}[/]: skipped.");
                }
                else if (!settings.DryRun)
                {
                    try
                    {
                        WriteResult(await McpConfigHelper.InstallInstructionTargetAsync(target).ConfigureAwait(false));
                    }
                    catch (InvalidDataException ex)
                    {
                        AnsiConsole.MarkupLine($"[red]{target.ClientName}[/]: not changed: {Markup.Escape(target.FilePath)}: {Markup.Escape(ex.Message)}");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]{target.ClientName}[/]: would configure [green]{Markup.Escape(target.FilePath)}[/].");
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]What You Need To Do[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Required[/]");
            if (remote)
                AnsiConsole.MarkupLine("[green]1.[/] For Codex, export " + AdmiralTargetResolver.TokenEnvironmentVariable + "=<bearer> in the environment Codex runs in. The remote Admiral must be running and reachable at " + Markup.Escape(mcpUrl) + ".");
            else
                AnsiConsole.MarkupLine("[green]1.[/] Start the Admiral server for the HTTP clients (Claude Code, Gemini CLI, Cursor, Mux): [green]armada server start[/]");
            AnsiConsole.MarkupLine("[green]2.[/] Restart any MCP client you want to use so it reloads the new config.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Only If Needed[/]");
            AnsiConsole.MarkupLine("[green]3.[/] Re-run [green]install-mcp[/] from each project where you want project-scoped files updated.");
            AnsiConsole.MarkupLine($"[dim]Project-scoped files:[/] [green]{Markup.Escape(McpConfigHelper.GetProjectAgentsPath())}[/], [green]{Markup.Escape(McpConfigHelper.GetProjectGeminiInstructionsPath())}[/], [green]{Markup.Escape(McpConfigHelper.GetCursorConfigPath())}[/]");
            AnsiConsole.MarkupLine("[green]4.[/] Use the manual snippets above only if automatic setup was skipped or failed.");
            AnsiConsole.WriteLine();
            if (!remote) AnsiConsole.MarkupLine("[dim]Codex uses stdio MCP by default, so it does not need the HTTP server.[/]");

            return 0;
        }

        private static void WriteResult(McpConfigHelper.ApplyResult result)
        {
            string scope = result.IsProjectScoped ? "project" : "user";
            string color = result.Changed ? "green" : "gold1";
            AnsiConsole.MarkupLine($"[{color}]{result.ClientName}[/] ({scope}-scoped): {Markup.Escape(result.Message)}");
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(result.FilePath)}[/]");
        }

        /// <summary>
        /// The MCP URL for a remote install: --mcp-url (with /mcp appended when it has no path), or the target's scheme
        /// and host on the Admiral's MCP port (from its health endpoint, else 7891) at /mcp.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="explicitUrl">--mcp-url value, or null.</param>
        /// <returns>MCP URL.</returns>
        /// <param name="advertisedMcpPort">MCP port from the Admiral's health endpoint, or null for the default 7891.</param>
        internal static string ResolveRemoteMcpUrl(AdmiralTarget target, string? explicitUrl, int? advertisedMcpPort = null)
        {
            if (!String.IsNullOrWhiteSpace(explicitUrl))
            {
                string normalized = AdmiralTargetResolver.NormalizeServerUrl(explicitUrl, "--mcp-url");
                Uri uri = new Uri(normalized);
                return uri.AbsolutePath == "/" ? normalized + "/mcp" : normalized;
            }

            Uri baseUri = new Uri(target.BaseUrl);
            UriBuilder builder = new UriBuilder(baseUri.Scheme, baseUri.Host, advertisedMcpPort ?? new ArmadaSettings().McpPort, "/mcp");
            return builder.Uri.AbsoluteUri;
        }

        private static List<McpConfigHelper.ConfigTarget> RewriteLocalTargets(List<McpConfigHelper.ConfigTarget> targets, string mcpUrl)
        {
            // A loopback --mcp-url without a token (for example an SSH tunnel to the remote MCP port): keep the local
            // client shapes but point the URL fields at the given endpoint.
            List<McpConfigHelper.ConfigTarget> rewritten = new List<McpConfigHelper.ConfigTarget>();
            foreach (McpConfigHelper.ConfigTarget target in targets)
            {
                if (target.ArmadaConfig == null)
                {
                    rewritten.Add(target);
                    continue;
                }

                System.Text.Json.Nodes.JsonObject config = target.ArmadaConfig.DeepClone().AsObject();
                if (target.IsMuxServers)
                {
                    Uri uri = new Uri(mcpUrl);
                    config["url"] = uri.GetLeftPart(UriPartial.Authority);
                    config["mcpPath"] = uri.AbsolutePath;
                }
                else
                {
                    config["url"] = mcpUrl;
                }

                rewritten.Add(target with { ArmadaConfig = config, ManualInstallCommand = target.Kind == McpClientKindEnum.ClaudeCode ? "claude mcp add --transport http --scope user armada " + mcpUrl : target.ManualInstallCommand });
            }

            return rewritten;
        }

        private static void WriteManualSection(McpConfigHelper.ConfigTarget target, int mcpPort, string mcpHost, bool remote)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]{target.ClientName} manual snippet[/] -> [green]{Markup.Escape(target.FilePath)}[/]");
            if (!String.IsNullOrEmpty(target.ManualInstallCommand))
            {
                AnsiConsole.MarkupLine("[dim]Run this command:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildManualSnippet(target))}[/]");
            }
            else
            {
                Console.WriteLine(McpConfigHelper.BuildManualSnippet(target));
            }
            if (target.Kind == McpClientKindEnum.ClaudeCode && !remote)
            {
                AnsiConsole.MarkupLine("[dim]Claude CLI helper:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildClaudeCliCommand(mcpPort, mcpHost))}[/]");
                AnsiConsole.MarkupLine("[dim]Claude stdio alternative:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildClaudeStdioCommand())}[/]");
                AnsiConsole.MarkupLine($"[dim]Claude agent file:[/] [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/]");
            }
            if (target.Kind == McpClientKindEnum.Mux && !remote)
            {
                AnsiConsole.MarkupLine("[dim]Or add it interactively:[/] start [green]mux[/], run [green]/mcp[/], choose [green]+ Add MCP server[/], then set transport [green]http[/], url [green]" + Markup.Escape(ArmadaMcpConfigBuilder.GetMcpBaseUrl(mcpPort, mcpHost)) + "[/], mcp path [green]/mcp[/], auth [green]none[/].");
            }
            AnsiConsole.WriteLine();
        }

        private static void WriteClientHeader(string clientName)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(clientName)}[/]").RuleStyle("grey"));
            AnsiConsole.WriteLine();
        }
    }
}
