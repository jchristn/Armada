namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox export-fixture &lt;entryId&gt; [--name] [-o dir]</c>: exports a sanitized regression fixture
    /// through the server's export route and writes <c>{name}.json</c>; by default into
    /// <c>src/Test.Shared/Fixtures/BlackBox</c> when run inside the Armada repository, else <c>-o</c> is required.
    /// </summary>
    [Description("Export a black box entry as a sanitized regression fixture")]
    public class BlackBoxExportFixtureCommand : BlackBoxCommandBase<BlackBoxExportFixtureSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxExportFixtureSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string? dir = String.IsNullOrWhiteSpace(settings.Output) ? BlackBoxCli.FindFixtureDirectory(Directory.GetCurrentDirectory()) : Path.GetFullPath(settings.Output!);
            if (dir == null)
            {
                Status("Error: not inside the Armada repository; pass -o <directory> for the fixture.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxFixture? fixture = await client.ExportBlackBoxFixtureAsync(settings.Id, settings.Name, cancellationToken).ConfigureAwait(false);
                if (fixture == null)
                {
                    Status("Error: the server returned no fixture.");
                    return 1;
                }

                string path = await BlackBoxFixtureExporter.WriteAsync(fixture, dir, cancellationToken).ConfigureAwait(false);
                if (settings.Json) WritePureJson(new { Path = path, fixture.Name, fixture.ExpectedTriage, fixture.ExpectedRuleIds });
                else AnsiConsole.MarkupLine("[green]Wrote[/] " + Markup.Escape(path) + " [dim](triage " + fixture.ExpectedTriage + ", rules " + Markup.Escape(String.Join(", ", fixture.ExpectedRuleIds)) + ")[/]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
