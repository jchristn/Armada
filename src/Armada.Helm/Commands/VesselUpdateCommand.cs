namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada vessel update &lt;vessel&gt; --serialize-overlapping-missions true|false</c>: change a vessel setting. The
    /// vessel is read and written back whole, so nothing else changes. With no setting to change it is refused.
    /// </summary>
    [Description("Update a vessel's settings")]
    public class VesselUpdateCommand : BlackBoxCommandBase<VesselUpdateSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such vessel. List vessels with: armada vessel list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselUpdateSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.SerializeOverlappingMissions.HasValue)
            {
                Status("Error: nothing to update. Pass --serialize-overlapping-missions true|false.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                Vessel? vessel = await ResolveVesselAsync(client, settings.Vessel, cancellationToken).ConfigureAwait(false);
                if (vessel == null) return BlackBoxCli.NotFoundExitCode;
                VesselUpsertRequest body = VesselUpsertRequest.From(vessel);
                body.SerializeOverlappingMissions = settings.SerializeOverlappingMissions.Value;
                Vessel? updated = await client.UpdateVesselAsync(vessel.Id, body, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(updated);
                    return 0;
                }

                bool on = updated?.SerializeOverlappingMissions ?? body.SerializeOverlappingMissions;
                AnsiConsole.MarkupLine("[green]Vessel updated:[/] [bold]" + Markup.Escape(vessel.Name) + "[/] [dim](" + Markup.Escape(vessel.Id) + ")[/]");
                AnsiConsole.MarkupLine("Serialize overlapping missions: [bold]" + (on ? "on" : "off") + "[/]" + (on ? " [dim](a mission that overlaps an active one starts after it lands)[/]" : ""));
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
