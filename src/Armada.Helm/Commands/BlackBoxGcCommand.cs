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
    /// <c>armada blackbox gc [--dry-run]</c> (Admin): runs black box garbage collection now, or with <c>--dry-run</c>
    /// reports what it would remove.
    /// </summary>
    [Description("Run black box garbage collection now (--dry-run reports what would go)")]
    public class BlackBoxGcCommand : BlackBoxCommandBase<BlackBoxGcSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxGcSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxGcReport? report = await client.RunBlackBoxGcAsync(settings.DryRun, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(report);
                    return 0;
                }

                if (report == null) return 1;
                AnsiConsole.MarkupLine(report.DryRun ? "[bold]GC dry run:[/] nothing was deleted." : "[green]GC finished[/] in " + report.DurationMs + " ms.");
                AnsiConsole.MarkupLine("  Expired " + report.Expired + ", evicted " + report.Evicted + ", cascaded " + report.Cascaded + ", orphans " + report.OrphansRemoved
                    + ", objects missing " + report.ObjectsMissing + ", failures " + report.Failures + ", bytes " + Markup.Escape(BlackBoxCli.Bytes(report.BytesFreed)) + ".");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
