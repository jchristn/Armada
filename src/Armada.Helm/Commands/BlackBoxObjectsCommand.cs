namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox objects &lt;entryId&gt;</c>: one page of the entry's evidence objects (name, type, size).
    /// </summary>
    [Description("List a black box entry's evidence objects")]
    public class BlackBoxObjectsCommand : BlackBoxCommandBase<BlackBoxIdSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxIdSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                EnumerationResult<BlackBoxObjectSummary>? result = await client.ListBlackBoxObjectsAsync(settings.Id, settings.Page, settings.PageSize, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(result);
                    return 0;
                }

                if (result == null || result.Objects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[gold1]No evidence objects.[/]");
                    return 0;
                }

                TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
                Table table = TableRenderer.CreateTable("Objects", null);
                table.AddColumn("Name");
                table.AddColumn("Type");
                table.AddColumn("Size");
                table.AddColumn("State");
                foreach (BlackBoxObjectSummary o in result.Objects)
                {
                    table.AddRow(
                        "[green]" + Markup.Escape(o.Name) + "[/]",
                        Markup.Escape(o.ContentType),
                        Markup.Escape(BlackBoxCli.Bytes(o.UncompressedBytes > 0 ? o.UncompressedBytes : o.SizeBytes)),
                        o.IsMissing ? "[red]Missing[/]" : "Stored");
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("[dim]Read one:[/] armada blackbox cat " + Markup.Escape(settings.Id) + " <name> [[--offset N]] [[--length N]]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
