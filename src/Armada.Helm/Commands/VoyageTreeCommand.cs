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
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// <c>armada voyage tree &lt;voyage&gt;</c>: the voyage's logical missions as an indented tree, each with its
    /// pipeline stages.
    /// </summary>
    [Description("Show a voyage's missions as a tree")]
    public class VoyageTreeCommand : BlackBoxCommandBase<VoyageShowSettings>
    {
        /// <inheritdoc />
        protected override string NotFoundHint
        {
            get { return "No such voyage. List voyages with: armada voyage list"; }
        }

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageShowSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(settings.Json, async client =>
            {
                string id = settings.Id;
                if (!id.StartsWith("vyg_", StringComparison.Ordinal))
                {
                    EnumerationResult<Voyage>? voyages = await client.ListVoyagesAsync(null, cancellationToken).ConfigureAwait(false);
                    Voyage? match = voyages?.Objects.FirstOrDefault(v => String.Equals(v.Title, id, StringComparison.OrdinalIgnoreCase));
                    if (match == null) return BlackBoxCli.NotFoundExitCode;
                    id = match.Id;
                }
                VoyageTree? tree = await client.GetVoyageTreeAsync(id, cancellationToken).ConfigureAwait(false);
                if (settings.Json)
                {
                    WritePureJson(tree);
                    return 0;
                }
                if (tree == null) return BlackBoxCli.NotFoundExitCode;

                Tree root = new Tree("[bold dodgerblue1]" + Markup.Escape(tree.Title) + "[/] [dim]" + Markup.Escape(tree.VoyageId) + " · "
                    + tree.TotalMissions + " mission" + (tree.TotalMissions == 1 ? "" : "s") + "[/]");
                root.Guide = TreeGuide.Line;
                foreach (MissionTreeNode node in tree.Roots) Add(root.AddNode(Label(node)), node);
                if (tree.Roots.Count == 0) root.AddNode("[dim]No missions yet.[/]");
                AnsiConsole.Write(root);
                return 0;
            }).ConfigureAwait(false);
        }

        private static void Add(TreeNode parent, MissionTreeNode node)
        {
            foreach (MissionTreeNode child in node.Children) Add(parent.AddNode(Label(child)), child);
        }

        private static string Label(MissionTreeNode node)
        {
            string label = Status(node.Mission.Status) + " " + Markup.Escape(node.Mission.Title) + " [dim]" + Markup.Escape(node.Mission.Id) + "[/]";
            if (node.Stages.Count > 0)
                label += " [dim]→[/] " + String.Join(" [dim]→[/] ", node.Stages.Select(s => Status(s.Status) + " " + Markup.Escape(s.Persona ?? s.Title)));
            return label;
        }

        private static string Status(MissionStatusEnum status)
        {
            string color = status switch
            {
                MissionStatusEnum.Complete => "green",
                MissionStatusEnum.Failed or MissionStatusEnum.LandingFailed => "red",
                MissionStatusEnum.Cancelled => "grey",
                MissionStatusEnum.Pending => "dodgerblue1",
                _ => "gold1"
            };
            return "[" + color + "]" + status + "[/]";
        }
    }
}
