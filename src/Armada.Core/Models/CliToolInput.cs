namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed view of the input of a CLI captain's own tool, reduced to the fields permission rules match on (Claude Code
    /// tool input field names). Unknown fields are ignored.
    /// </summary>
    public class CliToolInput
    {
        #region Public-Members

        /// <summary>
        /// Shell command (Bash).
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; } = null;

        /// <summary>
        /// URL (WebFetch).
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// File path (Read, Edit, Write, MultiEdit).
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// Notebook path (NotebookEdit).
        /// </summary>
        [JsonPropertyName("notebook_path")]
        public string? NotebookPath { get; set; } = null;

        /// <summary>
        /// Directory path (Glob, Grep, LS).
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; } = null;

        /// <summary>
        /// Search query (WebSearch).
        /// </summary>
        [JsonPropertyName("query")]
        public string? Query { get; set; } = null;

        /// <summary>
        /// Search pattern (Glob, Grep).
        /// </summary>
        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// The path a file tool targets: file_path, then notebook_path, then path; or null.
        /// </summary>
        [JsonIgnore]
        public string? TargetPath
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(FilePath)) return FilePath;
                if (!String.IsNullOrWhiteSpace(NotebookPath)) return NotebookPath;
                if (!String.IsNullOrWhiteSpace(Path)) return Path;
                return null;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliToolInput()
        {
        }

        #endregion
    }
}
