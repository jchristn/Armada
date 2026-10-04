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
}
