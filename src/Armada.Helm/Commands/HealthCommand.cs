namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Show the vessel health table, optionally filtered by status and fleet, and optionally start an evaluation.
    /// </summary>
    [Description("Show vessel health")]
    public class HealthCommand : BaseCommand<HealthSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, HealthSettings settings, CancellationToken cancellationToken)
        {
            if (settings.Evaluate)
            {
                VesselHealthEvaluateRequest evaluate = new VesselHealthEvaluateRequest();
                evaluate.FleetId = String.IsNullOrWhiteSpace(settings.Fleet) ? null : settings.Fleet.Trim();
                try
                {
                    VesselHealthEvaluationStart? start = await PostAsync<VesselHealthEvaluationStart>("/api/v1/vessel-health/evaluate", evaluate).ConfigureAwait(false);
                    if (start != null)
                        AnsiConsole.MarkupLine("[green]Evaluation started[/] for " + start.VesselCount + " vessel(s). Job [dim]" + Markup.Escape(start.JobId) + "[/].");
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    AnsiConsole.MarkupLine("[gold1]An evaluation is already running.[/] " + Markup.Escape(ex.Message));
                }
            }

            VesselHealthEnumerateRequest request = new VesselHealthEnumerateRequest();
            request.SortBy = VesselHealthSortEnum.OverallStatus;
            if (settings.Page.HasValue) request.PageNumber = settings.Page.Value;
            if (settings.PageSize.HasValue) request.PageSize = settings.PageSize.Value;
            if (!String.IsNullOrWhiteSpace(settings.Fleet)) request.FleetId = settings.Fleet.Trim();
            if (!String.IsNullOrWhiteSpace(settings.Status))
            {
                if (!Enum.TryParse(settings.Status.Trim(), true, out VesselHealthStatusEnum status) || Char.IsDigit(settings.Status.Trim()[0]))
                {
                    AnsiConsole.MarkupLine("[red]Unknown status[/] " + Markup.Escape(settings.Status) + ". Use Pass, Warn, Fail, NotApplicable, or Unknown.");
                    return 1;
                }

                request.OverallStatus = new List<VesselHealthStatusEnum> { status };
            }

            EnumerationResult<VesselHealth>? result = await PostAsync<EnumerationResult<VesselHealth>>("/api/v1/vessel-health/enumerate", request).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No vessels found.[/] Register repositories with [green]armada vessel add[/].");
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Vessel Health", null);
            table.AddColumn("Vessel");
            table.AddColumn("Fleet");
            table.AddColumn("Overall");
            table.AddColumn("Ahead/Behind");
            table.AddColumn("Dirty");
            table.AddColumn("Branches");
            table.AddColumn("Deps");
            table.AddColumn("Vulns");
            table.AddColumn("Tests");
            table.AddColumn("Evaluated");

            foreach (VesselHealth row in result.Objects)
            {
                table.AddRow(
                    "[bold]" + Markup.Escape(row.VesselName ?? row.VesselId) + "[/]",
                    Markup.Escape(row.FleetName ?? "-"),
                    Status(row.OverallStatus),
                    (row.AheadOfDefault.HasValue ? row.AheadOfDefault.Value.ToString() : "?") + "/" + (row.BehindDefault.HasValue ? row.BehindDefault.Value.ToString() : "?"),
                    row.IsDirty.HasValue ? (row.IsDirty.Value ? "yes" : "no") : "-",
                    (row.BranchCount.HasValue ? row.BranchCount.Value.ToString() : "-") + (row.StaleBranchCount.HasValue && row.StaleBranchCount.Value > 0 ? " (" + row.StaleBranchCount.Value + " stale)" : ""),
                    Status(row.DependencyStatus) + (row.OutdatedCount.HasValue && row.OutdatedCount.Value > 0 ? " " + row.OutdatedCount.Value : ""),
                    Status(row.VulnerabilityStatus) + (row.VulnerableCount.HasValue && row.VulnerableCount.Value > 0 ? " " + row.VulnerableCount.Value : ""),
                    Status(row.TestInfraStatus),
                    row.EvaluatedUtc.HasValue ? "[dim]" + row.EvaluatedUtc.Value.ToString("yyyy-MM-dd HH:mm") + "[/]" : "[dim]never[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }

        private static string Status(VesselHealthStatusEnum status)
        {
            switch (status)
            {
                case VesselHealthStatusEnum.Pass: return "[green]Pass[/]";
                case VesselHealthStatusEnum.Warn: return "[gold1]Warn[/]";
                case VesselHealthStatusEnum.Fail: return "[red]Fail[/]";
                case VesselHealthStatusEnum.NotApplicable: return "[dim]N/A[/]";
                default: return "[dim]Unknown[/]";
            }
        }
    }
}
