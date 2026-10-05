namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

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
            // Line roles come from the structural parser (hunk line counts), so a content line that looks like a
            // header ("+++ ...", "--- ...", "diff ...") is colored as the added or removed line it is.
            string[] lines = diff.Split('\n');
            UnifiedDiffParser.Parse(diff, out List<UnifiedDiffLineKindEnum> kinds);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                UnifiedDiffLineKindEnum kind = i < kinds.Count ? kinds[i] : UnifiedDiffLineKindEnum.Other;
                switch (kind)
                {
                    case UnifiedDiffLineKindEnum.FileHeader:
                        AnsiConsole.MarkupLine("[bold yellow]" + Markup.Escape(line) + "[/]");
                        break;
                    case UnifiedDiffLineKindEnum.Meta:
                        AnsiConsole.MarkupLine("[bold]" + Markup.Escape(line) + "[/]");
                        break;
                    case UnifiedDiffLineKindEnum.HunkHeader:
                        AnsiConsole.MarkupLine("[cyan]" + Markup.Escape(line) + "[/]");
                        break;
                    case UnifiedDiffLineKindEnum.Added:
                        AnsiConsole.MarkupLine("[green]" + Markup.Escape(line) + "[/]");
                        break;
                    case UnifiedDiffLineKindEnum.Deleted:
                        AnsiConsole.MarkupLine("[red]" + Markup.Escape(line) + "[/]");
                        break;
                    default:
                        AnsiConsole.WriteLine(line);
                        break;
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
}
