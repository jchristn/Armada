namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Codex app-server initialize clientInfo inside the sanitized fixture.
    /// </summary>
    public class CodexClientInfoFixture
    {
        /// <summary>
        /// Client name (required by the schema).
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Client title, nullable.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Client version (required by the schema).
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; } = null;
    }
}
