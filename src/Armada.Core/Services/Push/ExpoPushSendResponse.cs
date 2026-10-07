namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Expo Push Service wire format: the response of POST /push/send.
    /// </summary>
    public class ExpoPushSendResponse
    {
        #region Public-Members

        /// <summary>
        /// One ticket per message, in message order.
        /// </summary>
        [JsonPropertyName("data")]
        public List<ExpoPushTicketDto>? Data { get; set; } = null;

        /// <summary>
        /// Request-level errors.
        /// </summary>
        [JsonPropertyName("errors")]
        public List<ExpoPushRequestError>? Errors { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ExpoPushSendResponse()
        {
        }

        #endregion
    }
}
