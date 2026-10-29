namespace Armada.Core.Models
{
    /// <summary>
    /// Typed payload of a <c>mission.landing_target_advanced</c> event: the mission dock's head before and after landing
    /// integrated the current target, and the target tips it integrated against. Stored as the event's Payload JSON.
    /// </summary>
    public class LandingTargetAdvancedPayload
    {
        #region Public-Members

        /// <summary>
        /// The target branch.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// The dock's head commit before integrating the target.
        /// </summary>
        public string? Before { get; set; } = null;

        /// <summary>
        /// The dock's head commit after integrating the target; landing verifies and publishes this commit.
        /// </summary>
        public string? After { get; set; } = null;

        /// <summary>
        /// The remote target tip integrated against, when it resolved.
        /// </summary>
        public string? RemoteTargetTip { get; set; } = null;

        /// <summary>
        /// The local target checkout's tip integrated against, when it resolved.
        /// </summary>
        public string? LocalTargetTip { get; set; } = null;

        #endregion
    }
}
