namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Ask Armada a read-only question about fleet state in plain language.
    /// </summary>
    [Description("Ask Armada about fleet state in plain language")]
    public class AskCommand : BaseCommand<AskSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, AskSettings settings, CancellationToken cancellationToken)
        {
            AskResponse? response = await PostAsync<AskResponse>("/api/v1/ask", new { message = settings.Message }).ConfigureAwait(false);

            if (IsJsonMode(settings))
            {
                WriteJson(response);
                return response == null ? 1 : 0;
            }

            if (response == null)
            {
                AnsiConsole.MarkupLine("[red]Unable to reach the assistant.[/] Is the Admiral running?");
                return 1;
            }

            AnsiConsole.MarkupLine(Markup.Escape(response.Reply));

            if (response.Links != null && response.Links.Count > 0)
            {
                AnsiConsole.WriteLine();
                foreach (AskLink link in response.Links)
                    AnsiConsole.MarkupLine($"[dim]->[/] [dodgerblue1]{Markup.Escape(link.Label)}[/] [dim]{Markup.Escape(link.Href)}[/]");
            }

            return 0;
        }
    }
}
