namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Ids that records data expiry never deletes still point at, so data expiry keeps them: live slot and session
    /// ownership, open wait intervals, deployments and incidents, and black box entries and successes that have not
    /// expired. Missions, voyages and docks that remain after a pass add to this through the service's own checks.
    /// </summary>
    internal sealed class DataExpiryReferences
    {
        #region Internal-Members

        /// <summary>
        /// Mission ids that must not be deleted.
        /// </summary>
        internal HashSet<string> MissionIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Voyage ids that must not be deleted.
        /// </summary>
        internal HashSet<string> VoyageIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Dock ids that must not be deleted.
        /// </summary>
        internal HashSet<string> DockIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Merge entry ids that must not be deleted.
        /// </summary>
        internal HashSet<string> MergeEntryIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Add an id to a set when it is not null or empty.
        /// </summary>
        /// <param name="set">Target set.</param>
        /// <param name="id">Id, or null.</param>
        internal static void AddId(HashSet<string> set, string? id)
        {
            if (!String.IsNullOrEmpty(id)) set.Add(id);
        }

        #endregion
    }
}
