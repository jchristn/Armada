namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Show a model profile's served context, tier, recovery policy and timeouts.
    /// </summary>
    [Description("Show a model profile's context, tier, recovery policy and timeouts")]
    public class ModelProfileShowCommand : BaseCommand<ModelProfileIdSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ModelProfileIdSettings settings, CancellationToken cancellationToken)
        {
            ModelProfile? profile = await GetAsync<ModelProfile>("/api/v1/model-profiles/" + Uri.EscapeDataString(settings.ProfileId)).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(profile);
                return 0;
            }

            if (profile == null)
            {
                AnsiConsole.MarkupLine("[red]Model profile not found.[/]");
                return 1;
            }

            ModelProfileRendering.Write(profile);
            return 0;
        }
    }
}
