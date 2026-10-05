namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the multi_edit built-in tool.
    /// </summary>
    public class MultiEditToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The path of the file to edit. Required.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// The edit operations to apply in order. Required.
        /// </summary>
        [JsonPropertyName("edits")]
        public List<MultiEditOperationArguments?>? Edits { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that the file path and every edit operation are present and complete.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? filePathProblem = ToolArgumentParser.Required(FilePath, "file_path");
            if (filePathProblem != null) return filePathProblem;

            if (Edits == null) return "Parameter 'edits' is required and must be an array.";

            for (int i = 0; i < Edits.Count; i++)
            {
                MultiEditOperationArguments? edit = Edits[i];
                if (edit == null) return "Edit at index " + i + ": must be an object, not null.";

                string? problem = edit.Validate();
                if (problem != null) return "Edit at index " + i + ": " + problem;
            }

            return null;
        }

        #endregion
    }
}
