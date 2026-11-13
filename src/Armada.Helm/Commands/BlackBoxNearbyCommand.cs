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
    /// <c>armada blackbox nearby &lt;entryId&gt;</c>: one page of the successful landings on the same vessel around a
    /// failure that touched the same files.
    /// </summary>
    [Description("List the successful landings near a failure that touched the same files")]
    public class BlackBoxNearbyCommand : BlackBoxCommandBase<BlackBoxIdSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxIdSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.PageNumber = settings.Page ?? 1;
                query.PageSize = settings.PageSize ?? 25;
                EnumerationResult<BlackBoxNearbySuccess>? page = await client.ListBlackBoxNearbySuccessesAsync(settings.Id, query, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(page);
                    return 0;
                }

                if (page == null || page.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[gold1]No successful landings near this failure touched the same files.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
                Table table = TableRenderer.CreateTable("Nearby successes", null);
                table.AddColumn("Landed (UTC)");
                table.AddColumn("Mission");
                table.AddColumn("From failure");
                table.AddColumn("Same files");
                table.AddColumn("Base");
                foreach (BlackBoxNearbySuccess n in page.Objects)
                {
                    table.AddRow(
                        Markup.Escape(n.LandedUtc.ToString("yyyy-MM-dd HH:mm")),
                        "[green]" + Markup.Escape(n.MissionId) + "[/]" + (String.IsNullOrEmpty(n.MissionTitle) ? "" : " " + Markup.Escape(n.MissionTitle!)),
                        Markup.Escape((n.HoursFromFailure >= 0 ? "+" : "") + n.HoursFromFailure.ToString("0.0", CultureInfo.InvariantCulture) + " h"),
                        n.OverlappingFileCount.ToString(CultureInfo.InvariantCulture),
                        Markup.Escape(String.IsNullOrEmpty(n.BaseCommit) ? "-" : n.BaseCommit!.Substring(0, Math.Min(8, n.BaseCommit.Length))));
                }

                AnsiConsole.Write(table);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
