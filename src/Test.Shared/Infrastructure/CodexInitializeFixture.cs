namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The sanitized Codex app-server initialize params fixture.
    /// </summary>
    public class CodexInitializeFixture
    {
        /// <summary>
        /// Client identity; the only required initialize property.
        /// </summary>
        [JsonPropertyName("clientInfo")]
        public CodexClientInfoFixture? ClientInfo { get; set; } = null;

        /// <summary>
        /// Optional capabilities.
        /// </summary>
        [JsonPropertyName("capabilities")]
        public CodexInitializeCapabilitiesFixture? Capabilities { get; set; } = null;
    }
}
