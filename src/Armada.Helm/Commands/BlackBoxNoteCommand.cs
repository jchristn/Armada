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
    /// <c>armada blackbox note &lt;entryId&gt; "&lt;text&gt;"</c>: adds a note written by an Operator.
    /// </summary>
    [Description("Add a note to a black box entry")]
    public class BlackBoxNoteCommand : BlackBoxCommandBase<BlackBoxNoteSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxNoteSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrWhiteSpace(settings.Text))
            {
                Status("Error: the note text is empty.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxNoteAddRequest request = new BlackBoxNoteAddRequest { Body = settings.Text.Trim(), AuthorKind = BlackBoxNoteAuthorKindEnum.Operator };
                BlackBoxNote? note = await client.AddBlackBoxNoteAsync(settings.Id, request, cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(note);
                else AnsiConsole.MarkupLine("[green]Note added[/] to " + Markup.Escape(settings.Id) + ".");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
