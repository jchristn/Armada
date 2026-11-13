namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// <c>armada vessel concurrency &lt;vessel&gt;</c>: the vessel's voyage slots, the voyages holding them, waiting
    /// missions and recent slot wait.
    /// </summary>
    [Description("Show a vessel's slots, waiting missions and slot wait")]
    public class VesselConcurrencyCommand : BlackBoxCommandBase<VesselIdSettings>
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
                VesselConcurrency? report = await client.GetVesselConcurrencyAsync(vessel.Id, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(report);
                    return 0;
                }
                if (report == null) return BlackBoxCli.NotFoundExitCode;

                string voyages = report.MaxParallelVoyages == 0 ? report.VoyageSlotsInUse + " (unlimited)" : report.VoyageSlotsInUse + " / " + report.MaxParallelVoyages;
                string perVoyage = report.MaxParallelMissionsPerVoyage == 0 ? "unlimited" : report.MaxParallelMissionsPerVoyage.ToString(CultureInfo.InvariantCulture);
                AnsiConsole.MarkupLine("[bold dodgerblue1]" + Markup.Escape(report.Name) + "[/] [dim]" + Markup.Escape(report.VesselId) + "[/]");
                AnsiConsole.MarkupLine("[dodgerblue1]Voyage slots:[/]        " + voyages);
                AnsiConsole.MarkupLine("[dodgerblue1]Missions per voyage:[/] " + perVoyage);
                AnsiConsole.MarkupLine("[dodgerblue1]Running missions:[/]    " + report.RunningMissions);
                AnsiConsole.MarkupLine("[dodgerblue1]Slot wait 24h:[/]       " + Describe(report.SlotWait24h));
                AnsiConsole.MarkupLine("[dodgerblue1]Slot wait 7d:[/]        " + Describe(report.SlotWait7d));

                if (report.HoldingVoyages.Count > 0)
                {
                    Table holding = TableRenderer.CreateTable("Voyages holding a slot", null);
                    holding.AddColumn("Voyage");
                    holding.AddColumn("Running");
                    holding.AddColumn("Pending");
                    foreach (VesselVoyageSlotUsage usage in report.HoldingVoyages)
                        holding.AddRow(Markup.Escape(usage.Title) + " [dim]" + Markup.Escape(usage.VoyageId ?? usage.MissionId ?? "") + "[/]",
                            usage.RunningMissions.ToString(CultureInfo.InvariantCulture), usage.PendingMissions.ToString(CultureInfo.InvariantCulture));
                    AnsiConsole.Write(holding);
                }

                if (report.Waiting.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]No missions are waiting on this vessel.[/]");
                    return 0;
                }
                string title = report.WaitingCount > report.Waiting.Count ? "Waiting (" + report.Waiting.Count + " of " + report.WaitingCount + ")" : "Waiting";
                Table waiting = TableRenderer.CreateTable(title, null);
                waiting.AddColumn("Mission");
                waiting.AddColumn("Waiting on");
                waiting.AddColumn("For");
                foreach (WaitingMissionInfo item in report.Waiting)
                    waiting.AddRow(Markup.Escape(item.Title) + " [dim]" + Markup.Escape(item.MissionId) + "[/]",
                        "[gold1]" + item.Gate + "[/]" + (String.IsNullOrEmpty(item.Detail) ? "" : " [dim]" + Markup.Escape(item.Detail!) + "[/]"),
                        Duration(item.WaitedSeconds));
                AnsiConsole.Write(waiting);
                return 0;
            }).ConfigureAwait(false);
        }

        private static string Describe(SlotWaitStatistics stats)
            => stats.Missions == 0 || !stats.P50Seconds.HasValue
                ? "[dim]no waits recorded[/]"
                : "p50 " + Duration(stats.P50Seconds.Value) + ", p95 " + Duration(stats.P95Seconds ?? 0) + ", max " + Duration(stats.MaxSeconds ?? 0) + " [dim](" + stats.Missions + " missions)[/]";

        private static string Duration(double seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalSeconds < 60) return ((int)span.TotalSeconds) + "s";
            if (span.TotalMinutes < 60) return ((int)span.TotalMinutes) + "m " + span.Seconds + "s";
            if (span.TotalHours < 24) return ((int)span.TotalHours) + "h " + span.Minutes + "m";
            return ((int)span.TotalDays) + "d " + span.Hours + "h";
        }
    }
}
