namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    #region Settings

    /// <summary>
    /// Settings for captain list command.
    /// </summary>
    public class CaptainListSettings : BaseSettings
    {
    }

    /// <summary>
    /// Settings for captain add command.
    /// </summary>
    public class CaptainAddSettings : BaseSettings
    {
        /// <summary>
        /// Captain name.
        /// </summary>
        [Description("Name of the captain")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Agent runtime type.
        /// </summary>
        [Description("Agent runtime (claude, codex, custom)")]
        [CommandOption("--runtime|-r")]
        public string? Runtime { get; set; }
    }

    /// <summary>
    /// Settings for captain stop command.
    /// </summary>
    public class CaptainStopSettings : BaseSettings
    {
        /// <summary>
        /// Captain identifier or name.
        /// </summary>
        [Description("Captain name or ID")]
        [CommandArgument(0, "<captain>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for captain remove command.
    /// </summary>
    public class CaptainRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Captain identifier or name.
        /// </summary>
        [Description("Captain name or ID")]
        [CommandArgument(0, "<captain>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for captain stop-all command.
    /// </summary>
    public class CaptainStopAllSettings : BaseSettings
    {
    }

    #endregion

    #region Commands

    /// <summary>
    /// List all captains.
    /// </summary>
    [Description("List all captains")]
    public class CaptainListCommand : BaseCommand<CaptainListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainListSettings settings, CancellationToken cancellationToken)
        {
            string path = AppendPagination("/api/v1/captains", settings);
            EnumerationResult<Captain>? result = await GetAsync<EnumerationResult<Captain>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No captains found.[/]");
                AnsiConsole.MarkupLine("Captains are auto-created when you run [green]armada go \"your task\"[/], or add one manually with [green]armada captain add <name>[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Captains", null);
            table.AddColumn("[bold]Id[/]");
            table.AddColumn("[bold]Name[/]");
            table.AddColumn("[bold]Runtime[/]");
            table.AddColumn("[bold]State[/]");
            table.AddColumn("[bold]Mission[/]");
            table.AddColumn("[bold]Heartbeat[/]");

            foreach (Captain captain in result.Objects)
            {
                string stateColor = TableRenderer.CaptainStateColor(captain.State);

                string heartbeat = captain.LastHeartbeatUtc.HasValue
                    ? captain.LastHeartbeatUtc.Value.ToString("HH:mm:ss")
                    : "[dim]-[/]";

                table.AddRow(
                    $"[dim]{Markup.Escape(captain.Id)}[/]",
                    $"[bold]{Markup.Escape(captain.Name)}[/]",
                    $"{captain.Runtime}",
                    $"[{stateColor}]{captain.State}[/]",
                    Markup.Escape(captain.CurrentMissionId ?? "-"),
                    heartbeat);
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }

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
                "codex" => "Codex",
                "custom" => "Custom",
                _ => "ClaudeCode"
            };

            object body = new
            {
                Name = settings.Name,
                Runtime = runtimeValue
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

    /// <summary>
    /// Stop a captain by name or ID.
    /// </summary>
    [Description("Stop a captain")]
    public class CaptainStopCommand : BaseCommand<CaptainStopSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainStopSettings settings, CancellationToken cancellationToken)
        {
            // Resolve by name or ID
            string captainId = settings.Id;
            if (!captainId.StartsWith("cpt_"))
            {
                EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
                List<Captain>? captains = captainResult?.Objects;
                if (captains != null)
                {
                    Captain? match = EntityResolver.ResolveCaptain(captains, settings.Id);
                    if (match != null)
                    {
                        captainId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Captain not found:[/] {Markup.Escape(settings.Id)}");
                        if (captains.Count > 0)
                        {
                            AnsiConsole.MarkupLine("[dim]Available captains:[/]");
                            foreach (Captain c in captains)
                            {
                                AnsiConsole.MarkupLine($"  [dim]-[/] [bold]{Markup.Escape(c.Name)}[/] [dim]({Markup.Escape(c.Id)})[/] [{TableRenderer.CaptainStateColor(c.State)}]{c.State}[/]");
                            }
                        }
                        return 1;
                    }
                }
            }

            await PostAsync($"/api/v1/captains/{captainId}/stop").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Recall signal sent to captain:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }

    /// <summary>
    /// Remove a captain. Accepts name or ID.
    /// </summary>
    [Description("Remove a captain")]
    public class CaptainRemoveCommand : BaseCommand<CaptainRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainRemoveSettings settings, CancellationToken cancellationToken)
        {
            string captainId = settings.Id;
            if (!captainId.StartsWith("cpt_"))
            {
                EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
                List<Captain>? captains = captainResult?.Objects;
                if (captains != null)
                {
                    Captain? match = EntityResolver.ResolveCaptain(captains, settings.Id);
                    if (match != null)
                    {
                        captainId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Captain not found:[/] {Markup.Escape(settings.Id)}");
                        return 1;
                    }
                }
            }

            await DeleteAsync($"/api/v1/captains/{captainId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Captain removed:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }

    /// <summary>
    /// Stop all captains.
    /// </summary>
    [Description("Stop all captains")]
    public class CaptainStopAllCommand : BaseCommand<CaptainStopAllSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainStopAllSettings settings, CancellationToken cancellationToken)
        {
            if (!AnsiConsole.Confirm("[bold red]RECALL ALL CAPTAINS?[/] This will stop all active agents.", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[dodgerblue1]Cancelled. Fleet remains operational.[/]");
                return 0;
            }

            EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
            List<Captain>? captains = captainResult?.Objects;

            if (captains == null || captains.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No captains to recall.[/]");
                return 0;
            }

            int stopped = 0;
            foreach (Captain captain in captains)
            {
                try
                {
                    await PostAsync($"/api/v1/captains/{captain.Id}/stop").ConfigureAwait(false);
                    AnsiConsole.MarkupLine($"  [gold1]Recalled:[/] [bold]{Markup.Escape(captain.Name)}[/] [dim]({Markup.Escape(captain.Id)})[/]");
                    stopped++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"  [red]Failed:[/] {Markup.Escape(captain.Id)} -- {Markup.Escape(ex.Message)}");
                }
            }

            AnsiConsole.MarkupLine($"\n[green]Recall signal sent to {stopped} captain(s).[/]");
            return 0;
        }
    }

    #endregion
}
