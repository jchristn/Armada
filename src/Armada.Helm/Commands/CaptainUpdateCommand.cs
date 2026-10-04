namespace Armada.Helm.Commands
{
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Update an existing captain.
    /// </summary>
    public class CaptainUpdateCommand : BaseCommand<CaptainUpdateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainUpdateSettings settings, CancellationToken cancellationToken)
        {
            EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
            List<Captain> captains = captainResult?.Objects ?? new List<Captain>();

            Captain? captain = settings.Captain.StartsWith("cpt_", StringComparison.OrdinalIgnoreCase)
                ? captains.FirstOrDefault(c => c.Id.Equals(settings.Captain, StringComparison.OrdinalIgnoreCase))
                : EntityResolver.ResolveCaptain(captains, settings.Captain);

            if (captain == null)
            {
                AnsiConsole.MarkupLine($"[red]Captain not found:[/] {Markup.Escape(settings.Captain)}");
                return 1;
            }

            Captain? current = await GetAsync<Captain>("/api/v1/captains/" + captain.Id).ConfigureAwait(false);
            if (current == null)
            {
                AnsiConsole.MarkupLine($"[red]Captain not found:[/] {Markup.Escape(captain.Id)}");
                return 1;
            }

            string runtimeValue = ResolveRuntimeValue(settings.Runtime, current.Runtime.ToString());
            MuxCaptainOptions muxOptions = current.Runtime == AgentRuntimeEnum.Mux
                ? (CaptainRuntimeOptions.GetMuxOptions(current) ?? new MuxCaptainOptions())
                : new MuxCaptainOptions();

            if (runtimeValue == "Mux")
            {
                if (settings.MuxConfigDirectory != null) muxOptions.ConfigDirectory = settings.MuxConfigDirectory;
                if (settings.MuxEndpoint != null) muxOptions.Endpoint = settings.MuxEndpoint;
                if (settings.MuxBaseUrl != null) muxOptions.BaseUrl = settings.MuxBaseUrl;
                if (settings.MuxAdapterType != null) muxOptions.AdapterType = settings.MuxAdapterType;
                if (settings.MuxTemperature.HasValue) muxOptions.Temperature = settings.MuxTemperature;
                if (settings.MuxMaxTokens.HasValue) muxOptions.MaxTokens = settings.MuxMaxTokens;
                if (settings.MuxSystemPromptPath != null) muxOptions.SystemPromptPath = settings.MuxSystemPromptPath;
                if (settings.MuxApprovalPolicy != null) muxOptions.ApprovalPolicy = settings.MuxApprovalPolicy;

                if (String.IsNullOrWhiteSpace(muxOptions.Endpoint))
                {
                    AnsiConsole.MarkupLine("[red]Mux captains require --mux-endpoint <name>.[/]");
                    return 1;
                }
            }

            object body = new
            {
                Id = current.Id,
                TenantId = current.TenantId,
                UserId = current.UserId,
                Name = String.IsNullOrWhiteSpace(settings.Name) ? current.Name : settings.Name.Trim(),
                Runtime = runtimeValue,
                Model = settings.Model != null ? (String.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim()) : current.Model,
                SystemInstructions = current.SystemInstructions,
                AllowedPersonas = current.AllowedPersonas,
                PreferredPersona = current.PreferredPersona,
                RuntimeOptionsJson = runtimeValue == "Mux" ? CaptainRuntimeOptions.Serialize(muxOptions) : null
            };

            Captain? updated = await PutAsync<Captain>("/api/v1/captains/" + current.Id, body).ConfigureAwait(false);
            if (updated == null)
            {
                AnsiConsole.MarkupLine($"[red]Failed to update captain:[/] {Markup.Escape(current.Id)}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Captain updated:[/] [bold]{Markup.Escape(updated.Name)}[/] [dim]({Markup.Escape(updated.Id)})[/]");
            return 0;
        }

        private static string ResolveRuntimeValue(string? requestedRuntime, string currentRuntime)
        {
            return requestedRuntime?.ToLowerInvariant() switch
            {
                "claude" => "ClaudeCode",
                "codex" => "Codex",
                "gemini" => "Gemini",
                "cursor" => "Cursor",
                "mux" => "Mux",
                "custom" => "Custom",
                _ => currentRuntime
            };
        }
    }
}
