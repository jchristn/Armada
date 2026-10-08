namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A transition of a Harbor's link (connected, reconnecting, disconnected) with the time the Admiral saw it.
    /// </summary>
    public class HarborLinkEvent
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (hle_ prefix).
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
        /// Harbor identifier.
        /// </summary>
        public string HarborId { get; set; } = String.Empty;

        /// <summary>
        /// The transition.
        /// </summary>
        public HarborLinkEventTypeEnum EventType { get; set; } = HarborLinkEventTypeEnum.Connected;

        /// <summary>
        /// When it happened (UTC).
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Why, in plain words, or null.
        /// </summary>
        public string? Detail { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.HarborLinkEventIdPrefix, 24);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLinkEvent()
        {
        }

        #endregion
    }
}
