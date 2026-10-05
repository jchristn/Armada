namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the write_file built-in tool.
    /// </summary>
    public class WriteFileToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The path of the file to write. Required.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// The content to write. Required.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that required parameters are present.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? filePathProblem = ToolArgumentParser.Required(FilePath, "file_path");
            if (filePathProblem != null) return filePathProblem;
            string? contentProblem = ToolArgumentParser.Required(Content, "content");
            if (contentProblem != null) return contentProblem;
            return null;
        }

        #endregion
    }
}
