namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Services;
    using Armada.Core.Settings;

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
            List<McpConfigHelper.ConfigTarget> targets = McpConfigHelper.BuildTargets(armadaSettings.McpPort, mcpHost);
            List<McpConfigHelper.InstructionTarget> instructionTargets = McpConfigHelper.BuildInstructionTargets();

            AnsiConsole.MarkupLine("[bold dodgerblue1]Armada MCP Install[/]");
            AnsiConsole.MarkupLine($"[dim]MCP endpoint:[/] [green]{Markup.Escape(mcpUrl)}[/]");
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

                WriteManualSection(target, armadaSettings.McpPort, mcpHost);
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
            AnsiConsole.MarkupLine("[green]1.[/] Start the Admiral server for the HTTP clients (Claude Code, Gemini CLI, Cursor, Mux): [green]armada server start[/]");
            AnsiConsole.MarkupLine("[green]2.[/] Restart any MCP client you want to use so it reloads the new config.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Only If Needed[/]");
            AnsiConsole.MarkupLine("[green]3.[/] Re-run [green]install-mcp[/] from each project where you want project-scoped files updated.");
            AnsiConsole.MarkupLine($"[dim]Project-scoped files:[/] [green]{Markup.Escape(McpConfigHelper.GetProjectAgentsPath())}[/], [green]{Markup.Escape(McpConfigHelper.GetProjectGeminiInstructionsPath())}[/], [green]{Markup.Escape(McpConfigHelper.GetCursorConfigPath())}[/]");
            AnsiConsole.MarkupLine("[green]4.[/] Use the manual snippets above only if automatic setup was skipped or failed.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Codex uses stdio MCP by default, so it does not need the HTTP server.[/]");

            return 0;
        }

        private static void WriteResult(McpConfigHelper.ApplyResult result)
        {
            string scope = result.IsProjectScoped ? "project" : "user";
            string color = result.Changed ? "green" : "gold1";
            AnsiConsole.MarkupLine($"[{color}]{result.ClientName}[/] ({scope}-scoped): {Markup.Escape(result.Message)}");
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(result.FilePath)}[/]");
        }

        private static void WriteManualSection(McpConfigHelper.ConfigTarget target, int mcpPort, string mcpHost)
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
            if (target.Kind == McpClientKindEnum.ClaudeCode)
            {
                AnsiConsole.MarkupLine("[dim]Claude CLI helper:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildClaudeCliCommand(mcpPort, mcpHost))}[/]");
                AnsiConsole.MarkupLine("[dim]Claude stdio alternative:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildClaudeStdioCommand())}[/]");
                AnsiConsole.MarkupLine($"[dim]Claude agent file:[/] [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/]");
            }
            if (target.Kind == McpClientKindEnum.Mux)
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
