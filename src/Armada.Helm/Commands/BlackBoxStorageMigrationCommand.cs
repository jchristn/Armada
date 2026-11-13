namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox storage migration &lt;id&gt;</c> (Admin): the progress of a storage migration job.
    /// </summary>
    [Description("Show a black box storage migration job")]
    public class BlackBoxStorageMigrationCommand : BlackBoxCommandBase<BlackBoxIdSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxIdSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxStorageMigrationJob? job = await client.GetBlackBoxStorageMigrationAsync(settings.Id, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(job);
                    return 0;
                }

                if (job == null) return 1;
                AnsiConsole.MarkupLine(Markup.Escape(job.Id) + "  " + job.State + "  " + (job.From?.ToString() ?? "current") + " -> " + job.To + "  " + job.MigratedEntries + "/" + job.TotalEntries
                    + " migrated, " + job.FailedEntries + " failed, " + Markup.Escape(BlackBoxCli.Bytes(job.BytesCopied)) + (String.IsNullOrEmpty(job.LastError) ? "" : "  [red]" + Markup.Escape(job.LastError!) + "[/]"));
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
