namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;

    /// <summary>
    /// Configure MCP integration for supported clients.
    /// </summary>
    [Description("Configure MCP integration for Claude Code, Codex, Gemini, and Cursor")]
    public class McpInstallCommand : BaseCommand<McpInstallSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, McpInstallSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
            string mcpRpcUrl = McpConfigHelper.GetMcpRpcUrl(armadaSettings.McpPort);
            List<McpConfigHelper.ConfigTarget> targets = McpConfigHelper.BuildTargets(armadaSettings.McpPort);

            AnsiConsole.MarkupLine("[bold dodgerblue1]Armada MCP Install[/]");
            AnsiConsole.MarkupLine($"[dim]MCP endpoint:[/] [green]{Markup.Escape(mcpRpcUrl)}[/]");
            AnsiConsole.WriteLine();

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]Dry run:[/] no files will be modified.");
                AnsiConsole.WriteLine();
            }

            foreach (McpConfigHelper.ConfigTarget target in targets)
            {
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

                WriteManualSection(target, armadaSettings.McpPort);
            }

            AnsiConsole.MarkupLine("[bold]What You Need To Do[/]");
            AnsiConsole.MarkupLine("[green]1.[/] Start the Admiral server if you plan to use HTTP MCP: [green]armada server start[/]");
            AnsiConsole.MarkupLine("[green]2.[/] Restart the MCP client after config changes so it reloads the entry.");
            AnsiConsole.MarkupLine($"[green]3.[/] Cursor is project-scoped. The file was targeted at [green]{Markup.Escape(McpConfigHelper.GetCursorConfigPath())}[/]. Run the command from the project you want Cursor to use.");
            AnsiConsole.MarkupLine("[green]4.[/] If automatic config was skipped or blocked, use the snippets shown above and edit the files manually.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]HTTP mode requires the Admiral server running. Claude Code also supports stdio mode via the command shown above.[/]");

            return 0;
        }

        private static void WriteResult(McpConfigHelper.ApplyResult result)
        {
            string scope = result.IsProjectScoped ? "project" : "user";
            string color = result.Changed ? "green" : "gold1";
            AnsiConsole.MarkupLine($"[{color}]{result.ClientName}[/] ({scope}-scoped): {Markup.Escape(result.Message)}");
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(result.FilePath)}[/]");
        }

        private static void WriteManualSection(McpConfigHelper.ConfigTarget target, int mcpPort)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]{target.ClientName} manual snippet[/] -> [green]{Markup.Escape(target.FilePath)}[/]");
            Console.WriteLine(McpConfigHelper.BuildManualSnippet(target));
            if (target.ClientName == "Claude Code")
            {
                AnsiConsole.MarkupLine("[dim]Claude CLI helper:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildClaudeCliCommand(mcpPort))}[/]");
                AnsiConsole.MarkupLine("[dim]Claude stdio alternative:[/]");
                AnsiConsole.MarkupLine($"[green]  {Markup.Escape(McpConfigHelper.BuildClaudeStdioCommand())}[/]");
                AnsiConsole.MarkupLine($"[dim]Claude agent file:[/] [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/]");
            }
            AnsiConsole.WriteLine();
        }
    }
}
