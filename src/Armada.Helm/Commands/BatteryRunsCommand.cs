namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// List fixture battery runs, newest first.
    /// </summary>
    [Description("List fixture battery runs")]
    public class BatteryRunsCommand : BaseCommand<BatteryRunsSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BatteryRunsSettings settings, CancellationToken cancellationToken)
        {
            List<string> parts = new List<string>
            {
                "pageNumber=" + (settings.Page ?? 1).ToString(CultureInfo.InvariantCulture),
                "pageSize=" + (settings.PageSize ?? 25).ToString(CultureInfo.InvariantCulture)
            };
            if (!String.IsNullOrWhiteSpace(settings.Status)) parts.Add("status=" + Uri.EscapeDataString(settings.Status.Trim()));
            if (!String.IsNullOrWhiteSpace(settings.Captain)) parts.Add("captainId=" + Uri.EscapeDataString(settings.Captain.Trim()));

            EnumerationResult<BatteryRun>? result = await GetAsync<EnumerationResult<BatteryRun>>("/api/v1/battery/runs?" + String.Join("&", parts)).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No battery runs found.[/]");
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Battery Runs", null);
            table.AddColumn("Id");
            table.AddColumn("Status");
            table.AddColumn("Pipeline");
            table.AddColumn("Captain");
            table.AddColumn("Passed");
            table.AddColumn("Failed");
            table.AddColumn("Total");
            table.AddColumn("Created");
            foreach (BatteryRun run in result.Objects)
            {
                table.AddRow(
                    $"[dim]{Markup.Escape(run.Id)}[/]",
                    Markup.Escape(run.Status.ToString()),
                    Markup.Escape(run.Pipeline),
                    Markup.Escape(run.CaptainName ?? run.CaptainId ?? ""),
                    run.PassedCount.ToString(CultureInfo.InvariantCulture),
                    run.FailedCount.ToString(CultureInfo.InvariantCulture),
                    run.TotalTasks.ToString(CultureInfo.InvariantCulture),
                    run.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
