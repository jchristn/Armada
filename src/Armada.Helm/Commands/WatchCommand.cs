namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Helm.Rendering;

    #region Settings

    /// <summary>
    /// Settings for watch command.
    /// </summary>
    public class WatchSettings : BaseSettings
    {
        /// <summary>
        /// Refresh interval in seconds.
        /// </summary>
        [Description("Refresh interval in seconds (default: 5)")]
        [CommandOption("--interval|-i")]
        public int? Interval { get; set; }

        /// <summary>
        /// Filter to a specific captain.
        /// </summary>
        [Description("Filter to a specific captain")]
        [CommandOption("--captain|-c")]
        public string? Captain { get; set; }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Live-updating status dashboard with action-required alerts.
    /// </summary>
    [Description("Live-updating status dashboard")]
    public class WatchCommand : BaseCommand<WatchSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, WatchSettings settings, CancellationToken cancellationToken)
        {
            int intervalMs = (settings.Interval ?? 5) * 1000;
            ArmadaSettings armadaSettings = GetSettings();

            // Track previous state for change detection
            HashSet<string> _SeenCompletedIds = new HashSet<string>();
            HashSet<string> _SeenFailedIds = new HashSet<string>();
            bool firstPoll = true;

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // Ensure server is running before clearing screen,
            // so startup messages don't bleed into the live view.
            await EnsureServerAsync().ConfigureAwait(false);
            AnsiConsole.Clear();

            await AnsiConsole.Live(new Text("Loading..."))
                .AutoClear(true)
                .StartAsync(async ctx =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            // Fetch all data in parallel
                            Task<ArmadaStatus?> statusTask = GetAsync<ArmadaStatus>("/api/v1/status");
                            Task<EnumerationResult<Captain>?> captainsTask = GetAsync<EnumerationResult<Captain>>("/api/v1/captains");
                            Task<EnumerationResult<Mission>?> missionsTask = GetAsync<EnumerationResult<Mission>>("/api/v1/missions");
                            Task<EnumerationResult<Vessel>?> vesselsTask = GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels");
                            Task<EnumerationResult<Voyage>?> voyagesTask = GetAsync<EnumerationResult<Voyage>>("/api/v1/voyages");
                            Task<EnumerationResult<ArmadaEvent>?> eventsTask = GetAsync<EnumerationResult<ArmadaEvent>>("/api/v1/events?limit=10");

                            await Task.WhenAll(statusTask, captainsTask, missionsTask, vesselsTask, voyagesTask, eventsTask).ConfigureAwait(false);

                            ArmadaStatus? status = statusTask.Result;
                            if (status == null)
                            {
                                ctx.UpdateTarget(new Markup("[red]Unable to connect to Admiral server.[/] Retrying..."));
                                await Task.Delay(intervalMs, cts.Token).ConfigureAwait(false);
                                continue;
                            }

                            List<Captain> captains = captainsTask.Result?.Objects ?? new List<Captain>();
                            List<Mission> missions = missionsTask.Result?.Objects ?? new List<Mission>();
                            List<Vessel> vessels = vesselsTask.Result?.Objects ?? new List<Vessel>();
                            List<Voyage> voyages = voyagesTask.Result?.Objects ?? new List<Voyage>();
                            List<ArmadaEvent> events = eventsTask.Result?.Objects ?? new List<ArmadaEvent>();

                            // Build lookup dictionaries
                            Dictionary<string, Vessel> vesselLookup = new Dictionary<string, Vessel>();
                            foreach (Vessel v in vessels) vesselLookup[v.Id] = v;

                            Dictionary<string, Voyage> voyageLookup = new Dictionary<string, Voyage>();
                            foreach (Voyage v in voyages) voyageLookup[v.Id] = v;

                            Dictionary<string, Captain> captainLookup = new Dictionary<string, Captain>();
                            foreach (Captain c in captains) captainLookup[c.Id] = c;

                            Dictionary<string, Mission> missionLookup = new Dictionary<string, Mission>();
                            foreach (Mission m in missions) missionLookup[m.Id] = m;

                            Table dashboard = BuildDashboard(status, settings.Captain, captains, missions, events, vesselLookup, voyageLookup, captainLookup, missionLookup);
                            ctx.UpdateTarget(dashboard);

                            // Detect new completions and failures for notifications
                            List<Mission> completedMissions = missions.FindAll(m => m.Status == MissionStatusEnum.Complete);
                            List<Mission> failedMissions = missions.FindAll(m => m.Status == MissionStatusEnum.Failed);

                            if (!firstPoll)
                            {
                                // Collect new missions before adding to seen sets
                                List<Mission> newlyCompleted = completedMissions.FindAll(m => !_SeenCompletedIds.Contains(m.Id));
                                List<Mission> newlyFailed = failedMissions.FindAll(m => !_SeenFailedIds.Contains(m.Id));

                                foreach (Mission m in newlyCompleted) _SeenCompletedIds.Add(m.Id);
                                foreach (Mission m in newlyFailed) _SeenFailedIds.Add(m.Id);

                                if (newlyCompleted.Count > 0 || newlyFailed.Count > 0)
                                {
                                    if (armadaSettings.TerminalBell)
                                    {
                                        NotificationService.Bell();
                                    }

                                    if (armadaSettings.Notifications)
                                    {
                                        // Build notification with mission titles
                                        List<string> notifLines = new List<string>();
                                        foreach (Mission cm in newlyCompleted)
                                            notifLines.Add("Completed: " + cm.Title);
                                        foreach (Mission fm in newlyFailed)
                                            notifLines.Add("Failed: " + fm.Title);

                                        string notifTitle = notifLines.Count == 1
                                            ? notifLines[0]
                                            : $"{newlyCompleted.Count + newlyFailed.Count} missions finished";
                                        string notifBody = notifLines.Count > 1
                                            ? string.Join("\n", notifLines.Take(5))
                                            : "";

                                        NotificationService.Send("Armada", notifTitle + (string.IsNullOrEmpty(notifBody) ? "" : "\n" + notifBody));
                                    }
                                }
                            }
                            else
                            {
                                // Seed seen sets on first poll
                                foreach (Mission m in completedMissions) _SeenCompletedIds.Add(m.Id);
                                foreach (Mission m in failedMissions) _SeenFailedIds.Add(m.Id);
                                firstPoll = false;
                            }
                        }
                        catch (HttpRequestException)
                        {
                            ctx.UpdateTarget(new Markup("[red]Connection lost. Retrying...[/]"));
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        try
                        {
                            await Task.Delay(intervalMs, cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }).ConfigureAwait(false);

            return 0;
        }

        private Table BuildDashboard(
            ArmadaStatus status,
            string? captainFilter,
            List<Captain> captains,
            List<Mission> missions,
            List<ArmadaEvent> events,
            Dictionary<string, Vessel> vesselLookup,
            Dictionary<string, Voyage> voyageLookup,
            Dictionary<string, Captain> captainLookup,
            Dictionary<string, Mission> missionLookup)
        {
            Table outer = new Table();
            outer.Border(TableBorder.Heavy);
            outer.BorderColor(Color.DodgerBlue1);
            outer.Width(Math.Max(90, Console.WindowWidth - 2));
            outer.Title("[bold dodgerblue1]Armada Watch[/]  [dim]" + DateTime.UtcNow.ToString("HH:mm:ss") + " UTC  server: " + GetBaseUrl() + "  ctrl-c to end[/]");
            outer.AddColumn(new TableColumn("").NoWrap());

            // Summary row
            string summaryRow =
                $"[bold]Captains:[/] [dodgerblue1]{status.IdleCaptains} idle[/] [green]{status.WorkingCaptains} working[/] [red]{status.StalledCaptains} stalled[/]";
            if (status.MissionsByStatus.Count > 0)
            {
                List<string> missionParts = new List<string>();
                foreach (KeyValuePair<string, int> kvp in status.MissionsByStatus)
                {
                    string color = TableRenderer.MissionStatusColor(kvp.Key);
                    missionParts.Add($"[{color}]{kvp.Value} {kvp.Key}[/]");
                }
                summaryRow += $"    [bold]Missions:[/] " + String.Join(" ", missionParts);
            }
            outer.AddRow(new Markup(summaryRow));

            // Missions table — show active + recently completed/failed (last 30 min)
            DateTime now = DateTime.UtcNow;
            List<Mission> displayMissions = new List<Mission>();

            // Active missions first
            List<Mission> activeMissions = missions.FindAll(m =>
                m.Status == MissionStatusEnum.Pending ||
                m.Status == MissionStatusEnum.Assigned ||
                m.Status == MissionStatusEnum.InProgress ||
                m.Status == MissionStatusEnum.Testing ||
                m.Status == MissionStatusEnum.Review);

            // Apply captain filter
            if (!string.IsNullOrEmpty(captainFilter))
            {
                activeMissions = activeMissions.FindAll(m =>
                    !string.IsNullOrEmpty(m.CaptainId) &&
                    (m.CaptainId.Contains(captainFilter, StringComparison.OrdinalIgnoreCase) ||
                     (captainLookup.TryGetValue(m.CaptainId, out Captain? fc) && fc.Name.Contains(captainFilter, StringComparison.OrdinalIgnoreCase))));
            }

            displayMissions.AddRange(activeMissions.OrderBy(m => m.CreatedUtc));

            // Recently finished (last 30 min)
            List<Mission> recentFinished = missions.FindAll(m =>
                (m.Status == MissionStatusEnum.Complete || m.Status == MissionStatusEnum.Failed) &&
                m.CompletedUtc.HasValue && (now - m.CompletedUtc.Value).TotalMinutes <= 30);
            displayMissions.AddRange(recentFinished.OrderByDescending(m => m.CompletedUtc));

            if (displayMissions.Count > 0)
            {
                outer.AddRow(new Markup(""));

                Table missionTable = new Table();
                missionTable.Border(TableBorder.Rounded);
                missionTable.AddColumn(new TableColumn("[bold]Status[/]").Width(12));
                missionTable.AddColumn(new TableColumn("[bold]Mission[/]"));
                missionTable.AddColumn(new TableColumn("[bold]Captain[/]").Width(14));
                missionTable.AddColumn(new TableColumn("[bold]Vessel[/]").Width(16));
                missionTable.AddColumn(new TableColumn("[bold]Elapsed[/]").Width(8));

                foreach (Mission mission in displayMissions)
                {
                    string statusStr = mission.Status.ToString();
                    string statusColor = TableRenderer.MissionStatusColor(statusStr);
                    string statusMarkup = $"[{statusColor}]{statusStr}[/]";

                    string title = Markup.Escape(Truncate(mission.Title, 60));

                    string captainName = "";
                    if (!string.IsNullOrEmpty(mission.CaptainId) && captainLookup.TryGetValue(mission.CaptainId, out Captain? cap))
                        captainName = Markup.Escape(cap.Name);

                    string vesselName = "";
                    if (!string.IsNullOrEmpty(mission.VesselId) && vesselLookup.TryGetValue(mission.VesselId, out Vessel? ves))
                        vesselName = Markup.Escape(ves.Name);

                    string elapsed = "";
                    if (mission.StartedUtc.HasValue)
                    {
                        DateTime endTime = mission.CompletedUtc ?? now;
                        elapsed = FormatAge(endTime - mission.StartedUtc.Value);
                    }

                    missionTable.AddRow(statusMarkup, title, captainName, vesselName, elapsed);
                }

                outer.AddRow(missionTable);
            }

            // Voyage progress (if any active)
            List<VoyageProgress> activeVoyages = status.Voyages.FindAll(vp => vp.Voyage != null &&
                (vp.Voyage.Status == VoyageStatusEnum.InProgress || vp.Voyage.Status == VoyageStatusEnum.Open));
            if (activeVoyages.Count > 0)
            {
                outer.AddRow(new Markup(""));
                foreach (VoyageProgress vp in activeVoyages)
                {
                    string title = Markup.Escape(Truncate(vp.Voyage!.Title, 50));
                    int pct = vp.TotalMissions > 0 ? (int)(100.0 * vp.CompletedMissions / vp.TotalMissions) : 0;
                    string bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);
                    string progressColor = vp.FailedMissions > 0 ? "red" : "green";
                    string failedNote = vp.FailedMissions > 0 ? $" [red]{vp.FailedMissions} failed[/]" : "";

                    outer.AddRow(new Markup($"  [bold]{title}[/]  [{progressColor}]{bar}[/] {vp.CompletedMissions}/{vp.TotalMissions}{failedNote}"));
                }
            }

            // Event log
            BuildEventLog(outer, events);

            // ACTION REQUIRED section (stalled captains only — completed/failed now in table)
            BuildActionRequired(outer, captains, missions, vesselLookup, voyageLookup, captainLookup, missionLookup);

            return outer;
        }

        private void BuildEventLog(Table outer, List<ArmadaEvent> events)
        {
            if (events.Count == 0) return;

            outer.AddRow(new Markup(""));
            outer.AddRow(new Markup("[bold]Event Log[/]"));

            // Events come back newest-first from API; display in that order
            foreach (ArmadaEvent evt in events)
            {
                string time = evt.CreatedUtc.ToString("HH:mm:ss");
                string color = EventTypeColor(evt.EventType);
                string message = Markup.Escape(Truncate(evt.Message, 100));

                // Build ID context from event metadata
                List<string> ids = new List<string>();
                if (!string.IsNullOrEmpty(evt.MissionId)) ids.Add(evt.MissionId);
                if (!string.IsNullOrEmpty(evt.CaptainId)) ids.Add(evt.CaptainId);
                if (!string.IsNullOrEmpty(evt.VesselId)) ids.Add(evt.VesselId);
                if (!string.IsNullOrEmpty(evt.VoyageId)) ids.Add(evt.VoyageId);
                string idSuffix = ids.Count > 0 ? "  [dim]" + Markup.Escape(string.Join(" ", ids)) + "[/]" : "";

                outer.AddRow(new Markup($"  [dim]{time}[/]  [{color}]{message}[/]{idSuffix}"));
            }
        }

        private string EventTypeColor(string eventType)
        {
            if (eventType.Contains("completed")) return "green";
            if (eventType.Contains("failed") || eventType.Contains("error")) return "red";
            if (eventType.Contains("stalled")) return "yellow";
            if (eventType.Contains("created") || eventType.Contains("assigned")) return "dodgerblue1";
            if (eventType.Contains("started") || eventType.Contains("progress")) return "cyan1";
            return "dim";
        }

        private void BuildActionRequired(
            Table outer,
            List<Captain> captains,
            List<Mission> missions,
            Dictionary<string, Vessel> vesselLookup,
            Dictionary<string, Voyage> voyageLookup,
            Dictionary<string, Captain> captainLookup,
            Dictionary<string, Mission> missionLookup)
        {
            List<string> actionItems = new List<string>();
            DateTime now = DateTime.UtcNow;

            // Stalled captains
            foreach (Captain captain in captains)
            {
                if (captain.State != CaptainStateEnum.Stalled) continue;

                string chain = BuildChain(captain, captain.CurrentMissionId, missionLookup, vesselLookup);
                string heartbeatAge = captain.LastHeartbeatUtc.HasValue
                    ? FormatAge(now - captain.LastHeartbeatUtc.Value)
                    : "never";

                actionItems.Add($"  [red]STALLED[/]  {chain}  [dim](no heartbeat for {heartbeatAge})[/]");
                actionItems.Add($"           [dim]> armada captain stop {Markup.Escape(captain.Name)}[/]");
            }

            // Missions in Review status
            foreach (Mission mission in missions)
            {
                if (mission.Status != MissionStatusEnum.Review) continue;
                actionItems.Add($"  [dodgerblue1]REVIEW[/]   [bold]{Markup.Escape(mission.Title)}[/]");
                actionItems.Add($"           [dim]> armada diff {Markup.Escape(mission.Id)}[/]");
            }

            if (actionItems.Count > 0)
            {
                outer.AddRow(new Markup(""));
                outer.AddRow(new Markup("[bold yellow]ACTION REQUIRED[/]"));
                foreach (string item in actionItems)
                {
                    outer.AddRow(new Markup(item));
                }
            }
        }

        /// <summary>
        /// Build the chain string for a captain: "captain-name >vessel-name >mission title"
        /// </summary>
        private string BuildChain(Captain captain, string? missionId, Dictionary<string, Mission> missionLookup, Dictionary<string, Vessel> vesselLookup)
        {
            string captainName = $"[bold]{Markup.Escape(captain.Name)}[/]";

            string vesselName = "";
            string missionTitle = "";

            if (!string.IsNullOrEmpty(missionId) && missionLookup.TryGetValue(missionId, out Mission? mission))
            {
                missionTitle = Markup.Escape(Truncate(mission.Title, 50));

                if (!string.IsNullOrEmpty(mission.VesselId) && vesselLookup.TryGetValue(mission.VesselId, out Vessel? vessel))
                {
                    vesselName = Markup.Escape(vessel.Name);
                }
            }

            if (!string.IsNullOrEmpty(vesselName) && !string.IsNullOrEmpty(missionTitle))
            {
                return $"{captainName} >{vesselName} >\"{missionTitle}\"";
            }
            else if (!string.IsNullOrEmpty(missionTitle))
            {
                return $"{captainName} >\"{missionTitle}\"";
            }

            return captainName;
        }

        /// <summary>
        /// Build the chain string for a mission: "captain-name >vessel-name >mission title"
        /// </summary>
        private string BuildMissionChain(Mission mission, Dictionary<string, Captain> captainLookup, Dictionary<string, Vessel> vesselLookup)
        {
            string captainName = "";
            if (!string.IsNullOrEmpty(mission.CaptainId) && captainLookup.TryGetValue(mission.CaptainId, out Captain? captain))
            {
                captainName = captain.Name;
            }

            string vesselName = "";
            if (!string.IsNullOrEmpty(mission.VesselId) && vesselLookup.TryGetValue(mission.VesselId, out Vessel? vessel))
            {
                vesselName = vessel.Name;
            }

            string missionTitle = Markup.Escape(Truncate(mission.Title, 50));

            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(captainName)) parts.Add($"[bold]{Markup.Escape(captainName)}[/]");
            if (!string.IsNullOrEmpty(vesselName)) parts.Add(Markup.Escape(vesselName));
            parts.Add($"\"{missionTitle}\"");

            return string.Join(" >", parts);
        }

        /// <summary>
        /// Build voyage context string for a mission (e.g. "API Hardening · ").
        /// Returns empty string if mission has no voyage.
        /// </summary>
        private string BuildVoyageInfo(Mission mission, Dictionary<string, Voyage> voyageLookup)
        {
            if (!string.IsNullOrEmpty(mission.VoyageId) && voyageLookup.TryGetValue(mission.VoyageId, out Voyage? voyage))
            {
                return $"{Markup.Escape(voyage.Title)} · ";
            }
            return "";
        }

        /// <summary>
        /// Format a TimeSpan as a human-readable age string.
        /// </summary>
        private string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours}h";
            return $"{(int)age.TotalDays}d";
        }

        /// <summary>
        /// Truncate a string to maxLength, appending "..." if truncated.
        /// </summary>
        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength - 3) + "...";
        }
    }

    #endregion
}
