namespace Armada.Core.Services
{
    /// <summary>
    /// Stable skip and failure reason codes recorded on fleet action run targets (SkipReason and FailureReason).
    /// Codes are part of the API contract: clients localize them, so they never change spelling.
    /// </summary>
    public static class FleetActionReasonCodes
    {
        #region Skip-Reasons

        /// <summary>
        /// The working tree had uncommitted changes and the run requires a clean tree.
        /// </summary>
        public const string DirtyTree = "DirtyTree";

        /// <summary>
        /// The vessel has no working directory, or it does not exist on the Admiral host.
        /// </summary>
        public const string NoWorkingDirectory = "NoWorkingDirectory";

        /// <summary>
        /// The template references {{vessel.buildCommand}} and the vessel has no build command.
        /// </summary>
        public const string NoBuildCommand = "NoBuildCommand";

        /// <summary>
        /// Dispatch validation rejected the voyage; the validator's message is in ErrorText.
        /// </summary>
        public const string DispatchRejected = "DispatchRejected";

        /// <summary>
        /// The caller was not authorized to act on the vessel.
        /// </summary>
        public const string NotAuthorized = "NotAuthorized";

        /// <summary>
        /// The vessel was deleted after the run was created.
        /// </summary>
        public const string VesselNotFound = "VesselNotFound";

        /// <summary>
        /// The vessel prefers a Harbor that is not currently connected, so its checkout is unreachable.
        /// </summary>
        public const string HarborUnavailable = "HarborUnavailable";

        #endregion

        #region Failure-Reasons

        /// <summary>
        /// The Admiral restarted while the command was running.
        /// </summary>
        public const string Interrupted = "Interrupted";

        /// <summary>
        /// The command exited with a non-zero exit code.
        /// </summary>
        public const string NonZeroExit = "NonZeroExit";

        /// <summary>
        /// The command exceeded the run's timeout and was killed.
        /// </summary>
        public const string Timeout = "Timeout";

        /// <summary>
        /// The dirty-tree pre-check (git status) could not run; details are in ErrorText.
        /// </summary>
        public const string GitStatusFailed = "GitStatusFailed";

        /// <summary>
        /// The template could not be rendered.
        /// </summary>
        public const string TemplateError = "TemplateError";

        /// <summary>
        /// The command could not be started or the runner hit an unexpected error; details are in ErrorText.
        /// </summary>
        public const string ExecutionError = "ExecutionError";

        /// <summary>
        /// Dispatching the voyage threw after validation passed; details are in ErrorText.
        /// </summary>
        public const string DispatchFailed = "DispatchFailed";

        /// <summary>
        /// No mission dispatcher is configured on this server.
        /// </summary>
        public const string DispatchUnavailable = "DispatchUnavailable";

        /// <summary>
        /// The voyage reached Failed (a mission failed or its landing failed).
        /// </summary>
        public const string VoyageFailed = "VoyageFailed";

        /// <summary>
        /// The voyage no longer exists.
        /// </summary>
        public const string VoyageMissing = "VoyageMissing";

        #endregion
    }
}
