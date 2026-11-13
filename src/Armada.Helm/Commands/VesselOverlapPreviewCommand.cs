namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada vessel overlap-preview &lt;vessel&gt; --brief|--brief-file</c>: the overlaps a mission would have on the
    /// vessel (active missions, conflict hotspots, reservations) and its brief lint findings. Nothing is dispatched.
    /// Warnings never change the exit code.
    /// </summary>
    [Description("Preview a mission's overlaps and brief lint on a vessel (nothing is dispatched)")]
    public class VesselOverlapPreviewCommand : BlackBoxCommandBase<VesselOverlapPreviewSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such vessel. List vessels with: armada vessel list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselOverlapPreviewSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string? brief = ReadBrief(settings.Brief, settings.BriefFile);
            if (brief == null) return 1;
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                Vessel? vessel = await ResolveVesselAsync(client, settings.Vessel, cancellationToken).ConfigureAwait(false);
                if (vessel == null) return BlackBoxCli.NotFoundExitCode;
                MissionOverlapPreviewRequest request = new MissionOverlapPreviewRequest();
                request.Title = String.IsNullOrWhiteSpace(settings.Title) ? null : settings.Title.Trim();
                request.Description = brief;
                request.DependsOnMissionId = String.IsNullOrWhiteSpace(settings.DependsOn) ? null : settings.DependsOn.Trim();
                request.ExcludeMissionId = String.IsNullOrWhiteSpace(settings.ExcludeMission) ? null : settings.ExcludeMission.Trim();
                MissionOverlapPreview? preview = await client.PreviewMissionOverlapAsync(vessel.Id, request, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(preview);
                    return 0;
                }

                if (preview == null)
                {
                    Status("Error: the Admiral returned no preview.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[dim]Preview only: nothing was dispatched.[/]");
                AnsiConsole.MarkupLine("[dodgerblue1]" + Markup.Escape(vessel.Name) + "[/] [dim](" + Markup.Escape(vessel.Id) + ")[/]: " + preview.WarningCount + " overlap warning(s), "
                    + preview.LikelyFileCount + " likely file(s), " + preview.ActiveMissionsCompared + " active mission(s) compared" + (preview.ActiveMissionsTruncated ? " [dim](truncated)[/]" : ""));
                if (preview.Warnings.Count == 0) AnsiConsole.MarkupLine("[green]No overlaps with active missions, hotspots or reservations.[/]");
                foreach (MissionOverlapWarning w in preview.Warnings) BlackBoxInsightRendering.Overlap(w);
                if (!String.IsNullOrEmpty(preview.SuggestedDependsOnMissionId))
                {
                    AnsiConsole.MarkupLine(preview.SerializeOverlappingMissions
                        ? "[dodgerblue1]The vessel serializes overlapping missions: dispatch would chain this mission after " + Markup.Escape(preview.SuggestedDependsOnMissionId!) + ".[/]"
                        : "[dim]Consider --depends-on " + Markup.Escape(preview.SuggestedDependsOnMissionId!) + ", or: armada vessel update " + Markup.Escape(vessel.Id) + " --serialize-overlapping-missions true[/]");
                }

                if (preview.LikelyFiles.Count > 0) AnsiConsole.MarkupLine("[dim]Likely files:[/] " + Markup.Escape(String.Join(", ", preview.LikelyFiles.Take(20))) + (preview.LikelyFiles.Count > 20 ? ", ..." : ""));
                BlackBoxInsightRendering.Lint(preview.BriefLint);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
