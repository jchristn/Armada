namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    #region Settings

    /// <summary>
    /// Settings for mission list command.
    /// </summary>
    public class MissionListSettings : BaseSettings
    {
        /// <summary>
        /// Optional status filter.
        /// </summary>
        [Description("Filter by mission status")]
        [CommandOption("--status|-s")]
        public string? Status { get; set; }

        /// <summary>
        /// Optional vessel filter.
        /// </summary>
        [Description("Filter by vessel name or ID")]
        [CommandOption("--vessel|-v")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Optional captain filter.
        /// </summary>
        [Description("Filter by captain name or ID")]
        [CommandOption("--captain|-c")]
        public string? Captain { get; set; }

        /// <summary>
        /// Optional voyage filter.
        /// </summary>
        [Description("Filter by voyage ID or title")]
        [CommandOption("--voyage")]
        public string? Voyage { get; set; }
    }

    /// <summary>
    /// Settings for mission show command.
    /// </summary>
    public class MissionShowSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title substring.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "<mission>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for mission create command.
    /// </summary>
    public class MissionCreateSettings : BaseSettings
    {
        /// <summary>
        /// Mission title.
        /// </summary>
        [Description("Mission title")]
        [CommandArgument(0, "<title>")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Target vessel identifier or name.
        /// </summary>
        [Description("Target vessel (name or ID)")]
        [CommandOption("--vessel|-v")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Optional voyage identifier to attach to.
        /// </summary>
        [Description("Voyage ID or title")]
        [CommandOption("--voyage")]
        public string? Voyage { get; set; }

        /// <summary>
        /// Optional mission description.
        /// </summary>
        [Description("Mission description")]
        [CommandOption("--description|-d")]
        public string? Description { get; set; }

        /// <summary>
        /// Optional priority (lower is higher).
        /// </summary>
        [Description("Priority (lower is higher)")]
        [CommandOption("--priority|-p")]
        public int? Priority { get; set; }
    }

    /// <summary>
    /// Settings for mission cancel command.
    /// </summary>
    public class MissionCancelSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "<mission>")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Settings for mission restart command.
    /// </summary>
    public class MissionRestartSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "<mission>")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Optional new title for the restarted mission.
        /// </summary>
        [Description("New mission title")]
        [CommandOption("--title|-t")]
        public string? Title { get; set; }

        /// <summary>
        /// Optional new description for the restarted mission.
        /// </summary>
        [Description("New mission description/instructions")]
        [CommandOption("--description|-d")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Settings for mission retry command (alias for restart, creates new mission).
    /// </summary>
    public class MissionRetrySettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "<mission>")]
        public string Id { get; set; } = string.Empty;
    }

    #endregion

    #region Commands

    /// <summary>
    /// List missions with optional filters.
    /// </summary>
    [Description("List missions")]
    public class MissionListCommand : BaseCommand<MissionListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionListSettings settings, CancellationToken cancellationToken)
        {
            List<string> queryParams = new List<string>();
            if (!string.IsNullOrEmpty(settings.Status)) queryParams.Add("status=" + settings.Status);
            if (!string.IsNullOrEmpty(settings.Vessel)) queryParams.Add("vesselId=" + settings.Vessel);
            if (!string.IsNullOrEmpty(settings.Captain)) queryParams.Add("captainId=" + settings.Captain);
            if (!string.IsNullOrEmpty(settings.Voyage)) queryParams.Add("voyageId=" + settings.Voyage);

            string path = "/api/v1/missions";
            if (queryParams.Count > 0)
                path += "?" + string.Join("&", queryParams);
            path = AppendPagination(path, settings);

            EnumerationResult<Mission>? result = await GetAsync<EnumerationResult<Mission>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No missions found.[/] Dispatch work with [green]armada go \"your task\"[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Missions", null);
            table.AddColumn("Id");
            table.AddColumn("Title");
            table.AddColumn("Vessel");
            table.AddColumn("Captain");
            table.AddColumn("Status");
            table.AddColumn("Created");

            foreach (Mission mission in result.Objects)
            {
                string statusColor = TableRenderer.MissionStatusColor(mission.Status.ToString());

                table.AddRow(
                    $"[dim]{Markup.Escape(mission.Id)}[/]",
                    $"[bold]{Markup.Escape(mission.Title)}[/]",
                    Markup.Escape(mission.VesselId ?? "-"),
                    Markup.Escape(mission.CaptainId ?? "-"),
                    $"[{statusColor}]{mission.Status}[/]",
                    $"[dim]{mission.CreatedUtc.ToString("yyyy-MM-dd HH:mm")}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }

    /// <summary>
    /// Show mission details. Accepts ID or title substring.
    /// </summary>
    [Description("Show mission details")]
    public class MissionShowCommand : BaseCommand<MissionShowSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionShowSettings settings, CancellationToken cancellationToken)
        {
            Mission? mission = null;

            // Try direct ID lookup first
            if (settings.Id.StartsWith("msn_"))
            {
                mission = await GetAsync<Mission>($"/api/v1/missions/{settings.Id}").ConfigureAwait(false);
            }

            // Fallback to name-based resolution
            if (mission == null)
            {
                EnumerationResult<Mission>? allResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
                List<Mission>? all = allResult?.Objects;
                if (all != null)
                    mission = EntityResolver.ResolveMission(all, settings.Id);
            }

            if (IsJsonMode(settings))
            {
                WriteJson(mission);
                return mission == null ? 1 : 0;
            }

            if (mission == null)
            {
                AnsiConsole.MarkupLine($"[red]Mission not found:[/] {Markup.Escape(settings.Id)}");
                AnsiConsole.MarkupLine("List missions with [green]armada mission list[/].");
                return 1;
            }

            string statusColor = TableRenderer.MissionStatusColor(mission.Status.ToString());

            Panel panel = new Panel(
                new Rows(
                    new Markup($"[dodgerblue1]Id:[/]          [dim]{Markup.Escape(mission.Id)}[/]"),
                    new Markup($"[dodgerblue1]Title:[/]       [bold]{Markup.Escape(mission.Title)}[/]"),
                    new Markup($"[dodgerblue1]Description:[/] {Markup.Escape(mission.Description ?? "-")}"),
                    new Markup($"[dodgerblue1]Status:[/]      [{statusColor}]{mission.Status}[/]"),
                    new Markup($"[dodgerblue1]Priority:[/]    {mission.Priority}"),
                    new Markup($"[dodgerblue1]Voyage:[/]      {Markup.Escape(mission.VoyageId ?? "-")}"),
                    new Markup($"[dodgerblue1]Vessel:[/]      {Markup.Escape(mission.VesselId ?? "-")}"),
                    new Markup($"[dodgerblue1]Captain:[/]     {Markup.Escape(mission.CaptainId ?? "-")}"),
                    new Markup($"[dodgerblue1]Branch:[/]      {Markup.Escape(mission.BranchName ?? "-")}"),
                    new Markup($"[dodgerblue1]PR:[/]          {Markup.Escape(mission.PrUrl ?? "-")}"),
                    new Markup($"[dodgerblue1]Commit:[/]      {Markup.Escape(mission.CommitHash ?? "-")}"),
                    new Markup($"[dodgerblue1]Created:[/]     {mission.CreatedUtc:yyyy-MM-dd HH:mm}"),
                    new Markup($"[dodgerblue1]Started:[/]     {(mission.StartedUtc.HasValue ? mission.StartedUtc.Value.ToString("yyyy-MM-dd HH:mm") : "-")}"),
                    new Markup($"[dodgerblue1]Completed:[/]   {(mission.CompletedUtc.HasValue ? mission.CompletedUtc.Value.ToString("yyyy-MM-dd HH:mm") : "-")}")));
            panel.Header = new PanelHeader($"[{statusColor}]Mission Details[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.DodgerBlue1);

            AnsiConsole.Write(panel);

            // Show session log if available
            {
                string? logFile = null;

                // Try per-mission log first
                string missionLogPath = Path.Combine(Constants.DefaultDataDirectory, "logs", "missions", mission.Id + ".log");
                if (File.Exists(missionLogPath))
                {
                    logFile = missionLogPath;
                }
                else if (!string.IsNullOrEmpty(mission.CaptainId))
                {
                    // Fallback to captain pointer or direct log
                    string pointerPath = Path.Combine(Constants.DefaultDataDirectory, "logs", "captains", mission.CaptainId + ".current");
                    if (File.Exists(pointerPath))
                    {
                        string target = File.ReadAllText(pointerPath).Trim();
                        if (File.Exists(target)) logFile = target;
                    }

                    if (logFile == null)
                    {
                        string captainLog = Path.Combine(Constants.DefaultDataDirectory, "logs", "captains", mission.CaptainId + ".log");
                        if (File.Exists(captainLog)) logFile = captainLog;
                    }
                }

                if (logFile != null)
                {
                    List<string> lineList = new List<string>();
                    using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string? line;
                        while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                            lineList.Add(line);
                    }
                    string[] lines = lineList.ToArray();
                    if (lines.Length > 0)
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[dodgerblue1]Session Log[/] [dim](last 30 lines)[/]");
                        AnsiConsole.MarkupLine("[dim]" + new string('-', 60) + "[/]");

                        int skip = Math.Max(0, lines.Length - 30);
                        for (int i = skip; i < lines.Length; i++)
                        {
                            AnsiConsole.WriteLine(lines[i]);
                        }

                        AnsiConsole.MarkupLine("[dim]" + new string('-', 60) + "[/]");
                        AnsiConsole.MarkupLine($"[dim]Full log: armada log {Markup.Escape(mission.Id)}[/]");
                    }
                }
            }

            // Suggest next action based on status
            if (mission.Status == MissionStatusEnum.Review || mission.Status == MissionStatusEnum.InProgress || mission.Status == MissionStatusEnum.Testing)
            {
                AnsiConsole.MarkupLine($"  Review changes with [green]armada diff {Markup.Escape(mission.Id)}[/].");
            }

            if (mission.Status == MissionStatusEnum.Failed)
            {
                AnsiConsole.MarkupLine($"  Retry with [green]armada mission retry {Markup.Escape(mission.Id)}[/].");
            }

            return 0;
        }
    }

    /// <summary>
    /// Create a standalone mission.
    /// </summary>
    [Description("Create a new mission")]
    public class MissionCreateCommand : BaseCommand<MissionCreateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionCreateSettings settings, CancellationToken cancellationToken)
        {
            Mission mission = new Mission(settings.Title, settings.Description);

            // Resolve vessel by name or ID
            if (!string.IsNullOrEmpty(settings.Vessel))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;
                if (vessels != null)
                {
                    Vessel? match = EntityResolver.ResolveVessel(vessels, settings.Vessel);
                    if (match != null) mission.VesselId = match.Id;
                    else mission.VesselId = settings.Vessel; // Pass raw, let server validate
                }
            }

            if (!string.IsNullOrEmpty(settings.Voyage)) mission.VoyageId = settings.Voyage;
            if (settings.Priority.HasValue) mission.Priority = settings.Priority.Value;

            Mission? created = await PostAsync<Mission>("/api/v1/missions", mission).ConfigureAwait(false);

            if (created == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to create mission.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Mission created![/]");
            AnsiConsole.MarkupLine($"  [dodgerblue1]Id:[/]     [dim]{Markup.Escape(created.Id)}[/]");
            AnsiConsole.MarkupLine($"  [dodgerblue1]Title:[/]  [bold]{Markup.Escape(created.Title)}[/]");
            AnsiConsole.MarkupLine($"  [dodgerblue1]Status:[/] {created.Status}");
            if (!string.IsNullOrEmpty(created.CaptainId))
                AnsiConsole.MarkupLine($"  [dodgerblue1]Captain:[/] {Markup.Escape(created.CaptainId)}");
            AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            return 0;
        }
    }

    /// <summary>
    /// Cancel a mission. Accepts ID or title.
    /// </summary>
    [Description("Cancel a mission")]
    public class MissionCancelCommand : BaseCommand<MissionCancelSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionCancelSettings settings, CancellationToken cancellationToken)
        {
            string missionId = await ResolveMissionIdAsync(settings.Id).ConfigureAwait(false);

            if (!AnsiConsole.Confirm($"Cancel mission [bold]{Markup.Escape(missionId)}[/]?"))
            {
                AnsiConsole.MarkupLine("[dim]Aborted.[/]");
                return 0;
            }

            try
            {
                await DeleteAsync($"/api/v1/missions/{missionId}").ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[gold1]Mission {Markup.Escape(missionId)} cancelled.[/]");
            }
            catch (HttpRequestException)
            {
                AnsiConsole.MarkupLine("[red]Mission not found or could not be cancelled.[/]");
                AnsiConsole.MarkupLine("List missions with [green]armada mission list[/].");
                return 1;
            }

            return 0;
        }

        private async Task<string> ResolveMissionIdAsync(string identifier)
        {
            if (identifier.StartsWith("msn_")) return identifier;

            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
            List<Mission>? missions = missionResult?.Objects;
            if (missions != null)
            {
                Mission? match = EntityResolver.ResolveMission(missions, identifier);
                if (match != null) return match.Id;
            }
            return identifier;
        }
    }

    /// <summary>
    /// Restart a failed or cancelled mission, resetting it to Pending.
    /// </summary>
    [Description("Restart a failed or cancelled mission")]
    public class MissionRestartCommand : BaseCommand<MissionRestartSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionRestartSettings settings, CancellationToken cancellationToken)
        {
            string missionId = await ResolveMissionIdAsync(settings.Id).ConfigureAwait(false);

            Mission? mission = await GetAsync<Mission>($"/api/v1/missions/{missionId}").ConfigureAwait(false);
            if (mission == null)
            {
                AnsiConsole.MarkupLine($"[red]Mission not found:[/] {Markup.Escape(missionId)}");
                return 1;
            }

            if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
            {
                AnsiConsole.MarkupLine($"[gold1]Mission is {mission.Status}, not Failed or Cancelled.[/] Only failed/cancelled missions can be restarted.");
                return 1;
            }

            string title = settings.Title ?? mission.Title;
            string description = settings.Description ?? mission.Description ?? "";

            AnsiConsole.MarkupLine($"[dodgerblue1]Mission:[/]  {Markup.Escape(missionId)}");
            AnsiConsole.MarkupLine($"[dodgerblue1]Title:[/]    {Markup.Escape(title)}");
            if (title != mission.Title)
                AnsiConsole.MarkupLine($"[dim]  (was: {Markup.Escape(mission.Title)})[/]");

            if (!AnsiConsole.Confirm("Restart this mission?"))
            {
                AnsiConsole.MarkupLine("[dim]Aborted.[/]");
                return 0;
            }

            Mission? restarted = await PostAsync<Mission>($"/api/v1/missions/{missionId}/restart", new
            {
                Title = settings.Title,
                Description = settings.Description
            }).ConfigureAwait(false);

            if (restarted != null)
            {
                AnsiConsole.MarkupLine($"[green]Mission restarted![/] Status: [bold]{restarted.Status}[/]");
                AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            }

            return 0;
        }

        private async Task<string> ResolveMissionIdAsync(string identifier)
        {
            if (identifier.StartsWith("msn_")) return identifier;

            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
            List<Mission>? missions = missionResult?.Objects;
            if (missions != null)
            {
                Mission? match = EntityResolver.ResolveMission(missions, identifier);
                if (match != null) return match.Id;
            }
            return identifier;
        }
    }

    /// <summary>
    /// Retry a failed mission (creates a new mission with the same details).
    /// </summary>
    [Description("Retry a failed mission (creates a new copy)")]
    public class MissionRetryCommand : BaseCommand<MissionRetrySettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionRetrySettings settings, CancellationToken cancellationToken)
        {
            // Resolve mission
            Mission? mission = null;
            if (settings.Id.StartsWith("msn_"))
            {
                mission = await GetAsync<Mission>($"/api/v1/missions/{settings.Id}").ConfigureAwait(false);
            }

            if (mission == null)
            {
                EnumerationResult<Mission>? allResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
                List<Mission>? all = allResult?.Objects;
                if (all != null)
                    mission = EntityResolver.ResolveMission(all, settings.Id);
            }

            if (mission == null)
            {
                AnsiConsole.MarkupLine($"[red]Mission not found:[/] {Markup.Escape(settings.Id)}");
                return 1;
            }

            if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
            {
                AnsiConsole.MarkupLine($"[gold1]Mission is {mission.Status}, not Failed or Cancelled.[/] Only failed/cancelled missions can be retried.");
                return 1;
            }

            // Re-create as a new mission with the same details
            Mission? retried = await PostAsync<Mission>("/api/v1/missions", new
            {
                Title = mission.Title,
                Description = mission.Description,
                VesselId = mission.VesselId,
                VoyageId = mission.VoyageId,
                Priority = mission.Priority
            }).ConfigureAwait(false);

            if (retried != null)
            {
                AnsiConsole.MarkupLine($"[green]Mission retried![/]");
                AnsiConsole.MarkupLine($"  [dodgerblue1]Original:[/] [dim]{Markup.Escape(mission.Id)}[/]");
                AnsiConsole.MarkupLine($"  [dodgerblue1]New:[/]      [dim]{Markup.Escape(retried.Id)}[/]");
                AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            }

            return 0;
        }
    }

    #endregion
}
