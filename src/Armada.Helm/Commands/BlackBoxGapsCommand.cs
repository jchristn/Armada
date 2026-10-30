namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox gaps</c>: one page (at most 50) of Armada resilience gaps, failure modes in Armada's own
    /// handoff and landing that the black box triaged ArmadaDefect, grouped by fingerprint and most severe first, with
    /// occurrences, Armada versions, whether Armada could have healed them and the top proposed Armada change.
    /// Filters <c>--vessel</c>, <c>--resolution</c>, <c>--phase</c> and <c>--since</c>; <c>--json</c> prints the page alone.
    /// </summary>
    [Description("List Armada resilience gaps (handoff and landing failures triaged ArmadaDefect, by fingerprint)")]
    public class BlackBoxGapsCommand : BlackBoxCommandBase<BlackBoxGapsSettings>
    {
        /// <summary>
        /// Most gaps per page (the server cap).
        /// </summary>
        public const int MaxPageSize = 50;

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxGapsSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxResilienceGapRequest request = new BlackBoxResilienceGapRequest();
                request.PageNumber = Math.Max(1, settings.Page ?? 1);
                request.PageSize = Math.Clamp(settings.PageSize ?? 25, 1, MaxPageSize);
                if (!String.IsNullOrWhiteSpace(settings.Vessel)) request.VesselId = settings.Vessel.Trim();
                request.Resolution = BlackBoxCli.ParseEnum<BlackBoxResolutionEnum>("--resolution", settings.Resolution);
                request.Phase = BlackBoxCli.ParseEnum<BlackBoxPhaseEnum>("--phase", settings.Phase);
                if (!String.IsNullOrWhiteSpace(settings.Since)) request.CreatedAfter = BlackBoxCli.ParseSince(settings.Since, DateTime.UtcNow);
                EnumerationResult<BlackBoxResilienceGap>? page = await client.EnumerateBlackBoxResilienceGapsAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(page);
                    return 0;
                }

                if (page == null || page.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]No Armada resilience gaps recorded.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
                Table table = TableRenderer.CreateTable("Armada resilience gaps", null);
                table.AddColumn("Severity");
                table.AddColumn("Seen");
                table.AddColumn("Where");
                table.AddColumn("Self-heal");
                table.AddColumn("Top proposed Armada change");
                table.AddColumn("Newest entry");
                foreach (BlackBoxResilienceGap g in page.Objects)
                {
                    string seen = g.Occurrences.ToString(CultureInfo.InvariantCulture) + "x (" + g.OpenCount.ToString(CultureInfo.InvariantCulture) + " open)"
                        + (g.ArmadaVersions.Count > 1 ? ", " + g.ArmadaVersions.Count.ToString(CultureInfo.InvariantCulture) + " versions" : "");
                    table.AddRow(
                        BlackBoxResilienceRendering.Severity(g.Severity),
                        Markup.Escape(seen),
                        Markup.Escape(g.Phase + " / " + g.Step) + (g.Suspected ? " [gold1](suspected)[/]" : ""),
                        Markup.Escape(g.SelfHealable.ToString()),
                        Markup.Escape(g.TopProposedChange?.Title ?? "-"),
                        "[dim]" + Markup.Escape(g.LatestEntryId ?? "-") + "[/]");
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("[dim]Resilience section of an entry:[/] armada blackbox show <entryId>");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
