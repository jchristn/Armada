namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Show a persona's captain priorities and escalation ladder.
    /// </summary>
    [Description("Show a persona's captain priorities and escalation ladder")]
    public class PersonaShowCommand : BaseCommand<PersonaNameSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, PersonaNameSettings settings, CancellationToken cancellationToken)
        {
            Persona? persona = await GetAsync<Persona>("/api/v1/personas/" + Uri.EscapeDataString(settings.Name)).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(persona);
                return 0;
            }

            if (persona == null)
            {
                AnsiConsole.MarkupLine("[red]Persona not found.[/]");
                return 1;
            }

            PersonaRendering.Write(persona);
            return 0;
        }
    }
}
