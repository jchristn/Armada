namespace Armada.Core.Settings
{
    using Armada.Core.Enums;

    /// <summary>
    /// The landing fields of a settings file written before 1.0.1, when the global landing mode was optional and the
    /// autoPush and autoCreatePullRequests flags decided the landing when it was unset. Read only to choose the
    /// landing mode for such a file.
    /// </summary>
    public class LegacyLandingSettings
    {
        #region Public-Members

        /// <summary>
        /// Landing mode in the file, if any.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Legacy autoPush flag in the file, if any (the old default was true).
        /// </summary>
        public bool? AutoPush { get; set; } = null;

        /// <summary>
        /// Legacy autoCreatePullRequests flag in the file, if any (the old default was false).
        /// </summary>
        public bool? AutoCreatePullRequests { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The landing mode equivalent to the legacy flags: PullRequest when autoCreatePullRequests was true, LocalMerge
        /// when autoPush was false, otherwise MergeAndPush (which is also the result when neither flag is present).
        /// </summary>
        /// <returns>Landing mode.</returns>
        public LandingModeEnum ResolveLandingMode()
        {
            if (AutoCreatePullRequests == true) return LandingModeEnum.PullRequest;
            if (AutoPush == false) return LandingModeEnum.LocalMerge;
            return LandingModeEnum.MergeAndPush;
        }

        #endregion
    }
}
