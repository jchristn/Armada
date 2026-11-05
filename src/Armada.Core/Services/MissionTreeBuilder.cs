namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Arranges missions into logical missions and their children. A mission with no upstream in the set is a
    /// logical root; missions reached from it by <c>DependsOnMissionId</c> are its pipeline stages (D14). A root's
    /// <c>ParentMissionId</c> places it under the logical mission that owns that mission, stage references included.
    /// </summary>
    public static class MissionTreeBuilder
    {
        /// <summary>Build the forest of top-level logical missions.</summary>
        /// <param name="missions">Missions of one voyage (or one vessel's standalone missions).</param>
        /// <returns>Top-level nodes in creation order.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="missions"/> is null.</exception>
        public static List<MissionTreeNode> Build(IEnumerable<MissionSummary> missions)
        {
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            List<MissionSummary> ordered = missions.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            Dictionary<string, MissionSummary> byId = new Dictionary<string, MissionSummary>(StringComparer.Ordinal);
            foreach (MissionSummary mission in ordered) byId[mission.Id] = mission;

            Dictionary<string, string> rootOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (MissionSummary mission in ordered) rootOf[mission.Id] = RootOf(mission, byId);

            Dictionary<string, MissionTreeNode> nodes = new Dictionary<string, MissionTreeNode>(StringComparer.Ordinal);
            foreach (MissionSummary mission in ordered)
                if (rootOf[mission.Id] == mission.Id) nodes[mission.Id] = new MissionTreeNode { Mission = mission };
            foreach (MissionSummary mission in ordered)
                if (rootOf[mission.Id] != mission.Id) nodes[rootOf[mission.Id]].Stages.Add(mission);
            foreach (MissionTreeNode node in nodes.Values)
                node.Stages = OrderStages(node.Mission, node.Stages);

            List<MissionTreeNode> top = new List<MissionTreeNode>();
            foreach (MissionTreeNode node in nodes.Values)
            {
                string? parentId = node.Mission.ParentMissionId;
                MissionTreeNode? parent = null;
                if (!String.IsNullOrEmpty(parentId) && rootOf.TryGetValue(parentId!, out string? parentRoot)) nodes.TryGetValue(parentRoot, out parent);
                if (parent != null && parent != node && !IsAncestor(node, parent)) parent.Children.Add(node);
                else top.Add(node);
            }
            return top;
        }

        /// <summary>Build a voyage's tree from its missions, holding at most the 1000 oldest.</summary>
        /// <param name="voyage">The voyage.</param>
        /// <param name="missions">The voyage's missions.</param>
        /// <returns>The tree; <see cref="VoyageTree.Truncated"/> is true when missions were left out.</returns>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        public static VoyageTree BuildVoyageTree(Voyage voyage, List<MissionSummary> missions)
        {
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            List<MissionSummary> held = missions.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal)
                .Take(CollectionLimits.MaxLinkedIds).ToList();
            List<MissionTreeNode> roots = Build(held);
            return new VoyageTree
            {
                VoyageId = voyage.Id, Title = voyage.Title, TotalMissions = missions.Count,
                Truncated = missions.Count > held.Count, MaxDepth = MaxDepth(roots), Roots = roots
            };
        }

        /// <summary>Deepest node level in a forest, 0 for a flat list.</summary>
        /// <param name="roots">Top-level nodes.</param>
        /// <returns>Depth of the deepest node.</returns>
        public static int MaxDepth(IEnumerable<MissionTreeNode> roots)
        {
            int max = 0;
            Stack<KeyValuePair<MissionTreeNode, int>> pending = new Stack<KeyValuePair<MissionTreeNode, int>>();
            foreach (MissionTreeNode root in roots ?? Enumerable.Empty<MissionTreeNode>()) pending.Push(new KeyValuePair<MissionTreeNode, int>(root, 0));
            while (pending.Count > 0)
            {
                KeyValuePair<MissionTreeNode, int> item = pending.Pop();
                max = Math.Max(max, item.Value);
                foreach (MissionTreeNode child in item.Key.Children) pending.Push(new KeyValuePair<MissionTreeNode, int>(child, item.Value + 1));
            }
            return max;
        }

        private static string RootOf(MissionSummary mission, Dictionary<string, MissionSummary> byId)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { mission.Id };
            MissionSummary current = mission;
            while (!String.IsNullOrEmpty(current.DependsOnMissionId)
                && byId.TryGetValue(current.DependsOnMissionId!, out MissionSummary? upstream) && seen.Add(upstream.Id))
                current = upstream;
            return current.Id;
        }

        // Follow DependsOn links from the root; stages not on the chain (branching dependents) keep creation order.
        private static List<MissionSummary> OrderStages(MissionSummary root, List<MissionSummary> stages)
        {
            List<MissionSummary> result = new List<MissionSummary>();
            HashSet<string> placed = new HashSet<string>(StringComparer.Ordinal);
            string current = root.Id;
            while (true)
            {
                MissionSummary? next = stages.FirstOrDefault(s => s.DependsOnMissionId == current && !placed.Contains(s.Id));
                if (next == null) break;
                result.Add(next); placed.Add(next.Id); current = next.Id;
            }
            result.AddRange(stages.Where(s => !placed.Contains(s.Id)));
            return result;
        }

        private static bool IsAncestor(MissionTreeNode candidate, MissionTreeNode node)
        {
            Stack<MissionTreeNode> pending = new Stack<MissionTreeNode>(candidate.Children);
            while (pending.Count > 0)
            {
                MissionTreeNode current = pending.Pop();
                if (current == node) return true;
                foreach (MissionTreeNode child in current.Children) pending.Push(child);
            }
            return false;
        }
    }
}
