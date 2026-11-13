namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Text for the voyage mission tree: flattening, titles, pipeline and branch labels.</summary>
    public static class MissionTreeText
    {
        private static readonly Regex _PersonaSuffix = new Regex(@"\s*\[([^\]]+)\]\s*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        /// <summary>Depth-first rows, children under their parent and the integration mission last at its level.</summary>
        /// <param name="roots">Top-level nodes.</param>
        /// <returns>Rows in display order.</returns>
        public static List<MissionTreeRow> Rows(IEnumerable<MissionTreeNode> roots)
        {
            List<MissionTreeRow> rows = new List<MissionTreeRow>();
            void Visit(MissionTreeNode node, int depth)
            {
                rows.Add(new MissionTreeRow(depth, node.Mission, node.Stages));
                foreach (MissionTreeNode child in Ordered(node.Children)) Visit(child, depth + 1);
            }
            foreach (MissionTreeNode root in Ordered(roots ?? Enumerable.Empty<MissionTreeNode>())) Visit(root, 0);
            return rows;
        }

        /// <summary>"Add the table [Worker]" reads "Add the table".</summary>
        /// <param name="title">Mission title.</param>
        /// <returns>Title without a trailing persona tag.</returns>
        public static string BaseTitle(string? title)
        {
            string value = title ?? String.Empty;
            string trimmed = _PersonaSuffix.Replace(value, String.Empty);
            return trimmed.Length == 0 ? value : trimmed;
        }

        /// <summary>The pipeline as "Worker: Complete > Judge: InProgress".</summary>
        /// <param name="row">Row.</param>
        /// <returns>Pipeline text, or "-" for a single-stage mission without a persona tag.</returns>
        public static string Pipeline(MissionTreeRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            List<MissionSummary> chain = new List<MissionSummary> { row.Mission };
            chain.AddRange(row.Stages);
            if (row.Stages.Count == 0 && !_PersonaSuffix.IsMatch(row.Mission.Title ?? String.Empty)) return "-";
            return String.Join(" > ", chain.Select(m => Stage(m) + ": " + m.Status));
        }

        /// <summary>The branch relationship to the parent: "continues parent", "forks from parent", "integration" or "-".</summary>
        /// <param name="mission">Root mission.</param>
        /// <returns>Label.</returns>
        public static string Branch(MissionSummary mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.WaitForVoyageWorkers) return "integration";
            if (String.IsNullOrEmpty(mission.ParentMissionId)) return "-";
            return mission.BranchStrategy switch
            {
                MissionBranchStrategyEnum.ContinueParent => "continues parent",
                MissionBranchStrategyEnum.ForkFromParent => "forks from parent",
                _ => "child"
            };
        }

        /// <summary>Logical missions running now (any stage Assigned or InProgress).</summary>
        /// <param name="rows">Rows.</param>
        /// <returns>Count.</returns>
        public static int Running(IEnumerable<MissionTreeRow> rows)
        {
            return (rows ?? Enumerable.Empty<MissionTreeRow>()).Count(r => new[] { r.Mission }.Concat(r.Stages)
                .Any(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress));
        }

        private static string Stage(MissionSummary mission)
        {
            Match match = _PersonaSuffix.Match(mission.Title ?? String.Empty);
            return match.Success ? match.Groups[1].Value : String.IsNullOrEmpty(mission.Persona) ? "Stage" : mission.Persona!;
        }

        private static IEnumerable<MissionTreeNode> Ordered(IEnumerable<MissionTreeNode> nodes)
        {
            List<MissionTreeNode> list = nodes.ToList();
            return list.Where(n => !n.Mission.WaitForVoyageWorkers).Concat(list.Where(n => n.Mission.WaitForVoyageWorkers));
        }
    }
}
