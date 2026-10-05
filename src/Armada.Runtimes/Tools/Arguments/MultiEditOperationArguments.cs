namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One edit operation in a multi_edit call.
    /// </summary>
    public class MultiEditOperationArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The exact string to find and replace. Required.
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
            string? oldStringProblem = ToolArgumentParser.Required(OldString, "old_string");
            if (oldStringProblem != null) return oldStringProblem;
            string? newStringProblem = ToolArgumentParser.Required(NewString, "new_string");
            if (newStringProblem != null) return newStringProblem;
            return null;
        }

        #endregion
    }
}
