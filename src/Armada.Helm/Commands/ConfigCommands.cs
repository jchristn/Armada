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

    #region Settings

    /// <summary>
    /// Settings for config show command.
    /// </summary>
    public class ConfigShowSettings : BaseSettings
    {
    }

    /// <summary>
    /// Settings for config set command.
    /// </summary>
    public class ConfigSetSettings : BaseSettings
    {
        /// <summary>
        /// Configuration key (e.g. admiralPort, mcpPort, dataDirectory).
        /// </summary>
        [Description("Configuration key")]
        [CommandArgument(0, "<key>")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Value to set.
        /// </summary>
        [Description("Value to set")]
        [CommandArgument(1, "<value>")]
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for config init command.
    /// </summary>
    public class ConfigInitSettings : BaseSettings
    {
    }

    #endregion

    #region Commands

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

    /// <summary>
    /// Set a configuration value.
    /// </summary>
    [Description("Set a configuration value")]
    public class ConfigSetCommand : BaseCommand<ConfigSetSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetSettings settings, CancellationToken cancellationToken)
        {
            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);

            string key = settings.Key.ToLowerInvariant();
            string value = settings.Value;

            switch (key)
            {
                case "datadirectory":
                    armadaSettings.DataDirectory = value;
                    break;
                case "databasepath":
                    armadaSettings.DatabasePath = value;
                    break;
                case "logdirectory":
                    armadaSettings.LogDirectory = value;
                    break;
                case "docksdirectory":
                    armadaSettings.DocksDirectory = value;
                    break;
                case "reposdirectory":
                    armadaSettings.ReposDirectory = value;
                    break;
                case "admiralport":
                    if (!int.TryParse(value, out int admiralPort))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid port number.[/]");
                        return 1;
                    }
                    armadaSettings.AdmiralPort = admiralPort;
                    break;
                case "mcpport":
                    if (!int.TryParse(value, out int mcpPort))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid port number.[/]");
                        return 1;
                    }
                    armadaSettings.McpPort = mcpPort;
                    break;
                case "heartbeatintervalseconds":
                    if (!int.TryParse(value, out int hbInterval))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid number.[/]");
                        return 1;
                    }
                    armadaSettings.HeartbeatIntervalSeconds = hbInterval;
                    break;
                case "stallthresholdminutes":
                    if (!int.TryParse(value, out int stallThreshold))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid number.[/]");
                        return 1;
                    }
                    armadaSettings.StallThresholdMinutes = stallThreshold;
                    break;
                case "autocreatepullrequests":
                    if (!bool.TryParse(value, out bool autoPr))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid boolean. Use true or false.[/]");
                        return 1;
                    }
                    armadaSettings.AutoCreatePullRequests = autoPr;
                    break;
                case "apikey":
                    armadaSettings.ApiKey = value;
                    break;
                case "defaultruntime":
                    armadaSettings.DefaultRuntime = value;
                    break;
                case "notifications":
                    if (!bool.TryParse(value, out bool notif))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid boolean. Use true or false.[/]");
                        return 1;
                    }
                    armadaSettings.Notifications = notif;
                    break;
                case "terminalbell":
                    if (!bool.TryParse(value, out bool bell))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid boolean. Use true or false.[/]");
                        return 1;
                    }
                    armadaSettings.TerminalBell = bell;
                    break;
                case "idlecaptaintimeoutseconds":
                    if (!int.TryParse(value, out int idleTimeout))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid number.[/]");
                        return 1;
                    }
                    armadaSettings.IdleCaptainTimeoutSeconds = idleTimeout;
                    break;
                case "maxcaptains":
                    if (!int.TryParse(value, out int maxCaptains))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid number.[/]");
                        return 1;
                    }
                    armadaSettings.MaxCaptains = maxCaptains;
                    break;
                case "messagetemplates.enablecommitmetadata":
                    if (!bool.TryParse(value, out bool enableCommit))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid boolean. Use true or false.[/]");
                        return 1;
                    }
                    armadaSettings.MessageTemplates.EnableCommitMetadata = enableCommit;
                    break;
                case "messagetemplates.enableprmetadata":
                    if (!bool.TryParse(value, out bool enablePr))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid boolean. Use true or false.[/]");
                        return 1;
                    }
                    armadaSettings.MessageTemplates.EnablePrMetadata = enablePr;
                    break;
                case "messagetemplates.commitmessagetemplate":
                    armadaSettings.MessageTemplates.CommitMessageTemplate = value;
                    break;
                case "messagetemplates.prdescriptiontemplate":
                    armadaSettings.MessageTemplates.PrDescriptionTemplate = value;
                    break;
                case "messagetemplates.mergecommittemplate":
                    armadaSettings.MessageTemplates.MergeCommitTemplate = value;
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown configuration key:[/] [bold]{Markup.Escape(settings.Key)}[/]");
                    AnsiConsole.MarkupLine("[dim]Valid keys: dataDirectory, databasePath, logDirectory, docksDirectory, reposDirectory, admiralPort, mcpPort, heartbeatIntervalSeconds, stallThresholdMinutes, autoCreatePullRequests, apiKey, defaultRuntime, notifications, terminalBell, idleCaptainTimeoutSeconds, maxCaptains, messageTemplates.enableCommitMetadata, messageTemplates.enablePrMetadata, messageTemplates.commitMessageTemplate, messageTemplates.prDescriptionTemplate, messageTemplates.mergeCommitTemplate[/]");
                    return 1;
            }

            await armadaSettings.SaveAsync().ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Set[/] [bold]{Markup.Escape(settings.Key)}[/] = [bold]{Markup.Escape(value)}[/]");

            return 0;
        }
    }

    /// <summary>
    /// Interactive first-time setup.
    /// </summary>
    [Description("Interactive first-time setup")]
    public class ConfigInitCommand : BaseCommand<ConfigInitSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ConfigInitSettings settings, CancellationToken cancellationToken)
        {
            Program.WriteBanner();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold dodgerblue1]First-Time Setup[/]");
            AnsiConsole.WriteLine();

            // Check for existing configuration
            bool existingConfig = File.Exists(ArmadaSettings.DefaultSettingsPath);
            bool existingData = Directory.Exists(Core.Constants.DefaultDataDirectory)
                && Directory.GetFiles(Core.Constants.DefaultDataDirectory).Length > 0;

            if (existingConfig || existingData)
            {
                AnsiConsole.MarkupLine("[gold1]Existing configuration detected:[/]");
                if (existingConfig)
                    AnsiConsole.MarkupLine($"  [dim]Settings:[/] {Markup.Escape(ArmadaSettings.DefaultSettingsPath)}");
                if (existingData)
                    AnsiConsole.MarkupLine($"  [dim]Data:[/]     {Markup.Escape(Core.Constants.DefaultDataDirectory)}");
                AnsiConsole.WriteLine();

                bool wipe = AnsiConsole.Confirm(
                    "[bold red]Delete existing configuration and data?[/]",
                    defaultValue: false);

                if (wipe)
                {
                    // Stop embedded server if running (releases DB and file locks)
                    EmbeddedServer.Stop();

                    // Try to stop a standalone server too
                    try
                    {
                        using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                        ArmadaSettings tempSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
                        await client.PostAsync("http://localhost:" + tempSettings.AdmiralPort + "/api/v1/server/stop", null).ConfigureAwait(false);
                        AnsiConsole.MarkupLine("[dim]Stopped running Admiral server.[/]");
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    catch { }

                    if (existingConfig) File.Delete(ArmadaSettings.DefaultSettingsPath);
                    if (existingData)
                    {
                        try
                        {
                            Directory.Delete(Core.Constants.DefaultDataDirectory, recursive: true);
                        }
                        catch (IOException)
                        {
                            // Retry after a brief delay for file handles to release
                            await Task.Delay(1000).ConfigureAwait(false);
                            try
                            {
                                Directory.Delete(Core.Constants.DefaultDataDirectory, recursive: true);
                            }
                            catch (IOException ex)
                            {
                                AnsiConsole.MarkupLine($"[red]Could not delete data directory:[/] {Markup.Escape(ex.Message)}");
                                AnsiConsole.MarkupLine("[gold1]Close any running armada commands (watch, go --log) and try again.[/]");
                                return 1;
                            }
                        }
                        AnsiConsole.MarkupLine("[dim]Previous configuration and data deleted.[/]");
                    }
                    AnsiConsole.WriteLine();
                }
            }

            ArmadaSettings armadaSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);

            // Data directory
            string dataDir = AnsiConsole.Ask(
                "[dodgerblue1]Data directory[/]:",
                armadaSettings.DataDirectory);
            armadaSettings.DataDirectory = dataDir;

            // Admiral port
            int admiralPort = AnsiConsole.Ask(
                "[dodgerblue1]Admiral API port[/]:",
                armadaSettings.AdmiralPort);
            armadaSettings.AdmiralPort = admiralPort;

            // MCP port
            int mcpPort = AnsiConsole.Ask(
                "[dodgerblue1]MCP server port[/]:",
                armadaSettings.McpPort);
            armadaSettings.McpPort = mcpPort;

            // Auto PR
            bool autoPr = AnsiConsole.Confirm(
                "[dodgerblue1]Auto-create pull requests on mission completion?[/]",
                armadaSettings.AutoCreatePullRequests);
            armadaSettings.AutoCreatePullRequests = autoPr;

            // API key
            bool setApiKey = AnsiConsole.Confirm(
                "[dodgerblue1]Set an API key for Admiral authentication?[/]",
                false);
            if (setApiKey)
            {
                string apiKey = AnsiConsole.Prompt(
                    new TextPrompt<string>("[dodgerblue1]API key[/]:")
                        .Secret());
                armadaSettings.ApiKey = apiKey;
            }

            // Save
            await armadaSettings.SaveAsync().ConfigureAwait(false);
            armadaSettings.InitializeDirectories();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Configuration saved![/]");
            AnsiConsole.MarkupLine($"[dim]  Settings: {Markup.Escape(ArmadaSettings.DefaultSettingsPath)}[/]");
            AnsiConsole.MarkupLine($"[dim]  Data:     {Markup.Escape(armadaSettings.DataDirectory)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Next steps:");
            AnsiConsole.MarkupLine("  [green]armada server start[/]    Start the Admiral server");
            AnsiConsole.MarkupLine("  [green]armada fleet add[/]       Register a fleet");
            AnsiConsole.MarkupLine("  [green]armada vessel add[/]      Register a repository");

            return 0;
        }
    }

    #endregion
}
