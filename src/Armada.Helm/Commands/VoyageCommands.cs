namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    #region Settings

    /// <summary>
    /// Settings for voyage list command.
    /// </summary>
    public class VoyageListSettings : BaseSettings
    {
        /// <summary>
        /// Optional status filter.
        /// </summary>
        [Description("Filter by voyage status")]
        [CommandOption("--status|-s")]
        public string? Status { get; set; }
    }

    /// <summary>
    /// Settings for voyage create command.
    /// </summary>
    public class VoyageCreateSettings : BaseSettings
    {
        /// <summary>
        /// Voyage title.
        /// </summary>
        [Description("Voyage title")]
        [CommandArgument(0, "<title>")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Target vessel name or ID.
        /// </summary>
        [Description("Target vessel (name or ID)")]
        [CommandOption("--vessel|-v")]
        public string? VesselId { get; set; }

        /// <summary>
        /// Mission descriptions (repeatable).
        /// </summary>
        [Description("Mission description (repeatable)")]
        [CommandOption("--mission|-m")]
        public string[]? Missions { get; set; }
    }

    /// <summary>
    /// Settings for voyage show command.
    /// </summary>
    public class VoyageShowSettings : BaseSettings
    {
        /// <summary>
        /// Voyage identifier or title.
        /// </summary>
        [Description("Voyage ID or title")]
        [CommandArgument(0, "<voyage>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for voyage cancel command.
    /// </summary>
    public class VoyageCancelSettings : BaseSettings
    {
        /// <summary>
        /// Voyage identifier or title.
        /// </summary>
        [Description("Voyage ID or title")]
        [CommandArgument(0, "<voyage>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for voyage retry command.
    /// </summary>
    public class VoyageRetrySettings : BaseSettings
    {
        /// <summary>
        /// Voyage identifier or title.
        /// </summary>
        [Description("Voyage ID or title")]
        [CommandArgument(0, "<voyage>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response wrapper for GET /api/v1/voyages/{id}.
    /// </summary>
    public class VoyageDetailResponse
    {
        /// <summary>
        /// Voyage details.
        /// </summary>
        public Voyage? Voyage { get; set; }

        /// <summary>
        /// Missions in this voyage.
        /// </summary>
        public List<Mission>? Missions { get; set; }
    }

    #endregion

    #region Commands

    /// <summary>
    /// List all voyages.
    /// </summary>
    [Description("List all voyages")]
    public class VoyageListCommand : BaseCommand<VoyageListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageListSettings settings, CancellationToken cancellationToken)
        {
            string path = "/api/v1/voyages";
            if (!string.IsNullOrEmpty(settings.Status))
                path += "?status=" + settings.Status;
            path = AppendPagination(path, settings);

            EnumerationResult<Voyage>? result = await GetAsync<EnumerationResult<Voyage>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No voyages found.[/] Start one with [green]armada go \"your task\"[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Voyages", null);
            table.AddColumn("Id");
            table.AddColumn("Title");
            table.AddColumn("Status");
            table.AddColumn("Created");

            foreach (Voyage voyage in result.Objects)
            {
                string statusColor = voyage.Status switch
                {
                    VoyageStatusEnum.Complete => "green",
                    VoyageStatusEnum.InProgress => "gold1",
                    VoyageStatusEnum.Cancelled => "grey",
                    _ => "dodgerblue1"
                };

                table.AddRow(
                    $"[dim]{Markup.Escape(voyage.Id)}[/]",
                    $"[bold]{Markup.Escape(voyage.Title)}[/]",
                    $"[{statusColor}]{voyage.Status}[/]",
                    $"[dim]{voyage.CreatedUtc.ToString("yyyy-MM-dd HH:mm")}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }

    /// <summary>
    /// Create a new voyage with missions.
    /// </summary>
    [Description("Create a new voyage with missions")]
    public class VoyageCreateCommand : BaseCommand<VoyageCreateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageCreateSettings settings, CancellationToken cancellationToken)
        {
            // Resolve vessel by name
            string? vesselId = settings.VesselId;
            if (!string.IsNullOrEmpty(vesselId) && !vesselId.StartsWith("vsl_"))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;
                if (vessels != null)
                {
                    Vessel? match = EntityResolver.ResolveVessel(vessels, vesselId);
                    if (match != null) vesselId = match.Id;
                }
            }

            List<object> missions = new List<object>();

            if (settings.Missions != null)
            {
                foreach (string desc in settings.Missions)
                {
                    missions.Add(new
                    {
                        Title = desc,
                        Description = desc,
                        VesselId = vesselId
                    });
                }
            }

            object body = new
            {
                Title = settings.Title,
                VesselId = vesselId,
                Missions = missions
            };

            Voyage? voyage = await PostAsync<Voyage>("/api/v1/voyages", body).ConfigureAwait(false);

            if (voyage != null)
            {
                AnsiConsole.MarkupLine($"[green]Voyage launched![/] [bold]{Markup.Escape(voyage.Title)}[/] [dim]({Markup.Escape(voyage.Id)})[/]");
                AnsiConsole.MarkupLine($"  [dodgerblue1]{missions.Count} mission(s)[/] queued.");
                AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            }

            return 0;
        }
    }

    /// <summary>
    /// Show voyage details. Accepts ID or title.
    /// </summary>
    [Description("Show voyage details")]
    public class VoyageShowCommand : BaseCommand<VoyageShowSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageShowSettings settings, CancellationToken cancellationToken)
        {
            Voyage? voyage = null;

            if (settings.Id.StartsWith("vyg_"))
            {
                // Server returns { Voyage: {...}, Missions: [...] } wrapper
                VoyageDetailResponse? detail = await GetAsync<VoyageDetailResponse>($"/api/v1/voyages/{settings.Id}").ConfigureAwait(false);
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
                AnsiConsole.MarkupLine($"[red]Voyage not found:[/] {Markup.Escape(settings.Id)}");
                AnsiConsole.MarkupLine("List voyages with [green]armada voyage list[/].");
                return 1;
            }

            string statusColor = voyage.Status switch
            {
                VoyageStatusEnum.Complete => "green",
                VoyageStatusEnum.InProgress => "gold1",
                VoyageStatusEnum.Cancelled => "grey",
                _ => "dodgerblue1"
            };

            Panel panel = new Panel(
                new Rows(
                    new Markup($"[dodgerblue1]Id:[/]          [dim]{Markup.Escape(voyage.Id)}[/]"),
                    new Markup($"[dodgerblue1]Title:[/]       [bold]{Markup.Escape(voyage.Title)}[/]"),
                    new Markup($"[dodgerblue1]Status:[/]      [{statusColor}]{voyage.Status}[/]"),
                    new Markup($"[dodgerblue1]Created:[/]     {voyage.CreatedUtc:yyyy-MM-dd HH:mm}"),
                    new Markup($"[dodgerblue1]Description:[/] {Markup.Escape(voyage.Description ?? "-")}")));
            panel.Header = new PanelHeader("[bold dodgerblue1]Voyage Details[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.DodgerBlue1);

            AnsiConsole.Write(panel);

            // Fetch missions for this voyage
            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>($"/api/v1/missions?voyageId={voyage.Id}").ConfigureAwait(false);
            List<Mission>? missions = missionResult?.Objects;

            if (missions != null && missions.Count > 0)
            {
                Table table = TableRenderer.CreateTable("Missions", null);
                table.AddColumn("[bold]Id[/]");
                table.AddColumn("[bold]Title[/]");
                table.AddColumn("[bold]Status[/]");
                table.AddColumn("[bold]Captain[/]");

                foreach (Mission mission in missions)
                {
                    string missionColor = TableRenderer.MissionStatusColor(mission.Status.ToString());

                    table.AddRow(
                        $"[dim]{Markup.Escape(mission.Id)}[/]",
                        $"[bold]{Markup.Escape(mission.Title)}[/]",
                        $"[{missionColor}]{mission.Status}[/]",
                        Markup.Escape(mission.CaptainId ?? "-"));
                }

                AnsiConsole.Write(table);

                // Suggest retry if there are failed missions
                int failedCount = missions.Count(m => m.Status == MissionStatusEnum.Failed);
                if (failedCount > 0)
                {
                    AnsiConsole.MarkupLine($"  [red]{failedCount} failed mission(s).[/] Retry with [green]armada voyage retry {Markup.Escape(voyage.Id)}[/].");
                }

                // Suggest viewing logs
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]View mission details/logs with:[/]");
                foreach (Mission mission in missions)
                {
                    AnsiConsole.MarkupLine($"  [green]armada mission show {Markup.Escape(mission.Id)}[/]");
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// Cancel a voyage. Accepts ID or title.
    /// </summary>
    [Description("Cancel a voyage")]
    public class VoyageCancelCommand : BaseCommand<VoyageCancelSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageCancelSettings settings, CancellationToken cancellationToken)
        {
            string voyageId = await ResolveVoyageIdAsync(settings.Id).ConfigureAwait(false);

            await DeleteAsync($"/api/v1/voyages/{voyageId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Voyage cancelled:[/] [bold]{Markup.Escape(voyageId)}[/]");
            return 0;
        }

        private async Task<string> ResolveVoyageIdAsync(string identifier)
        {
            if (identifier.StartsWith("vyg_")) return identifier;

            EnumerationResult<Voyage>? voyageResult = await GetAsync<EnumerationResult<Voyage>>("/api/v1/voyages").ConfigureAwait(false);
            List<Voyage>? voyages = voyageResult?.Objects;
            if (voyages != null)
            {
                Voyage? match = EntityResolver.ResolveVoyage(voyages, identifier);
                if (match != null) return match.Id;
            }
            return identifier;
        }
    }

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
                VoyageDetailResponse? detail = await GetAsync<VoyageDetailResponse>($"/api/v1/voyages/{settings.Id}").ConfigureAwait(false);
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
                AnsiConsole.MarkupLine($"[red]Voyage not found:[/] {Markup.Escape(settings.Id)}");
                return 1;
            }

            // Find failed missions
            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>($"/api/v1/missions?voyageId={voyage.Id}").ConfigureAwait(false);
            List<Mission>? missions = missionResult?.Objects;
            if (missions == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to fetch missions.[/]");
                return 1;
            }

            List<Mission> failed = missions.FindAll(m =>
                m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.Cancelled);

            if (failed.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No failed or cancelled missions to retry.[/]");
                return 0;
            }

            int retried = 0;
            foreach (Mission m in failed)
            {
                Mission? newMission = await PostAsync<Mission>("/api/v1/missions", new
                {
                    Title = m.Title,
                    Description = m.Description,
                    VesselId = m.VesselId,
                    VoyageId = m.VoyageId,
                    Priority = m.Priority
                }).ConfigureAwait(false);

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

    #endregion
}
