namespace Armada.Core.Services.Push
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Expo Push Service wire format: the details object of a ticket or receipt error.
    /// </summary>
    public class ExpoPushErrorDetails
    {
        #region Public-Members

        /// <summary>
        /// Error code (for example DeviceNotRegistered).
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ExpoPushErrorDetails()
        {
        }

        #endregion
    }
}
