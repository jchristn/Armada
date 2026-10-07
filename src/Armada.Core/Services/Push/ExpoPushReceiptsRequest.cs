namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Expo Push Service wire format: the body of POST /push/getReceipts.
    /// </summary>
    public class ExpoPushReceiptsRequest
    {
        #region Public-Members

        /// <summary>
        /// Ticket identifiers.
        /// </summary>
        [JsonPropertyName("ids")]
        public List<string> Ids { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ExpoPushReceiptsRequest()
        {
        }

        #endregion
    }
}
