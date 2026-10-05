namespace Armada.Runtimes.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The result of an MCP <c>tools/list</c> call: one page of tools and the cursor for the next page.
    /// </summary>
    public class McpListToolsResult
    {
        #region Public-Members

        /// <summary>
        /// Tools on this page.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<McpRemoteTool> Tools
        {
            get { return _Tools; }
            set { _Tools = value ?? new List<McpRemoteTool>(); }
        }

        /// <summary>
        /// Cursor for the next page, or null on the last page.
        /// </summary>
        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; set; } = null;

        #endregion

        #region Private-Members

        private List<McpRemoteTool> _Tools = new List<McpRemoteTool>();

        #endregion
    }
}
