namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Spectre.Console;
    using Armada.Core.Models;

    /// <summary>
    /// Console rendering for a persona's routing lists.
    /// </summary>
    public static class PersonaRendering
    {
        /// <summary>
        /// Write the persona's name, captain priorities and escalation ladder.
        /// </summary>
        /// <param name="persona">Persona to render.</param>
        /// <exception cref="ArgumentNullException">When persona is null.</exception>
        public static void Write(Persona persona)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(persona.Name)}[/]" + (String.IsNullOrEmpty(persona.Description) ? "" : " " + Markup.Escape(persona.Description!)));
            AnsiConsole.MarkupLine($"Availability {persona.PriorityAvailability}, exhaustion {persona.PriorityExhaustion}");
            WriteList("Captain priorities", persona.CaptainPriorities, "none (default captain order)");
            WriteList("Escalation ladder", persona.EscalationLadder, "none (escalation uses the next tier up)");
        }

        private static void WriteList(string heading, List<PersonaCaptainPriority> rungs, string empty)
        {
            AnsiConsole.MarkupLine($"[dodgerblue1]{Markup.Escape(heading)}:[/]" + (rungs.Count == 0 ? " " + Markup.Escape(empty) : ""));
            for (int i = 0; i < rungs.Count; i++)
                AnsiConsole.MarkupLine("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + Markup.Escape(PersonaRungParser.Describe(rungs[i])));
        }
    }
}
