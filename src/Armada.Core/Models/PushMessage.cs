namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One push message to one device, in the Expo Push Service wire format (property names are the Expo field names).
    /// The content never carries secrets, code, diffs, or command text beyond a truncated, redacted summary.
    /// </summary>
    public class PushMessage
    {
        #region Public-Members

        /// <summary>
        /// Expo push token of the target device.
        /// </summary>
        [JsonPropertyName("to")]
        public string To { get; set; } = String.Empty;

        /// <summary>
        /// Title (short; truncated by the sender).
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body (short; truncated by the sender).
        /// </summary>
        [JsonPropertyName("body")]
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Data delivered to the app (deep link, kind, entity, category).
        /// </summary>
        [JsonPropertyName("data")]
        public PushMessageData Data { get; set; } = new PushMessageData();

        /// <summary>
        /// Sound ("default").
        /// </summary>
        [JsonPropertyName("sound")]
        public string? Sound { get; set; } = "default";

        /// <summary>
        /// Badge count (the recipient's pending approvals), or null to leave the badge unchanged.
        /// </summary>
        [JsonPropertyName("badge")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Badge { get; set; } = null;

        /// <summary>
        /// Notification category (iOS) whose action buttons the app registered, or null. Set only for actionable kinds
        /// the recipient may decide; the app performs the action through the REST API.
        /// </summary>
        [JsonPropertyName("categoryId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CategoryId { get; set; } = null;

        /// <summary>
        /// Delivery priority ("default" or "high").
        /// </summary>
        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "default";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushMessage()
        {
        }

        #endregion
    }
}
