namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Expo Push Service wire format: the response of POST /push/getReceipts (receipts keyed by ticket id).
    /// </summary>
    public class ExpoPushReceiptsResponse
    {
        #region Public-Members

        /// <summary>
        /// Receipts keyed by ticket identifier.
        /// </summary>
        [JsonPropertyName("data")]
        public Dictionary<string, ExpoPushTicketDto>? Data { get; set; } = null;

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
        public ExpoPushReceiptsResponse()
        {
        }

        #endregion
    }
}
