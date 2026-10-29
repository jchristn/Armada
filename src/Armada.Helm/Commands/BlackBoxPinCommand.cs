namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox pin &lt;entryId...&gt;</c>.
    /// </summary>
    [Description("Pin black box entries")]
    public class BlackBoxPinCommand : BlackBoxCommandBase<BlackBoxIdsSettings>
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
                foreach (string id in settings.Ids)
                {
                    await client.PinBlackBoxAsync(id, cancellationToken).ConfigureAwait(false);
                    if (!settings.Json) AnsiConsole.MarkupLine("[green]Pinned[/] " + Markup.Escape(id));
                }

                if (settings.Json) WritePureJson(settings.Ids);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
