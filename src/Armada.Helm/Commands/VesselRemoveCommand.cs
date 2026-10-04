namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Remove a vessel by name or ID.
    /// </summary>
    [Description("Remove a vessel")]
    public class VesselRemoveCommand : BaseCommand<VesselRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselRemoveSettings settings, CancellationToken cancellationToken)
        {
            string vesselId = settings.Id;
            if (!vesselId.StartsWith("vsl_"))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;
                if (vessels != null)
                {
                    Vessel? match = EntityResolver.ResolveVessel(vessels, settings.Id);
                    if (match != null)
                    {
                        vesselId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Vessel not found:[/] {Markup.Escape(settings.Id)}");
                        if (vessels.Count > 0)
                        {
                            AnsiConsole.MarkupLine("[dim]Available vessels:[/]");
                            foreach (Vessel v in vessels)
                            {
                                AnsiConsole.MarkupLine($"  [dim]-[/] [bold]{Markup.Escape(v.Name)}[/] [dim]({Markup.Escape(v.Id)})[/]");
                            }
                        }
                        return 1;
                    }
                }
            }

            await DeleteAsync($"/api/v1/vessels/{vesselId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Vessel decommissioned:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }
}
