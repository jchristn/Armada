namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Run a fleet action over vessels.
    /// </summary>
    [Description("Run a fleet action over vessels")]
    public class ActionRunCommand : BaseCommand<ActionRunSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ActionRunSettings settings, CancellationToken cancellationToken)
        {
            string actionId = await ResolveActionIdAsync(settings.Action).ConfigureAwait(false);

            EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels?pageSize=1000").ConfigureAwait(false);
            List<Vessel> allVessels = vesselResult?.Objects ?? new List<Vessel>();

            List<string> vesselIds = new List<string>();
            if (!String.IsNullOrWhiteSpace(settings.Fleet))
            {
                vesselIds.AddRange(allVessels.Where(v => String.Equals(v.FleetId, settings.Fleet, StringComparison.Ordinal)).Select(v => v.Id));
            }

            foreach (string identifier in settings.Vessels ?? Array.Empty<string>())
            {
                Vessel? match = EntityResolver.ResolveVessel(allVessels, identifier);
                vesselIds.Add(match != null ? match.Id : identifier);
            }

            vesselIds = vesselIds.Distinct(StringComparer.Ordinal).ToList();
            if (vesselIds.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No target vessels.[/] Use [green]--vessel <id-or-name>[/] (repeatable) or [green]--fleet <id>[/].");
                return 1;
            }

            FleetActionRunRequest request = new FleetActionRunRequest
            {
                VesselIds = vesselIds,
                Concurrency = settings.Concurrency
            };

            FleetActionRunStartResult? result = await PostAsync<FleetActionRunStartResult>("/api/v1/fleet-actions/" + actionId + "/run", request).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            if (result == null)
            {
                AnsiConsole.MarkupLine("[red]The server returned no run.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[gold1]Run started:[/] [bold]{Markup.Escape(result.RunId)}[/] ({result.Kind}, {result.TargetCount} vessel(s), concurrency {result.Concurrency})");
            AnsiConsole.MarkupLine($"Follow it with [green]armada action status {Markup.Escape(result.RunId)}[/].");
            return 0;
        }

        private async Task<string> ResolveActionIdAsync(string identifier)
        {
            if (identifier.StartsWith("fac_", StringComparison.Ordinal)) return identifier;

            FleetActionEnumerateRequest query = new FleetActionEnumerateRequest { PageSize = 1000 };
            EnumerationResult<FleetAction>? result = await PostAsync<EnumerationResult<FleetAction>>("/api/v1/fleet-actions/enumerate", query).ConfigureAwait(false);
            FleetAction? match = result?.Objects.FirstOrDefault(a => String.Equals(a.Name, identifier, StringComparison.OrdinalIgnoreCase))
                ?? result?.Objects.FirstOrDefault(a => String.Equals(a.BuiltInKey, identifier, StringComparison.OrdinalIgnoreCase));
            return match != null ? match.Id : identifier;
        }
    }
}
