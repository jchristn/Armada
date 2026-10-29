namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox delete &lt;entryId...&gt;</c>: deletes entries and their objects (one id through
    /// <c>DELETE /api/v1/blackbox/{id}</c>, several through the bounded bulk route).
    /// </summary>
    [Description("Delete black box entries and their evidence")]
    public class BlackBoxDeleteCommand : BlackBoxCommandBase<BlackBoxIdsSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxIdsSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Ids.Length == 0)
            {
                Status("Error: name at least one entry id.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxDeleteResult? result = settings.Ids.Length == 1
                    ? await client.DeleteBlackBoxAsync(settings.Ids[0], cancellationToken).ConfigureAwait(false)
                    : await client.DeleteBlackBoxEntriesAsync(settings.Ids.Distinct(StringComparer.Ordinal).ToList(), cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(result);
                else if (result != null)
                    AnsiConsole.MarkupLine("[green]Deleted[/] " + result.Deleted + " of " + result.Requested + (result.Pending > 0 ? " [gold1](" + result.Pending + " pending)[/]" : "") + ", freed " + Markup.Escape(BlackBoxCli.Bytes(result.BytesFreed)) + ".");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
