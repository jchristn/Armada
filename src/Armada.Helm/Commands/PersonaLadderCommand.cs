namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Show or replace a persona's escalation ladder.
    /// </summary>
    [Description("Show or replace a persona's escalation ladder")]
    public class PersonaLadderCommand : BaseCommand<PersonaLadderSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, PersonaLadderSettings settings, CancellationToken cancellationToken)
        {
            bool hasRungs = settings.Rungs != null && settings.Rungs.Length > 0;
            if (hasRungs && settings.Clear)
            {
                HelmConsole.Error.MarkupLine("[red]Use --rung or --clear, not both.[/]");
                return 1;
            }

            string path = "/api/v1/personas/" + Uri.EscapeDataString(settings.Name);
            if (!hasRungs && !settings.Clear)
            {
                Persona? current = await GetAsync<Persona>(path).ConfigureAwait(false);
                if (IsJsonMode(settings))
                {
                    WriteJson(current?.EscalationLadder);
                    return 0;
                }

                if (current == null)
                {
                    AnsiConsole.MarkupLine("[red]Persona not found.[/]");
                    return 1;
                }

                PersonaRendering.Write(current);
                return 0;
            }

            List<PersonaCaptainPriority> ladder = new List<PersonaCaptainPriority>();
            if (hasRungs)
            {
                foreach (string spec in settings.Rungs!)
                {
                    if (!PersonaRungParser.TryParse(spec, out PersonaCaptainPriority rung, out string? error))
                    {
                        HelmConsole.Error.MarkupLine($"[red]--rung {Markup.Escape(spec)}: {Markup.Escape(error ?? "invalid")}[/]");
                        return 1;
                    }
                    ladder.Add(rung);
                }
            }

            // The update request carries only the ladder, so every other persona field keeps its value.
            Persona? updated = await PutAsync<Persona>(path, new PersonaLadderUpdate { EscalationLadder = ladder }).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(updated);
                return 0;
            }

            AnsiConsole.MarkupLine(ladder.Count == 0
                ? $"[green]Escalation ladder cleared for[/] [bold]{Markup.Escape(settings.Name)}[/]."
                : $"[green]Escalation ladder saved for[/] [bold]{Markup.Escape(settings.Name)}[/] ({ladder.Count} rungs).");
            if (updated != null) PersonaRendering.Write(updated);
            return 0;
        }
    }
}
