namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Tail a captain or mission session log.
    /// </summary>
    [Description("Tail a captain or mission session log")]
    public class LogCommand : BaseCommand<LogSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, LogSettings settings, CancellationToken cancellationToken)
        {
            string? logFile = await ResolveLogFileAsync(settings.Id).ConfigureAwait(false);

            if (logFile == null || !File.Exists(logFile))
            {
                AnsiConsole.MarkupLine($"[gold1]No log found for[/] [bold]{Markup.Escape(settings.Id)}[/]");
                AnsiConsole.MarkupLine("[dim]Accepts: captain name/ID, mission ID, or mission title substring.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[dodgerblue1]Log file:[/] [dim]{Markup.Escape(logFile)}[/]");
            AnsiConsole.MarkupLine("");

            int linesToShow = settings.Lines ?? 50;

            if (settings.Follow)
            {
                await FollowLogAsync(logFile, linesToShow).ConfigureAwait(false);
            }
            else
            {
                await ShowTailAsync(logFile, linesToShow).ConfigureAwait(false);
            }

            return 0;
        }

        private async Task<string?> ResolveLogFileAsync(string identifier)
        {
            string missionLogDir = Path.Combine(Constants.DefaultDataDirectory, "logs", "missions");
            string captainLogDir = Path.Combine(Constants.DefaultDataDirectory, "logs", "captains");

            // 1. Direct mission ID (msn_ prefix)
            if (identifier.StartsWith("msn_"))
            {
                string missionLog = Path.Combine(missionLogDir, identifier + ".log");
                if (File.Exists(missionLog)) return missionLog;
            }

            // 2. Direct captain ID (cpt_ prefix) — check .current pointer first, then .log
            if (identifier.StartsWith("cpt_"))
            {
                return ResolveCaptainLog(captainLogDir, identifier);
            }

            // 3. Captain name match — look up via API
            try
            {
                EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
                List<Captain>? captains = captainResult?.Objects;
                if (captains != null)
                {
                    Captain? captain = captains.Find(c =>
                        c.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Contains(identifier, StringComparison.OrdinalIgnoreCase));

                    if (captain != null)
                    {
                        string? captainLog = ResolveCaptainLog(captainLogDir, captain.Id);
                        if (captainLog != null) return captainLog;
                    }
                }
            }
            catch { }

            // 4. Mission title match — look up via API, get mission ID
            try
            {
                EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
                List<Mission>? missions = missionResult?.Objects;
                if (missions != null)
                {
                    Mission? mission = EntityResolver.ResolveMission(missions, identifier);
                    if (mission != null)
                    {
                        string missionLog = Path.Combine(missionLogDir, mission.Id + ".log");
                        if (File.Exists(missionLog)) return missionLog;
                    }
                }
            }
            catch { }

            // 5. Fallback: glob for partial match in both directories
            if (Directory.Exists(captainLogDir))
            {
                string[] matches = Directory.GetFiles(captainLogDir, "*" + identifier + "*");
                if (matches.Length > 0)
                    return matches.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
            }

            if (Directory.Exists(missionLogDir))
            {
                string[] matches = Directory.GetFiles(missionLogDir, "*" + identifier + "*");
                if (matches.Length > 0)
                    return matches.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
            }

            return null;
        }

        private string? ResolveCaptainLog(string captainLogDir, string captainId)
        {
            // Check .current pointer (points to the current mission's log)
            string pointerFile = Path.Combine(captainLogDir, captainId + ".current");
            if (File.Exists(pointerFile))
            {
                string target = File.ReadAllText(pointerFile).Trim();
                if (File.Exists(target)) return target;
            }

            // Fallback to legacy captain log
            string directLog = Path.Combine(captainLogDir, captainId + ".log");
            if (File.Exists(directLog)) return directLog;

            return null;
        }

        private async Task ShowTailAsync(string logFile, int lineCount)
        {
            List<string> lineList = new List<string>();
            using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                string? line;
                while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                    lineList.Add(line);
            }
            string[] allLines = lineList.ToArray();
            int skip = Math.Max(0, allLines.Length - lineCount);

            for (int i = skip; i < allLines.Length; i++)
            {
                AnsiConsole.WriteLine(allLines[i]);
            }
        }

        private async Task FollowLogAsync(string logFile, int initialLines)
        {
            await ShowTailAsync(logFile, initialLines).ConfigureAwait(false);

            AnsiConsole.MarkupLine("[dim]--- following (Ctrl+C to stop) ---[/]");

            long lastPosition = new FileInfo(logFile).Length;

            using CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    FileInfo fi = new FileInfo(logFile);
                    if (fi.Length > lastPosition)
                    {
                        using FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fs.Seek(lastPosition, SeekOrigin.Begin);
                        using StreamReader reader = new StreamReader(fs);
                        string? line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            AnsiConsole.WriteLine(line);
                        }
                        lastPosition = fi.Length;
                    }

                    await Task.Delay(500, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
