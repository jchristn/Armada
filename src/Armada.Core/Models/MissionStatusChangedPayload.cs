namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Typed payload of a <c>mission.status_changed</c> event: stored as the event's Payload JSON and sent as the
    /// <c>status</c> and <c>previousStatus</c> fields of the WebSocket event, so consumers read the new status from a
    /// field instead of the message text.
    /// </summary>
    public class MissionStatusChangedPayload
    {
        #region Public-Members

        /// <summary>
        /// The mission's status after the change.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// The mission's status before the change, or null when unknown.
        /// </summary>
        public MissionStatusEnum? PreviousStatus { get; set; } = null;

        #endregion
    }
}
