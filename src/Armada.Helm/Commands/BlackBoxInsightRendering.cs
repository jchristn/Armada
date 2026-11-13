namespace Armada.Helm.Commands
{
    using System;
    using Armada.Core.Models;
    using Spectre.Console;

    /// <summary>
    /// Console rendering shared by the black box prevention commands (overlap warnings and brief lint findings).
    /// </summary>
    public static class BlackBoxInsightRendering
    {
        /// <summary>
        /// Print brief lint findings (or "clean").
        /// </summary>
        /// <param name="lint">Lint result, or null.</param>
        public static void Lint(BriefLintResult? lint)
        {
            if (lint == null) return;
            if (lint.Clean || lint.Findings.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]Brief lint: clean.[/]" + (lint.PathsChecked ? "" : " [dim](paths not checked: no vessel)[/]"));
                return;
            }

            AnsiConsole.MarkupLine("[gold1]Brief lint: " + lint.FindingCount + " finding(s)[/]" + (lint.PathsChecked ? "" : " [dim](paths not checked: no vessel)[/]"));
            foreach (BriefLintFinding f in lint.Findings)
            {
                string where = !String.IsNullOrEmpty(f.Path) ? " " + f.Path : !String.IsNullOrEmpty(f.Excerpt) ? " \"" + f.Excerpt + "\"" : "";
                AnsiConsole.MarkupLine("  [gold1]" + Markup.Escape(f.Severity.ToString()) + "[/] [bold]" + Markup.Escape(f.RuleId) + "[/]" + Markup.Escape(where) + ": " + Markup.Escape(f.Message));
                if (!String.IsNullOrEmpty(f.Suggestion)) AnsiConsole.MarkupLine("    [dim]" + Markup.Escape(f.Suggestion!) + "[/]");
            }
        }

        /// <summary>
        /// Print one overlap warning.
        /// </summary>
        /// <param name="w">Warning.</param>
        public static void Overlap(MissionOverlapWarning w)
        {
            if (w == null) return;
            string target = !String.IsNullOrEmpty(w.OverlappingMissionId)
                ? w.OverlappingMissionId + (String.IsNullOrEmpty(w.OverlappingMissionTitle) ? "" : " \"" + w.OverlappingMissionTitle + "\"") + (String.IsNullOrEmpty(w.OverlappingMissionStatus) ? "" : " (" + w.OverlappingMissionStatus + ")")
                : (w.Directory ?? "");
            AnsiConsole.MarkupLine("  [gold1]" + Markup.Escape(w.Kind.ToString()) + "[/] " + Markup.Escape(target)
                + (w.FileCount > 0 ? " [dim]" + w.FileCount + " file(s)[/]" : "") + (w.HotspotEntryCount > 0 ? " [dim]" + w.HotspotEntryCount + " past failure(s)[/]" : "")
                + (w.Serialized ? " [dodgerblue1](chained after it)[/]" : "") + (String.IsNullOrEmpty(w.Message) ? "" : ": " + Markup.Escape(w.Message)));
        }
    }
}
