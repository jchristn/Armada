namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Cancel a voyage. Accepts ID or title.
    /// </summary>
    [Description("Cancel a voyage")]
    public class VoyageCancelCommand : BaseCommand<VoyageCancelSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageCancelSettings settings, CancellationToken cancellationToken)
        {
            string voyageId = await ResolveVoyageIdAsync(settings.Id).ConfigureAwait(false);

            await DeleteAsync($"/api/v1/voyages/{voyageId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Voyage cancelled:[/] [bold]{Markup.Escape(voyageId)}[/]");
            return 0;
        }

        private async Task<string> ResolveVoyageIdAsync(string identifier)
        {
            if (identifier.StartsWith("vyg_")) return identifier;

            EnumerationResult<Voyage>? voyageResult = await GetAsync<EnumerationResult<Voyage>>("/api/v1/voyages").ConfigureAwait(false);
            List<Voyage>? voyages = voyageResult?.Objects;
            if (voyages != null)
            {
                Voyage? match = EntityResolver.ResolveVoyage(voyages, identifier);
                if (match != null) return match.Id;
            }
            return identifier;
        }
    }
}
