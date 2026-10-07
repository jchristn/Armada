namespace Armada.Core.Services.Push
{
    using System;

    /// <summary>
    /// A ticket whose receipt has not been checked yet.
    /// </summary>
    public class PushPendingReceipt
    {
        #region Public-Members

        /// <summary>
        /// Ticket identifier.
        /// </summary>
        public string TicketId { get; set; } = String.Empty;

        /// <summary>
        /// Device the message went to.
        /// </summary>
        public string DeviceId { get; set; } = String.Empty;

        /// <summary>
        /// UTC time the message was sent.
        /// </summary>
        public DateTime SentUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushPendingReceipt()
        {
        }

        #endregion
    }
}
