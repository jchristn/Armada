namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// One mission of a plan as <see cref="Services.MissionPlanValidator"/> sees it: its links and the paths it owns.
    /// Every dispatch path (REST and MCP voyages, Architect plans in every format) describes its missions this way.
    /// </summary>
    public sealed class PlanNode
    {
        /// <summary>Mission title, used in messages.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Key other missions of the same dispatch can name as their parent.</summary>
        public string? Key { get; set; } = null;

        /// <summary>Key of an earlier mission that is this one's parent.</summary>
        public string? ParentKey { get; set; } = null;

        /// <summary>1-based position of the parent.</summary>
        public int? Parent { get; set; } = null;

        /// <summary>1-based dependsOn position, read as the parent when no parent is given.</summary>
        public int? DependsOn { get; set; } = null;

        /// <summary>A text parent reference the caller could not map to a mission.</summary>
        public string? UnresolvedParent { get; set; } = null;

        /// <summary>Branch strategy: continue, fork, or null for the default rule.</summary>
        public string? BranchStrategy { get; set; } = null;

        /// <summary>Normalized paths the mission may change.</summary>
        public List<string> OwnedPaths { get; set; } = new List<string>();

        /// <summary>True when the mission waits for every other mission, so it never runs alongside one.</summary>
        public bool WaitsForAll { get; set; } = false;
    }
}
