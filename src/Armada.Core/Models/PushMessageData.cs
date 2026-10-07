namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The data object of a push message: what the app needs to open (and, for actionable kinds, act on) the item.
    /// </summary>
    public class PushMessageData
    {
        #region Public-Members

        /// <summary>
        /// Dashboard-style path the app opens (for example /missions/msn_x or /ask/ath_x).
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = "/";

        /// <summary>
        /// Kind (see <see cref="PushNotificationKinds"/>; the inbox kinds plus voyage_finished and test).
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Identifier of the entity the push is about (msn_, cpr_, aap_, dpl_, cpt_, vyg_, or pdv_ for a test).
        /// </summary>
        [JsonPropertyName("entityId")]
        public string EntityId { get; set; } = String.Empty;

        /// <summary>
        /// Category name (a PushCategoryEnum value), or "Test".
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// Ask thread (ath_ prefix) for Ask proposals and thread CLI permission requests, so the app can call the
        /// thread-scoped approve and reject routes; null otherwise.
        /// </summary>
        [JsonPropertyName("threadId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ThreadId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushMessageData()
        {
        }

        #endregion
    }
}
