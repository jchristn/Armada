namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The state each black box recovery action needs before it can do anything, checked on the dry run and again when
    /// the run is confirmed. A step whose precondition is not met is refused with the reason instead of being run and
    /// reported as a success: restarting a mission that is already Pending, retrying the landing of a mission that is no
    /// longer LandingFailed, cancelling a mission that already finished, or acting on a mission, vessel or dock that no
    /// longer exists.
    /// </summary>
    public static class BlackBoxActionPreconditions
    {
        #region Public-Methods

        /// <summary>
        /// The unmet precondition of one action, or null when the action can run.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <param name="missionStatus">Status of the mission the action targets, or null when it no longer exists.</param>
        /// <param name="missionRequiresReview">Whether that mission requires review.</param>
        /// <param name="vesselExists">Whether the vessel the action targets exists.</param>
        /// <param name="dockExists">Whether the dock the action targets exists.</param>
        /// <returns>The reason the action cannot run, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
        public static string? Check(BlackBoxAction action, MissionStatusEnum? missionStatus, bool missionRequiresReview, bool vesselExists, bool dockExists)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            switch (action.Tool)
            {
                case "restart_mission":
                    if (!missionStatus.HasValue) return "restart_mission: the mission no longer exists.";
                    if (missionStatus.Value == MissionStatusEnum.Pending)
                        return "restart_mission: the mission is already Pending (it was restarted or requeued), so restarting it again would do nothing.";
                    if (missionStatus.Value != MissionStatusEnum.Failed && missionStatus.Value != MissionStatusEnum.Cancelled && missionStatus.Value != MissionStatusEnum.LandingFailed)
                        return "restart_mission: the mission is " + missionStatus.Value + "; only a Failed, LandingFailed or Cancelled mission is restarted.";
                    return null;
                case "retry_landing":
                    if (!missionStatus.HasValue) return "retry_landing: the mission no longer exists.";
                    if (missionStatus.Value == MissionStatusEnum.LandingFailed || missionStatus.Value == MissionStatusEnum.WorkProduced) return null;
                    if (missionStatus.Value == MissionStatusEnum.Review && !missionRequiresReview) return null;
                    return "retry_landing: the mission is " + missionStatus.Value + "; only a LandingFailed or WorkProduced mission is landed again.";
                case "cancel_mission":
                    if (!missionStatus.HasValue) return "cancel_mission: the mission no longer exists.";
                    if (missionStatus.Value == MissionStatusEnum.Cancelled || missionStatus.Value == MissionStatusEnum.Complete)
                        return "cancel_mission: the mission is already " + missionStatus.Value + ".";
                    return null;
                case "update_mission":
                    return missionStatus.HasValue ? null : "update_mission: the mission no longer exists.";
                case "update_vessel":
                    return vesselExists ? null : "update_vessel: the vessel no longer exists.";
                case "unstick_dock":
                case "repair_dock":
                    return dockExists ? null : action.Tool + ": the dock no longer exists.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The mission status an action leaves behind when it succeeds, so the preconditions of a later step in the same
        /// plan are checked against it (a cancel followed by a restart is valid).
        /// </summary>
        /// <param name="tool">Tool name.</param>
        /// <param name="status">Status before the action, or null when the mission does not exist.</param>
        /// <returns>Status after the action.</returns>
        public static MissionStatusEnum? StatusAfter(string? tool, MissionStatusEnum? status)
        {
            if (!status.HasValue) return null;
            if (String.Equals(tool, "cancel_mission", StringComparison.Ordinal)) return MissionStatusEnum.Cancelled;
            if (String.Equals(tool, "restart_mission", StringComparison.Ordinal)) return MissionStatusEnum.Pending;
            return status;
        }

        #endregion
    }
}
