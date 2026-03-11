namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A recorded event representing a state change in the system.
    /// </summary>
    public class ArmadaEvent
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Event type (e.g. "mission.created", "captain.stalled", "voyage.completed").
        /// </summary>
        public string EventType { get; set; } = "";

        /// <summary>
        /// Related entity type (e.g. "mission", "captain", "voyage").
        /// </summary>
        public string? EntityType { get; set; } = null;

        /// <summary>
        /// Related entity identifier.
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// Related captain identifier.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Related mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Related vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Related voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Human-readable event message.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Optional JSON payload with event details.
        /// </summary>
        public string? Payload { get; set; } = null;

        /// <summary>
        /// Event timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable("evt_", 24);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ArmadaEvent()
        {
        }

        /// <summary>
        /// Instantiate with event type and message.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <param name="message">Event message.</param>
        public ArmadaEvent(string eventType, string message)
        {
            EventType = eventType;
            Message = message;
        }

        #endregion
    }
}
