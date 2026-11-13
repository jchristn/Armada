namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// What landing target synchronization integrated: the mission dock's head before and after, and the remote and
    /// local target tips it integrated against.
    /// </summary>
    public class LandingTargetSynchronizationResult
    {
        #region Public-Members

        /// <summary>
        /// The dock's head commit before synchronization.
        /// </summary>
        public string? Before { get; set; } = null;

        /// <summary>
        /// The dock's head commit after synchronization.
        /// </summary>
        public string? After { get; set; } = null;

        /// <summary>
        /// The remote target tip (origin/{target}) integrated against, when it resolved.
        /// </summary>
        public string? RemoteTargetTip { get; set; } = null;

        /// <summary>
        /// The local target checkout's tip integrated against, when one was given and it resolved.
        /// </summary>
        public string? LocalTargetTip { get; set; } = null;

        /// <summary>
        /// True when the dock's head changed.
        /// </summary>
        public bool Advanced => !String.Equals(Before, After, StringComparison.Ordinal);

        #endregion
    }
}
