namespace Armada.Core.Services.Push
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Expo Push Service wire format: a request-level error.
    /// </summary>
    public class ExpoPushRequestError
    {
        #region Public-Members

        /// <summary>
        /// Error code.
        /// </summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; } = null;

        /// <summary>
        /// Error message.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ExpoPushRequestError()
        {
        }

        #endregion
    }
}
