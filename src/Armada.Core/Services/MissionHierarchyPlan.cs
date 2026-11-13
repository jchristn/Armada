namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Resolves the <see cref="MissionDescription.Key"/> and <see cref="MissionDescription.ParentKey"/> links of one
    /// dispatch into parent positions and depths, enforcing D17: unique keys, parents earlier in the list (so no cycle),
    /// and depth within <c>Concurrency.MaxHierarchyDepth</c>.
    /// </summary>
    public sealed class MissionHierarchyPlan
    {
        #region Private-Members

        private readonly Dictionary<int, int> _Parents = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _Depths = new Dictionary<int, int>();

        #endregion

        #region Constructors-and-Factories

        private MissionHierarchyPlan()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>Resolve and validate the hierarchy described by a dispatch.</summary>
        /// <param name="descriptions">Descriptions in plan order.</param>
        /// <param name="maxDepth">Deepest allowed child (a root is depth 0).</param>
        /// <returns>The resolved plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptions"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown for a duplicate key, an unknown or later parent key, or excess depth.</exception>
        public static MissionHierarchyPlan Resolve(IReadOnlyList<MissionDescription> descriptions, int maxDepth)
        {
            if (descriptions == null) throw new ArgumentNullException(nameof(descriptions));
            MissionHierarchyPlan plan = new MissionHierarchyPlan();
            Dictionary<string, int> keys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < descriptions.Count; index++)
            {
                MissionDescription md = descriptions[index];
                string? parentKey = String.IsNullOrWhiteSpace(md.ParentKey) ? null : md.ParentKey!.Trim();
                int depth = 0;
                if (parentKey != null)
                {
                    if (!keys.TryGetValue(parentKey, out int parentIndex))
                        throw new ArgumentException("Mission '" + md.Title + "' names ParentKey '" + parentKey + "', which is not the Key of an earlier mission in this dispatch.", nameof(descriptions));
                    depth = plan._Depths[parentIndex] + 1;
                    if (depth > maxDepth)
                        throw new ArgumentException("Mission '" + md.Title + "' would be at hierarchy depth " + depth + "; the maximum is " + maxDepth + " (Concurrency.MaxHierarchyDepth).", nameof(descriptions));
                    plan._Parents[index] = parentIndex;
                }
                plan._Depths[index] = depth;
                if (!String.IsNullOrWhiteSpace(md.Key))
                {
                    string key = md.Key!.Trim();
                    if (!keys.TryAdd(key, index))
                        throw new ArgumentException("Key '" + key + "' is used by more than one mission in this dispatch.", nameof(descriptions));
                }
            }
            return plan;
        }

        /// <summary>The position of a description's parent, or -1 for a root.</summary>
        /// <param name="index">Description position.</param>
        /// <returns>Parent position or -1.</returns>
        public int ParentOf(int index) => _Parents.TryGetValue(index, out int parent) ? parent : -1;

        /// <summary>Hierarchy depth of a description (0 for a root).</summary>
        /// <param name="index">Description position.</param>
        /// <returns>Depth.</returns>
        public int DepthOf(int index) => _Depths.TryGetValue(index, out int depth) ? depth : 0;

        #endregion
    }
}
