namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Inventory of the sanitized usage interface fixtures under
    /// <c>src/Test.Shared/Fixtures/Usage/</c>. Every fixture file must be listed here with its
    /// provenance, so a fixture can never be mistaken for a captured provider response.
    /// </summary>
    public class UsageFixtureManifest
    {
        #region Public-Members

        /// <summary>
        /// Manifest schema version.
        /// </summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Why the fixture set exists.
        /// </summary>
        [JsonPropertyName("purpose")]
        public string? Purpose { get; set; } = null;

        /// <summary>
        /// One entry per fixture file.
        /// </summary>
        [JsonPropertyName("fixtures")]
        public List<UsageFixtureEntry> Fixtures { get; set; } = new List<UsageFixtureEntry>();

        /// <summary>
        /// Runtimes whose account-limit interface is explicitly unavailable, with the reason.
        /// </summary>
        [JsonPropertyName("unavailable")]
        public List<UsageUnavailableEntry> Unavailable { get; set; } = new List<UsageUnavailableEntry>();

        #endregion
    }
}
