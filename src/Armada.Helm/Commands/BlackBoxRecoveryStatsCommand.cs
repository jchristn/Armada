namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox recovery-stats</c>: one page of recovery path statistics (confirmed runs followed to the
    /// mission's next terminal status, and each path's success rate), filtered by <c>--rule</c> and <c>--vessel</c>.
    /// </summary>
    [Description("Show recovery path success rates")]
    public class BlackBoxRecoveryStatsCommand : BlackBoxCommandBase<BlackBoxRecoveryStatsSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxRecoveryStatsSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxRecoveryStatsRequest request = new BlackBoxRecoveryStatsRequest();
                request.PageNumber = settings.Page ?? 1;
                request.PageSize = settings.PageSize ?? 25;
                if (!String.IsNullOrWhiteSpace(settings.Rule)) request.RuleId = settings.Rule.Trim();
                if (!String.IsNullOrWhiteSpace(settings.Vessel)) request.VesselId = settings.Vessel.Trim();
                EnumerationResult<BlackBoxRecoveryStat>? page = await client.GetBlackBoxRecoveryStatsAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(page);
                    return 0;
                }

                if (page == null || page.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[gold1]No recovery runs have been tracked yet.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
                Table table = TableRenderer.CreateTable("Recovery stats", null);
                table.AddColumn("Path");
                table.AddColumn("Rule");
                table.AddColumn("Success");
                table.AddColumn("Runs");
                table.AddColumn("Ok/Fail/Pend/Exp");
                foreach (BlackBoxRecoveryStat s in page.Objects)
                {
                    table.AddRow(
                        "[green]" + Markup.Escape(s.PathId) + "[/]",
                        Markup.Escape(s.RuleId ?? "-"),
                        s.SuccessRate.HasValue ? (s.SuccessRate.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "-",
                        s.Runs.ToString(CultureInfo.InvariantCulture),
                        s.Succeeded.ToString(CultureInfo.InvariantCulture) + "/" + s.Failed.ToString(CultureInfo.InvariantCulture) + "/" + s.Pending.ToString(CultureInfo.InvariantCulture) + "/" + s.Expired.ToString(CultureInfo.InvariantCulture));
                }

                AnsiConsole.Write(table);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
