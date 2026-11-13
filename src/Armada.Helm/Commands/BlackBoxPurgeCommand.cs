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
    /// <c>armada blackbox purge [filters] [--execute] [--yes] [--include-pinned] [--max]</c> (Admin): a dry run by
    /// default that reports how many entries match. With <c>--execute</c> it dry runs, asks to confirm (or takes
    /// <c>--yes</c>; refused when not interactive without it), then deletes. Purging with no filter at all is refused.
    /// </summary>
    [Description("Purge black box entries by filter (dry run unless --execute)")]
    public class BlackBoxPurgeCommand : BlackBoxCommandBase<BlackBoxPurgeSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxPurgeSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxPurgeRequest request = BlackBoxCli.ApplyFilters(new BlackBoxPurgeRequest(), settings, DateTime.UtcNow);
                if (!HasFilter(settings))
                {
                    Status("Refused: purge needs at least one filter (for example --vessel, --before, --resolution).");
                    return BlackBoxCli.RefusedExitCode;
                }

                request.IncludePinned = settings.IncludePinned;
                if (settings.Max.HasValue) request.MaxEntries = settings.Max.Value;
                request.DryRun = true;
                BlackBoxPurgeResult? preview = await client.PurgeBlackBoxAsync(request, cancellationToken).ConfigureAwait(false);
                if (preview == null)
                {
                    Status("Error: the server returned no result.");
                    return 1;
                }

                if (!settings.Execute)
                {
                    if (settings.Json) WritePureJson(preview);
                    else
                    {
                        AnsiConsole.MarkupLine("[bold]Dry run:[/] " + preview.Matched + " entries match" + (preview.SkippedPinned > 0 ? ", " + preview.SkippedPinned + " pinned kept" : "") + ". Nothing was deleted.");
                        AnsiConsole.MarkupLine("[dim]Delete them with --execute.[/]");
                    }

                    return 0;
                }

                if (!Confirmed("Delete " + preview.Matched + " black box entries?", settings.Yes))
                {
                    Status("Refused: not confirmed. Pass --yes to purge without the question (required when not interactive).");
                    return BlackBoxCli.RefusedExitCode;
                }

                request.DryRun = false;
                BlackBoxPurgeResult? result = await client.PurgeBlackBoxAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(result);
                else if (result != null) AnsiConsole.MarkupLine("[green]Purged[/] " + result.Deleted + " entries" + (result.Pending > 0 ? " [gold1](" + result.Pending + " pending)[/]" : "") + ", freed " + Markup.Escape(BlackBoxCli.Bytes(result.BytesFreed)) + ".");
                return 0;
            }).ConfigureAwait(false);
        }

        private static bool HasFilter(BlackBoxFilterSettings s)
        {
            return !String.IsNullOrWhiteSpace(s.Vessel) || !String.IsNullOrWhiteSpace(s.Voyage) || !String.IsNullOrWhiteSpace(s.Mission) || !String.IsNullOrWhiteSpace(s.Captain)
                || !String.IsNullOrWhiteSpace(s.Kind) || !String.IsNullOrWhiteSpace(s.Triage) || !String.IsNullOrWhiteSpace(s.Phase) || !String.IsNullOrWhiteSpace(s.Step)
                || !String.IsNullOrWhiteSpace(s.Resolution) || !String.IsNullOrWhiteSpace(s.Fingerprint) || s.Pinned || !String.IsNullOrWhiteSpace(s.Since) || !String.IsNullOrWhiteSpace(s.Before);
        }
    }
}
