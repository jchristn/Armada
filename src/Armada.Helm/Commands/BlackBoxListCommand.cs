namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox list</c>: one page of black box entries, newest first, with the shared filters (vessel, voyage,
    /// mission, captain, kind, triage, phase, step, resolution, fingerprint, pinned, since, before) and paging.
    /// </summary>
    [Description("List black box entries (failures), newest first")]
    public class BlackBoxListCommand : BlackBoxCommandBase<BlackBoxListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxListSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxEnumerateRequest request = BlackBoxCli.ApplyFilters(new BlackBoxEnumerateRequest(), settings, DateTime.UtcNow);
                request.PageNumber = settings.Page ?? 1;
                request.PageSize = settings.PageSize ?? 25;
                request.Order = Armada.Core.Enums.EnumerationOrderEnum.CreatedDescending;
                EnumerationResult<BlackBoxEntrySummary>? result = await client.EnumerateBlackBoxAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(result);
                    return 0;
                }

                if (result == null || result.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[gold1]No black box entries found.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
                Table table = TableRenderer.CreateTable("Black Box", null);
                table.AddColumn("Id");
                table.AddColumn("Captured");
                table.AddColumn("Subject");
                table.AddColumn("Where");
                table.AddColumn("Kind");
                table.AddColumn("Triage");
                table.AddColumn("Resolution");
                foreach (BlackBoxEntrySummary e in result.Objects)
                {
                    string subject = e.MissionId ?? e.VoyageId ?? "-";
                    if (!String.IsNullOrEmpty(e.MissionTitle)) subject += "  " + e.MissionTitle;
                    table.AddRow(
                        "[dim]" + Markup.Escape(e.Id) + "[/]",
                        "[dim]" + e.CreatedUtc.ToString("yyyy-MM-dd HH:mm") + "[/]",
                        Markup.Escape(subject),
                        Markup.Escape(e.Phase + " / " + e.Step),
                        Markup.Escape(e.FailureKind?.ToString() ?? "-"),
                        Markup.Escape(e.Triage.ToString()),
                        Markup.Escape(e.Resolution.ToString()) + (e.Pinned ? " [gold1]*[/]" : ""));
                }

                AnsiConsole.Write(table);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
