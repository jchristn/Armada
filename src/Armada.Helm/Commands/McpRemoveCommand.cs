namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;

    /// <summary>
    /// Remove MCP integration for supported clients.
    /// </summary>
    [Description("Remove MCP integration for Claude Code, Codex, Gemini, and Cursor")]
    public class McpRemoveCommand : BaseCommand<McpRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, McpRemoveSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
            List<McpConfigHelper.ConfigTarget> targets = McpConfigHelper.BuildTargets(armadaSettings.McpPort);

            AnsiConsole.MarkupLine("[bold dodgerblue1]Armada MCP Remove[/]");
            AnsiConsole.WriteLine();

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]Dry run:[/] no files will be modified.");
                AnsiConsole.WriteLine();
            }

            foreach (McpConfigHelper.ConfigTarget target in targets)
            {
                bool shouldRemove = settings.DryRun || settings.Yes || AnsiConsole.Confirm(
                    $"[dodgerblue1]Remove[/] Armada MCP entry from [green]{target.ClientName}[/] at [green]{Markup.Escape(target.FilePath)}[/]?",
                    true);

                if (!shouldRemove)
                {
                    AnsiConsole.MarkupLine($"[yellow]{target.ClientName}[/]: skipped.");
                }
                else if (!settings.DryRun)
                {
                    WriteResult(await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false));
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]{target.ClientName}[/]: would remove Armada MCP entry from [green]{Markup.Escape(target.FilePath)}[/].");
                }

                if (target.InstallAgent)
                {
                    bool shouldRemoveAgent = settings.DryRun || settings.Yes || AnsiConsole.Confirm(
                        $"[dodgerblue1]Remove[/] Claude Code Armada agent at [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/]?",
                        true);

                    if (!shouldRemoveAgent)
                    {
                        AnsiConsole.MarkupLine("[yellow]Claude Code Agent[/]: skipped.");
                    }
                    else if (!settings.DryRun)
                    {
                        WriteResult(await McpConfigHelper.RemoveClaudeAgentAsync().ConfigureAwait(false));
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Claude Code Agent[/]: would remove [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/].");
                    }
                }

                WriteManualSection(target);
            }

            AnsiConsole.MarkupLine("[bold]What You Need To Do[/]");
            AnsiConsole.MarkupLine("[green]1.[/] Restart the MCP client after removal so it forgets the Armada entry.");
            AnsiConsole.MarkupLine("[green]2.[/] If a file was skipped or could not be changed automatically, use the path shown above and remove the `mcpServers.armada` object manually.");
            AnsiConsole.MarkupLine($"[green]3.[/] Cursor removal is project-scoped. The command targets [green]{Markup.Escape(McpConfigHelper.GetCursorConfigPath())}[/] for the current working directory.");

            return 0;
        }

        private static void WriteResult(McpConfigHelper.ApplyResult result)
        {
            string scope = result.IsProjectScoped ? "project" : "user";
            string color = result.Changed ? "green" : "gold1";
            AnsiConsole.MarkupLine($"[{color}]{result.ClientName}[/] ({scope}-scoped): {Markup.Escape(result.Message)}");
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(result.FilePath)}[/]");
        }

        private static void WriteManualSection(McpConfigHelper.ConfigTarget target)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]{target.ClientName} manual cleanup[/] -> [green]{Markup.Escape(target.FilePath)}[/]");
            AnsiConsole.MarkupLine("[dim]Remove the `armada` object from the `mcpServers` section. If it becomes empty, you can remove `mcpServers` entirely.[/]");
            if (target.ClientName == "Claude Code")
            {
                AnsiConsole.MarkupLine($"[dim]Claude agent file:[/] [green]{Markup.Escape(McpConfigHelper.GetClaudeAgentPath())}[/]");
            }
            AnsiConsole.WriteLine();
        }
    }
}
