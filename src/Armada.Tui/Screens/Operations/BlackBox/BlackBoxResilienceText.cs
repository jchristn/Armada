namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The Armada resilience section of the black box entry views (the Failure tab and the entry screen's Brief tab):
    /// scope, whether Armada could have healed the failure and how, the detection gap, the severity with its recurrence,
    /// the code pointers at the build commit, the proposed Armada changes, the regression test and the recoveries Armada
    /// already attempted; outside the handoff and landing scope the short reason no Armada change is suggested. Also the
    /// "Draft Armada fix mission" text and the Armada vessel lookup. Thread-safe (stateless).
    /// </summary>
    public static class BlackBoxResilienceText
    {
        #region Public-Methods

        /// <summary>
        /// Whether the entry's ArmadaDefect verdict is suspected (only the handoff and landing prior voted for it).
        /// </summary>
        /// <param name="entry">Entry, or null.</param>
        /// <returns>True when suspected.</returns>
        public static bool IsSuspected(BlackBoxEntry? entry)
        {
            BlackBoxTriageVerdict? t = entry?.Record?.Analysis?.Triage;
            return t != null && t.Suspected && t.Verdict == BlackBoxTriageEnum.ArmadaDefect;
        }

        /// <summary>
        /// The most impactful proposed Armada change of the entry, or null.
        /// </summary>
        /// <param name="entry">Entry, or null.</param>
        /// <returns>The change, or null.</returns>
        public static BlackBoxProposedChange? TopChange(BlackBoxEntry? entry)
        {
            List<BlackBoxProposedChange>? changes = entry?.Record?.Analysis?.Resilience?.ProposedChanges;
            return changes == null ? null : changes.OrderBy(c => c.Rank).FirstOrDefault();
        }

        /// <summary>
        /// The mission brief of a proposed Armada change (its MissionBrief, or the title, change and acceptance test).
        /// </summary>
        /// <param name="change">Change.</param>
        /// <returns>Brief.</returns>
        /// <exception cref="ArgumentNullException">Thrown when change is null.</exception>
        public static string DraftBrief(BlackBoxProposedChange change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            if (!String.IsNullOrWhiteSpace(change.MissionBrief)) return change.MissionBrief!;
            string brief = change.Title + "\n\n" + change.Change;
            if (!String.IsNullOrWhiteSpace(change.AcceptanceTest)) brief += "\n\nAcceptance test: " + change.AcceptanceTest;
            return brief;
        }

        /// <summary>
        /// The vessel that holds Armada's own source: named Armada, or whose repository is Armada. Null when none.
        /// </summary>
        /// <param name="vessels">Vessels.</param>
        /// <returns>Vessel id, or null.</returns>
        public static string? ArmadaVesselId(IEnumerable<Vessel>? vessels)
        {
            if (vessels == null) return null;
            List<Vessel> list = vessels.Where(v => v != null).ToList();
            Vessel? named = list.FirstOrDefault(v => String.Equals((v.Name ?? "").Trim(), "Armada", StringComparison.OrdinalIgnoreCase));
            if (named != null) return named.Id;
            Vessel? repo = list.FirstOrDefault(v =>
            {
                string url = (v.RepoUrl ?? "").Trim().TrimEnd('/');
                if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 4);
                return url.EndsWith("/Armada", StringComparison.OrdinalIgnoreCase);
            });
            return repo?.Id;
        }

        /// <summary>
        /// Add the Armada resilience section to a document.
        /// </summary>
        /// <param name="doc">Document.</param>
        /// <param name="entry">Entry with its record.</param>
        /// <param name="draftKey">Key that drafts the Armada fix mission (shown next to the proposals), or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when doc or entry is null.</exception>
        public static void Append(OpsDocument doc, BlackBoxEntry entry, string? draftKey)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            BlackBoxResilience? res = entry.Record?.Analysis?.Resilience;
            doc.Section("Armada resilience");
            if (res == null)
            {
                doc.Note("No resilience analysis yet (enrichment or analysis pending).");
                return;
            }

            if (!res.InScope && res.ProposedChanges.Count == 0)
            {
                doc.Text(res.NoChangeReason ?? doc.Loc.T("No Armada change suggested: the failure is outside Armada's handoff and landing."), doc.Theme.Muted);
                return;
            }

            if (IsSuspected(entry)) doc.Text(doc.Loc.T("Suspected Armada defect: only the handoff and landing prior voted ArmadaDefect."), doc.Theme.Warning);
            doc.Field("Scope", res.InScope ? doc.Loc.T("Armada handoff and landing") + (String.IsNullOrEmpty(res.ScopeReason) ? "" : " (" + res.ScopeReason + ")") : doc.Loc.T("Outside handoff and landing"));
            doc.Field("Self-healable", res.SelfHealable.ToString(), res.SelfHealable == BlackBoxSelfHealableEnum.Yes ? doc.Theme.Warning : null);
            if (!String.IsNullOrEmpty(res.SelfHealingProposal)) doc.Text(doc.Loc.T("Armada could have") + " " + res.SelfHealingProposal!.TrimEnd('.') + ".");
            if (!String.IsNullOrEmpty(res.DetectionGap)) doc.Field("Detection gap", res.DetectionGapKind + ": " + res.DetectionGap);
            if (!String.IsNullOrEmpty(res.Invariant)) doc.Field("Invariant", res.Invariant);
            doc.Field("Severity", res.Severity + "  " + Recurrence(res), res.Severity == BlackBoxSeverityEnum.Critical || res.Severity == BlackBoxSeverityEnum.High ? doc.Theme.Error : null);
            if (res.AttemptedRecoveries.Count > 0) doc.Field("Already tried", String.Join("; ", res.AttemptedRecoveries));
            if (res.CodePointers.Count > 0)
            {
                string version = res.ArmadaVersion ?? entry.ArmadaVersion ?? "unknown";
                string? commit = res.ArmadaCommit ?? entry.ArmadaCommit;
                doc.Field("Code", "Armada " + version + (commit != null ? " commit " + commit : ""));
                foreach (string p in res.CodePointers) doc.Text("   " + p, doc.Theme.Info);
            }

            if (res.RegressionTest != null) doc.Field("Regression test", res.RegressionTest.Suite + ": " + res.RegressionTest.Description);
            if (res.ProposedChanges.Count > 0)
            {
                doc.Text(doc.Loc.T("Proposed Armada changes") + (draftKey != null ? "  (" + draftKey + " " + doc.Loc.T("Draft Armada fix mission") + ")" : ""), doc.Theme.Accent);
                foreach (BlackBoxProposedChange c in res.ProposedChanges.OrderBy(c => c.Rank))
                {
                    doc.Text(c.Rank.ToString(CultureInfo.InvariantCulture) + ". " + c.Title + "  [" + c.Severity + "]");
                    if (!String.IsNullOrEmpty(c.AcceptanceTest)) doc.Text("   " + doc.Loc.T("Acceptance test") + ": " + c.AcceptanceTest, doc.Theme.Muted);
                }
            }
        }

        /// <summary>
        /// The recurrence behind a severity: occurrences in 30 days and Armada versions.
        /// </summary>
        /// <param name="res">Resilience section.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when res is null.</exception>
        public static string Recurrence(BlackBoxResilience res)
        {
            if (res == null) throw new ArgumentNullException(nameof(res));
            return "(seen " + res.Occurrences30Days.ToString(CultureInfo.InvariantCulture) + (res.Occurrences30Days == 1 ? " time" : " times") + " in 30 days"
                + (res.ArmadaVersionCount > 1 ? ", in " + res.ArmadaVersionCount.ToString(CultureInfo.InvariantCulture) + " Armada versions" : "") + ")";
        }

        #endregion
    }
}
