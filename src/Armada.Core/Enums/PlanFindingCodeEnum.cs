namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>A rule of <see cref="Services.MissionPlanValidator"/> that a plan broke.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlanFindingCodeEnum
    {
        /// <summary>Two missions in one plan use the same key.</summary>
        DuplicateKey,
        /// <summary>A parent key does not name an earlier mission's key.</summary>
        UnknownParentKey,
        /// <summary>A text parent reference names no mission in the plan.</summary>
        UnresolvedParent,
        /// <summary>A mission names itself as its parent.</summary>
        ParentSelf,
        /// <summary>A parent is not an earlier mission in the plan.</summary>
        ParentNotEarlier,
        /// <summary>A mission uses dependsOn, which was removed (D-026); the plan must use parent.</summary>
        DependsOnRemoved,
        /// <summary>A parent chain is deeper than Concurrency.MaxHierarchyDepth.</summary>
        DepthExceeded,
        /// <summary>A branch strategy is neither continue nor fork.</summary>
        BranchStrategyInvalid,
        /// <summary>A branch strategy is given without a parent.</summary>
        BranchStrategyNeedsParent,
        /// <summary>Two missions that can run at the same time own the same path.</summary>
        OwnershipOverlap
    }
}
