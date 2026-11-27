namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// The verification stamp a stage-check event payload carries when the check ran for a Verifying mission: the
    /// mission's last update time at that moment, so a resumed verification can tell whether the saved check is current.
    /// </summary>
    public sealed class StageCheckVerificationStamp
    {
        /// <summary>
        /// Mission last update time when the check ran, or null when the check did not run during verification.
        /// </summary>
        public DateTime? VerificationVersion { get; set; } = null;
    }
}
