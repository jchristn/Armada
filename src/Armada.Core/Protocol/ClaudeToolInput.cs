namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The input fields of Claude Code's built-in tools that describe a call in a few words: the command and description
    /// of Bash, the path of Read, Edit, and Write, the pattern of Grep and Glob, and so on. Fields a tool does not have
    /// stay null.
    /// </summary>
    public class ClaudeToolInput
    {
        #region Public-Members

        /// <summary>
        /// Shell command (Bash).
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; } = null;

        /// <summary>
        /// What the call is for, in the model's words (Bash, Task).
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// File path (Read, Edit, MultiEdit, Write).
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// Notebook path (NotebookEdit).
        /// </summary>
        [JsonPropertyName("notebook_path")]
        public string? NotebookPath { get; set; } = null;

        /// <summary>
        /// Directory or file to search (Grep, Glob, LS).
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; } = null;

        /// <summary>
        /// Search pattern (Grep, Glob).
        /// </summary>
        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// URL (WebFetch).
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// Search query (WebSearch).
        /// </summary>
        [JsonPropertyName("query")]
        public string? Query { get; set; } = null;

        #endregion
    }
}
