namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Discover git repositories on the Admiral host and import them as vessels.
    /// </summary>
    [Description("Discover repositories and import them as vessels")]
    public class VesselImportCommand : BaseCommand<VesselImportCommandSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselImportCommandSettings settings, CancellationToken cancellationToken)
        {
            List<string> directories = (settings.Paths ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).Select(ToAbsolute).ToList();
            List<string> roots = (settings.Roots ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).Select(ToAbsolute).ToList();
            if (directories.Count == 0 && roots.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]Provide at least one path or --root.[/]");
                return 1;
            }

            string? fleetId = settings.Fleet;
            if (!string.IsNullOrEmpty(fleetId) && !fleetId.StartsWith("flt_"))
            {
                EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
                Fleet? match = fleetResult?.Objects == null ? null : EntityResolver.ResolveFleet(fleetResult.Objects, fleetId);
                if (match == null)
                {
                    AnsiConsole.MarkupLine($"[red]Fleet not found:[/] {Markup.Escape(fleetId)}");
                    return 1;
                }

                fleetId = match.Id;
            }

            VesselDiscoveryRequest discoverBody = new VesselDiscoveryRequest();
            discoverBody.Directories = directories;
            discoverBody.Roots = roots;
            discoverBody.MaxDepth = settings.Depth;

            VesselImportDiscoverResponse? discovered = await PostAsync<VesselImportDiscoverResponse>("/api/v1/vessels/import/discover", discoverBody).ConfigureAwait(false);
            if (discovered == null)
            {
                AnsiConsole.MarkupLine("[red]Discovery returned no result.[/]");
                return 1;
            }

            List<VesselImportItem> selectable = discovered.Candidates.Where(c => c.CandidateStatus == VesselImportCandidateStatusEnum.New).ToList();

            if (IsJsonMode(settings) && settings.DryRun)
            {
                WriteJson(discovered);
                return 0;
            }

            if (!IsJsonMode(settings))
            {
                Table table = TableRenderer.CreateTable("Import candidates (batch " + discovered.BatchId + ")", null);
                table.AddColumn("Status");
                table.AddColumn("Name");
                table.AddColumn("Path");
                table.AddColumn("Branch");
                table.AddColumn("Remote");
                foreach (VesselImportItem item in discovered.Candidates)
                {
                    string color = item.CandidateStatus == VesselImportCandidateStatusEnum.New ? "green" : "grey";
                    table.AddRow(
                        $"[{color}]{item.CandidateStatus}[/]",
                        Markup.Escape(item.ProposedName),
                        Markup.Escape(item.Path),
                        Markup.Escape(item.DefaultBranch ?? "-"),
                        Markup.Escape(item.RemoteUrl ?? "-"));
                }

                AnsiConsole.Write(table);
                foreach (VesselImportHint hint in discovered.Hints)
                    AnsiConsole.MarkupLine($"[gold1]{Markup.Escape(hint.Code)}:[/] {Markup.Escape(hint.Message)}");
                AnsiConsole.MarkupLine($"{selectable.Count} new of {discovered.Candidates.Count} candidates.");
            }

            if (settings.DryRun) return 0;
            if (selectable.Count == 0)
            {
                if (!IsJsonMode(settings)) AnsiConsole.MarkupLine("[gold1]Nothing new to import.[/]");
                else WriteJson(discovered);
                return 0;
            }

            if (!settings.Yes && !AnsiConsole.Confirm($"Import {selectable.Count} vessel(s)?", defaultValue: false))
                return 0;

            VesselImportRequest importBody = new VesselImportRequest();
            importBody.BatchId = discovered.BatchId;
            importBody.Paths = selectable.Select(c => c.Path).ToList();
            importBody.FleetId = fleetId;
            if (settings.Categorize)
            {
                if (String.IsNullOrWhiteSpace(settings.Captain))
                {
                    AnsiConsole.MarkupLine("[red]--categorize requires --captain <id>.[/]");
                    return 1;
                }

                VesselImportCategorizationRequest categorization = new VesselImportCategorizationRequest();
                categorization.Enabled = true;
                categorization.CaptainId = settings.Captain;
                categorization.ApplyAutomatically = settings.Apply;
                if (!String.IsNullOrWhiteSpace(settings.PromptFile))
                {
                    if (!File.Exists(settings.PromptFile))
                    {
                        AnsiConsole.MarkupLine($"[red]Prompt file not found:[/] {Markup.Escape(settings.PromptFile)}");
                        return 1;
                    }

                    categorization.Prompt = await File.ReadAllTextAsync(settings.PromptFile, cancellationToken).ConfigureAwait(false);
                }

                importBody.Categorization = categorization;
            }

            VesselImportResponse? imported = await PostAsync<VesselImportResponse>("/api/v1/vessels/import", importBody).ConfigureAwait(false);
            if (imported == null)
            {
                AnsiConsole.MarkupLine("[red]Import returned no result.[/]");
                return 1;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(imported);
                return 0;
            }

            if (settings.Categorize)
            {
                AnsiConsole.MarkupLine($"Fleet categorization runs in the background for batch [bold]{Markup.Escape(imported.BatchId)}[/]; review it in the dashboard import history or with GET /api/v1/vessels/import/batches/{Markup.Escape(imported.BatchId)}.");
            }

            if (imported.RunsInBackground)
            {
                AnsiConsole.MarkupLine($"[green]Import running as job[/] [bold]{Markup.Escape(imported.JobId ?? "-")}[/] for batch {Markup.Escape(imported.BatchId)}.");
                return 0;
            }

            VesselImportBatch? batch = imported.Batch;
            AnsiConsole.MarkupLine($"[green]Import finished:[/] {batch?.CreatedCount ?? 0} created, {batch?.SkippedCount ?? 0} skipped, {batch?.FailedCount ?? 0} failed.");
            foreach (VesselImportItem item in imported.Items.Where(i => i.Outcome == VesselImportOutcomeEnum.Failed))
                AnsiConsole.MarkupLine($"  [red]{Markup.Escape(item.Path)}[/]: {Markup.Escape(item.OutcomeReason ?? "")} {Markup.Escape(item.OutcomeMessage ?? "")}");
            return batch != null && batch.FailedCount > 0 ? 1 : 0;
        }

        private static string ToAbsolute(string path)
        {
            string trimmed = path.Trim();
            if (trimmed.StartsWith("~")) return trimmed;
            return Path.GetFullPath(trimmed);
        }
    }
}
