namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of cancelling a voyage (REST DELETE /api/v1/voyages/{id}, MCP cancel_voyage, WebSocket cancel_voyage):
    /// the cancelled voyage and counts. Which missions were or were not cancelled is read through the missions
    /// enumeration filtered by voyageId (and status).
    /// </summary>
    public class VoyageCancelResult
    {
        #region Public-Members

        /// <summary>
        /// The voyage, now Cancelled.
        /// </summary>
        public Voyage Voyage { get; set; } = null!;

        /// <summary>
        /// Number of Pending, Assigned, or Blocked missions this cancel cancelled.
        /// </summary>
        public int CancelledMissions { get; set; } = 0;

        /// <summary>
        /// Number of Pending, Assigned, or Blocked missions that could not be cancelled (for example because a running
        /// agent did not confirm it stopped). They keep their status; retry the cancel or cancel them one by one.
        /// </summary>
        public int NotCancelledMissions { get; set; } = 0;

        /// <summary>
        /// Reason the first mission could not be cancelled, or null when every mission was cancelled.
        /// </summary>
        public string? FirstNotCancelledReason { get; set; } = null;

        /// <summary>
        /// The missions this cancel cancelled, for change broadcasts. Not serialized.
        /// </summary>
        [JsonIgnore]
        public List<Mission> Cancelled { get; set; } = new List<Mission>();

        #endregion
    }
}
