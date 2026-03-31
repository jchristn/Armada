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
}
