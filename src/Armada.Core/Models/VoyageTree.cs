namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>A voyage's missions arranged by parent and pipeline stage.</summary>
    public class VoyageTree
    {
        /// <summary>The voyage.</summary>
        public string VoyageId { get; set; } = String.Empty;

        /// <summary>Voyage title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>Number of missions in the voyage, stages included.</summary>
        public int TotalMissions { get; set; }

        /// <summary>True when the voyage has more missions than the tree holds (1000, oldest first).</summary>
        public bool Truncated { get; set; }

        /// <summary>The deepest logical mission depth present (0 when the voyage is flat).</summary>
        public int MaxDepth { get; set; }

        /// <summary>Top-level logical missions, in creation order.</summary>
        [CollectionBound(CollectionLimits.MaxLinkedIds, Reason = "The tree holds at most 1000 missions; VoyageTree.Truncated says when more exist")]
        public List<MissionTreeNode> Roots { get; set; } = new List<MissionTreeNode>();
    }
}
