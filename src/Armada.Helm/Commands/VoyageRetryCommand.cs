namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Retry all failed missions in a voyage.
    /// </summary>
    [Description("Retry failed missions in a voyage")]
    public class VoyageRetryCommand : BaseCommand<VoyageRetrySettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageRetrySettings settings, CancellationToken cancellationToken)
        {
            // Resolve voyage
            Voyage? voyage = null;
            if (settings.Id.StartsWith("vyg_"))
            {
                VoyageProgress? detail = await GetAsync<VoyageProgress>($"/api/v1/voyages/{settings.Id}").ConfigureAwait(false);
                if (detail?.Voyage != null)
                    voyage = detail.Voyage;
            }

            if (voyage == null)
            {
                EnumerationResult<Voyage>? allResult = await GetAsync<EnumerationResult<Voyage>>("/api/v1/voyages").ConfigureAwait(false);
                List<Voyage>? all = allResult?.Objects;
                if (all != null)
                    voyage = EntityResolver.ResolveVoyage(all, settings.Id);
            }

            if (voyage == null)
            {
                HelmConsole.Error.MarkupLine($"[red]Voyage not found:[/] {Markup.Escape(settings.Id)}");
                return 1;
            }

            // Find every failed or cancelled mission, reading bounded pages explicitly (before any retry is created).
            List<Mission> failed = new List<Mission>();
            foreach (MissionStatusEnum status in new[] { MissionStatusEnum.Failed, MissionStatusEnum.Cancelled })
            {
                for (int pageNumber = 1; ; pageNumber++)
                {
                    EnumerationResult<Mission>? page = await GetAsync<EnumerationResult<Mission>>($"/api/v1/missions?voyageId={voyage.Id}&status={status}&pageNumber={pageNumber}&pageSize=100&order=CreatedAscending").ConfigureAwait(false);
                    if (page == null)
                    {
                        HelmConsole.Error.MarkupLine("[red]Failed to fetch missions.[/]");
                        return 1;
                    }

                    failed.AddRange(page.Objects);
                    if (page.Objects.Count == 0 || pageNumber >= page.TotalPages) break;
                }
            }

            if (failed.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No failed or cancelled missions to retry.[/]");
                return 0;
            }

            int retried = 0;
            foreach (Mission m in failed)
            {
                MissionDispatchResult? result = await CreateMissionAsync(new
                {
                    Title = m.Title,
                    Description = m.Description,
                    VesselId = m.VesselId,
                    VoyageId = m.VoyageId,
                    Priority = m.Priority
                }).ConfigureAwait(false);
                Mission? newMission = result?.Mission;

                if (newMission != null)
                {
                    AnsiConsole.MarkupLine($"  [green]Retried:[/] {Markup.Escape(m.Title)} -> [dim]{Markup.Escape(newMission.Id)}[/]");
                    retried++;
                }
            }

            AnsiConsole.MarkupLine($"\n[green]{retried} mission(s) retried.[/] Run [green]armada watch[/] to monitor.");
            return 0;
        }
    }
}
