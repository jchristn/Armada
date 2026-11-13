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
    /// <c>armada vessel doctor &lt;vessel&gt; [--window-days N]</c>: one page of the configuration recommendations that
    /// keep recurring on the vessel (for example BBX-LAND-001 recommended LandingMode PullRequest 6 times this month),
    /// each with an example entry to apply it from.
    /// </summary>
    [Description("List a vessel's recurring black box configuration recommendations")]
    public class VesselDoctorCommand : BlackBoxCommandBase<VesselDoctorSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such vessel. List vessels with: armada vessel list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselDoctorSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.WindowDays.HasValue && (settings.WindowDays.Value < 1 || settings.WindowDays.Value > 365))
            {
                Status("Error: --window-days must be 1 to 365.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                Vessel? vessel = await ResolveVesselAsync(client, settings.Vessel, cancellationToken).ConfigureAwait(false);
                if (vessel == null) return BlackBoxCli.NotFoundExitCode;
                BlackBoxDoctorRequest request = new BlackBoxDoctorRequest();
                request.PageNumber = settings.Page ?? 1;
                request.PageSize = settings.PageSize ?? 25;
                request.WindowDays = settings.WindowDays;
                BlackBoxDoctorPage? page = await client.GetVesselBlackBoxDoctorAsync(vessel.Id, request, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(page);
                    return 0;
                }

                if (page == null || page.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]No recurring black box recommendations for " + Markup.Escape(vessel.Name) + " in the last " + (page?.WindowDays ?? settings.WindowDays ?? 30) + " days.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(page.PageNumber, page.TotalPages, page.TotalRecords, page.TotalMs);
                Table table = TableRenderer.CreateTable("Black box doctor: " + vessel.Name, null);
                table.AddColumn("Times");
                table.AddColumn("Rule");
                table.AddColumn("Setting");
                table.AddColumn("Change");
                table.AddColumn("Example entry");
                table.AddColumn("Apply");
                foreach (BlackBoxDoctorFinding f in page.Objects)
                {
                    bool apply = f.Applicable && !String.IsNullOrEmpty(f.RecommendationId);
                    table.AddRow(
                        f.Count.ToString(CultureInfo.InvariantCulture) + "x",
                        Markup.Escape(f.RuleId ?? "-"),
                        Markup.Escape(f.Setting + " (" + f.Scope + ")"),
                        Markup.Escape((f.CurrentValue ?? "-") + " -> " + (f.SuggestedValue ?? "-")),
                        "[dim]" + Markup.Escape(f.ExampleEntryId) + "[/]",
                        apply ? "[green]" + Markup.Escape(f.RecommendationId!) + "[/]" : "[dim]advice[/]");
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("[dim]" + page.EntriesScanned + " of " + page.EntriesInWindow + " entries in the last " + page.WindowDays + " days scanned" + (page.ScanTruncated ? " (truncated)" : "") + ".[/]");
                AnsiConsole.MarkupLine("[dim]Apply one from the dashboard, the TUI (vessel screen, Black Box Doctor tab, A), or MCP apply_blackbox_recommendation with the example entry.[/]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
