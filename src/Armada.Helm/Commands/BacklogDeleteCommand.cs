namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Deletes a backlog item.
    /// </summary>
    [Description("Delete a backlog item")]
    public sealed class BacklogDeleteCommand : BaseCommand<BacklogDeleteCommand.Settings>
    {
        /// <summary>
        /// Settings for deleting a backlog item.
        /// </summary>
        public sealed class Settings : BaseSettings
        {
            /// <summary>
            /// Gets or sets the backlog item identifier or title.
            /// </summary>
            [CommandArgument(0, "<backlog>")]
            public string Backlog { get; set; } = String.Empty;
        }

        /// <summary>
        /// Executes the backlog delete command.
        /// </summary>
        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            Objective? objective = await BacklogCommandSupport.ResolveBacklogItemAsync(GetApiClient(), settings.Backlog, cancellationToken).ConfigureAwait(false);
            if (objective == null)
            {
                AnsiConsole.MarkupLine("[red]Backlog item not found:[/] " + Markup.Escape(settings.Backlog));
                return 1;
            }

            await GetApiClient().DeleteBacklogItemAsync(objective.Id, cancellationToken).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(new { deleted = objective.Id });
                return 0;
            }

            AnsiConsole.MarkupLine("[gold1]Backlog item deleted:[/] [bold]" + Markup.Escape(objective.Title) + "[/] [dim](" + Markup.Escape(objective.Id) + ")[/]");
            return 0;
        }
    }
}
