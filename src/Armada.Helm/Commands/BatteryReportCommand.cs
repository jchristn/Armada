namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Show the latest reliability gate report, or one by ID.
    /// </summary>
    [Description("Show the latest reliability gate report (or one by ID)")]
    public class BatteryReportCommand : BaseCommand<BatteryReportSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BatteryReportSettings settings, CancellationToken cancellationToken)
        {
            string id = String.IsNullOrWhiteSpace(settings.ReportId) ? "latest" : settings.ReportId.Trim();
            GateReport? report = await GetAsync<GateReport>("/api/v1/reliability/gate-reports/" + Uri.EscapeDataString(id)).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(report);
                return 0;
            }

            if (report == null)
            {
                AnsiConsole.MarkupLine("[gold1]No gate report found.[/]");
                return 1;
            }

            string verdict = report.Passed ? "[green]passed[/]" : "[red]failed[/]";
            AnsiConsole.MarkupLine($"[bold]Gate report[/] [dim]{Markup.Escape(report.Id ?? "-")}[/] {verdict}, Armada {Markup.Escape(report.ArmadaVersion ?? "-")}");
            AnsiConsole.MarkupLine($"Ran {report.StartedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} to {report.FinishedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, packs {Markup.Escape(String.Join(", ", report.Packs))}");

            if (report.Pipelines.Count > 0)
            {
                Table pipelines = TableRenderer.CreateTable("Pipelines", null);
                pipelines.AddColumn("Pipeline");
                pipelines.AddColumn("Status");
                pipelines.AddColumn("Passed");
                pipelines.AddColumn("Failed");
                pipelines.AddColumn("Could not run");
                foreach (GatePipelineResult result in report.Pipelines)
                {
                    pipelines.AddRow(
                        Markup.Escape(result.Pipeline),
                        Markup.Escape(result.Status),
                        result.Passed.ToString(CultureInfo.InvariantCulture),
                        result.Failed.ToString(CultureInfo.InvariantCulture),
                        result.CouldNotRun.ToString(CultureInfo.InvariantCulture));
                }
                AnsiConsole.Write(pipelines);
            }

            if (report.Measures.Count > 0)
            {
                Table measures = TableRenderer.CreateTable("Measures", null);
                measures.AddColumn("Measure");
                measures.AddColumn("Value");
                measures.AddColumn("Threshold");
                measures.AddColumn("Status");
                foreach (GateMeasure measure in report.Measures)
                {
                    measures.AddRow(Markup.Escape(measure.Name), Markup.Escape(measure.Value), Markup.Escape(measure.Threshold), Markup.Escape(measure.Status.ToString()));
                }
                AnsiConsole.Write(measures);
            }

            return 0;
        }
    }
}
