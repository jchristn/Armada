namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One sanitized usage interface fixture and the provenance of its shape.
    /// </summary>
    public class UsageFixtureEntry
    {
        #region Public-Members

        /// <summary>
        /// File name inside the usage fixture directory.
        /// </summary>
        [JsonPropertyName("file")]
        public string File { get; set; } = string.Empty;

        /// <summary>
        /// Runtime the fixture belongs to, matching <c>AgentRuntimeEnum</c>.
        /// </summary>
        [JsonPropertyName("runtime")]
        public string Runtime { get; set; } = string.Empty;

        /// <summary>
        /// The interface the fixture documents.
        /// </summary>
        [JsonPropertyName("interface")]
        public string Interface { get; set; } = string.Empty;

        /// <summary>
        /// Provenance label; only an interface observed on the inspecting host may be verified.
        /// </summary>
        [JsonPropertyName("provenance")]
        public string Provenance { get; set; } = string.Empty;

        /// <summary>
        /// True only when the shape was produced by the installed binary or an observed response.
        /// </summary>
        [JsonPropertyName("verified")]
        public bool Verified { get; set; } = false;

        /// <summary>
        /// The non-prompt command that produced the shape, when one was run.
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; } = null;

        /// <summary>
        /// What the fixture proves and what it does not.
        /// </summary>
        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        #endregion
    }
}
