namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox storage migrate --to &lt;target&gt; [--from] [--delete-source] [--batch-size]</c> (Admin): starts
    /// a background migration job and prints its id.
    /// </summary>
    [Description("Migrate black box objects to another storage target")]
    public class BlackBoxStorageMigrateCommand : BlackBoxCommandBase<BlackBoxStorageMigrateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxStorageMigrateSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxStorageTargetEnum? to = BlackBoxCli.ParseEnum<BlackBoxStorageTargetEnum>("--to", settings.To);
                if (to == null)
                {
                    Status("Error: --to is required (" + String.Join(", ", Enum.GetNames(typeof(BlackBoxStorageTargetEnum))) + ").");
                    return 1;
                }

                BlackBoxStorageMigrationRequest request = new BlackBoxStorageMigrationRequest { To = to.Value, From = BlackBoxCli.ParseEnum<BlackBoxStorageTargetEnum>("--from", settings.From), DeleteSource = settings.DeleteSource };
                if (settings.BatchSize.HasValue) request.BatchSize = settings.BatchSize.Value;
                BlackBoxStorageMigrationJob? job = await client.MigrateBlackBoxStorageAsync(request, cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(job);
                else if (job != null) AnsiConsole.MarkupLine("[green]Migration started[/] " + Markup.Escape(job.Id) + " to " + job.To + ". Follow it with: armada blackbox storage migration " + Markup.Escape(job.Id));
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
