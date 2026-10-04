namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// List playbooks.
    /// </summary>
    [Description("List all playbooks")]
    public class PlaybookListCommand : BaseCommand<PlaybookListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, PlaybookListSettings settings, CancellationToken cancellationToken)
        {
            string path = AppendPagination("/api/v1/playbooks", settings);
            EnumerationResult<Playbook>? result = await GetAsync<EnumerationResult<Playbook>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No playbooks found.[/] Use [green]armada playbook add <file-name>[/] to create one.");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Playbooks", null);
            table.AddColumn("Id");
            table.AddColumn("File");
            table.AddColumn("Active");
            table.AddColumn("Updated");

            foreach (Playbook playbook in result.Objects)
            {
                table.AddRow(
                    Markup.Escape(playbook.Id),
                    Markup.Escape(playbook.FileName),
                    playbook.Active ? "[green]true[/]" : "[red]false[/]",
                    playbook.LastUpdateUtc.ToString("yyyy-MM-dd HH:mm"));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
