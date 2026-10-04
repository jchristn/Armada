namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// Typed MCP tools/call result: a list of content items.
    /// </summary>
    public sealed class McpToolCallResult
    {
        /// <summary>
        /// Content items; tool results are returned as a single text item holding JSON.
        /// </summary>
        public List<McpContentItem> Content { get; set; } = new List<McpContentItem>();
    }
}
