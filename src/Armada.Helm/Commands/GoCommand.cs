namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Settings for the go (quick dispatch) command.
    /// </summary>
    public class GoSettings : BaseSettings
    {
        /// <summary>
        /// Mission prompt.
        /// </summary>
        [Description("What to do")]
        [CommandArgument(0, "<prompt>")]
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Target vessel name or ID.
        /// </summary>
        [Description("Target vessel (name, ID, or URL)")]
        [CommandOption("--vessel|-v")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Target repository path or URL.
        /// Infers from current directory if not specified.
        /// </summary>
        [Description("Repository path or URL (default: current directory)")]
        [CommandOption("--repo|-r")]
        public string? Repo { get; set; }

        /// <summary>
        /// Path to write the captain's output log.
        /// </summary>
        [Description("Write captain output to a log file")]
        [CommandOption("--log|-l")]
        public string? LogFile { get; set; }

        /// <summary>
        /// Override AutoPush: push changes to remote.
        /// </summary>
        [Description("Push changes to remote (overrides config)")]
        [CommandOption("--push")]
        public bool? Push { get; set; }

        /// <summary>
        /// Override AutoPush: do not push.
        /// </summary>
        [Description("Do not push changes to remote")]
        [CommandOption("--no-push")]
        public bool NoPush { get; set; } = false;

        /// <summary>
        /// Override AutoCreatePullRequests: create a PR.
        /// </summary>
        [Description("Create a pull request (overrides config)")]
        [CommandOption("--pr")]
        public bool? Pr { get; set; }

        /// <summary>
        /// Override AutoCreatePullRequests: do not create PR.
        /// </summary>
        [Description("Do not create a pull request")]
        [CommandOption("--no-pr")]
        public bool NoPr { get; set; } = false;

        /// <summary>
        /// Override AutoMergePullRequests: auto-merge the PR.
        /// </summary>
        [Description("Auto-merge the pull request (overrides config)")]
        [CommandOption("--merge")]
        public bool? Merge { get; set; }

        /// <summary>
        /// Override AutoMergePullRequests: do not auto-merge.
        /// </summary>
        [Description("Do not auto-merge the pull request")]
        [CommandOption("--no-merge")]
        public bool NoMerge { get; set; } = false;
    }

    /// <summary>
    /// Quick dispatch: create a voyage with one or more missions.
    /// Zero-setup: auto-initializes config, detects runtime, infers repo from CWD,
    /// creates default fleet, auto-provisions captain, and dispatches.
    /// </summary>
    [Description("Quick dispatch -- natural language task assignment")]
    public class GoCommand : BaseCommand<GoSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, GoSettings settings, CancellationToken cancellationToken)
        {
            // Step 1: Resolve vessel
            string? vesselId = await ResolveVesselIdAsync(settings).ConfigureAwait(false);
            if (vesselId == null) return 1;

            // Step 2: Ensure at least one captain exists
            List<Captain> captains = await EnsureCaptainsAsync().ConfigureAwait(false);
            if (captains.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No captains available and auto-creation failed.[/]");
                return 1;
            }

            // Step 3: Split prompt into tasks
            List<string> tasks = DetectMultipleTasks(settings.Prompt);

            // Step 4: Auto-scale captains for multi-task voyages
            if (tasks.Count > 1)
            {
                await AutoScaleCaptainsAsync(captains.Count, tasks.Count).ConfigureAwait(false);
            }

            // Step 5: Build and dispatch voyage
            List<object> missions = new List<object>();
            foreach (string task in tasks)
            {
                missions.Add(new
                {
                    Title = task,
                    Description = task,
                    VesselId = vesselId
                });
            }

            // Resolve per-voyage overrides (--no-x takes precedence over --x)
            bool? autoPush = settings.NoPush ? false : settings.Push;
            bool? autoPr = settings.NoPr ? false : settings.Pr;
            bool? autoMerge = settings.NoMerge ? false : settings.Merge;

            object body = new
            {
                Title = tasks.Count > 1 ? settings.Prompt : tasks[0],
                VesselId = vesselId,
                Missions = missions,
                AutoPush = autoPush,
                AutoCreatePullRequests = autoPr,
                AutoMergePullRequests = autoMerge
            };

            Voyage? voyage = await PostAsync<Voyage>("/api/v1/voyages", body).ConfigureAwait(false);

            if (voyage == null) return 1;

            AnsiConsole.MarkupLine($"[green]Dispatched![/]");
            AnsiConsole.MarkupLine($"  Voyage:   [bold]{Markup.Escape(voyage.Id)}[/]");
            AnsiConsole.MarkupLine($"  Missions: [bold]{missions.Count}[/]");
            if (tasks.Count > 1)
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    AnsiConsole.MarkupLine($"    [dim]{i + 1}.[/] {Markup.Escape(tasks[i])}");
                }
            }

            if (!string.IsNullOrEmpty(settings.LogFile))
            {
                AnsiConsole.MarkupLine($"  Waiting for completion (logging to [bold]{Markup.Escape(settings.LogFile)}[/])...");
                return await WaitAndLogAsync(voyage.Id, settings.LogFile, cancellationToken).ConfigureAwait(false);
            }

            AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            return 0;
        }

        #region Private-Methods

        private async Task<string?> ResolveVesselIdAsync(GoSettings settings)
        {
            // Explicit --vessel flag: resolve by name/ID
            if (!string.IsNullOrEmpty(settings.Vessel))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;

                if (vessels == null || vessels.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]No vessels registered.[/]");
                    AnsiConsole.MarkupLine("Register one with [green]armada vessel add <name> <repoUrl>[/] or use [green]--repo[/].");
                    return null;
                }

                Vessel? match = EntityResolver.ResolveVessel(vessels, settings.Vessel);
                if (match != null) return match.Id;

                AnsiConsole.MarkupLine($"[red]Vessel not found:[/] {Markup.Escape(settings.Vessel)}");
                AnsiConsole.MarkupLine("[dim]Available vessels:[/]");
                foreach (Vessel v in vessels)
                {
                    AnsiConsole.MarkupLine($"  [dim]-[/] [dodgerblue1]{Markup.Escape(v.Name)}[/] [dim]({Markup.Escape(v.Id)})[/]");
                }
                return null;
            }

            // Explicit --repo flag or default to CWD
            string? repoSource = settings.Repo;
            if (string.IsNullOrEmpty(repoSource))
            {
                // Default: infer from current directory
                repoSource = null; // ResolveOrRegisterVesselAsync defaults to CWD
            }

            return await ResolveOrRegisterVesselAsync(repoSource).ConfigureAwait(false);
        }

        private async Task AutoScaleCaptainsAsync(int currentCaptainCount, int taskCount)
        {
            Core.Settings.ArmadaSettings armadaSettings = GetSettings();
            int maxCaptains = armadaSettings.MaxCaptains > 0 ? armadaSettings.MaxCaptains : Core.Constants.DefaultMaxCaptains;
            int needed = Math.Min(taskCount, maxCaptains) - currentCaptainCount;

            for (int i = 0; i < needed; i++)
            {
                int captainNumber = currentCaptainCount + i + 1;
                string runtimeValue = armadaSettings.DefaultRuntime ?? "ClaudeCode";

                try
                {
                    Captain? captain = await PostAsync<Captain>("/api/v1/captains", new
                    {
                        Name = $"captain-{captainNumber}",
                        Runtime = runtimeValue
                    }).ConfigureAwait(false);

                    if (captain != null)
                    {
                        AnsiConsole.MarkupLine($"[dim]Auto-created captain-{captainNumber} for parallel work.[/]");
                    }
                }
                catch
                {
                    break; // Stop trying if creation fails
                }
            }
        }

        private async Task<int> WaitAndLogAsync(string voyageId, string logFile, CancellationToken token)
        {
            using StreamWriter writer = new StreamWriter(logFile, append: false) { AutoFlush = true };

            // Poll for mission assignment so we can find the captain log
            string? captainLogPath = null;
            long lastLogPosition = 0;

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token).ConfigureAwait(false);

                // Check voyage status
                VoyageDetailResponse? detail = await GetAsync<VoyageDetailResponse>($"/api/v1/voyages/{voyageId}").ConfigureAwait(false);
                if (detail?.Voyage == null) continue;

                // Find mission log path from the first mission
                if (captainLogPath == null && detail.Missions != null)
                {
                    foreach (Mission m in detail.Missions)
                    {
                        // Try per-mission log first
                        string missionLogPath = Path.Combine(Constants.DefaultDataDirectory, "logs", "missions", m.Id + ".log");
                        if (File.Exists(missionLogPath))
                        {
                            captainLogPath = missionLogPath;
                            break;
                        }

                        // Fallback to captain log pointer
                        if (!string.IsNullOrEmpty(m.CaptainId))
                        {
                            string pointerPath = Path.Combine(Constants.DefaultDataDirectory, "logs", "captains", m.CaptainId + ".current");
                            if (File.Exists(pointerPath))
                            {
                                string target = File.ReadAllText(pointerPath).Trim();
                                if (File.Exists(target))
                                {
                                    captainLogPath = target;
                                    break;
                                }
                            }

                            string candidatePath = Path.Combine(Constants.DefaultDataDirectory, "logs", "captains", m.CaptainId + ".log");
                            if (File.Exists(candidatePath))
                            {
                                captainLogPath = candidatePath;
                                break;
                            }
                        }
                    }
                }

                // Stream new log content to file and console
                if (captainLogPath != null && File.Exists(captainLogPath))
                {
                    FileInfo fi = new FileInfo(captainLogPath);
                    if (fi.Length > lastLogPosition)
                    {
                        using FileStream fs = new FileStream(captainLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fs.Seek(lastLogPosition, SeekOrigin.Begin);
                        using StreamReader reader = new StreamReader(fs);
                        string? line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            writer.WriteLine(line);
                            AnsiConsole.WriteLine(line);
                        }
                        lastLogPosition = fi.Length;
                    }
                }

                // Check if voyage is done
                if (detail.Voyage.Status == VoyageStatusEnum.Complete ||
                    detail.Voyage.Status == VoyageStatusEnum.Cancelled)
                {
                    // Flush any remaining log content
                    if (captainLogPath != null && File.Exists(captainLogPath))
                    {
                        FileInfo fi = new FileInfo(captainLogPath);
                        if (fi.Length > lastLogPosition)
                        {
                            using FileStream fs = new FileStream(captainLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            fs.Seek(lastLogPosition, SeekOrigin.Begin);
                            using StreamReader reader = new StreamReader(fs);
                            string? line;
                            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                            {
                                writer.WriteLine(line);
                                AnsiConsole.WriteLine(line);
                            }
                        }
                    }

                    string statusColor = detail.Voyage.Status == VoyageStatusEnum.Complete ? "green" : "red";
                    AnsiConsole.MarkupLine($"\n[{statusColor}]Voyage {detail.Voyage.Status}.[/]");
                    AnsiConsole.MarkupLine($"[dim]Log saved to: {Markup.Escape(logFile)}[/]");

                    return detail.Voyage.Status == VoyageStatusEnum.Complete ? 0 : 1;
                }
            }

            return 1;
        }

        private List<string> DetectMultipleTasks(string prompt)
        {
            List<string> tasks = new List<string>();

            // Check for numbered list: "1. ... 2. ... 3. ..."
            MatchCollection numberedMatches = Regex.Matches(prompt, @"(?:^|\s)(\d+)\.\s+(.+?)(?=(?:\s+\d+\.\s)|$)", RegexOptions.Singleline);
            if (numberedMatches.Count >= 2)
            {
                foreach (Match m in numberedMatches)
                {
                    string task = m.Groups[2].Value.Trim();
                    if (!String.IsNullOrEmpty(task)) tasks.Add(task);
                }
                if (tasks.Count >= 2) return tasks;
                tasks.Clear();
            }

            // Check for semicolon-separated tasks
            string[] semiParts = prompt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (semiParts.Length >= 2)
            {
                foreach (string part in semiParts)
                {
                    if (!String.IsNullOrEmpty(part)) tasks.Add(part);
                }
                return tasks;
            }

            // Single task
            tasks.Add(prompt);
            return tasks;
        }

        #endregion
    }
}
