namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>Result of the hierarchy parent gate for one mission.</summary>
    internal sealed class HierarchyReadiness
    {
        /// <summary>Why the mission waits, or null when its parent (if any) has finished.</summary>
        internal MissionAssignmentBlocker? Blocker { get; set; }

        /// <summary>The parent's terminal stage when it failed, was cancelled, or failed to land.</summary>
        internal Mission? FailedParent { get; set; }

        /// <summary>The parent's terminal stage, when the mission has a parent.</summary>
        internal Mission? ParentTerminal { get; set; }
    }
}
