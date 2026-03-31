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

    /// <summary>
    /// Show Armada dashboard status.
    /// Contextual: when run inside a git repo, focuses on that vessel.
    /// </summary>
    [Description("Show Armada status dashboard")]
    public class StatusCommand : BaseCommand<StatusSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, StatusSettings settings, CancellationToken cancellationToken)
        {
            ArmadaStatus? status = await GetAsync<ArmadaStatus>("/api/v1/status").ConfigureAwait(false);

            if (IsJsonMode(settings))
            {
                WriteJson(status);
                return status == null ? 1 : 0;
            }

            if (status == null)
            {
                AnsiConsole.MarkupLine("[red]Unable to retrieve status.[/] Is the Admiral running? Try [green]armada server start[/].");
                return 1;
            }

            // Contextual: detect current vessel from CWD
            string? contextVesselId = null;
            string? contextVesselName = null;
            if (!settings.All)
            {
                string cwd = Directory.GetCurrentDirectory();
                if (GitInference.IsGitRepository(cwd))
                {
                    string? remoteUrl = GitInference.GetRemoteUrl(cwd);
                    if (!string.IsNullOrEmpty(remoteUrl))
                    {
                        EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                        List<Vessel>? vessels = vesselResult?.Objects;
                        if (vessels != null)
                        {
                            Vessel? match = EntityResolver.ResolveVesselByRemoteUrl(vessels, remoteUrl);
                            if (match != null)
                            {
                                contextVesselId = match.Id;
                                contextVesselName = match.Name;
                            }
                        }
                    }
                }
            }

            // Header
            Program.WriteBanner();

            if (contextVesselName != null)
            {
                AnsiConsole.MarkupLine($"[dim]Status for[/] [bold dodgerblue1]{Markup.Escape(contextVesselName)}[/]  [dim](use --all for global view)[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Multi-Agent Orchestration System[/]");
            }
            AnsiConsole.WriteLine();

            // Captain summary
            Table captainTable = TableRenderer.CreateTable("Captains", null);
            captainTable.AddColumn("[bold]Total[/]");
            captainTable.AddColumn("[dodgerblue1]Idle[/]");
            captainTable.AddColumn("[green]Working[/]");
            captainTable.AddColumn("[red]Stalled[/]");
            captainTable.AddRow(
                $"[bold]{status.TotalCaptains}[/]",
                $"[dodgerblue1]{status.IdleCaptains}[/]",
                $"[green]{status.WorkingCaptains}[/]",
                $"[red]{status.StalledCaptains}[/]");
            AnsiConsole.Write(captainTable);
            AnsiConsole.WriteLine();

            // Mission breakdown (filtered by vessel if contextual)
            if (status.MissionsByStatus.Count > 0)
            {
                Table missionTable = TableRenderer.CreateTable("Missions", null);
                missionTable.AddColumn("Status");
                missionTable.AddColumn("Count");

                foreach (KeyValuePair<string, int> kvp in status.MissionsByStatus)
                {
                    string color = TableRenderer.MissionStatusColor(kvp.Key);

                    missionTable.AddRow(
                        $"[{color}]{kvp.Key}[/]",
                        $"[bold]{kvp.Value}[/]");
                }

                AnsiConsole.Write(missionTable);
                AnsiConsole.WriteLine();
            }

            // Active voyages with progress
            if (status.Voyages.Count > 0)
            {
                Table voyageTable = TableRenderer.CreateTable($"Active Voyages ({status.ActiveVoyages})", null);
                voyageTable.AddColumn("Voyage");
                voyageTable.AddColumn("Progress");
                voyageTable.AddColumn("Status");

                foreach (VoyageProgress vp in status.Voyages)
                {
                    string statusColor = vp.CompletedMissions == vp.TotalMissions ? "green" : "dodgerblue1";
                    string bar = $"[{statusColor}]{vp.CompletedMissions}/{vp.TotalMissions}[/]";

                    string statusInfo = "";
                    if (vp.FailedMissions > 0)
                        statusInfo += $"[red]{vp.FailedMissions} failed[/] ";
                    if (vp.InProgressMissions > 0)
                        statusInfo += $"[yellow]{vp.InProgressMissions} active[/]";
                    if (string.IsNullOrEmpty(statusInfo))
                        statusInfo = "[dim]-[/]";

                    string title = vp.Voyage != null ? Markup.Escape(vp.Voyage.Title) : "-";
                    voyageTable.AddRow(title, bar, statusInfo);
                }

                AnsiConsole.Write(voyageTable);
                AnsiConsole.WriteLine();
            }

            // Recent signals
            if (status.RecentSignals.Count > 0)
            {
                Table signalTable = TableRenderer.CreateTable("Recent Signals", null);
                signalTable.AddColumn("Time");
                signalTable.AddColumn("Type");
                signalTable.AddColumn("From");
                signalTable.AddColumn("To");

                foreach (Signal signal in status.RecentSignals)
                {
                    string typeColor = TableRenderer.SignalTypeColor(signal.Type);

                    signalTable.AddRow(
                        $"[dim]{signal.CreatedUtc.ToString("HH:mm:ss")}[/]",
                        $"[{typeColor}]{signal.Type}[/]",
                        Markup.Escape(signal.FromCaptainId ?? "Admiral"),
                        Markup.Escape(signal.ToCaptainId ?? "Admiral"));
                }

                AnsiConsole.Write(signalTable);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Snapshot: {status.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC[/]");

            // Suggest next steps if nothing is happening
            if (status.TotalCaptains == 0 && status.MissionsByStatus.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]Get started: [green]armada go \"your task\"[/] to dispatch work.[/]");
            }

            return 0;
        }
    }
}
