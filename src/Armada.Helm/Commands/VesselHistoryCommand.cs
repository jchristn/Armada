namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Show a vessel's commit history (grouped by local day) or its commit activity heatmap.
    /// </summary>
    [Description("Show a vessel's commit history or activity heatmap")]
    public class VesselHistoryCommand : BaseCommand<VesselHistorySettings>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselHistorySettings settings, CancellationToken cancellationToken)
        {
            Vessel? vessel = await ResolveVesselAsync(settings.Vessel).ConfigureAwait(false);
            if (vessel == null) return 1;

            TimeZoneInfo zone = TimeZoneInfo.Local;
            if (settings.Heatmap) return await ShowHeatmapAsync(vessel, settings, zone).ConfigureAwait(false);
            return await ShowCommitsAsync(vessel, settings, zone).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<Vessel?> ResolveVesselAsync(string identifier)
        {
            if (identifier.StartsWith("vsl_", StringComparison.Ordinal))
            {
                Vessel? byId = await GetAsync<Vessel>("/api/v1/vessels/" + Uri.EscapeDataString(identifier)).ConfigureAwait(false);
                if (byId != null) return byId;
            }

            EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels?pageSize=1000").ConfigureAwait(false);
            List<Vessel> vessels = vesselResult?.Objects ?? new List<Vessel>();
            Vessel? match = EntityResolver.ResolveVessel(vessels, identifier);
            if (match != null) return match;

            AnsiConsole.MarkupLine($"[red]Vessel not found:[/] {Markup.Escape(identifier)}");
            if (vessels.Count > 0)
            {
                AnsiConsole.MarkupLine("[dim]Available vessels:[/]");
                foreach (Vessel v in vessels)
                {
                    AnsiConsole.MarkupLine($"  [dim]-[/] [bold]{Markup.Escape(v.Name)}[/] [dim]({Markup.Escape(v.Id)})[/]");
                }
            }
            return null;
        }

        private async Task<int> ShowHeatmapAsync(Vessel vessel, VesselHistorySettings settings, TimeZoneInfo zone)
        {
            int offset = (int)Math.Round(zone.GetUtcOffset(DateTime.UtcNow).TotalMinutes);
            List<string> query = new List<string>();
            if (!String.IsNullOrWhiteSpace(settings.Branch)) query.Add("branch=" + Uri.EscapeDataString(settings.Branch.Trim()));
            if (!String.IsNullOrWhiteSpace(settings.From)) query.Add("from=" + Uri.EscapeDataString(settings.From.Trim()));
            if (!String.IsNullOrWhiteSpace(settings.To)) query.Add("to=" + Uri.EscapeDataString(settings.To.Trim()));
            query.Add("utcOffsetMinutes=" + offset.ToString(CultureInfo.InvariantCulture));
            string path = "/api/v1/vessels/" + Uri.EscapeDataString(vessel.Id) + "/history/activity?" + String.Join("&", query);

            VesselCommitActivity? activity = await GetAsync<VesselCommitActivity>(path).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(activity);
                return activity == null || activity.Error != null ? 1 : 0;
            }

            if (activity == null)
            {
                AnsiConsole.MarkupLine("[red]No response from the Admiral.[/]");
                return 1;
            }

            if (activity.Error != null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(activity.Error)}");
                return 1;
            }

            bool defaultRange = String.IsNullOrWhiteSpace(settings.From) && String.IsNullOrWhiteSpace(settings.To);
            string rangeLabel = defaultRange ? "in the last year" : "from " + activity.From + " to " + activity.To;
            bool colors = AnsiConsole.Profile.Capabilities.ColorSystem != ColorSystem.NoColors;
            bool unicode = AnsiConsole.Profile.Capabilities.Unicode;

            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(vessel.Name)}[/] [dim]commit activity[/]");
            AnsiConsole.WriteLine();
            foreach (string line in VesselHistoryRenderer.HeatmapLines(activity, colors, unicode, rangeLabel, zone))
                AnsiConsole.MarkupLine(line);
            return 0;
        }

        private async Task<int> ShowCommitsAsync(Vessel vessel, VesselHistorySettings settings, TimeZoneInfo zone)
        {
            bool json = IsJsonMode(settings);
            List<VesselCommitPage> pages = new List<VesselCommitPage>();
            string? cursor = null;
            string? lastDay = null;
            int shown = 0;
            DateTime nowUtc = DateTime.UtcNow;

            while (true)
            {
                List<string> query = new List<string>();
                if (cursor != null)
                {
                    query.Add("cursor=" + Uri.EscapeDataString(cursor));
                }
                else
                {
                    if (!String.IsNullOrWhiteSpace(settings.Branch)) query.Add("branch=" + Uri.EscapeDataString(settings.Branch.Trim()));
                    if (!String.IsNullOrWhiteSpace(settings.Before)) query.Add("before=" + Uri.EscapeDataString(settings.Before.Trim()));
                }
                if (settings.Limit.HasValue) query.Add("limit=" + settings.Limit.Value.ToString(CultureInfo.InvariantCulture));
                string path = "/api/v1/vessels/" + Uri.EscapeDataString(vessel.Id) + "/history/commits" + (query.Count > 0 ? "?" + String.Join("&", query) : "");

                VesselCommitPage? page = await GetAsync<VesselCommitPage>(path).ConfigureAwait(false);
                if (page == null)
                {
                    if (!json) AnsiConsole.MarkupLine("[red]No response from the Admiral.[/]");
                    return 1;
                }

                pages.Add(page);
                if (page.Error != null)
                {
                    if (json) break;
                    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(page.Error)}");
                    return 1;
                }

                if (!json)
                {
                    if (shown == 0)
                        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(vessel.Name)}[/] [dim]history on {Markup.Escape(page.Branch)}[/]");
                    foreach (VesselCommit commit in page.Commits)
                    {
                        string day = VesselHistoryRenderer.LocalDate(commit.CommittedUtc, zone);
                        if (day != lastDay)
                        {
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine(VesselHistoryRenderer.DayHeader(commit.CommittedUtc, zone));
                            lastDay = day;
                        }
                        foreach (string line in VesselHistoryRenderer.CommitLines(commit, zone, nowUtc, settings.Verbose))
                            AnsiConsole.MarkupLine(line);
                        shown++;
                    }
                }

                if (!settings.All || page.NextCursor == null) break;
                cursor = page.NextCursor;
            }

            if (json)
            {
                if (settings.All) WriteJson(pages);
                else WriteJson(pages[0]);
                return pages[pages.Count - 1].Error != null ? 1 : 0;
            }

            VesselCommitPage last = pages[pages.Count - 1];
            AnsiConsole.WriteLine();
            if (shown == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No commits found.[/]");
            }
            else if (last.NextCursor != null && last.Commits.Count > 0)
            {
                VesselCommit oldest = last.Commits[last.Commits.Count - 1];
                string before = oldest.CommittedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                AnsiConsole.MarkupLine($"[dim]{shown} commits shown. Older history: add --all, or --before {before}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[dim]{shown} commits shown (end of history).[/]");
            }
            return 0;
        }

        #endregion
    }
}
