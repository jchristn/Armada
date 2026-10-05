namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the edit_file built-in tool.
    /// </summary>
    public class EditFileToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The path of the file to edit. Required.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// The exact string to replace. Required.
        /// </summary>
        [JsonPropertyName("old_string")]
        public string? OldString { get; set; } = null;

        /// <summary>
        /// The replacement string. Required.
        /// </summary>
        [JsonPropertyName("new_string")]
        public string? NewString { get; set; } = null;

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
            string? oldStringProblem = ToolArgumentParser.Required(OldString, "old_string");
            if (oldStringProblem != null) return oldStringProblem;
            string? newStringProblem = ToolArgumentParser.Required(NewString, "new_string");
            if (newStringProblem != null) return newStringProblem;
            return null;
        }

        #endregion
    }
}
