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
    /// <c>armada vessel reservations &lt;vessel&gt;</c>: one page of the vessel's soft directory reservations (an active
    /// mission whose scoped files sit in a conflict hotspot reserves that directory).
    /// </summary>
    [Description("List a vessel's soft directory reservations")]
    public class VesselReservationsCommand : BlackBoxCommandBase<VesselIdSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such vessel. List vessels with: armada vessel list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselIdSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                Vessel? vessel = await ResolveVesselAsync(client, settings.Vessel, cancellationToken).ConfigureAwait(false);
                if (vessel == null) return BlackBoxCli.NotFoundExitCode;
                EnumerationQuery query = new EnumerationQuery();
                query.PageNumber = settings.Page ?? 1;
                query.PageSize = settings.PageSize ?? 25;
                VesselReservationPage? page = await client.ListVesselReservationsAsync(vessel.Id, query, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(page);
                    return 0;
                }

                if (page == null || page.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[gold1]No directory reservations on " + Markup.Escape(vessel.Name) + ".[/]"
                        + (page != null && page.Threshold == 0 ? " [dim](reservations are off: blackBox.hotspotReservationThreshold is 0)[/]" : ""));
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
                Table table = TableRenderer.CreateTable("Reservations: " + vessel.Name, null);
                table.AddColumn("Directory");
                table.AddColumn("Mission");
                table.AddColumn("Status");
                table.AddColumn("Failures");
                table.AddColumn("Files");
                table.AddColumn("Since (UTC)");
                foreach (VesselReservation r in page.Objects)
                {
                    table.AddRow(
                        "[green]" + Markup.Escape(r.Directory) + "[/]",
                        Markup.Escape(r.MissionId) + (String.IsNullOrEmpty(r.MissionTitle) ? "" : " " + Markup.Escape(r.MissionTitle!)),
                        Markup.Escape(r.MissionStatus ?? "-"),
                        r.HotspotEntryCount.ToString(CultureInfo.InvariantCulture),
                        r.FileCount.ToString(CultureInfo.InvariantCulture),
                        r.SinceUtc.ToString("yyyy-MM-dd HH:mm"));
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("[dim]Hotspot threshold: " + page.Threshold + " failures; " + page.HotDirectoryCount + " hot director(ies); " + page.ActiveMissionsScanned + " active mission(s) scanned" + (page.ScanTruncated ? " (truncated)" : "") + ".[/]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
