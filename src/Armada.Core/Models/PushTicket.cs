namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The push service's answer for one sent message (in the order the messages were sent).
    /// </summary>
    public class PushTicket
    {
        #region Public-Members

        /// <summary>
        /// Whether the message was accepted.
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Ticket identifier (used to fetch the receipt) when accepted.
        /// </summary>
        public string? TicketId { get; set; } = null;

        /// <summary>
        /// Error code when rejected.
        /// </summary>
        public PushErrorCodeEnum? Error { get; set; } = null;

        /// <summary>
        /// Human-readable error detail when rejected (for logs only; never branched on).
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushTicket()
        {
        }

        #endregion
    }
}
