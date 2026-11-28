namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The one hierarchy and ownership check for a plan of missions (RS-4.4), whatever its source: a REST or MCP voyage,
    /// or an Architect plan in any format. Pure: it reads only its arguments. Each rule has one outcome everywhere; the
    /// only difference by source is whether an ownership overlap rejects the plan (Architect contract v4) or warns.
    /// </summary>
    public static class MissionPlanValidator
    {
        /// <summary>Validate a plan.</summary>
        /// <param name="nodes">Missions in plan order.</param>
        /// <param name="maxDepth">Deepest allowed child (a root is depth 0), Concurrency.MaxHierarchyDepth.</param>
        /// <param name="overlapIsError">True when an ownership overlap between parallel missions rejects the plan.</param>
        /// <returns>The errors, warnings, and the resolved parents and depths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="nodes"/> is null.</exception>
        public static PlanValidationResult Validate(IReadOnlyList<PlanNode> nodes, int maxDepth, bool overlapIsError)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            PlanValidationResult result = new PlanValidationResult();
            int count = nodes.Count;
            int[] parents = new int[count + 1];
            int[] depths = new int[count + 1];
            Dictionary<string, int> keys = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int position = 1; position <= count; position++)
            {
                PlanNode node = nodes[position - 1];
                string prefix = "mission " + position + (String.IsNullOrWhiteSpace(node.Title) ? "" : " ('" + node.Title!.Trim() + "')") + ": ";
                int? parent = ResolveParent(node, position, prefix, keys, result);
                if (parent.HasValue)
                {
                    if (parent.Value == position) Add(result.Errors, PlanFindingCodeEnum.ParentSelf, position, prefix + "parent cannot refer to the mission itself");
                    else if (parent.Value < 1 || parent.Value > position) Add(result.Errors, PlanFindingCodeEnum.ParentNotEarlier, position, prefix + "parent must name an earlier mission");
                    else
                    {
                        parents[position] = parent.Value;
                        depths[position] = depths[parent.Value] + 1;
                        if (depths[position] > maxDepth)
                            Add(result.Errors, PlanFindingCodeEnum.DepthExceeded, position, prefix + "parent chain is " + depths[position] + " deep; the maximum is " + maxDepth
                                + " (Concurrency.MaxHierarchyDepth). Flatten the plan");
                    }
                }

                if (node.BranchStrategy != null && node.BranchStrategy != "continue" && node.BranchStrategy != "fork")
                    Add(result.Errors, PlanFindingCodeEnum.BranchStrategyInvalid, position, prefix + "branchStrategy must be continue or fork");
                else if (node.BranchStrategy != null && !parent.HasValue && node.UnresolvedParent == null && node.ParentKey == null)
                    Add(result.Errors, PlanFindingCodeEnum.BranchStrategyNeedsParent, position, prefix + "branchStrategy needs a parent");

                if (!String.IsNullOrWhiteSpace(node.Key) && !keys.TryAdd(node.Key!.Trim(), position))
                    Add(result.Errors, PlanFindingCodeEnum.DuplicateKey, position, prefix + "key '" + node.Key!.Trim() + "' is used by more than one mission");
            }

            result.Parents = parents;
            result.Depths = depths;
            if (result.IsValid) CheckOverlaps(nodes, parents, overlapIsError ? result.Errors : result.Warnings);
            return result;
        }

        private static int? ResolveParent(PlanNode node, int position, string prefix, Dictionary<string, int> keys, PlanValidationResult result)
        {
            if (node.UnresolvedParent != null)
            {
                Add(result.Errors, PlanFindingCodeEnum.UnresolvedParent, position, prefix + "parent '" + node.UnresolvedParent + "' names no mission in this plan; use an earlier mission's number");
                return null;
            }
            if (!String.IsNullOrWhiteSpace(node.ParentKey))
            {
                string parentKey = node.ParentKey!.Trim();
                if (keys.TryGetValue(parentKey, out int keyed)) return keyed;
                Add(result.Errors, PlanFindingCodeEnum.UnknownParentKey, position, prefix + "parentKey '" + parentKey + "' is not the key of an earlier mission in this dispatch");
                return null;
            }
            if (node.DependsOn.HasValue)
            {
                Add(result.Errors, PlanFindingCodeEnum.DependsOnRemoved, position, prefix + "dependsOn is no longer supported; write parent " + node.DependsOn.Value + " instead");
                return null;
            }
            return node.Parent;
        }

        // Two missions can run at the same time when neither is an ancestor of the other and neither waits for all others.
        private static void CheckOverlaps(IReadOnlyList<PlanNode> nodes, int[] parents, List<PlanFinding> target)
        {
            int count = nodes.Count;
            for (int i = 1; i <= count; i++)
                for (int j = i + 1; j <= count; j++)
                {
                    PlanNode a = nodes[i - 1], b = nodes[j - 1];
                    if (a.WaitsForAll || b.WaitsForAll) continue;
                    if (IsAncestor(parents, i, j) || IsAncestor(parents, j, i)) continue;
                    string? overlap = OwnedPathMatcher.FirstOverlap(a.OwnedPaths, b.OwnedPaths);
                    if (overlap != null)
                        Add(target, PlanFindingCodeEnum.OwnershipOverlap, j, "Missions " + i + " and " + j + " can run in parallel but both own " + overlap
                            + "; make one the parent of the other, merge them, or split ownership");
                }
        }

        private static bool IsAncestor(int[] parents, int ancestor, int position)
        {
            for (int hops = 0; hops < parents.Length && parents[position] > 0; hops++)
            {
                position = parents[position];
                if (position == ancestor) return true;
            }
            return false;
        }

        private static void Add(List<PlanFinding> target, PlanFindingCodeEnum code, int position, string message)
        {
            target.Add(new PlanFinding { Code = code, Position = position, Message = message });
        }
    }
}
