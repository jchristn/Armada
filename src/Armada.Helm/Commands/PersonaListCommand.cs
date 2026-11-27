namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;

    /// <summary>
    /// List personas with their captain priority and escalation ladder sizes.
    /// </summary>
    [Description("List personas")]
    public class PersonaListCommand : BaseCommand<BaseSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
        {
            int page = settings.Page ?? 1;
            int size = settings.PageSize ?? 100;
            EnumerationResult<Persona>? result = await GetAsync<EnumerationResult<Persona>>(
                "/api/v1/personas?pageNumber=" + page.ToString(CultureInfo.InvariantCulture) + "&pageSize=" + size.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No personas found.[/]");
                return 0;
            }

            Table table = TableRenderer.CreateTable("Personas", null);
            table.AddColumn("Name");
            table.AddColumn("Built in");
            table.AddColumn("Priorities");
            table.AddColumn("Ladder");
            table.AddColumn("Description");
            foreach (Persona persona in result.Objects)
            {
                table.AddRow(
                    Markup.Escape(persona.Name),
                    persona.IsBuiltIn ? "yes" : "",
                    persona.CaptainPriorities.Count.ToString(CultureInfo.InvariantCulture),
                    persona.EscalationLadder.Count.ToString(CultureInfo.InvariantCulture),
                    Markup.Escape(persona.Description ?? ""));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
