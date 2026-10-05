namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The JSON content of a failed built-in tool result: a machine-readable error code and a message.
    /// </summary>
    public class ToolErrorContent
    {
        #region Public-Members

        /// <summary>
        /// Always false for an error payload.
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; } = false;

        /// <summary>
        /// The machine-readable error code, for example "invalid_parameter".
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; } = String.Empty;

        /// <summary>
        /// A human-readable description of the error.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}
