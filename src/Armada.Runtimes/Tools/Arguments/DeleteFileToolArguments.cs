namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the delete_file built-in tool.
    /// </summary>
    public class DeleteFileToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The path of the file to delete. Required.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

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
