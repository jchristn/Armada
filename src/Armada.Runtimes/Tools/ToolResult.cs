namespace Armada.Runtimes.Tools
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents the result of executing a tool call.
    /// </summary>
    public class ToolResult
    {
        #region Private-Members

        private string _ToolCallId = string.Empty;
        private bool _Success = true;
        private string _Content = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolResult"/> class with default values.
        /// </summary>
        public ToolResult()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The identifier of the tool call this result corresponds to.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string ToolCallId
        {
            get => _ToolCallId;
            set => _ToolCallId = value ?? throw new ArgumentNullException(nameof(ToolCallId));
        }

        /// <summary>
        /// Whether the tool execution completed successfully.
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success
        {
            get => _Success;
            set => _Success = value;
        }

        /// <summary>
        /// The tool execution output as a JSON string.
        /// </summary>
        [JsonPropertyName("content")]
        public string Content
        {
            get => _Content;
            set => _Content = value ?? throw new ArgumentNullException(nameof(Content));
        }

        #endregion
    }
}
