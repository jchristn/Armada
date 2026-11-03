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
    using Armada.Core.Enums;
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
            bool followUp = !String.IsNullOrWhiteSpace(settings.FromRun);
            if (followUp && vesselIds.Count > 0)
            {
                AnsiConsole.MarkupLine("[red]Use either --from-run or --vessel/--fleet, not both.[/]");
                return 1;
            }

            if (!followUp && vesselIds.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No target vessels.[/] Use [green]--vessel <id-or-name>[/] (repeatable), [green]--fleet <id>[/], or [green]--from-run <run-id>[/].");
                return 1;
            }

            Dictionary<string, string> inputValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in settings.Inputs ?? Array.Empty<string>())
            {
                int equals = pair.IndexOf('=');
                if (equals < 1)
                {
                    AnsiConsole.MarkupLine("[red]Inputs take the form name=value:[/] " + Markup.Escape(pair));
                    return 1;
                }

                inputValues[pair.Substring(0, equals).Trim()] = pair.Substring(equals + 1);
            }

            List<FleetActionTargetStatusEnum>? fromStatuses = null;
            if (settings.FromStatuses != null && settings.FromStatuses.Length > 0)
            {
                fromStatuses = new List<FleetActionTargetStatusEnum>();
                foreach (string value in settings.FromStatuses)
                {
                    if (!Enum.TryParse(value, true, out FleetActionTargetStatusEnum status))
                    {
                        AnsiConsole.MarkupLine("[red]Unknown target status:[/] " + Markup.Escape(value));
                        return 1;
                    }

                    fromStatuses.Add(status);
                }
            }

            FleetActionRunRequest request = new FleetActionRunRequest
            {
                VesselIds = vesselIds,
                FromRunId = followUp ? settings.FromRun!.Trim() : null,
                FromTargetStatuses = fromStatuses,
                InputValues = inputValues,
                Concurrency = settings.Concurrency,
                Overrides = new FleetActionRunOverrides { ApprovalOptions = RunApprovalArguments.Build(settings.ApproveAll, settings.ApprovalTimeoutSeconds, settings.ApprovalDefault) }
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
