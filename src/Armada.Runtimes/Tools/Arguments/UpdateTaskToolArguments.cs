namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the update_task built-in tool.
    /// </summary>
    public class UpdateTaskToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The id of the task to update. Required.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// The new status text. Required.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// An optional note; required when the status is failed.
        /// </summary>
        [JsonPropertyName("note")]
        public string? Note { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that required parameters are present.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? idProblem = ToolArgumentParser.Required(Id, "id");
            if (idProblem != null) return idProblem;
            string? statusProblem = ToolArgumentParser.Required(Status, "status");
            if (statusProblem != null) return statusProblem;
            if (String.IsNullOrWhiteSpace(Id)) return "Required parameter 'id' is missing or empty.";
            return null;
        }

        #endregion
    }
}
