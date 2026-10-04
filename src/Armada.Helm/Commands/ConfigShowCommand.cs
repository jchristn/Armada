namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Display current settings.
    /// </summary>
    [Description("Display current settings")]
    public class ConfigShowCommand : BaseCommand<ConfigShowSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ConfigShowSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);

            Table table = TableRenderer.CreateTable("Configuration", null);
            table.AddColumn("Key");
            table.AddColumn("Value");

            table.AddRow("dataDirectory", Markup.Escape(armadaSettings.DataDirectory));
            table.AddRow("databasePath", Markup.Escape(armadaSettings.DatabasePath));
            table.AddRow("logDirectory", Markup.Escape(armadaSettings.LogDirectory));
            table.AddRow("docksDirectory", Markup.Escape(armadaSettings.DocksDirectory));
            table.AddRow("reposDirectory", Markup.Escape(armadaSettings.ReposDirectory));
            table.AddRow("admiralPort", armadaSettings.AdmiralPort.ToString());
            table.AddRow("mcpPort", armadaSettings.McpPort.ToString());
            table.AddRow("heartbeatIntervalSeconds", armadaSettings.HeartbeatIntervalSeconds.ToString());
            table.AddRow("stallThresholdMinutes", armadaSettings.StallThresholdMinutes.ToString());
            table.AddRow("autoCreatePullRequests", armadaSettings.AutoCreatePullRequests.ToString());
            table.AddRow("apiKey", String.IsNullOrEmpty(armadaSettings.ApiKey) ? "[dim](not set)[/]" : "[dim]****[/]");
            table.AddRow("messageTemplates.enableCommitMetadata", armadaSettings.MessageTemplates.EnableCommitMetadata.ToString());
            table.AddRow("messageTemplates.enablePrMetadata", armadaSettings.MessageTemplates.EnablePrMetadata.ToString());
            table.AddRow("messageTemplates.commitMessageTemplate", Markup.Escape(armadaSettings.MessageTemplates.CommitMessageTemplate));
            table.AddRow("messageTemplates.prDescriptionTemplate", Markup.Escape(armadaSettings.MessageTemplates.PrDescriptionTemplate));
            table.AddRow("messageTemplates.mergeCommitTemplate", Markup.Escape(armadaSettings.MessageTemplates.MergeCommitTemplate));

            AnsiConsole.Write(table);

            if (armadaSettings.Agents.Count > 0)
            {
                AnsiConsole.WriteLine();
                Table agentTable = TableRenderer.CreateTable("Agent Runtimes", null);
                agentTable.AddColumn("Runtime");
                agentTable.AddColumn("Command");
                agentTable.AddColumn("Args");
                agentTable.AddColumn("Resume");
                agentTable.AddColumn("Max Concurrent");

                foreach (AgentSettings agent in armadaSettings.Agents)
                {
                    agentTable.AddRow(
                        agent.Runtime.ToString(),
                        Markup.Escape(agent.Command),
                        Markup.Escape(agent.Args),
                        agent.SupportsResume ? "[green]Yes[/]" : "[dim]No[/]",
                        agent.MaxConcurrent == 0 ? "[dim]unlimited[/]" : agent.MaxConcurrent.ToString());
                }

                AnsiConsole.Write(agentTable);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Settings file: {Markup.Escape(ArmadaSettings.DefaultSettingsPath)}[/]");

            return 0;
        }
    }
}
