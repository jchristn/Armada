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
}
