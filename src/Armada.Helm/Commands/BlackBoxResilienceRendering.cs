namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console;
    using Spectre.Console.Rendering;

    /// <summary>
    /// The Armada resilience section of <c>armada blackbox show</c>: whether the failure is in Armada's handoff and
    /// landing scope, whether Armada could have healed it and how, the detection gap and invariant, the severity with its
    /// recurrence, the code pointers at the build commit, the proposed Armada changes, the regression test and the
    /// recoveries Armada already attempted. Outside the scope it prints the short reason no Armada change is suggested.
    /// </summary>
    public static class BlackBoxResilienceRendering
    {
        #region Public-Methods

        /// <summary>
        /// Build the section (a panel), or null when the entry has not been analyzed.
        /// </summary>
        /// <param name="entry">Entry with its record.</param>
        /// <returns>The panel, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public static IRenderable? Build(BlackBoxEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            BlackBoxResilience? res = entry.Record?.Analysis?.Resilience;
            if (res == null) return null;
            List<IRenderable> rows = new List<IRenderable>();
            if (!res.InScope && res.ProposedChanges.Count == 0)
            {
                rows.Add(new Markup("[dim]" + Markup.Escape(res.NoChangeReason ?? "No Armada change suggested: the failure is outside Armada's handoff and landing.") + "[/]"));
                return Wrap(rows);
            }

            rows.Add(new Markup("[dodgerblue1]Scope:[/]        " + (res.InScope ? "Armada handoff and landing" + (String.IsNullOrEmpty(res.ScopeReason) ? "" : " (" + Markup.Escape(res.ScopeReason!) + ")") : "outside handoff and landing")));
            if (entry.Record?.Analysis?.Triage?.Suspected == true) rows.Add(new Markup("[gold1]Suspected Armada defect:[/] only the handoff and landing prior voted ArmadaDefect"));
            rows.Add(new Markup("[dodgerblue1]Self-healable:[/] [bold]" + Markup.Escape(res.SelfHealable.ToString()) + "[/]" + (String.IsNullOrEmpty(res.SelfHealingProposal) ? "" : "  Armada could have " + Markup.Escape(res.SelfHealingProposal!.TrimEnd('.')) + ".")));
            if (!String.IsNullOrEmpty(res.DetectionGap)) rows.Add(new Markup("[dodgerblue1]Detection gap:[/] " + Markup.Escape(res.DetectionGapKind + ": " + res.DetectionGap)));
            if (!String.IsNullOrEmpty(res.Invariant)) rows.Add(new Markup("[dodgerblue1]Invariant:[/]     " + Markup.Escape(res.Invariant!)));
            rows.Add(new Markup("[dodgerblue1]Severity:[/]      " + Severity(res.Severity) + "  " + Markup.Escape(Recurrence(res))));
            if (res.AttemptedRecoveries.Count > 0) rows.Add(new Markup("[dodgerblue1]Already tried:[/] " + Markup.Escape(String.Join("; ", res.AttemptedRecoveries))));
            if (res.CodePointers.Count > 0)
                rows.Add(new Markup("[dodgerblue1]Code:[/]          " + Markup.Escape(BuildLabel(res, entry)) + "  " + Markup.Escape(String.Join(", ", res.CodePointers))));
            foreach (BlackBoxProposedChange c in res.ProposedChanges.OrderBy(c => c.Rank))
            {
                rows.Add(new Markup("[green]" + c.Rank.ToString(CultureInfo.InvariantCulture) + ". " + Markup.Escape(c.Title) + "[/]  " + Severity(c.Severity)));
                if (!String.IsNullOrEmpty(c.AcceptanceTest)) rows.Add(new Markup("   [dim]Acceptance test:[/] " + Markup.Escape(c.AcceptanceTest!)));
            }

            if (res.RegressionTest != null) rows.Add(new Markup("[dodgerblue1]Regression test:[/] " + Markup.Escape(res.RegressionTest.Suite + ": " + res.RegressionTest.Description)));
            return Wrap(rows);
        }

        /// <summary>
        /// A severity as colored markup.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <returns>Markup.</returns>
        public static string Severity(BlackBoxSeverityEnum severity)
        {
            switch (severity)
            {
                case BlackBoxSeverityEnum.Critical: return "[red bold]Critical[/]";
                case BlackBoxSeverityEnum.High: return "[red]High[/]";
                case BlackBoxSeverityEnum.Medium: return "[gold1]Medium[/]";
                default: return "[dim]" + Markup.Escape(severity.ToString()) + "[/]";
            }
        }

        /// <summary>
        /// The recurrence behind a severity: occurrences in 30 days and Armada versions.
        /// </summary>
        /// <param name="res">Resilience section.</param>
        /// <returns>Text.</returns>
        public static string Recurrence(BlackBoxResilience res)
        {
            if (res == null) throw new ArgumentNullException(nameof(res));
            return "(seen " + res.Occurrences30Days.ToString(CultureInfo.InvariantCulture) + (res.Occurrences30Days == 1 ? " time" : " times") + " in 30 days"
                + (res.ArmadaVersionCount > 1 ? ", in " + res.ArmadaVersionCount.ToString(CultureInfo.InvariantCulture) + " Armada versions" : "") + ")";
        }

        #endregion

        #region Private-Methods

        private static string BuildLabel(BlackBoxResilience res, BlackBoxEntry entry)
        {
            string version = res.ArmadaVersion ?? entry.ArmadaVersion ?? "unknown";
            string? commit = res.ArmadaCommit ?? entry.ArmadaCommit;
            return "(Armada " + version + (commit != null ? " commit " + commit : "") + ")";
        }

        private static IRenderable Wrap(List<IRenderable> rows)
        {
            Panel panel = new Panel(new Rows(rows));
            panel.Header = new PanelHeader("[gold1]Armada resilience[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.Gold1);
            return panel;
        }

        #endregion
    }
}
