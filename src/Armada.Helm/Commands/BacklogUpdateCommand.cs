namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Updates a backlog item.
    /// </summary>
    [Description("Update a backlog item")]
    public sealed class BacklogUpdateCommand : BaseCommand<BacklogUpdateCommand.Settings>
    {
        /// <summary>
        /// Settings for updating a backlog item.
        /// </summary>
        public sealed class Settings : BacklogMutationSettingsBase
        {
            /// <summary>
            /// Gets or sets the backlog item identifier or title.
            /// </summary>
            [CommandArgument(0, "<backlog>")]
            public string Backlog { get; set; } = String.Empty;
        }

        /// <summary>
        /// Executes the backlog update command.
        /// </summary>
        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            Objective? existing = await BacklogCommandSupport.ResolveBacklogItemAsync(GetApiClient(), settings.Backlog, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                AnsiConsole.MarkupLine("[red]Backlog item not found:[/] " + Markup.Escape(settings.Backlog));
                return 1;
            }

            ObjectiveUpsertRequest request = BacklogCommandSupport.BuildUpsertRequest(settings);
            Objective? updated = await GetApiClient().UpdateBacklogItemAsync(existing.Id, request, cancellationToken).ConfigureAwait(false);
            if (updated == null)
            {
                AnsiConsole.MarkupLine("[red]Backlog item update failed.[/]");
                return 1;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(updated);
                return 0;
            }

            AnsiConsole.MarkupLine("[green]Backlog item updated.[/]");
            BacklogCommandSupport.RenderBacklogDetails(updated);
            return 0;
        }
    }
}
