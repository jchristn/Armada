namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;

    #region Settings

    /// <summary>
    /// Settings for diff command.
    /// </summary>
    public class DiffSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title substring.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "[mission]")]
        public string? Id { get; set; }
    }

    #endregion

    /// <summary>
    /// Response from the mission diff API endpoint.
    /// </summary>
    public class MissionDiffResponse
    {
        /// <summary>Mission identifier.</summary>
        public string MissionId { get; set; } = "";

        /// <summary>Branch name.</summary>
        public string Branch { get; set; } = "";

        /// <summary>Worktree path.</summary>
        public string WorktreePath { get; set; } = "";

        /// <summary>Unified diff output.</summary>
        public string Diff { get; set; } = "";
    }

    #region Commands

    /// <summary>
    /// Show the git diff of a mission's changes.
    /// </summary>
    [Description("Show diff of a mission's changes")]
    public class DiffCommand : BaseCommand<DiffSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, DiffSettings settings, CancellationToken cancellationToken)
        {
            string missionId = await ResolveMissionIdAsync(settings.Id).ConfigureAwait(false);

            if (string.IsNullOrEmpty(missionId))
            {
                AnsiConsole.MarkupLine("[red]No mission specified.[/] Usage: [green]armada diff <mission>[/]");
                return 1;
            }

            // Get the diff from the API
            MissionDiffResponse? result = null;
            try
            {
                result = await GetAsync<MissionDiffResponse>($"/api/v1/missions/{missionId}/diff").ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Handled below
            }

            if (result == null)
            {
                AnsiConsole.MarkupLine($"[red]Could not get diff for mission[/] [dim]{Markup.Escape(missionId)}[/]");
                AnsiConsole.MarkupLine("[dim]The worktree may have already been reclaimed after completion.[/]");
                AnsiConsole.MarkupLine("[dim]If using PRs, review the diff on GitHub instead.[/]");
                return 1;
            }

            string branch = result.Branch;
            string diff = result.Diff;

            if (string.IsNullOrWhiteSpace(diff))
            {
                AnsiConsole.MarkupLine($"[gold1]No changes found[/] for mission [dim]{Markup.Escape(missionId)}[/] on branch [dodgerblue1]{Markup.Escape(branch)}[/].");
                AnsiConsole.MarkupLine("[dim]The captain may not have committed yet, or changes were already merged.[/]");
                return 0;
            }

            // Header
            AnsiConsole.MarkupLine($"[dodgerblue1]Mission:[/] [dim]{Markup.Escape(missionId)}[/]  [dodgerblue1]Branch:[/] [bold]{Markup.Escape(branch)}[/]");
            AnsiConsole.WriteLine();

            // Render diff with syntax coloring
            foreach (string line in diff.Split('\n'))
            {
                if (line.StartsWith("+++") || line.StartsWith("---"))
                {
                    AnsiConsole.MarkupLine("[bold]" + Markup.Escape(line) + "[/]");
                }
                else if (line.StartsWith("@@"))
                {
                    AnsiConsole.MarkupLine("[cyan]" + Markup.Escape(line) + "[/]");
                }
                else if (line.StartsWith("diff "))
                {
                    AnsiConsole.MarkupLine("[bold yellow]" + Markup.Escape(line) + "[/]");
                }
                else if (line.StartsWith("+"))
                {
                    AnsiConsole.MarkupLine("[green]" + Markup.Escape(line) + "[/]");
                }
                else if (line.StartsWith("-"))
                {
                    AnsiConsole.MarkupLine("[red]" + Markup.Escape(line) + "[/]");
                }
                else
                {
                    AnsiConsole.WriteLine(line);
                }
            }

            return 0;
        }

        private async Task<string> ResolveMissionIdAsync(string? identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                // Find the most recent in-progress or review mission
                EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
                List<Mission>? missions = missionResult?.Objects;
                if (missions != null)
                {
                    Mission? active = missions
                        .Where(m => m.Status == Core.Enums.MissionStatusEnum.Review
                                 || m.Status == Core.Enums.MissionStatusEnum.InProgress
                                 || m.Status == Core.Enums.MissionStatusEnum.Testing)
                        .OrderByDescending(m => m.LastUpdateUtc)
                        .FirstOrDefault();

                    if (active != null) return active.Id;
                }
                return "";
            }

            if (identifier.StartsWith("msn_")) return identifier;

            EnumerationResult<Mission>? allResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
            List<Mission>? all = allResult?.Objects;
            if (all != null)
            {
                Mission? match = EntityResolver.ResolveMission(all, identifier);
                if (match != null) return match.Id;
            }
            return identifier;
        }
    }

    #endregion
}
