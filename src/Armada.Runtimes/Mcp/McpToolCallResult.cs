namespace Armada.Runtimes.Mcp
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A deserialized MCP <c>tools/call</c> result: the content parts and the <c>isError</c> flag the server sets for a
    /// failed call (including one refused by the server's rate limit).
    /// </summary>
    public class McpToolCallResult
    {
        #region Public-Members

        /// <summary>
        /// Content parts.
        /// </summary>
        [JsonPropertyName("content")]
        public List<McpToolContentPart> Content
        {
            get { return _Content; }
            set { _Content = value ?? new List<McpToolContentPart>(); }
        }

        /// <summary>
        /// True when the server reports the call failed.
        /// </summary>
        [JsonPropertyName("isError")]
        public bool IsError { get; set; } = false;

        /// <summary>
        /// The text parts joined with newlines.
        /// </summary>
        [JsonIgnore]
        public string Text
        {
            get { return string.Join("\n", _Content.Where(p => p.Type == "text" && p.Text != null).Select(p => p.Text)); }
        }

        #endregion

        #region Private-Members

        private List<McpToolContentPart> _Content = new List<McpToolContentPart>();

        #endregion
    }
}
