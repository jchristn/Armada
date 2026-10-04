namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Show a fleet action run and its targets.
    /// </summary>
    [Description("Show a fleet action run and its targets")]
    public class ActionStatusCommand : BaseCommand<ActionRunIdSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ActionRunIdSettings settings, CancellationToken cancellationToken)
        {
            FleetActionRunDetail? detail = await GetAsync<FleetActionRunDetail>("/api/v1/fleet-action-runs/" + settings.RunId).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(detail);
                return 0;
            }

            if (detail == null)
            {
                AnsiConsole.MarkupLine("[red]Run not found.[/]");
                return 1;
            }

            FleetActionRun run = detail.Run;
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(run.ActionName)}[/] [dim]{Markup.Escape(run.Id)}[/] ({run.Kind})  {ActionRendering.RunStatus(run.Status)}  {Markup.Escape(ActionRendering.Counts(run))}");

            Table table = TableRenderer.CreateTable("Targets", null);
            table.AddColumn("Vessel");
            table.AddColumn("Status");
            table.AddColumn("Reason");
            table.AddColumn("Exit");
            table.AddColumn("Voyage");
            table.AddColumn("Duration");

            foreach (FleetActionRunTargetSummary target in detail.Targets)
            {
                string reason = target.SkipReason ?? target.FailureReason ?? "";
                table.AddRow(
                    Markup.Escape(target.VesselName),
                    ActionRendering.TargetStatus(target.Status),
                    Markup.Escape(reason),
                    target.ExitCode.HasValue ? target.ExitCode.Value.ToString() : "",
                    Markup.Escape(target.VoyageId ?? ""),
                    target.DurationMs.HasValue ? (target.DurationMs.Value / 1000.0).ToString("F1") + "s" : "");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[dim]Per-target output: GET /api/v1/fleet-action-runs/{run}/targets/{target}[/]");
            return 0;
        }
    }
}
