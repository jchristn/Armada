namespace Armada.Helm.Commands
{
    using System;
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
            List<InboxItem>? items = await EnumerateAllAsync<InboxItem>("/api/v1/inbox/enumerate").ConfigureAwait(false);

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
                string detail = $"[dim]{Markup.Escape(item.Detail)}[/]";
                List<string> actions = ModelAlertActionLines(item);
                if (actions.Count > 0) detail += "\n" + String.Join("\n", actions.Select(Markup.Escape));
                table.AddRow(
                    $"[{color}]{item.Severity}[/]",
                    Markup.Escape(item.Title),
                    detail);
            }

            AnsiConsole.Write(table);
            if (items.Any(i => ModelAlertActionLines(i).Count > 0))
                AnsiConsole.MarkupLine("[dim]Model alerts are advisory: nothing changes until a suggested action is applied (dashboard, TUI, POST /api/v1/model-alerts/{id}/apply, or MCP).[/]");
            return 0;
        }

        /// <summary>
        /// Plain-text lines describing a model alert item's suggested actions (label, then available or the reason it is
        /// unavailable) and its latest applied action; empty for other kinds.
        /// </summary>
        /// <param name="item">Inbox item.</param>
        /// <returns>Lines, unescaped.</returns>
        public static List<string> ModelAlertActionLines(InboxItem item)
        {
            List<string> lines = new List<string>();
            if (item == null || item.Kind != InboxItemKinds.ModelAlert || item.ModelAlert == null) return lines;
            foreach (ModelAlertSuggestedAction action in item.ModelAlert.SuggestedActions ?? new List<ModelAlertSuggestedAction>())
            {
                string state = action.Available
                    ? "available"
                    : "unavailable: " + (String.IsNullOrEmpty(action.UnavailableReason) ? "no reason given" : action.UnavailableReason);
                lines.Add("- " + action.Label + " [" + action.Action + "] " + (action.Recommended ? "(recommended) " : "") + "(" + state + ")");
            }

            ModelAlertActionRecord? last = item.ModelAlert.Actions?.LastOrDefault();
            if (last != null && !String.IsNullOrEmpty(last.Result)) lines.Add("Last action: " + last.Result);
            return lines;
        }
    }
}
