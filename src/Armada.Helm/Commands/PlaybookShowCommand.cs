namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Show playbook details.
    /// </summary>
    [Description("Show playbook details")]
    public class PlaybookShowCommand : BaseCommand<PlaybookShowSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, PlaybookShowSettings settings, CancellationToken cancellationToken)
        {
            Playbook? playbook = await ResolvePlaybookAsync(settings.Id).ConfigureAwait(false);
            if (playbook == null)
            {
                HelmConsole.Error.MarkupLine($"[red]Playbook not found:[/] {Markup.Escape(settings.Id)}");
                return 1;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(playbook);
                return 0;
            }

            Panel panel = new Panel(
                new Rows(
                    new Markup($"[dodgerblue1]Id:[/]          [dim]{Markup.Escape(playbook.Id)}[/]"),
                    new Markup($"[dodgerblue1]File:[/]        [bold]{Markup.Escape(playbook.FileName)}[/]"),
                    new Markup($"[dodgerblue1]Active:[/]      {(playbook.Active ? "[green]true[/]" : "[red]false[/]")}"),
                    new Markup($"[dodgerblue1]Updated:[/]     {playbook.LastUpdateUtc:yyyy-MM-dd HH:mm}"),
                    new Markup($"[dodgerblue1]Description:[/] {Markup.Escape(playbook.Description ?? "-")}")));
            panel.Header = new PanelHeader("[bold dodgerblue1]Playbook[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.DodgerBlue1);
            AnsiConsole.Write(panel);

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(Markup.Escape(playbook.Content))
            {
                Header = new PanelHeader("[bold]Markdown[/]"),
                Border = BoxBorder.Rounded
            });
            return 0;
        }

        private async Task<Playbook?> ResolvePlaybookAsync(string idOrFileName)
        {
            if (idOrFileName.StartsWith("pbk_", StringComparison.OrdinalIgnoreCase))
            {
                return await GetAsync<Playbook>("/api/v1/playbooks/" + idOrFileName).ConfigureAwait(false);
            }

            List<Playbook> playbooks = await PlaybookPages.ReadAllAsync(path => GetAsync<EnumerationResult<Playbook>>(path)).ConfigureAwait(false);
            return playbooks.FirstOrDefault(p =>
                String.Equals(p.FileName, idOrFileName, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(p.Id, idOrFileName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
