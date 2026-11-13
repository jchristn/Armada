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
    /// <c>armada blackbox storage status</c> (Admin): the storage target, its health, and usage by target.
    /// </summary>
    [Description("Show black box storage: target, health, and usage")]
    public class BlackBoxStorageStatusCommand : BlackBoxCommandBase<BaseSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxStorageStatus? s = await client.GetBlackBoxStorageAsync(cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(s);
                    return 0;
                }

                if (s == null) return 1;
                AnsiConsole.MarkupLine("[dodgerblue1]Target:[/]   " + s.Target + (s.FallbackToDatabase ? " [gold1](falls back to Database)[/]" : ""));
                AnsiConsole.MarkupLine("[dodgerblue1]Health:[/]   " + (s.Health.Healthy ? "[green]healthy[/]" : "[red]unhealthy[/]") + " [dim]" + s.Health.LatencyMs + " ms[/]" + (String.IsNullOrEmpty(s.Health.Message) ? "" : "  " + Markup.Escape(s.Health.Message!)));
                if (!String.IsNullOrEmpty(s.ActiveMigrationId)) AnsiConsole.MarkupLine("[dodgerblue1]Migration:[/] " + Markup.Escape(s.ActiveMigrationId!) + " [dim](armada blackbox storage migration " + Markup.Escape(s.ActiveMigrationId!) + ")[/]");
                foreach (BlackBoxStorageTargetUsage u in s.Usage) AnsiConsole.MarkupLine("  " + u.Target + ": " + u.Entries + " entries, " + Markup.Escape(BlackBoxCli.Bytes(u.Bytes)));
                return s.Health.Healthy ? 0 : 1;
            }).ConfigureAwait(false);
        }
    }
}
