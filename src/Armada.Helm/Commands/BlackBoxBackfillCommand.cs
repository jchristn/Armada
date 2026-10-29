namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox backfill [--since 30d] [--max]</c> (Admin): records entries for failed missions and voyages
    /// that have none.
    /// </summary>
    [Description("Backfill black box entries for past failures")]
    public class BlackBoxBackfillCommand : BlackBoxCommandBase<BlackBoxBackfillSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxBackfillSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxBackfillRequest request = new BlackBoxBackfillRequest();
                if (!String.IsNullOrWhiteSpace(settings.Since)) request.Since = BlackBoxCli.ParseSince(settings.Since, DateTime.UtcNow);
                if (settings.Max.HasValue) request.MaxSubjects = settings.Max.Value;
                BlackBoxBackfillResult? r = await client.BackfillBlackBoxAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(r);
                else if (r != null) AnsiConsole.MarkupLine("[green]Backfill[/] scanned " + r.Scanned + ", captured " + r.Captured + ", skipped " + r.Skipped + ", failed " + r.Failed + ", requeued " + r.EnrichmentRequeued + ".");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
