namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the list_directory built-in tool.
    /// </summary>
    public class ListDirectoryToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The directory to list. Required.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that required parameters are present.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? pathProblem = ToolArgumentParser.Required(Path, "path");
            if (pathProblem != null) return pathProblem;
            return null;
        }

        #endregion
    }
}
