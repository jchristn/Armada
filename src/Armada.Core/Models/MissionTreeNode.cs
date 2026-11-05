namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// One logical mission in a voyage tree: its root mission, the pipeline stages that follow the root, and the
    /// logical child missions whose parent is this mission.
    /// </summary>
    public class MissionTreeNode
    {
        /// <summary>The logical mission's root (first stage).</summary>
        public MissionSummary Mission { get; set; } = new MissionSummary();

        /// <summary>Pipeline stages after the root, in order.</summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds, Reason = "The tree holds at most 1000 missions; VoyageTree.Truncated says when more exist")]
        public List<MissionSummary> Stages { get; set; } = new List<MissionSummary>();

        /// <summary>Child logical missions, in creation order.</summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds, Reason = "The tree holds at most 1000 missions; VoyageTree.Truncated says when more exist")]
        public List<MissionTreeNode> Children { get; set; } = new List<MissionTreeNode>();
    }
}
