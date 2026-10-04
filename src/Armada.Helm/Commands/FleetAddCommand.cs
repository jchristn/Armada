namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Add a new fleet.
    /// </summary>
    [Description("Add a new fleet")]
    public class FleetAddCommand : BaseCommand<FleetAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, FleetAddSettings settings, CancellationToken cancellationToken)
        {
            object body = new
            {
                Name = settings.Name,
                Description = settings.Description
            };

            Fleet? fleet = await PostAsync<Fleet>("/api/v1/fleets", body).ConfigureAwait(false);

            if (fleet != null)
            {
                AnsiConsole.MarkupLine($"[green]Fleet created![/] [bold]{Markup.Escape(fleet.Name)}[/] [dim]({Markup.Escape(fleet.Id)})[/]");
            }

            return 0;
        }
    }
}
