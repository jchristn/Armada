namespace Armada.Runtimes
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Arguments of an old write or edit call after compaction: the path and a note saying how much text was removed.
    /// Serialized as a JSON object, so providers that re-validate history arguments still accept it.
    /// </summary>
    public sealed class CompactedToolArguments
    {
        /// <summary>
        /// Target file path, kept from the original call.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        /// <summary>
        /// What was removed.
        /// </summary>
        [JsonPropertyName("armada_compacted")]
        public string Note { get; set; } = "";
    }
}
