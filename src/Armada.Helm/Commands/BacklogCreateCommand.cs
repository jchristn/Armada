namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Creates a backlog item.
    /// </summary>
    [Description("Create a backlog item")]
    public sealed class BacklogCreateCommand : BaseCommand<BacklogCreateCommand.Settings>
    {
        /// <summary>
        /// Settings for creating a backlog item.
        /// </summary>
        public sealed class Settings : BacklogMutationSettingsBase
        {
        }

        /// <summary>
        /// Executes the backlog create command.
        /// </summary>
        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            if (String.IsNullOrWhiteSpace(settings.Title))
            {
                AnsiConsole.MarkupLine("[red]A title is required.[/] Pass [green]--title[/] to create a backlog item.");
                return 1;
            }

            await EnsureServerAsync().ConfigureAwait(false);
            ObjectiveUpsertRequest request = BacklogCommandSupport.BuildUpsertRequest(settings);
            Objective? objective = await GetApiClient().CreateBacklogItemAsync(request, cancellationToken).ConfigureAwait(false);
            if (objective == null)
            {
                AnsiConsole.MarkupLine("[red]Backlog item creation failed.[/]");
                return 1;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(objective);
                return 0;
            }

            AnsiConsole.MarkupLine("[green]Backlog item created.[/]");
            BacklogCommandSupport.RenderBacklogDetails(objective);
            return 0;
        }
    }
}
