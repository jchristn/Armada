namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The provider error body a model API returns on failure, as embedded in a Claude Code "API Error" protocol
    /// line: <c>{"type":"error","error":{"type":"rate_limit_error","message":"..."}}</c>. Deserialized, never
    /// searched as text.
    /// </summary>
    public class ProviderErrorEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Envelope type (normally "error").
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// The error detail.
        /// </summary>
        [JsonPropertyName("error")]
        public ProviderErrorDetail? Error { get; set; } = null;

        #endregion
    }
}
