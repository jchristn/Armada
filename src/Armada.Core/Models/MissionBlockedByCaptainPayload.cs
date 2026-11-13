namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Payload of a <c>mission.blocked_by_captain</c> event: the mission failed without re-dispatch because its captain
    /// reported BLOCKED and left no work on its branch.
    /// </summary>
    public class MissionBlockedByCaptainPayload
    {
        #region Public-Members

        /// <summary>
        /// The blocked mission.
        /// </summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>
        /// The captain that reported the blocker, or null when unknown.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// The mission's persona, or null for the default Worker.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// The captain's blocking reason (bounded), or null when it gave none.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
