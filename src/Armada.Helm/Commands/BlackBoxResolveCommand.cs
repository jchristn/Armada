namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox resolve &lt;entryId&gt; --resolution Resolved [--note] [--link]</c>.
    /// </summary>
    [Description("Resolve a black box entry")]
    public class BlackBoxResolveCommand : BlackBoxCommandBase<BlackBoxResolveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxResolveSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxResolutionEnum? resolution = BlackBoxCli.ParseEnum<BlackBoxResolutionEnum>("--resolution", settings.Resolution);
                if (resolution == null)
                {
                    Status("Error: --resolution is required (Resolved, Duplicate, WontFix, ArmadaFixed, Open).");
                    return 1;
                }

                BlackBoxResolveRequest request = new BlackBoxResolveRequest { Resolution = resolution.Value, Note = settings.Note, Link = settings.Link };
                BlackBoxEntry? entry = await client.ResolveBlackBoxAsync(settings.Id, request, cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(entry);
                else AnsiConsole.MarkupLine("[green]Resolved[/] " + Markup.Escape(settings.Id) + " as " + Markup.Escape(resolution.Value.ToString()) + ".");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
