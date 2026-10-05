namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the read_file built-in tool.
    /// </summary>
    public class ReadFileToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The path of the file to read. Required.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// The 1-based line to start reading from, or null for the first line.
        /// </summary>
        [JsonPropertyName("offset")]
        public int? Offset { get; set; } = null;

        /// <summary>
        /// The maximum number of lines to read, or null for the whole file.
        /// </summary>
        [JsonPropertyName("limit")]
        public int? Limit { get; set; } = null;

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
            return null;
        }

        #endregion
    }
}
