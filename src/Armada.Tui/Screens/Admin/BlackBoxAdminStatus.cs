namespace Armada.Tui.Screens.Admin
{
    using Armada.Core.Models;

    /// <summary>
    /// The black box retention and storage status the Storage screen shows (either may be null when the server could
    /// not report it).
    /// </summary>
    public class BlackBoxAdminStatus
    {
        #region Public-Members

        /// <summary>
        /// Retention status, or null.
        /// </summary>
        public BlackBoxRetentionStatus? Retention { get; set; } = null;

        /// <summary>
        /// Storage status, or null.
        /// </summary>
        public BlackBoxStorageStatus? Storage { get; set; } = null;

        /// <summary>
        /// Why a part is missing (server message), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
