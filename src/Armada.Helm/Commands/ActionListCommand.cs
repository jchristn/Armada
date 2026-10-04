namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// List fleet actions, or recent fleet action runs with --runs.
    /// </summary>
    [Description("List fleet actions or runs")]
    public class ActionListCommand : BaseCommand<ActionListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ActionListSettings settings, CancellationToken cancellationToken)
        {
            if (settings.Runs) return await ListRunsAsync(settings).ConfigureAwait(false);

            FleetActionEnumerateRequest query = new FleetActionEnumerateRequest
            {
                PageNumber = settings.Page ?? 1,
                PageSize = settings.PageSize ?? 100,
                IncludeInactive = settings.IncludeInactive
            };

            EnumerationResult<FleetAction>? result = await PostAsync<EnumerationResult<FleetAction>>("/api/v1/fleet-actions/enumerate", query).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No fleet actions found.[/]");
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Fleet Actions", null);
            table.AddColumn("Id");
            table.AddColumn("Name");
            table.AddColumn("Kind");
            table.AddColumn("Built-in");
            table.AddColumn("Concurrency");

            foreach (FleetAction action in result.Objects)
            {
                table.AddRow(
                    $"[dim]{Markup.Escape(action.Id)}[/]",
                    $"[bold]{Markup.Escape(action.Name)}[/]" + (action.Active ? "" : " [grey](deleted)[/]"),
                    action.Kind == FleetActionKindEnum.Command ? "[dodgerblue1]Command[/]" : "[mediumpurple1]Mission[/]",
                    action.IsBuiltIn ? "yes" : "",
                    action.DefaultConcurrency.ToString());
            }

            AnsiConsole.Write(table);
            return 0;
        }

        private async Task<int> ListRunsAsync(ActionListSettings settings)
        {
            EnumerationQuery query = new EnumerationQuery
            {
                PageNumber = settings.Page ?? 1,
                PageSize = settings.PageSize ?? 25
            };

            EnumerationResult<FleetActionRun>? result = await PostAsync<EnumerationResult<FleetActionRun>>("/api/v1/fleet-action-runs/enumerate", query).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No fleet action runs found.[/]");
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Fleet Action Runs", null);
            table.AddColumn("Id");
            table.AddColumn("Action");
            table.AddColumn("Kind");
            table.AddColumn("Status");
            table.AddColumn("Targets");
            table.AddColumn("Created");

            foreach (FleetActionRun run in result.Objects)
            {
                table.AddRow(
                    $"[dim]{Markup.Escape(run.Id)}[/]",
                    Markup.Escape(run.ActionName),
                    run.Kind.ToString(),
                    ActionRendering.RunStatus(run.Status),
                    ActionRendering.Counts(run),
                    $"[dim]{run.CreatedUtc.ToString("yyyy-MM-dd HH:mm")}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
