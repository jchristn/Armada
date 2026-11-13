namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Delete a playbook.
    /// </summary>
    [Description("Delete a playbook")]
    public class PlaybookRemoveCommand : BaseCommand<PlaybookRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, PlaybookRemoveSettings settings, CancellationToken cancellationToken)
        {
            Playbook? playbook = await ResolvePlaybookAsync(settings.Id).ConfigureAwait(false);
            if (playbook == null)
            {
                HelmConsole.Error.MarkupLine($"[red]Playbook not found:[/] {Markup.Escape(settings.Id)}");
                return 1;
            }

            await DeleteAsync("/api/v1/playbooks/" + playbook.Id).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Deleted playbook[/] [bold]{Markup.Escape(playbook.FileName)}[/]");
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
