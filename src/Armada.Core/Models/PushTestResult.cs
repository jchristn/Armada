namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Result of POST /api/v1/push/devices/{id}/test.
    /// </summary>
    public class PushTestResult
    {
        #region Public-Members

        /// <summary>
        /// Device identifier (pdv_ prefix).
        /// </summary>
        public string DeviceId { get; set; } = String.Empty;

        /// <summary>
        /// Outcome.
        /// </summary>
        public PushTestStatusEnum Status { get; set; } = PushTestStatusEnum.Failed;

        /// <summary>
        /// Expo ticket identifier when the message was accepted.
        /// </summary>
        public string? TicketId { get; set; } = null;

        /// <summary>
        /// Error code reported by the Expo Push Service, when any.
        /// </summary>
        public PushErrorCodeEnum? Error { get; set; } = null;

        /// <summary>
        /// Human-readable detail.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushTestResult()
        {
        }

        #endregion
    }
}
