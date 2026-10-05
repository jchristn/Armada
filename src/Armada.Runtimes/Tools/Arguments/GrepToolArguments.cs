namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the grep built-in tool.
    /// </summary>
    public class GrepToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The regular expression to search for. Required.
        /// </summary>
        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// The directory to search, or null for the working directory.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; } = null;

        /// <summary>
        /// A file-name glob filter, or null to search all files.
        /// </summary>
        [JsonPropertyName("include")]
        public string? Include { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that required parameters are present.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? patternProblem = ToolArgumentParser.Required(Pattern, "pattern");
            if (patternProblem != null) return patternProblem;
            return null;
        }

        #endregion
    }
}
