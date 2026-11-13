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
    /// <c>armada blackbox diff &lt;entryId&gt; &lt;otherId&gt;</c>: what changed between two black box entries (captain,
    /// model, base commit, configuration, changed files, step).
    /// </summary>
    [Description("Show what changed between two black box entries")]
    public class BlackBoxDiffCommand : BlackBoxCommandBase<BlackBoxDiffSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxDiffSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string id = (settings.Id ?? "").Trim();
            string other = (settings.OtherId ?? "").Trim();
            if (id.Length == 0 || other.Length == 0)
            {
                Status("Error: two entry ids are required.");
                return 1;
            }

            if (id == other)
            {
                Status("Error: choose two different entries.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxAttemptDiff? diff = await client.DiffBlackBoxEntriesAsync(id, other, cancellationToken).ConfigureAwait(false);
                if (diff == null)
                {
                    Status("Error: no diff for " + id + " and " + other + ".");
                    return BlackBoxCli.NotFoundExitCode;
                }

                if (settings.Json)
                {
                    WritePureJson(diff);
                    return 0;
                }

                AnsiConsole.MarkupLine("[dodgerblue1]" + Markup.Escape(diff.EntryId) + "[/] (attempt " + diff.Attempt + ") vs [dodgerblue1]" + Markup.Escape(diff.OtherEntryId) + "[/] (attempt " + diff.OtherAttempt + ")");
                AnsiConsole.MarkupLine((diff.SameMission ? "Same mission" : "Different missions") + ", " + (diff.SameFingerprint ? "same fingerprint" : "different fingerprint") + ", [bold]" + diff.DifferenceCount + "[/] difference(s)");
                if (diff.Differences.Count > 0)
                {
                    Table table = TableRenderer.CreateTable("Differences", null);
                    table.AddColumn("Section");
                    table.AddColumn("Field");
                    table.AddColumn(Markup.Escape(diff.EntryId));
                    table.AddColumn(Markup.Escape(diff.OtherEntryId));
                    foreach (BlackBoxDiffField f in diff.Differences)
                        table.AddRow(Markup.Escape(f.Section), Markup.Escape(f.Field), Markup.Escape(f.Value ?? "-"), Markup.Escape(f.OtherValue ?? "-"));
                    AnsiConsole.Write(table);
                }

                if (!diff.ChangedFilesKnown)
                {
                    AnsiConsole.MarkupLine("[dim]The changed files of one of the entries are not known.[/]");
                    return 0;
                }

                AnsiConsole.MarkupLine("Files in both: " + diff.FilesInBothCount + ", only in " + Markup.Escape(diff.EntryId) + ": " + diff.FilesOnlyInEntryCount + ", only in " + Markup.Escape(diff.OtherEntryId) + ": " + diff.FilesOnlyInOtherCount);
                foreach (string f in diff.FilesOnlyInEntry) AnsiConsole.MarkupLine("  [green]+ " + Markup.Escape(f) + "[/]");
                foreach (string f in diff.FilesOnlyInOther) AnsiConsole.MarkupLine("  [red]- " + Markup.Escape(f) + "[/]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
