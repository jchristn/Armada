namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Codex app-server initialize capabilities inside the sanitized fixture.
    /// </summary>
    public class CodexInitializeCapabilitiesFixture
    {
        /// <summary>
        /// Experimental API opt-in; the usage probe must not need it.
        /// </summary>
        [JsonPropertyName("experimentalApi")]
        public bool? ExperimentalApi { get; set; } = null;
    }
}
