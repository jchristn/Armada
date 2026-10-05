namespace Armada.Client.Socket
{
    using System.Text.Json.Serialization;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// Payload of <c>mission.status_changed</c>: the mission's ids plus the new <see cref="Status"/> and the
    /// <see cref="PreviousStatus"/> as typed fields (read these, not the English message text).
    /// </summary>
    public class MissionStatusChangedEvent
    {
        #region Public-Members

        /// <summary>
        /// Entity type (<c>mission</c>), or null.
        /// </summary>
        public string? EntityType { get; set; } = null;

        /// <summary>
        /// Entity id (the mission id), or null.
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// Captain id, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Mission id, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Vessel id, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Voyage id, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// New status name (a <see cref="MissionStatusEnum"/> member), or null from an older server.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Previous status name, or null when unknown or from an older server.
        /// </summary>
        public string? PreviousStatus { get; set; } = null;

        /// <summary>
        /// <see cref="Status"/> as a mission status, or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public MissionStatusEnum? MissionStatus
        {
            get { return EnumNames.ParseOrNull<MissionStatusEnum>(Status); }
        }

        /// <summary>
        /// <see cref="PreviousStatus"/> as a mission status, or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public MissionStatusEnum? PreviousMissionStatus
        {
            get { return EnumNames.ParseOrNull<MissionStatusEnum>(PreviousStatus); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionStatusChangedEvent()
        {
        }

        #endregion
    }
}
