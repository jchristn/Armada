namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Create a new voyage with missions.
    /// </summary>
    [Description("Create a new voyage with missions")]
    public class VoyageCreateCommand : BaseCommand<VoyageCreateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageCreateSettings settings, CancellationToken cancellationToken)
        {
            // Resolve vessel by name
            string? vesselId = settings.VesselId;
            if (!string.IsNullOrEmpty(vesselId) && !vesselId.StartsWith("vsl_"))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;
                if (vessels != null)
                {
                    Vessel? match = EntityResolver.ResolveVessel(vessels, vesselId);
                    if (match != null) vesselId = match.Id;
                }
            }

            List<object> missions = new List<object>();
            List<SelectedPlaybook> selectedPlaybooks = await ResolvePlaybookSelectionsAsync(settings.Playbooks).ConfigureAwait(false);
            if (settings.Playbooks != null && selectedPlaybooks.Count != settings.Playbooks.Length)
            {
                return 1;
            }

            if (settings.Missions != null)
            {
                foreach (string desc in settings.Missions)
                {
                    missions.Add(new
                    {
                        Title = desc,
                        Description = desc,
                        VesselId = vesselId
                    });
                }
            }

            object body = new
            {
                Title = settings.Title,
                VesselId = vesselId,
                Missions = missions,
                SelectedPlaybooks = selectedPlaybooks
            };

            Voyage? voyage = await PostAsync<Voyage>("/api/v1/voyages", body).ConfigureAwait(false);

            if (voyage != null)
            {
                AnsiConsole.MarkupLine($"[green]Voyage launched![/] [bold]{Markup.Escape(voyage.Title)}[/] [dim]({Markup.Escape(voyage.Id)})[/]");
                AnsiConsole.MarkupLine($"  [dodgerblue1]{missions.Count} mission(s)[/] queued.");
                AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            }

            return 0;
        }

        private async Task<List<SelectedPlaybook>> ResolvePlaybookSelectionsAsync(string[]? selections)
        {
            List<SelectedPlaybook> results = new List<SelectedPlaybook>();
            if (selections == null || selections.Length == 0) return results;

            EnumerationResult<Playbook>? playbookResult = await GetAsync<EnumerationResult<Playbook>>("/api/v1/playbooks").ConfigureAwait(false);
            List<Playbook> playbooks = playbookResult?.Objects ?? new List<Playbook>();

            foreach (string rawSelection in selections)
            {
                if (string.IsNullOrWhiteSpace(rawSelection)) continue;

                string token = rawSelection.Trim();
                PlaybookDeliveryModeEnum mode = PlaybookDeliveryModeEnum.InlineFullContent;
                int modeSeparator = token.LastIndexOf(':');
                if (modeSeparator > 0)
                {
                    string candidateMode = token.Substring(modeSeparator + 1);
                    if (Enum.TryParse(candidateMode, true, out PlaybookDeliveryModeEnum parsedMode))
                    {
                        mode = parsedMode;
                        token = token.Substring(0, modeSeparator);
                    }
                }

                Playbook? playbook = playbooks.FirstOrDefault(p =>
                    string.Equals(p.Id, token, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.FileName, token, StringComparison.OrdinalIgnoreCase));

                if (playbook == null)
                {
                    AnsiConsole.MarkupLine($"[red]Playbook not found:[/] {Markup.Escape(token)}");
                    return new List<SelectedPlaybook>();
                }

                results.Add(new SelectedPlaybook
                {
                    PlaybookId = playbook.Id,
                    DeliveryMode = mode
                });
            }

            return results;
        }
    }
}
