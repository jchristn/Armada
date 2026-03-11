namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    #region Settings

    /// <summary>
    /// Settings for vessel list command.
    /// </summary>
    public class VesselListSettings : BaseSettings
    {
        /// <summary>
        /// Optional fleet filter.
        /// </summary>
        [Description("Filter by fleet name or ID")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }
    }

    /// <summary>
    /// Settings for vessel add command.
    /// </summary>
    public class VesselAddSettings : BaseSettings
    {
        /// <summary>
        /// Vessel name.
        /// </summary>
        [Description("Name of the vessel")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Repository URL.
        /// </summary>
        [Description("Remote repository URL")]
        [CommandArgument(1, "<repoUrl>")]
        public string RepoUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional fleet identifier or name.
        /// </summary>
        [Description("Fleet name or ID")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }

        /// <summary>
        /// Optional branch name.
        /// </summary>
        [Description("Default branch name")]
        [CommandOption("--branch|-b")]
        public string? Branch { get; set; }
    }

    /// <summary>
    /// Settings for vessel remove command.
    /// </summary>
    public class VesselRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Vessel name or ID.
        /// </summary>
        [Description("Vessel name or ID")]
        [CommandArgument(0, "<vessel>")]
        public string Id { get; set; } = string.Empty;
    }

    #endregion

    #region Commands

    /// <summary>
    /// List all vessels.
    /// </summary>
    [Description("List all vessels")]
    public class VesselListCommand : BaseCommand<VesselListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselListSettings settings, CancellationToken cancellationToken)
        {
            string path = "/api/v1/vessels";
            if (!string.IsNullOrEmpty(settings.Fleet))
                path += "?fleetId=" + settings.Fleet;
            path = AppendPagination(path, settings);

            EnumerationResult<Vessel>? result = await GetAsync<EnumerationResult<Vessel>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No vessels found.[/]");
                AnsiConsole.MarkupLine("Vessels are auto-registered when you run [green]armada go \"your task\"[/] from a git repo,");
                AnsiConsole.MarkupLine("or register one manually with [green]armada vessel add <name> <repoUrl>[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Vessels", null);
            table.AddColumn("Id");
            table.AddColumn("Name");
            table.AddColumn("Fleet");
            table.AddColumn("RepoUrl");
            table.AddColumn("Branch");
            table.AddColumn("Active");

            foreach (Vessel vessel in result.Objects)
            {
                string activeColor = vessel.Active ? "green" : "red";
                table.AddRow(
                    Markup.Escape(vessel.Id),
                    Markup.Escape(vessel.Name),
                    Markup.Escape(vessel.FleetId ?? "-"),
                    Markup.Escape(vessel.RepoUrl ?? "-"),
                    Markup.Escape(vessel.DefaultBranch),
                    $"[{activeColor}]{vessel.Active}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }

    /// <summary>
    /// Add a new vessel.
    /// </summary>
    [Description("Add a new vessel")]
    public class VesselAddCommand : BaseCommand<VesselAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselAddSettings settings, CancellationToken cancellationToken)
        {
            // Resolve fleet by name if provided
            string? fleetId = settings.Fleet;
            if (!string.IsNullOrEmpty(fleetId) && !fleetId.StartsWith("flt_"))
            {
                EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
                List<Fleet>? fleets = fleetResult?.Objects;
                if (fleets != null)
                {
                    Fleet? match = EntityResolver.ResolveFleet(fleets, fleetId);
                    if (match != null) fleetId = match.Id;
                }
            }

            object body = new
            {
                Name = settings.Name,
                RepoUrl = settings.RepoUrl,
                FleetId = fleetId,
                DefaultBranch = settings.Branch ?? "main"
            };

            Vessel? vessel = await PostAsync<Vessel>("/api/v1/vessels", body).ConfigureAwait(false);

            if (vessel != null)
            {
                AnsiConsole.MarkupLine($"[green]Vessel registered![/] [bold]{Markup.Escape(vessel.Name)}[/] [dim]({Markup.Escape(vessel.Id)})[/]");
                AnsiConsole.MarkupLine($"  Dispatch work with [green]armada go \"your task\" --vessel {Markup.Escape(vessel.Name)}[/].");
            }

            return 0;
        }
    }

    /// <summary>
    /// Remove a vessel by name or ID.
    /// </summary>
    [Description("Remove a vessel")]
    public class VesselRemoveCommand : BaseCommand<VesselRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselRemoveSettings settings, CancellationToken cancellationToken)
        {
            string vesselId = settings.Id;
            if (!vesselId.StartsWith("vsl_"))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;
                if (vessels != null)
                {
                    Vessel? match = EntityResolver.ResolveVessel(vessels, settings.Id);
                    if (match != null)
                    {
                        vesselId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Vessel not found:[/] {Markup.Escape(settings.Id)}");
                        if (vessels.Count > 0)
                        {
                            AnsiConsole.MarkupLine("[dim]Available vessels:[/]");
                            foreach (Vessel v in vessels)
                            {
                                AnsiConsole.MarkupLine($"  [dim]-[/] [bold]{Markup.Escape(v.Name)}[/] [dim]({Markup.Escape(v.Id)})[/]");
                            }
                        }
                        return 1;
                    }
                }
            }

            await DeleteAsync($"/api/v1/vessels/{vesselId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Vessel decommissioned:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }

    #endregion
}
