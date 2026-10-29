namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox timeline &lt;entryId&gt;</c>: one page of the entry's combined timeline (the entry and its notes,
    /// the mission, the dock, the merge queue entry, the captain and the Harbor link), oldest first unless
    /// <c>--newest-first</c>.
    /// </summary>
    [Description("Show a black box entry's combined timeline")]
    public class BlackBoxTimelineCommand : BlackBoxCommandBase<BlackBoxTimelineSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxTimelineSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.PageNumber = settings.Page ?? 1;
                query.PageSize = settings.PageSize ?? 25;
                query.Order = settings.NewestFirst ? EnumerationOrderEnum.CreatedDescending : EnumerationOrderEnum.CreatedAscending;
                BlackBoxTimelinePage? page = await client.GetBlackBoxTimelineAsync(settings.Id, query, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(page);
                    return 0;
                }

                if (page == null || page.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[gold1]No timeline events.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
                Table table = TableRenderer.CreateTable("Timeline", null);
                table.AddColumn("Time (UTC)");
                table.AddColumn("Source");
                table.AddColumn("Kind");
                table.AddColumn("Message");
                table.AddColumn("Entity");
                foreach (BlackBoxTimelineItem i in page.Objects)
                {
                    table.AddRow(
                        Markup.Escape(i.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")),
                        Markup.Escape(i.Source.ToString()),
                        Markup.Escape(i.Kind),
                        Markup.Escape(i.Message),
                        "[dim]" + Markup.Escape(i.EntityId ?? "-") + "[/]");
                }

                AnsiConsole.Write(table);
                if (page.Truncated) AnsiConsole.MarkupLine("[dim]Some sources were truncated; see --json for the per-source counts.[/]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
