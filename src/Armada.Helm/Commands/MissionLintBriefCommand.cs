namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console.Cli;

    /// <summary>
    /// <c>armada mission lint-brief --brief|--brief-file</c>: lint a mission brief (dependency language without a
    /// dependency, verification-only briefs, host paths and, with <c>--vessel</c>, paths the vessel does not have).
    /// Findings are warnings and never change the exit code.
    /// </summary>
    [Description("Lint a mission brief (warnings only)")]
    public class MissionLintBriefCommand : BlackBoxCommandBase<MissionLintBriefSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such vessel or mission. List vessels with: armada vessel list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionLintBriefSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string? brief = ReadBrief(settings.Brief, settings.BriefFile);
            if (brief == null) return 1;
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BriefLintRequest request = new BriefLintRequest();
                request.Title = String.IsNullOrWhiteSpace(settings.Title) ? null : settings.Title.Trim();
                request.Description = brief;
                request.DependsOnMissionId = String.IsNullOrWhiteSpace(settings.DependsOn) ? null : settings.DependsOn.Trim();
                request.VoyageId = String.IsNullOrWhiteSpace(settings.Voyage) ? null : settings.Voyage.Trim();
                if (!String.IsNullOrWhiteSpace(settings.Vessel))
                {
                    Vessel? vessel = await ResolveVesselAsync(client, settings.Vessel, cancellationToken).ConfigureAwait(false);
                    if (vessel == null) return BlackBoxCli.NotFoundExitCode;
                    request.VesselId = vessel.Id;
                }

                BriefLintResult? result = await client.LintMissionBriefAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(result);
                    return 0;
                }

                if (result == null)
                {
                    Status("Error: the Admiral returned no lint result.");
                    return 1;
                }

                BlackBoxInsightRendering.Lint(result);
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
