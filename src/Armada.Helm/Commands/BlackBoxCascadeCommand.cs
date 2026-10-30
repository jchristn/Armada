namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox cascade &lt;voyageId&gt;</c> (and <c>armada voyage cascade</c>): the voyage's cascade graph as a
    /// tree from the root failure (which failure took the others down), each mission with its status, failure kind,
    /// the edge that links it and its black box entry. Sibling failures are listed as their own roots.
    /// </summary>
    [Description("Show which failure in a voyage took the others down")]
    public class BlackBoxCascadeCommand : BlackBoxCommandBase<BlackBoxCascadeSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such voyage, or it has no black box failures. List voyages with: armada voyage list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxCascadeSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string voyageId = (settings.VoyageId ?? "").Trim();
            if (voyageId.Length == 0)
            {
                Status("Error: a voyage id is required.");
                return 1;
            }

            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                BlackBoxCascadeGraph? graph = await client.GetVoyageBlackBoxCascadeAsync(voyageId, cancellationToken).ConfigureAwait(false);
                if (graph == null)
                {
                    Status("Error: no cascade for " + voyageId + ".");
                    return BlackBoxCli.NotFoundExitCode;
                }

                if (settings.Json)
                {
                    WritePureJson(graph);
                    return 0;
                }

                Render(graph);
                return 0;
            }).ConfigureAwait(false);
        }

        private static void Render(BlackBoxCascadeGraph graph)
        {
            Tree tree = new Tree("[dodgerblue1]" + Markup.Escape(String.IsNullOrEmpty(graph.VoyageTitle) ? graph.VoyageId : graph.VoyageTitle + " (" + graph.VoyageId + ")") + "[/]  " + Markup.Escape(graph.VoyageStatus));
            Dictionary<string, BlackBoxCascadeNode> nodes = new Dictionary<string, BlackBoxCascadeNode>(StringComparer.Ordinal);
            foreach (BlackBoxCascadeNode n in graph.Nodes)
            {
                if (!nodes.ContainsKey(n.MissionId)) nodes[n.MissionId] = n;
            }

            List<BlackBoxCascadeEdge> edges = graph.Edges.Where(e => e.Kind != BlackBoxCascadeEdgeKindEnum.Sibling && nodes.ContainsKey(e.FromMissionId) && nodes.ContainsKey(e.ToMissionId) && e.FromMissionId != e.ToMissionId).ToList();
            HashSet<string> hasParent = new HashSet<string>(edges.Select(e => e.ToMissionId), StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<BlackBoxCascadeNode> roots = graph.Nodes.Where(n => !hasParent.Contains(n.MissionId)).OrderByDescending(n => n.IsRoot || n.MissionId == graph.RootMissionId).Concat(graph.Nodes);
            foreach (BlackBoxCascadeNode root in roots)
            {
                if (seen.Contains(root.MissionId)) continue;
                seen.Add(root.MissionId);
                TreeNode branch = tree.AddNode(Label(root, null));
                AddChildren(branch, root, nodes, edges, seen);
            }

            AnsiConsole.Write(tree);
            if (graph.Truncated) AnsiConsole.MarkupLine("[dim]The graph shows the first " + graph.Nodes.Count + " of " + graph.MissionCount + " missions.[/]");
            if (!String.IsNullOrEmpty(graph.RootEntryId)) AnsiConsole.MarkupLine("[dim]Root failure:[/] armada blackbox show " + Markup.Escape(graph.RootEntryId!));
        }

        private static void AddChildren(TreeNode parent, BlackBoxCascadeNode node, Dictionary<string, BlackBoxCascadeNode> nodes, List<BlackBoxCascadeEdge> edges, HashSet<string> seen)
        {
            foreach (BlackBoxCascadeEdge e in edges.Where(e => e.FromMissionId == node.MissionId))
            {
                if (!seen.Add(e.ToMissionId)) continue;
                BlackBoxCascadeNode child = nodes[e.ToMissionId];
                TreeNode branch = parent.AddNode(Label(child, e));
                AddChildren(branch, child, nodes, edges, seen);
            }
        }

        private static string Label(BlackBoxCascadeNode n, BlackBoxCascadeEdge? via)
        {
            string label = (n.IsRoot ? "[red]root[/] " : "") + (via != null ? "[dim]" + Markup.Escape(via.Kind.ToString()) + "[/] " : "")
                + "[green]" + Markup.Escape(n.MissionId) + "[/]" + (String.IsNullOrEmpty(n.Title) ? "" : " " + Markup.Escape(n.Title!))
                + "  [bold]" + Markup.Escape(n.Status) + "[/]" + (String.IsNullOrEmpty(n.FailureKind) ? "" : " (" + Markup.Escape(n.FailureKind!) + ")");
            if (!String.IsNullOrEmpty(n.EntryId)) label += "  [dim]" + Markup.Escape(n.EntryId!) + "[/]";
            return label;
        }
    }
}
