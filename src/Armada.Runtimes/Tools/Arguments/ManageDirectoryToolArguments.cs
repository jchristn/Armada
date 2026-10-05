namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the manage_directory built-in tool.
    /// </summary>
    public class ManageDirectoryToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The action: create, delete, or rename. Required.
        /// </summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; } = null;

        /// <summary>
        /// The directory to act on. Required.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; } = null;

        /// <summary>
        /// The destination path for the rename action.
        /// </summary>
        [JsonPropertyName("new_path")]
        public string? NewPath { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that required parameters are present.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? actionProblem = ToolArgumentParser.Required(Action, "action");
            if (actionProblem != null) return actionProblem;
            string? pathProblem = ToolArgumentParser.Required(Path, "path");
            if (pathProblem != null) return pathProblem;
            return null;
        }

        #endregion
    }
}
