namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Reorders a backlog item by rank.
    /// </summary>
    [Description("Update the rank of a backlog item")]
    public sealed class BacklogReorderCommand : BaseCommand<BacklogReorderCommand.Settings>
    {
        /// <summary>
        /// Settings for reordering a backlog item.
        /// </summary>
        public sealed class Settings : BaseSettings
        {
            /// <summary>
            /// Gets or sets the backlog item identifier or title.
            /// </summary>
            [CommandArgument(0, "<backlog>")]
            public string Backlog { get; set; } = String.Empty;

            /// <summary>
            /// Gets or sets the target rank.
            /// </summary>
            [CommandOption("--rank|-r")]
            public int? Rank { get; set; }
        }

        /// <summary>
        /// Executes the backlog reorder command.
        /// </summary>
        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            if (!settings.Rank.HasValue)
            {
                AnsiConsole.MarkupLine("[red]A target rank is required.[/] Pass [green]--rank <number>[/].");
                return 1;
            }

            await EnsureServerAsync().ConfigureAwait(false);
            Objective? objective = await BacklogCommandSupport.ResolveBacklogItemAsync(GetApiClient(), settings.Backlog, cancellationToken).ConfigureAwait(false);
            if (objective == null)
            {
                AnsiConsole.MarkupLine("[red]Backlog item not found:[/] " + Markup.Escape(settings.Backlog));
                return 1;
            }

            List<Objective>? updated = await GetApiClient().ReorderBacklogAsync(new ObjectiveReorderRequest
            {
                Items = new List<ObjectiveReorderItem>
                {
                    new ObjectiveReorderItem
                    {
                        ObjectiveId = objective.Id,
                        Rank = settings.Rank.Value
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            Objective? refreshed = updated?.FirstOrDefault(item => String.Equals(item.Id, objective.Id, StringComparison.OrdinalIgnoreCase))
                ?? await GetApiClient().GetBacklogItemAsync(objective.Id, cancellationToken).ConfigureAwait(false);
            if (refreshed == null)
            {
                AnsiConsole.MarkupLine("[red]Backlog reorder failed.[/]");
                return 1;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(refreshed);
                return 0;
            }

            AnsiConsole.MarkupLine("[green]Backlog rank updated.[/]");
            BacklogCommandSupport.RenderBacklogDetails(refreshed);
            return 0;
        }
    }
}
