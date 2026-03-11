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
    /// Settings for fleet list command.
    /// </summary>
    public class FleetListSettings : BaseSettings
    {
    }

    /// <summary>
    /// Settings for fleet add command.
    /// </summary>
    public class FleetAddSettings : BaseSettings
    {
        /// <summary>
        /// Fleet name.
        /// </summary>
        [Description("Name of the fleet to create")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional fleet description.
        /// </summary>
        [Description("Description of the fleet")]
        [CommandOption("--description|-d")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Settings for fleet remove command.
    /// </summary>
    public class FleetRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Fleet name or ID.
        /// </summary>
        [Description("Fleet name or ID")]
        [CommandArgument(0, "<fleet>")]
        public string Id { get; set; } = string.Empty;
    }

    #endregion

    #region Commands

    /// <summary>
    /// List all fleets.
    /// </summary>
    [Description("List all fleets")]
    public class FleetListCommand : BaseCommand<FleetListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, FleetListSettings settings, CancellationToken cancellationToken)
        {
            string path = AppendPagination("/api/v1/fleets", settings);
            EnumerationResult<Fleet>? result = await GetAsync<EnumerationResult<Fleet>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No fleets found.[/] Use [green]armada fleet add <name>[/] to create one.");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Fleets", null);
            table.AddColumn("Id");
            table.AddColumn("Name");
            table.AddColumn("Active");
            table.AddColumn("Created");

            foreach (Fleet fleet in result.Objects)
            {
                string activeColor = fleet.Active ? "green" : "red";
                table.AddRow(
                    Markup.Escape(fleet.Id),
                    Markup.Escape(fleet.Name),
                    $"[{activeColor}]{fleet.Active}[/]",
                    fleet.CreatedUtc.ToString("yyyy-MM-dd HH:mm"));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }

    /// <summary>
    /// Add a new fleet.
    /// </summary>
    [Description("Add a new fleet")]
    public class FleetAddCommand : BaseCommand<FleetAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, FleetAddSettings settings, CancellationToken cancellationToken)
        {
            object body = new
            {
                Name = settings.Name,
                Description = settings.Description
            };

            Fleet? fleet = await PostAsync<Fleet>("/api/v1/fleets", body).ConfigureAwait(false);

            if (fleet != null)
            {
                AnsiConsole.MarkupLine($"[green]Fleet created![/] [bold]{Markup.Escape(fleet.Name)}[/] [dim]({Markup.Escape(fleet.Id)})[/]");
            }

            return 0;
        }
    }

    /// <summary>
    /// Remove a fleet.
    /// </summary>
    [Description("Remove a fleet")]
    public class FleetRemoveCommand : BaseCommand<FleetRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, FleetRemoveSettings settings, CancellationToken cancellationToken)
        {
            string fleetId = settings.Id;
            if (!fleetId.StartsWith("flt_"))
            {
                EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
                List<Fleet>? fleets = fleetResult?.Objects;
                if (fleets != null)
                {
                    Fleet? match = EntityResolver.ResolveFleet(fleets, settings.Id);
                    if (match != null)
                    {
                        fleetId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Fleet not found:[/] {Markup.Escape(settings.Id)}");
                        return 1;
                    }
                }
            }

            await DeleteAsync($"/api/v1/fleets/{fleetId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Fleet removed:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }

    #endregion
}
