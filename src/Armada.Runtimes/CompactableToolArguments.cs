namespace Armada.Runtimes
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The bulky parts of a write_file, edit_file or multi_edit call that compaction measures before replacing them.
    /// </summary>
    public sealed class CompactableToolArguments
    {
        /// <summary>
        /// Target file path.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        /// <summary>
        /// write_file body.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// edit_file text to replace.
        /// </summary>
        [JsonPropertyName("old_string")]
        public string? OldString { get; set; }

        /// <summary>
        /// edit_file replacement text.
        /// </summary>
        [JsonPropertyName("new_string")]
        public string? NewString { get; set; }

        /// <summary>
        /// multi_edit edits.
        /// </summary>
        [JsonPropertyName("edits")]
        public List<CompactableToolArguments>? Edits { get; set; }
    }
}
