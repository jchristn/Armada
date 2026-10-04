namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Show the "needs you" inbox: items across the fleet awaiting a decision or intervention.
    /// </summary>
    [Description("Show items awaiting your attention (reviews, failures, stalls)")]
    public class InboxCommand : BaseCommand<InboxSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, InboxSettings settings, CancellationToken cancellationToken)
        {
            List<InboxItem>? items = await GetAsync<List<InboxItem>>("/api/v1/inbox").ConfigureAwait(false);

            if (IsJsonMode(settings))
            {
                WriteJson(items);
                return items == null ? 1 : 0;
            }

            if (items == null)
            {
                AnsiConsole.MarkupLine("[red]Unable to retrieve inbox.[/] Is the Admiral running?");
                return 1;
            }

            if (settings.CriticalOnly)
                items = items.Where(i => i.Severity == InboxSeverityEnum.Critical).ToList();

            if (items.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]You are all caught up.[/] Nothing needs your attention right now.");
                return 0;
            }

            Table table = TableRenderer.CreateTable($"Needs You ({items.Count})", null);
            table.AddColumn("Severity");
            table.AddColumn("Item");
            table.AddColumn("Detail");

            foreach (InboxItem item in items)
            {
                string color = item.Severity switch
                {
                    InboxSeverityEnum.Critical => "red",
                    InboxSeverityEnum.Warning => "yellow",
                    _ => "dim"
                };
                table.AddRow(
                    $"[{color}]{item.Severity}[/]",
                    Markup.Escape(item.Title),
                    $"[dim]{Markup.Escape(item.Detail)}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
