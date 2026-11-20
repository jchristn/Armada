namespace Armada.Core.Services
{
    /// <summary>
    /// One mission's links to other missions (hierarchy parent, rescue original, legacy dependency), as read by data
    /// expiry to keep every mission that a remaining mission points at.
    /// </summary>
    internal sealed class DataExpiryMissionLink
    {
        #region Internal-Members

        /// <summary>
        /// Mission id.
        /// </summary>
        internal string Id { get; set; } = "";

        /// <summary>
        /// parent_mission_id, or null.
        /// </summary>
        internal string? ParentMissionId { get; set; } = null;

        /// <summary>
        /// rescue_of_mission_id, or null.
        /// </summary>
        internal string? RescueOfMissionId { get; set; } = null;

        /// <summary>
        /// depends_on_mission_id, or null.
        /// </summary>
        internal string? DependsOnMissionId { get; set; } = null;

        #endregion
    }
}
