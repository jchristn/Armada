namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The auth block of a <see cref="MuxServerEntry"/>.
    /// </summary>
    public class MuxServerAuth
    {
        #region Public-Members

        /// <summary>
        /// Auth type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Header that carries the key.
        /// </summary>
        [JsonPropertyName("apiKeyHeader")]
        public string? ApiKeyHeader { get; set; } = null;

        /// <summary>
        /// Key value (an environment reference).
        /// </summary>
        [JsonPropertyName("apiKeyValue")]
        public string? ApiKeyValue { get; set; } = null;

        #endregion
    }
}
