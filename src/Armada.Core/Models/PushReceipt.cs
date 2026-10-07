namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The push service's final delivery result for one ticket.
    /// </summary>
    public class PushReceipt
    {
        #region Public-Members

        /// <summary>
        /// Ticket identifier.
        /// </summary>
        public string TicketId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the provider (APNs or FCM) accepted the message.
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// Error code when delivery failed.
        /// </summary>
        public PushErrorCodeEnum? Error { get; set; } = null;

        /// <summary>
        /// Human-readable error detail (for logs only; never branched on).
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushReceipt()
        {
        }

        #endregion
    }
}
