namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The detail object inside a <see cref="ProviderErrorEnvelope"/>.
    /// </summary>
    public class ProviderErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable error type (for example "rate_limit_error", "authentication_error").
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        #endregion
    }
}
