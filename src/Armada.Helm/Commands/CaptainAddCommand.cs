namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Add a new captain.
    /// </summary>
    [Description("Add a new captain")]
    public class CaptainAddCommand : BaseCommand<CaptainAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainAddSettings settings, CancellationToken cancellationToken)
        {
            string runtimeValue = settings.Runtime?.ToLowerInvariant() switch
            {
                "claude" => "ClaudeCode",
                "codex" => "Codex",
                "gemini" => "Gemini",
                "cursor" => "Cursor",
                "mux" => "Mux",
                "custom" => "Custom",
                _ => "ClaudeCode"
            };

            if (runtimeValue == "Mux" && String.IsNullOrWhiteSpace(settings.MuxEndpoint))
            {
                AnsiConsole.MarkupLine("[red]Mux captains require --mux-endpoint <name>.[/]");
                return 1;
            }

            string? runtimeOptionsJson = runtimeValue == "Mux"
                ? CaptainRuntimeOptions.Serialize(new MuxCaptainOptions
                {
                    ConfigDirectory = settings.MuxConfigDirectory,
                    Endpoint = settings.MuxEndpoint,
                    BaseUrl = settings.MuxBaseUrl,
                    AdapterType = settings.MuxAdapterType,
                    Temperature = settings.MuxTemperature,
                    MaxTokens = settings.MuxMaxTokens,
                    SystemPromptPath = settings.MuxSystemPromptPath,
                    ApprovalPolicy = settings.MuxApprovalPolicy
                })
                : null;

            object body = new
            {
                Name = settings.Name,
                Runtime = runtimeValue,
                Model = String.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim(),
                RuntimeOptionsJson = runtimeOptionsJson
            };

            Captain? captain = await PostAsync<Captain>("/api/v1/captains", body).ConfigureAwait(false);

            if (captain != null)
            {
                AnsiConsole.MarkupLine($"[green]Captain recruited![/] [bold]{Markup.Escape(captain.Name)}[/] [dim]({Markup.Escape(captain.Id)})[/] using [dodgerblue1]{captain.Runtime}[/]");
                AnsiConsole.MarkupLine($"  Dispatch work with [green]armada go \"your task\"[/].");
            }

            return 0;
        }
    }
}
