namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Add a new playbook.
    /// </summary>
    [Description("Add a new playbook")]
    public class PlaybookAddCommand : BaseCommand<PlaybookAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, PlaybookAddSettings settings, CancellationToken cancellationToken)
        {
            string content = settings.Content ?? "# Playbook\n\nDescribe the rules the model should follow.\n";
            if (!string.IsNullOrEmpty(settings.FromFile))
            {
                if (!File.Exists(settings.FromFile))
                {
                    AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(settings.FromFile)}");
                    return 1;
                }
                content = await File.ReadAllTextAsync(settings.FromFile, cancellationToken).ConfigureAwait(false);
            }

            Playbook? playbook = await PostAsync<Playbook>("/api/v1/playbooks", new
            {
                FileName = settings.FileName,
                Description = settings.Description,
                Content = content,
                Active = !settings.Inactive
            }).ConfigureAwait(false);

            if (playbook != null)
            {
                AnsiConsole.MarkupLine($"[green]Playbook created![/] [bold]{Markup.Escape(playbook.FileName)}[/] [dim]({Markup.Escape(playbook.Id)})[/]");
            }

            return 0;
        }
    }
}
