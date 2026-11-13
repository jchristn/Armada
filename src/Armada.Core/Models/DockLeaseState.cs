namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Who still needs a dock's worktree. A dock is leased while a mission that references it is between producing work
    /// and landing it (or waiting for review, a pull request or a landing retry), or while a merge queue entry for such a
    /// mission is queued or testing. A leased dock is not reclaimed unless the caller gives an explicit force reason.
    /// </summary>
    public class DockLeaseState
    {
        #region Public-Members

        /// <summary>
        /// Mission statuses that hold a lease on the mission's dock.
        /// </summary>
        public static IReadOnlyList<MissionStatusEnum> LeaseStatuses { get; } = new List<MissionStatusEnum>
        {
            MissionStatusEnum.WorkProduced,
            MissionStatusEnum.Testing,
            MissionStatusEnum.Review,
            MissionStatusEnum.Verifying,
            MissionStatusEnum.PullRequestOpen,
            MissionStatusEnum.LandingFailed
        };

        /// <summary>
        /// Missions whose DockId is this dock, most recently updated first.
        /// </summary>
        public List<MissionSummary> Missions { get; set; } = new List<MissionSummary>();

        /// <summary>
        /// The mission holding the lease, or null.
        /// </summary>
        public MissionSummary? HoldingMission { get; set; } = null;

        /// <summary>
        /// The merge queue entry holding the lease, or null.
        /// </summary>
        public MergeEntry? HoldingEntry { get; set; } = null;

        /// <summary>
        /// Whether the dock is leased.
        /// </summary>
        public bool IsHeld
        {
            get { return HoldingMission != null || HoldingEntry != null; }
        }

        /// <summary>
        /// The mission most closely tied to the dock: the lease holder, else the most recently updated reference.
        /// </summary>
        public MissionSummary? PrimaryMission
        {
            get { return HoldingMission ?? (Missions.Count > 0 ? Missions[0] : null); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a mission status holds a lease on its dock.
        /// </summary>
        /// <param name="status">Mission status.</param>
        /// <returns>True for a lease-holding status.</returns>
        public static bool IsLeaseStatus(MissionStatusEnum status)
        {
            foreach (MissionStatusEnum leased in LeaseStatuses)
            {
                if (leased == status) return true;
            }
            return false;
        }

        /// <summary>
        /// Describe what holds the lease.
        /// </summary>
        /// <returns>A sentence naming the holder, or an empty string when the dock is not leased.</returns>
        public string Describe()
        {
            if (HoldingMission != null)
                return "mission " + HoldingMission.Id + " is " + HoldingMission.Status;
            if (HoldingEntry != null)
                return "merge queue entry " + HoldingEntry.Id + " for mission " + HoldingEntry.MissionId + " is " + HoldingEntry.Status;
            return String.Empty;
        }

        #endregion
    }
}
