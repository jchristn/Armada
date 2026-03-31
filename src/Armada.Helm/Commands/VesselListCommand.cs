namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

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
}
