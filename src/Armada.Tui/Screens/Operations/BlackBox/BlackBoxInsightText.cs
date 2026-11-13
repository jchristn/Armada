namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Plain text for the black box prevention and forensics views (plan section 13): dispatch overlap warnings and
    /// brief lint findings, the overlap preview, an attempt diff, recovery statistics and the voyage cascade tree.
    /// Pure functions, so the screens and the tests share them.
    /// </summary>
    public static class BlackBoxInsightText
    {
        #region Public-Methods

        /// <summary>
        /// The dispatch warnings of a create response (overlaps first, then brief lint), or null when there are none.
        /// </summary>
        /// <param name="overlaps">Overlap warnings, or null.</param>
        /// <param name="lint">Brief lint findings, or null.</param>
        /// <returns>Text, or null.</returns>
        public static string? DispatchWarnings(IReadOnlyList<MissionOverlapWarning>? overlaps, IReadOnlyList<BriefLintFinding>? lint)
        {
            int o = overlaps?.Count ?? 0;
            int l = lint?.Count ?? 0;
            if (o == 0 && l == 0) return null;
            List<string> lines = new List<string>();
            lines.Add("The dispatch went ahead. Black box prevention found:");
            if (o > 0)
            {
                lines.Add("");
                lines.Add("Overlap warnings (" + o.ToString(CultureInfo.InvariantCulture) + ")");
                foreach (MissionOverlapWarning w in overlaps!) lines.Add(Overlap(w));
            }

            if (l > 0)
            {
                lines.Add("");
                lines.Add("Brief lint (" + l.ToString(CultureInfo.InvariantCulture) + ")");
                foreach (BriefLintFinding f in lint!) lines.AddRange(Lint(f));
            }

            return String.Join("\n", lines);
        }

        /// <summary>
        /// One overlap warning as a line.
        /// </summary>
        /// <param name="w">Warning.</param>
        /// <returns>Line.</returns>
        public static string Overlap(MissionOverlapWarning w)
        {
            if (w == null) return "";
            List<string> parts = new List<string>();
            parts.Add("- [" + w.Kind + "]");
            if (!String.IsNullOrEmpty(w.OverlappingMissionId))
                parts.Add(w.OverlappingMissionId + (String.IsNullOrEmpty(w.OverlappingMissionTitle) ? "" : " \"" + w.OverlappingMissionTitle + "\"") + (String.IsNullOrEmpty(w.OverlappingMissionStatus) ? "" : " (" + w.OverlappingMissionStatus + ")"));
            if (!String.IsNullOrEmpty(w.Directory)) parts.Add("dir " + w.Directory);
            if (w.FileCount > 0) parts.Add(w.FileCount.ToString(CultureInfo.InvariantCulture) + " file(s)");
            if (w.HotspotEntryCount > 0) parts.Add(w.HotspotEntryCount.ToString(CultureInfo.InvariantCulture) + " past failure(s)");
            if (w.Serialized) parts.Add("chained after it");
            string head = String.Join(" ", parts);
            return String.IsNullOrEmpty(w.Message) ? head : head + ": " + w.Message;
        }

        /// <summary>
        /// One brief lint finding as lines (the finding, then its suggestion).
        /// </summary>
        /// <param name="f">Finding.</param>
        /// <returns>Lines.</returns>
        public static List<string> Lint(BriefLintFinding f)
        {
            List<string> lines = new List<string>();
            if (f == null) return lines;
            string where = !String.IsNullOrEmpty(f.Path) ? " " + f.Path : !String.IsNullOrEmpty(f.Excerpt) ? " \"" + f.Excerpt + "\"" : "";
            lines.Add("- [" + f.Severity + "] " + f.RuleId + where + ": " + f.Message);
            if (!String.IsNullOrEmpty(f.Suggestion)) lines.Add("    " + f.Suggestion);
            return lines;
        }

        /// <summary>
        /// The overlap preview (and its brief lint) before a dispatch, or a lint result alone when no vessel is chosen.
        /// </summary>
        /// <param name="preview">Overlap preview, or null.</param>
        /// <param name="lint">Brief lint (used when the preview carries none), or null.</param>
        /// <returns>Text.</returns>
        public static string Preview(MissionOverlapPreview? preview, BriefLintResult? lint)
        {
            List<string> lines = new List<string>();
            lines.Add("Preview only: nothing was dispatched.");
            if (preview != null)
            {
                lines.Add("");
                lines.Add("Overlaps on " + preview.VesselId + ": " + preview.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s), "
                    + preview.LikelyFileCount.ToString(CultureInfo.InvariantCulture) + " likely file(s), " + preview.ActiveMissionsCompared.ToString(CultureInfo.InvariantCulture)
                    + " active mission(s) compared" + (preview.ActiveMissionsTruncated ? " (truncated)" : ""));
                if (preview.Warnings.Count == 0) lines.Add("No overlaps with active missions, hotspots or reservations.");
                foreach (MissionOverlapWarning w in preview.Warnings) lines.Add(Overlap(w));
                if (!String.IsNullOrEmpty(preview.SuggestedDependsOnMissionId))
                {
                    lines.Add(preview.SerializeOverlappingMissions
                        ? "The vessel serializes overlapping missions: dispatch would chain this mission after " + preview.SuggestedDependsOnMissionId + "."
                        : "Consider depending on " + preview.SuggestedDependsOnMissionId + " (or turn on Serialize Overlapping Missions for the vessel).");
                }

                if (preview.LikelyFiles.Count > 0) lines.Add("Likely files: " + String.Join(", ", preview.LikelyFiles.Take(12)) + (preview.LikelyFiles.Count > 12 ? ", ..." : ""));
            }

            BriefLintResult? l = preview?.BriefLint ?? lint;
            if (l != null)
            {
                lines.Add("");
                lines.Add(l.Clean ? "Brief lint: clean." : "Brief lint: " + l.FindingCount.ToString(CultureInfo.InvariantCulture) + " finding(s)" + (l.PathsChecked ? "" : " (paths not checked)"));
                foreach (BriefLintFinding f in l.Findings) lines.AddRange(Lint(f));
            }

            return String.Join("\n", lines);
        }

        /// <summary>
        /// An attempt diff as lines.
        /// </summary>
        /// <param name="d">Diff.</param>
        /// <returns>Lines.</returns>
        public static List<string> Diff(BlackBoxAttemptDiff d)
        {
            List<string> lines = new List<string>();
            if (d == null) return lines;
            lines.Add(d.EntryId + " (attempt " + d.Attempt.ToString(CultureInfo.InvariantCulture) + ") vs " + d.OtherEntryId + " (attempt " + d.OtherAttempt.ToString(CultureInfo.InvariantCulture) + ")");
            lines.Add((d.SameMission ? "Same mission" : "Different missions") + ", " + (d.SameFingerprint ? "same fingerprint" : "different fingerprint") + ", " + d.DifferenceCount.ToString(CultureInfo.InvariantCulture) + " difference(s)");
            return lines;
        }

        /// <summary>
        /// One recovery statistic as a line.
        /// </summary>
        /// <param name="s">Statistic.</param>
        /// <returns>Line.</returns>
        public static string RecoveryStat(BlackBoxRecoveryStat s)
        {
            if (s == null) return "";
            string rate = s.SuccessRate.HasValue ? (s.SuccessRate.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "-";
            return (s.RuleId ?? "-") + "  " + s.PathId + "  " + rate + "  runs " + s.Runs.ToString(CultureInfo.InvariantCulture) + "  ok " + s.Succeeded.ToString(CultureInfo.InvariantCulture)
                + "  failed " + s.Failed.ToString(CultureInfo.InvariantCulture) + "  pending " + s.Pending.ToString(CultureInfo.InvariantCulture) + "  expired " + s.Expired.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The cascade graph's nodes in tree order: each root (no incoming edge from a node in the graph, the cascade
        /// root first) followed by the nodes it took down, indented by depth. Every node appears once; a cycle stops at
        /// the first repeat.
        /// </summary>
        /// <param name="graph">Graph.</param>
        /// <returns>Rows (depth and node).</returns>
        public static List<BlackBoxCascadeRow> CascadeRows(BlackBoxCascadeGraph graph)
        {
            List<BlackBoxCascadeRow> rows = new List<BlackBoxCascadeRow>();
            if (graph == null) return rows;
            Dictionary<string, BlackBoxCascadeNode> nodes = new Dictionary<string, BlackBoxCascadeNode>(StringComparer.Ordinal);
            foreach (BlackBoxCascadeNode n in graph.Nodes)
            {
                if (!nodes.ContainsKey(n.MissionId)) nodes[n.MissionId] = n;
            }

            List<BlackBoxCascadeEdge> edges = graph.Edges.Where(e => e.Kind != BlackBoxCascadeEdgeKindEnum.Sibling && nodes.ContainsKey(e.FromMissionId) && nodes.ContainsKey(e.ToMissionId) && e.FromMissionId != e.ToMissionId).ToList();
            HashSet<string> hasParent = new HashSet<string>(edges.Select(e => e.ToMissionId), StringComparer.Ordinal);
            List<BlackBoxCascadeNode> roots = graph.Nodes.Where(n => !hasParent.Contains(n.MissionId)).OrderByDescending(n => n.IsRoot || n.MissionId == graph.RootMissionId).ToList();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BlackBoxCascadeNode r in roots) Walk(r, null, 0, nodes, edges, seen, rows);
            foreach (BlackBoxCascadeNode n in graph.Nodes) Walk(n, null, 0, nodes, edges, seen, rows);
            return rows;
        }

        /// <summary>
        /// The TUI route of an entity id by its prefix (mission, voyage, black box entry, dock, merge entry, captain,
        /// vessel, event, signal), or null when the TUI has no screen for it.
        /// </summary>
        /// <param name="id">Id, or null.</param>
        /// <returns>Route, or null.</returns>
        public static string? EntityRoute(string? id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            string[][] map = new string[][]
            {
                new[] { "msn_", "/missions/" }, new[] { "vyg_", "/voyages/" }, new[] { "bbx_", "/blackbox/" }, new[] { "dck_", "/docks/" },
                new[] { "mrg_", "/merge-queue/" }, new[] { "cpt_", "/captains/" }, new[] { "vsl_", "/vessels/" }, new[] { "evt_", "/events/" }, new[] { "sig_", "/signals/" }
            };
            foreach (string[] m in map)
            {
                if (id.StartsWith(m[0], StringComparison.Ordinal)) return m[1] + Uri.EscapeDataString(id);
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static void Walk(BlackBoxCascadeNode node, BlackBoxCascadeEdge? via, int depth, Dictionary<string, BlackBoxCascadeNode> nodes, List<BlackBoxCascadeEdge> edges, HashSet<string> seen, List<BlackBoxCascadeRow> rows)
        {
            if (!seen.Add(node.MissionId)) return;
            rows.Add(new BlackBoxCascadeRow(depth, node, via));
            foreach (BlackBoxCascadeEdge e in edges.Where(e => e.FromMissionId == node.MissionId))
            {
                Walk(nodes[e.ToMissionId], e, depth + 1, nodes, edges, seen, rows);
            }
        }

        #endregion
    }
}
