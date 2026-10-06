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
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// System health check with actionable diagnostics.
    /// </summary>
    [Description("Check system health and report issues with fixes")]
    public class DoctorCommand : BaseCommand<DoctorSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings, CancellationToken cancellationToken)
        {
            AnsiConsole.MarkupLine("[bold dodgerblue1]Armada Doctor[/]");
            AnsiConsole.MarkupLine("[dim]Checking system health...[/]");
            AnsiConsole.WriteLine();

            int issues = 0;
            AdmiralTarget target = GetTarget();

            if (target.IsLocal)
            {
                // 1. Settings file
                issues += CheckSettings();

                // 2. Git
                issues += CheckGit();

                // 3. Agent runtimes
                issues += CheckRuntimes();

                // 4. Database
                issues += await CheckDatabaseAsync().ConfigureAwait(false);

                // 5. Admiral server
                issues += await CheckAdmiralAsync().ConfigureAwait(false);
            }
            else
            {
                // A remote Admiral: this machine's settings, database, and agent runtimes are not the ones in use.
                AnsiConsole.MarkupLine("[dodgerblue1]INFO[/]  Target: " + Markup.Escape(target.Describe()) + "; credential: " + Markup.Escape(target.DescribeCredential()));
                AnsiConsole.MarkupLine("[dim]SKIP  Local settings, database, and agent runtimes (they belong to the remote Admiral's host or its Harbors)[/]");
                issues += CheckGit();
                int remoteIssues = await CheckRemoteAdmiralAsync(target).ConfigureAwait(false);
                issues += remoteIssues;
                if (remoteIssues > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[gold1]{issues} issue(s) found.[/] See suggestions above.");
                    return 1;
                }
            }

            // 6. Stalled captains
            issues += await CheckStalledCaptainsAsync().ConfigureAwait(false);

            // 7. Failed missions
            issues += await CheckFailedMissionsAsync().ConfigureAwait(false);

            // Summary
            AnsiConsole.WriteLine();
            if (issues == 0)
            {
                AnsiConsole.MarkupLine("[green]All checks passed.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[gold1]{issues} issue(s) found.[/] See suggestions above.");
            }

            return issues > 0 ? 1 : 0;
        }

        private int CheckSettings()
        {
            if (File.Exists(ArmadaSettings.DefaultSettingsPath))
            {
                AnsiConsole.MarkupLine("[green]PASS[/]  Settings file exists");
                try
                {
                    ArmadaSettings loaded = ArmadaSettings.LoadAsync().GetAwaiter().GetResult();
                    AnsiConsole.MarkupLine("[green]PASS[/]  Settings file is valid JSON");
                    return 0;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]FAIL[/]  Settings file is invalid: {Markup.Escape(ex.Message)}");
                    AnsiConsole.MarkupLine($"        Fix: Delete {Markup.Escape(ArmadaSettings.DefaultSettingsPath)} and run any command to regenerate.");
                    return 1;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[gold1]WARN[/]  No settings file (will be auto-created on first use)");
                return 0;
            }
        }

        private int CheckGit()
        {
            if (RuntimeDetectionService.IsCommandAvailable("git"))
            {
                AnsiConsole.MarkupLine("[green]PASS[/]  git is available");
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]FAIL[/]  git is not available on PATH");
                AnsiConsole.MarkupLine("        Fix: Install git from https://git-scm.com/");
                return 1;
            }
        }

        private int CheckRuntimes()
        {
            List<AgentRuntimeEnum> runtimes = RuntimeDetectionService.DetectAllRuntimes();
            if (runtimes.Count > 0)
            {
                foreach (AgentRuntimeEnum rt in runtimes)
                {
                    AnsiConsole.MarkupLine($"[green]PASS[/]  {rt} runtime available");
                }
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]FAIL[/]  No agent runtimes found on PATH");
                AnsiConsole.MarkupLine($"        Fix: {RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.ClaudeCode)}");
                return 1;
            }
        }

        private Task<int> CheckDatabaseAsync()
        {
            string dbPath = GetSettings().DatabasePath;
            if (File.Exists(dbPath))
            {
                AnsiConsole.MarkupLine("[green]PASS[/]  Database file exists");
                return Task.FromResult(0);
            }
            else
            {
                AnsiConsole.MarkupLine("[gold1]WARN[/]  Database not yet created (will be initialized on first use)");
                return Task.FromResult(0);
            }
        }

        private async Task<int> CheckAdmiralAsync()
        {
            try
            {
                bool healthy = await GetApiClient().HealthCheckAsync().ConfigureAwait(false);
                if (healthy)
                {
                    AnsiConsole.MarkupLine("[green]PASS[/]  Admiral server is running");
                    return 0;
                }
            }
            catch { }

            AnsiConsole.MarkupLine("[gold1]WARN[/]  Admiral server is not running (will auto-start on first command)");
            return 0;
        }

        private async Task<int> CheckRemoteAdmiralAsync(AdmiralTarget target)
        {
            bool healthy = await GetApiClient().HealthCheckAsync().ConfigureAwait(false);
            if (!healthy)
            {
                AnsiConsole.MarkupLine("[red]FAIL[/]  Admiral at " + Markup.Escape(target.BaseUrl) + " is not reachable");
                AnsiConsole.MarkupLine("        Fix: check the URL, rest.hostname on the server, the firewall, and the proxy. See docs/REMOTE_SERVER.md.");
                return 1;
            }

            AnsiConsole.MarkupLine("[green]PASS[/]  Admiral at " + Markup.Escape(target.BaseUrl) + " is reachable");
            if (target.SendsCredentialInsecurely)
                AnsiConsole.MarkupLine("[gold1]WARN[/]  The credential travels over plain HTTP; use an https:// URL through a TLS-terminating proxy");

            try
            {
                await GetAsync<ArmadaStatus>("/api/v1/status").ConfigureAwait(false);
                AnsiConsole.MarkupLine("[green]PASS[/]  Credential accepted (" + Markup.Escape(target.DescribeCredential()) + ")");
                return 0;
            }
            catch (AdmiralTargetException ex)
            {
                AnsiConsole.MarkupLine("[red]FAIL[/]  " + Markup.Escape(ex.Message));
                return 1;
            }
        }

        private async Task<int> CheckStalledCaptainsAsync()
        {
            try
            {
                EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
                List<Captain>? captains = captainResult?.Objects;
                if (captains == null) return 0;

                List<Captain> stalled = captains.FindAll(c => c.State == CaptainStateEnum.Stalled);
                if (stalled.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[red]FAIL[/]  {stalled.Count} stalled captain(s)");
                    foreach (Captain c in stalled)
                    {
                        AnsiConsole.MarkupLine($"        - {Markup.Escape(c.Name)} ({Markup.Escape(c.Id)})");
                    }
                    AnsiConsole.MarkupLine("        Fix: [green]armada captain stop <name>[/] to recall, or check agent logs with [green]armada log <name>[/].");
                    return 1;
                }
                else if (captains.Count > 0)
                {
                    AnsiConsole.MarkupLine("[green]PASS[/]  No stalled captains");
                }
            }
            catch { }

            return 0;
        }

        private async Task<int> CheckFailedMissionsAsync()
        {
            try
            {
                EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions?status=Failed").ConfigureAwait(false);
                List<Mission>? missions = missionResult?.Objects;
                if (missions != null && missions.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[gold1]WARN[/]  {missions.Count} failed mission(s)");
                    foreach (Mission m in missions)
                    {
                        AnsiConsole.MarkupLine($"        - {Markup.Escape(m.Title)} ({Markup.Escape(m.Id)})");
                    }
                    AnsiConsole.MarkupLine("        Fix: [green]armada mission retry <id>[/] to retry.");
                    return 0; // Warning, not failure
                }
            }
            catch { }

            return 0;
        }
    }
}
