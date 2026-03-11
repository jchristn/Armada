namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A message between the admiral and captains or between captains.
    /// </summary>
    public class Signal
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
        /// Sender captain identifier, or null for Admiral.
        /// </summary>
        public string? FromCaptainId { get; set; } = null;

        /// <summary>
        /// Recipient captain identifier, or null for Admiral.
        /// </summary>
        public string? ToCaptainId { get; set; } = null;

        /// <summary>
        /// Signal type.
        /// </summary>
        public SignalTypeEnum Type { get; set; } = SignalTypeEnum.Nudge;

        /// <summary>
        /// Signal payload as JSON string.
        /// </summary>
        public string? Payload { get; set; } = null;

        /// <summary>
        /// Whether the signal has been read.
        /// </summary>
        public bool Read { get; set; } = false;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.SignalIdPrefix, 24);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Signal()
        {
        }

        /// <summary>
        /// Instantiate with type and payload.
        /// </summary>
        /// <param name="type">Signal type.</param>
        /// <param name="payload">JSON payload.</param>
        public Signal(SignalTypeEnum type, string? payload = null)
        {
            Type = type;
            Payload = payload;
        }

        #endregion
    }
}
