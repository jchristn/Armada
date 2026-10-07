namespace Armada.Core.Services.Push
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Expo Push Service wire format: a push ticket (send response entry) or a push receipt.
    /// </summary>
    public class ExpoPushTicketDto
    {
        #region Public-Members

        /// <summary>
        /// "ok" or "error".
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// Ticket identifier (tickets only).
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Error message.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Error details.
        /// </summary>
        [JsonPropertyName("details")]
        public ExpoPushErrorDetails? Details { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ExpoPushTicketDto()
        {
        }

        #endregion
    }
}
